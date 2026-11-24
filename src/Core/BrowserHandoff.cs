using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Biandan {
 internal sealed class BrowserHandoff:IDisposable {
  public const string Argument="--biandan-huawei-browser";
  const string Prefix="Biandan.Huawei.";
  const string Variable="BIANDAN_HUAWEI_BROWSER_PIPE";
  readonly NamedPipeServerStream pipe;
  readonly string name;
  volatile bool disposed;

  public BrowserHandoff(Action<Uri> receive){
   name=Prefix+Guid.NewGuid().ToString("N");
   var access=new PipeSecurity();
   using(var identity=WindowsIdentity.GetCurrent())
    access.AddAccessRule(new PipeAccessRule(identity.User,PipeAccessRights.FullControl,AccessControlType.Allow));
   pipe=new NamedPipeServerStream(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,0,access);
   Listen(receive);
  }

  public void Configure(ProcessStartInfo start){
   start.EnvironmentVariables[Variable]=name;
   start.EnvironmentVariables["BROWSER"]="\""+Assembly.GetExecutingAssembly().Location+"\" "+Argument+" %s";
  }

  async void Listen(Action<Uri> receive){
   while(!disposed){
    try{
     await pipe.WaitForConnectionAsync().ConfigureAwait(false);
     using(var reader=new BinaryReader(pipe,Encoding.UTF8,true)){
      int length=reader.ReadInt32();
      if(length>0&&length<=8192){
       var bytes=reader.ReadBytes(length);
       Uri address;
       if(bytes.Length==length&&TryReadAddress(Encoding.UTF8.GetString(bytes),out address))receive(address);
      }
     }
     if(!disposed&&pipe.IsConnected)pipe.Disconnect();
    }catch(ObjectDisposedException){return;}
    catch(IOException){
     try{if(!disposed&&pipe.IsConnected)pipe.Disconnect();}
     catch(ObjectDisposedException){return;}
    }
   }
  }

  public static bool TryReadAddress(string value,out Uri address){
   return Uri.TryCreate(value,UriKind.Absolute,out address)&&address.Scheme==Uri.UriSchemeHttps&&
    address.Host=="oauth-login.cloud.huawei.com"&&address.IsDefaultPort&&address.UserInfo==""&&
    address.AbsolutePath=="/oauth2/v3/authorize"&&address.Query.Length>1&&address.Fragment=="";
  }

  public static int Send(string[] arguments){
   Uri address;
   string name=Environment.GetEnvironmentVariable(Variable)??"";
   if(arguments.Length!=2||!Regex.IsMatch(name,@"^Biandan\.Huawei\.[a-f0-9]{32}$")||
      !TryReadAddress(arguments[1],out address))return 1;
   try{
    using(var target=new NamedPipeClientStream(".",name,PipeDirection.Out)){
     target.Connect(3000);
     using(var writer=new BinaryWriter(target,Encoding.UTF8,true)){
      var bytes=Encoding.UTF8.GetBytes(address.AbsoluteUri);
      writer.Write(bytes.Length);writer.Write(bytes);writer.Flush();
     }
    }
    return 0;
   }catch(IOException){return 1;}
   catch(TimeoutException){return 1;}
   catch(UnauthorizedAccessException){return 1;}
  }

  public void Dispose(){disposed=true;pipe.Dispose();}
 }
}