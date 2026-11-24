using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace Biandan {
 public sealed partial class Settings {
  public readonly string DirectoryPath; public Dictionary<string,object> Values;
  public Settings(string directory) {DirectoryPath=directory;Directory.CreateDirectory(directory);try{Values=J.Parse(File.ReadAllText(Path.Combine(directory,"settings.json"),Encoding.UTF8));}catch{Values=J.Obj("minimizeToTray",true,"notifications",true,"widgetOpacity",98,"pinned",false);}}
  internal static readonly int[] SyncIntervals={1,3,5,10,15};
  public int SyncIntervalMinutes{
   get{
    int value=Values.I("syncIntervalMinutes",3);
    return Array.IndexOf(SyncIntervals,value)>=0?value:3;
   }
  }
  public int WidgetTransparency{get{return Math.Max(0,Math.Min(100,Values.I("widgetTransparency",100-Values.I("widgetOpacity",98))));}}
  public bool GetBool(string key,bool fallback=false){return Values.ContainsKey(key)?Values.B(key):fallback;}
  public void Set(string key,object value){Values[key]=value;Save();}
  public void Save(){AtomicWrite(Path.Combine(DirectoryPath,"settings.json"),Encoding.UTF8.GetBytes(J.Encode(Values)));}
  public static void AtomicWrite(string path,byte[] bytes){var tmp=path+".tmp";File.WriteAllBytes(tmp,bytes);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
  public void SaveSnapshot(Dictionary<string,object> snapshot,bool demo){var bytes=Encoding.UTF8.GetBytes(J.Encode(snapshot));if(!demo)bytes=ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);AtomicWrite(Path.Combine(DirectoryPath,demo?"demo.json":"snapshot.bin"),bytes);}
  public Dictionary<string,object> LoadSnapshot(bool demo){try{var bytes=File.ReadAllBytes(Path.Combine(DirectoryPath,demo?"demo.json":"snapshot.bin"));if(!demo)bytes=ProtectedData.Unprotect(bytes,null,DataProtectionScope.CurrentUser);return J.Parse(Encoding.UTF8.GetString(bytes));}catch{return null;}}
  public void ClearSnapshot(){var p=Path.Combine(DirectoryPath,"snapshot.bin");if(File.Exists(p))File.Delete(p);}
 }
}
