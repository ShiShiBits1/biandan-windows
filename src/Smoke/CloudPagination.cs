using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Biandan {
 public sealed partial class App {
  sealed class PaginationService:HttpMessageHandler {
   public Func<string,Dictionary<string,object>,Dictionary<string,object>> Reply;
   protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
    var envelope=J.Parse(await request.Content.ReadAsStringAsync());
    var call=envelope.D("params");
    var data=Reply(call.S("name"),call.D("arguments"));
    var body=J.Obj("jsonrpc","2.0","id",envelope["id"],"result",J.Obj("structuredContent",data));
    return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(J.Encode(body),Encoding.UTF8,"application/json")};
   }
  }

  static Dictionary<string,object> PaginationPage(object items,int total,object next){
   return J.Obj("items",items,"total",total,"next_offset",next);
  }

  internal static async Task VerifyCloudPaginationAsync(){
   var service=new PaginationService();
   using(var client=new McpClient(new HttpClient(service){BaseAddress=new Uri("http://localhost/mcp")})){
    var rows=Enumerable.Range(1,101).Select(id=>J.Obj("task_id",id)).ToArray();
    service.Reply=(name,query)=>{
     if(name!="biandan_search_tasks"||query.I("limit")!=100||query.S("query")!="sample")
      throw new InvalidOperationException("Task paging lost the method, page size or search filter.");
     int offset=query.I("offset",-1);
     if(offset==0)return PaginationPage(rows.Take(100),101,100);
     if(offset==100)return PaginationPage(rows.Skip(100),101,null);
     throw new InvalidOperationException("Unexpected page offset.");
    };
    var args=J.Obj("query","sample");
    var all=await client.AllAsync("search_tasks",args);
    if(all.Count!=101||all.Last().S("task_id")!="101"||args.ContainsKey("offset"))
     throw new InvalidOperationException("A complete task page was lost or the search arguments were changed.");
    var empty=PaginationPage(new object[0],0,null);
    service.Reply=(name,query)=>empty;
    if((await client.AllAsync("list_occurrences")).Count!=0)throw new InvalidOperationException("Valid empty page was rejected.");

    var full=PaginationPage(rows.Take(100),101,100);
    var duplicate=PaginationPage(new[]{rows[0],rows[0]},2,null);
    foreach(var bad in new[]{
     J.Obj("items",new object[0],"next_offset",null),J.Obj("total",0,"next_offset",null),
     J.Obj("total",0,"items",new object[0]),PaginationPage("invalid",0,null),
     PaginationPage(new object[]{null},1,null),PaginationPage(new[]{J.Obj("task_id","")},1,null),
     J.Obj("items",new object[0],"total","0","next_offset",null),PaginationPage(new object[0],-1,null),
     PaginationPage(new object[0],1,null),PaginationPage(rows.Take(100),101,null),
     PaginationPage(rows.Take(100),101,0),PaginationPage(rows.Take(100),101,200),
     PaginationPage(rows.Take(100),101,"100"),PaginationPage(rows,101,null),
     PaginationPage(new object[0],0,0),duplicate
    }){
     service.Reply=(name,query)=>bad;
     await ExpectIncompletePageAsync(()=>client.AllAsync("search_tasks"));
    }
    foreach(var tail in new[]{PaginationPage(new[]{J.Obj("task_id","0001")},101,null),PaginationPage(rows.Skip(100),102,null)}){
     service.Reply=(name,query)=>query.I("offset")==0?full:tail;
     await ExpectIncompletePageAsync(()=>client.AllAsync("search_tasks"));
    }
    await VerifyPartialCloudCacheAsync(client,service);
    await VerifyCloudTaskRelationsAsync(client,service);
   }
  }

  static async Task ExpectIncompletePageAsync(Func<Task> read){
   try{await read();}
   catch(McpException){return;}
   throw new InvalidOperationException("An incomplete cloud read was accepted.");
  }

  static bool PaginationCanUseDpapi(){
   var probe=new byte[]{42};
   try{
    var encrypted=ProtectedData.Protect(probe,null,DataProtectionScope.CurrentUser);
    return ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser).SequenceEqual(probe);
   }catch(CryptographicException){return false;}
  }

  static async Task VerifyCloudTaskRelationsAsync(McpClient client,PaginationService service){
   int reads=0;
   bool missing=false;
   service.Reply=(name,query)=>{
    switch(name){
     case "biandan_check_connection":return J.Obj("connected",true);
     case "biandan_list_lists":return J.Obj("items",new[]{J.Obj("list_id","001","name","默认")});
     case "biandan_search_tasks":
      reads++;
      return reads==1?PaginationPage(new object[0],0,null):PaginationPage(new[]{J.Obj("task_id","12","title","手机新建","list_id",-1)},1,null);
     case "biandan_list_occurrences":
      return PaginationPage(new[]{J.Obj("occurrence_id","34","task_id",missing?"13":"12","status","pending")},1,null);
     case "biandan_list_focus":
      return PaginationPage(new[]{J.Obj("focus_id","1880000000123","occurrence_id","77","minutes",12.5),
       J.Obj("focus_id","1880000000456","occurrence_id","旧版关联","minutes",1.5)},2,null);
     default:throw new InvalidOperationException("Unexpected cloud call "+name);
    }
   };
   string directory=Path.Combine(Path.GetTempPath(),"Biandan-Relations-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(directory);
   try{
    var repo=new Repository(new Settings(directory),client);
    await repo.LoginAsync();
    var fresh=repo.Items().Single();
    if(reads!=2||fresh.Title!="手机新建"||fresh.ListId!="1"||repo.Lists.Single().S("list_id")!="1"||
       repo.Focus.Count!=2||repo.Focus[0].S("occurrence_id")!="77"||repo.Focus[1].S("occurrence_id")!=""||
       repo.Focus.Sum(record=>FocusDuration.Minutes(record))!=14m)
     throw new InvalidOperationException("A new task or an independent focus record was dropped between cloud reads.");
    missing=true;
    var previous=repo.LastSync;
    await ExpectIncompletePageAsync(()=>repo.SyncAsync());
    if(reads!=4||repo.LastSync!=previous||repo.Items().Single().Id!="12")
     throw new InvalidOperationException("An unmatched occurrence replaced the previous task or caused repeated retries.");
   }finally{
    foreach(string file in Directory.GetFiles(directory))File.Delete(file);
    Directory.Delete(directory);
   }
  }

  static async Task VerifyPartialCloudCacheAsync(McpClient client,PaginationService service){
   bool incomplete=false;
   service.Reply=(name,query)=>{
    long id=incomplete?2200000000000L:1100000000000L;
    switch(name){
     case "biandan_check_connection":return J.Obj("connected",true);
     case "biandan_list_lists":return J.Obj("items",new[]{J.Obj("list_id","1","name","未分类")});
     case "biandan_search_tasks":return PaginationPage(new[]{J.Obj("task_id",id,"title","阅读")},1,null);
     case "biandan_list_occurrences":return PaginationPage(new[]{J.Obj("occurrence_id",10,"task_id","0"+id.ToString())},1,null);
     case "biandan_list_focus":
      if(!incomplete)return PaginationPage(new[]{J.Obj("focus_id",1730000000123L,"occurrence_id","0010")},1,null);
      if(query.I("offset")==0)return PaginationPage(Enumerable.Range(1,100).Select(n=>J.Obj("focus_id",n.ToString())),101,100);
      return PaginationPage(new object[0],101,null);
     default:throw new InvalidOperationException("Unexpected cloud call "+name);
    }
   };
   string directory=Path.Combine(Path.GetTempPath(),"Biandan-Pagination-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(directory);
   try{
    var repo=new Repository(new Settings(directory),client);
    await repo.LoginAsync();
    var savedAt=repo.LastSync;
    incomplete=true;
    await ExpectIncompletePageAsync(()=>repo.SyncAsync());
    var views=new List<Repository>{repo};
    if(PaginationCanUseDpapi()){
     var cached=new Repository(new Settings(directory),client);
     if(!cached.OpenCache())throw new InvalidOperationException("The previous cloud snapshot could not be reopened.");
     views.Add(cached);
    }
    foreach(var view in views){
     if(!savedAt.HasValue||view.LastSync!=savedAt||view.Tasks.Single().S("task_id")!="1100000000000"||
        view.Items().Single().Id!="1100000000000"||view.Occurrences.Single().S("task_id")!="1100000000000"||
        view.Occurrences.Single().S("occurrence_id")!="10"||view.Focus.Single().S("focus_id")!="1730000000123"||
        view.Focus.Single().S("occurrence_id")!="10")
      throw new InvalidOperationException("A partial cloud sync replaced confirmed data or advanced the sync time.");
    }
   }finally{
    foreach(string file in Directory.GetFiles(directory))File.Delete(file);
    Directory.Delete(directory);
   }
  }
 }
}
