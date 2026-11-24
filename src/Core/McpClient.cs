using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Biandan {
 public sealed class McpException:Exception {
  public readonly Dictionary<string,object> Details;
  public McpException(string message,Dictionary<string,object> details=null):base(message){Details=details;}
  internal bool WaitingForAuthorization{get{return Message.IndexOf("Complete Huawei authorization",StringComparison.OrdinalIgnoreCase)>=0;}}
 }
 public sealed class McpClient:IDisposable {
  readonly string stateDirectory;
  readonly bool directEndpoint;readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);HttpClient http;Process process;BackendJob job;BrowserHandoff handoff;volatile Uri authorizationUri;int requestId;
  public event Action<Uri> AuthorizationRequested;
  internal Func<bool> PrepareAuthorization;
  volatile bool authorizationStorageFailed;
  public Uri AuthorizationUri{get{return authorizationUri;}}
  public bool Running{get{return http!=null&&(directEndpoint||(process!=null&&!process.HasExited));}}
  public bool HasCredential{get{return CredentialFiles().Length>0;}}
  string[] CredentialFiles(){
   return Directory.Exists(stateDirectory)?Directory.GetFiles(stateDirectory,"huawei-refresh-*").Where(path=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path),@"^huawei-refresh-[a-f0-9]{24}$")).ToArray():new string[0];
  }
  public McpClient(string statePath){stateDirectory=statePath;}
  internal McpClient(Uri endpoint,string key){directEndpoint=true;ConfigureHttp(endpoint,key);}
  void ConfigureHttp(Uri endpoint,string key){http=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=endpoint,Timeout=TimeSpan.FromSeconds(120)};http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",key);http.DefaultRequestHeaders.Add("MCP-Protocol-Version","2025-06-18");}
  public Task StartAsync(){return StartAsync(false);}
  internal async Task StartAsync(bool smokeTest){
   if(Running)return;
   Stop();
   string executable;
   try{executable=await Task.Run(()=>BundledBackend.Prepare(stateDirectory));}
   catch(UnauthorizedAccessException){throw new McpException("无法写入本机登录服务目录。请确认当前 Windows 用户可写入 %LOCALAPPDATA% 后重试。");}
   catch(InvalidDataException){throw new McpException("此程序缺少或损坏了内置登录服务，请重新下载新版便单。");}
   catch(IOException){throw new McpException("无法准备内置登录服务。请关闭其他便单窗口后重试。");}
   var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
   var start=new ProcessStartInfo(executable,"--http --http-port "+port){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=Path.GetDirectoryName(executable)};
   start.EnvironmentVariables["BIANDAN_MCP_STATE_DIR"]=stateDirectory;
   foreach(string name in new[]{"BIANDAN_TOKEN","BIANDAN_CLIENT_ID","BIANDAN_REDIRECT_URI","BIANDAN_SCOPE","BIANDAN_RENEWAL_URL"})start.EnvironmentVariables.Remove(name);
   if(smokeTest)start.EnvironmentVariables["BIANDAN_TOKEN"]="local-transport-smoke-test";
   try{
    job=new BackendJob();
    if(!smokeTest){
     handoff=new BrowserHandoff(ReceiveAuthorization);
     handoff.Configure(start);
    }
    process=Process.Start(start);
    job.Add(process);
   }catch(System.ComponentModel.Win32Exception e){
    Stop();throw new McpException("Windows 无法运行内置登录服务（错误 "+e.NativeErrorCode+"）。请检查 Windows 安全中心是否拦截了程序。");
   }catch{Stop();throw;}
   process.OutputDataReceived+=delegate{};
   process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs entry){
    Uri address;
    if(ReferenceEquals(sender,process)&&BrowserHandoff.TryReadAddress(entry.Data,out address))ReceiveAuthorization(address);
   };process.BeginOutputReadLine();process.BeginErrorReadLine();
   string keyPath=Path.Combine(stateDirectory,"http-access-key");
   for(int i=0;i<100;i++){
    if(process.HasExited){Stop();throw new McpException("登录服务未能启动。请检查是否已有便单登录窗口，稍后重试。");}
    string key="";try{if(File.Exists(keyPath))key=File.ReadAllText(keyPath).Trim();}catch(IOException){}
    if(key.Length==43){if(http==null)ConfigureHttp(new Uri("http://127.0.0.1:"+port+"/mcp"),key);try{await RpcAsync("ping",J.Obj());return;}catch(HttpRequestException){}}
    await Task.Delay(150);
   }Stop();throw new McpException("本机登录服务启动超时，请重试。");
  }
  public async Task<Dictionary<string,object>> RpcAsync(string method,Dictionary<string,object> args){
   if(http==null)throw new McpException("请先登录便单。");
   var message=J.Obj("jsonrpc","2.0","id",Interlocked.Increment(ref requestId),"method",method,"params",args);
   using(var content=new StringContent(J.Encode(message),Encoding.UTF8,"application/json"))
   using(var response=await http.PostAsync("",content)){
    if(!response.IsSuccessStatusCode)throw new McpException("本机服务连接失败（"+(int)response.StatusCode+"），请重新登录。");
    var result=J.Parse(await response.Content.ReadAsStringAsync());if(result.D("error")!=null)throw new McpException(result.D("error").S("message"));
    var value=result.D("result");if(value==null)throw new McpException("服务返回了无法识别的数据。");return value;
   }
  }
  public async Task<Dictionary<string,object>> CallAsync(string name,Dictionary<string,object> args=null){
   if(authorizationStorageFailed)throw new McpException("无法保存本机授权标识。请检查磁盘权限并重新启动便单。");
   await gate.WaitAsync();try{
    var result=await RpcAsync("tools/call",J.Obj("name","biandan_"+name,"arguments",args??J.Obj()));var data=result.D("structuredContent");
    if(data==null)foreach(var item in result.Rows("content"))if(item.S("type")=="text"){data=J.Parse(item.S("text"));break;}
    if(data==null)throw new McpException("服务未返回操作结果。");if(result.B("isError")||data.ContainsKey("error"))throw new McpException(data.S("error","操作失败"),data);return data;
   }finally{gate.Release();}
  }
  public async Task<List<Dictionary<string,object>>> AllAsync(string name,Dictionary<string,object> args=null){
   var all=new List<Dictionary<string,object>>();var query=args==null?J.Obj():J.Clone(args);query["limit"]=100;int offset=0;var seen=new HashSet<int>();
   while(true){if(!seen.Add(offset))throw new McpException("分页结果重复，已停止同步以避免显示不完整数据。");query["offset"]=offset;var page=await CallAsync(name,query);all.AddRange(page.Rows("items"));if(!page.ContainsKey("next_offset")||page["next_offset"]==null)return all;offset=page.I("next_offset");if(seen.Count>10000)throw new McpException("数据量超过单次同步上限。");}
  }
  void ReceiveAuthorization(Uri address){
   if(handoff==null||Equals(authorizationUri,address))return;
   if(PrepareAuthorization!=null&&!PrepareAuthorization()){authorizationStorageFailed=true;return;}
   authorizationUri=address;
   var callback=AuthorizationRequested;
   if(callback!=null)callback(address);
  }

  public void Stop(){
   var browser=handoff;
   handoff=null;
   authorizationUri=null;
   if(browser!=null)browser.Dispose();
   if(http!=null){http.Dispose();http=null;}
   if(job!=null){job.Dispose();job=null;}
   if(process==null)return;
   try{
    if(!process.HasExited&&!process.WaitForExit(1500)){
     process.Kill();
     process.WaitForExit(1500);
    }
   }catch(InvalidOperationException){}
   finally{process.Dispose();process=null;}
  }
  public void Logout(){Stop();foreach(var path in CredentialFiles())File.Delete(path);}
  public void Dispose(){Stop();gate.Dispose();}
 }
}
