using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Biandan {
 public sealed partial class Settings {
  readonly object authorizationLock=new object();
  string authorizationScope,initialAuthorizationScope;
  Exception authorizationWriteError;
  internal string QueueScope;
  internal string[] QueueAnchors=new string[0];
  readonly Dictionary<string,HashSet<string>> receiptBuckets=new Dictionary<string,HashSet<string>>();
  HashSet<string> trackedChanges=new HashSet<string>();
  HashSet<string> pendingReceipts=new HashSet<string>(StringComparer.Ordinal);
  HashSet<string> storedChanges=new HashSet<string>(StringComparer.Ordinal);

  string ScopePath{get{return Path.Combine(DirectoryPath,"authorization-scope.json");}}
  string PendingPath{get{return Path.Combine(DirectoryPath,"pending-task-changes.bin");}}
  internal string InitialScope{get{lock(authorizationLock){ReadAuthorizationScope();return initialAuthorizationScope;}}}
  internal string AuthorizationScope{get{lock(authorizationLock){ReadAuthorizationScope();return authorizationScope;}}}

  void ReadAuthorizationScope(){
   if(authorizationWriteError!=null)throw new IOException("无法保存本机授权标识，修复存储权限并重新启动便单后再操作。",authorizationWriteError);
   if(authorizationScope!=null)return;
   if(File.Exists(ScopePath)){
    var data=J.Parse(File.ReadAllText(ScopePath,Encoding.UTF8));
    Guid value;
    if(!Guid.TryParseExact(data.S("initial"),"N",out value)||!Guid.TryParseExact(data.S("current"),"N",out value))
     throw new InvalidDataException("本机授权标识损坏，无法核对云端账号。");
    initialAuthorizationScope=data.S("initial");authorizationScope=data.S("current");
   }else{
    string first=Guid.NewGuid().ToString("N");
    WriteAuthorizationScope(first,first);
   }
  }

  void WriteAuthorizationScope(string initial,string current){
   AtomicWrite(ScopePath,Encoding.UTF8.GetBytes(J.Encode(J.Obj("initial",initial,"current",current))));
   initialAuthorizationScope=initial;authorizationScope=current;
  }

  internal void RenewAuthorizationScope(){
   lock(authorizationLock){
    try{ReadAuthorizationScope();WriteAuthorizationScope(initialAuthorizationScope,Guid.NewGuid().ToString("N"));}
    catch(Exception error){authorizationWriteError=error;throw;}
   }
  }

  internal static List<Dictionary<string,object>> ChangeRows(Dictionary<string,object> data,string field){
   object raw;
   if(data==null||!data.TryGetValue(field,out raw)||!(raw is IList))
    throw new InvalidDataException("待同步记录格式不正确。");
   var rows=data.Rows(field);
   if(((IList)raw).Count!=rows.Count)throw new InvalidDataException("待同步记录中含有无法识别的条目。");
   return rows;
  }

  internal static Dictionary<string,object> ReadProtected(string path){
   byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
   return J.Parse(Encoding.UTF8.GetString(plain));
  }

  internal static void WriteProtected(string path,Dictionary<string,object> data){
   var bytes=Encoding.UTF8.GetBytes(J.Encode(data));
   AtomicWrite(path,ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser));
  }

  internal List<Dictionary<string,object>> LoadTaskChanges(){
   QueueScope=AuthorizationScope;
   if(!File.Exists(PendingPath))return new List<Dictionary<string,object>>();
   var data=ReadProtected(PendingPath);
   var changes=ChangeRows(data,"changes");
   QueueScope=data.S("scope",InitialScope);
   pendingReceipts=new HashSet<string>(data.Strings("retired"),StringComparer.Ordinal);
   QueueAnchors=data.Strings("anchors").ToArray();

   bool legacy=!data.ContainsKey("scope")||changes.Any(c=>!c.ContainsKey("changeId"));
   foreach(var change in changes){
    if(legacy&&!change.ContainsKey("changeId"))change["changeId"]=Guid.NewGuid().ToString("N");
    Repository.ValidateBackupChange(change);
    if(change.B("recoveryTracked"))trackedChanges.Add(change.S("changeId"));
    if(!storedChanges.Add(change.S("changeId")))throw new InvalidDataException("待同步记录包含重复编号。");
   }
   if(legacy){
    var snapshot=LoadSnapshot(false);
    if(QueueAnchors.Length==0&&snapshot!=null&&snapshot.S("accountScope",InitialScope)==QueueScope)
     QueueAnchors=Repository.CloudAnchors(snapshot);
    SaveTaskChanges(changes);
   }
   return changes;
  }

  internal void SaveTaskChanges(List<Dictionary<string,object>> changes){StoreChanges(changes,true);}
  internal void DetachTaskChanges(){StoreChanges(new List<Dictionary<string,object>>(),false);}

  HashSet<string> ReceiptBucket(string prefix){
   HashSet<string> result;
   if(receiptBuckets.TryGetValue(prefix,out result))return result;
   string path=Path.Combine(DirectoryPath,"recovery-receipts-"+prefix+".bin");
   result=File.Exists(path)?new HashSet<string>(ReadProtected(path).Strings("ids"),StringComparer.Ordinal):new HashSet<string>(StringComparer.Ordinal);
   receiptBuckets.Add(prefix,result);return result;
  }
  internal bool AlreadyRecovered(string id){return pendingReceipts.Contains(id)||ReceiptBucket(id.Substring(0,2)).Contains(id);}

  void StoreChanges(List<Dictionary<string,object>> changes,bool retireRemoved){
   var current=new HashSet<string>(StringComparer.Ordinal);
   foreach(var change in changes){
    if(!change.ContainsKey("changeId"))change["changeId"]=Guid.NewGuid().ToString("N");
    if(!current.Add(change.S("changeId")))throw new InvalidDataException("待同步记录包含重复编号。");
   }
   // The queue and new receipts commit together. Move the preceding transaction's receipts
   // into bounded buckets before replacing that transaction, so a crash cannot reopen a finished change.
   foreach(var group in pendingReceipts.GroupBy(id=>id.Substring(0,2))){
    var ids=new HashSet<string>(ReceiptBucket(group.Key),StringComparer.Ordinal);
    ids.UnionWith(group);
    WriteProtected(Path.Combine(DirectoryPath,"recovery-receipts-"+group.Key+".bin"),J.Obj("ids",ids.ToArray()));
    receiptBuckets[group.Key]=ids;
   }
   var retired=retireRemoved?new HashSet<string>(trackedChanges.Except(current)):new HashSet<string>();
   if(changes.Count>0||retired.Count>0)
    WriteProtected(PendingPath,J.Obj("scope",QueueScope,"anchors",QueueAnchors,"changes",changes,"retired",retired.ToArray()));
   else if(File.Exists(PendingPath))File.Delete(PendingPath);
   pendingReceipts=retired;
   trackedChanges=new HashSet<string>(changes.Where(row=>row.B("recoveryTracked")).Select(row=>row.S("changeId")));
   storedChanges=current;
  }

  internal string ArchivePath(string scope){
   Guid id;
   if(!Guid.TryParseExact(scope,"N",out id))throw new InvalidDataException("本机队列缺少有效的授权标识。");
   return Path.Combine(DirectoryPath,"saved-queue-"+id.ToString("N")+".bin");
  }
 }
}
