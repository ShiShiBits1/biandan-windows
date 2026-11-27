using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Biandan {
 internal static class BundledBackend {
  internal const string Version="0.4.6";
  const string ResourceName="Biandan.Backend.exe";
  const string ExecutableName="biandan-mcp.exe";

  public static Stream OpenResource(string name){
   var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
   if(stream==null)throw new InvalidDataException("Missing embedded resource: "+name);
   return stream;
  }

  public static string Prepare(string stateDirectory){
   using(var embedded=OpenResource(ResourceName)){
    var hash=Fingerprint(embedded);
    var directory=Path.Combine(stateDirectory,"runtime",hash);
    var target=Path.Combine(directory,ExecutableName);
    if(Matches(target,hash))return target;

    Directory.CreateDirectory(directory);
    string temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
    try{
     embedded.Position=0;
     using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
      embedded.CopyTo(output);
      output.Flush(true);
     }
     try{
      if(File.Exists(target))File.Replace(temporary,target,null);
      else File.Move(temporary,target);
     }catch(IOException){
      if(!Matches(target,hash))throw;
     }
     return target;
    }finally{
     if(File.Exists(temporary))File.Delete(temporary);
    }
   }
  }

  static bool Matches(string file,string hash){
   if(!File.Exists(file))return false;
   using(var existing=File.OpenRead(file))return Fingerprint(existing)==hash;
  }

  static string Fingerprint(Stream stream){
   using(var sha=SHA256.Create())
    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
  }
 }
}