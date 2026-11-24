using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Biandan {
 public sealed partial class Repository {
  string viewScope;
  static readonly Dictionary<string,string> backupArguments=new Dictionary<string,string>{
   {"create_task","title remark list_id important urgent start_date end_date start_time end_time subtasks target_count reminder_date reminder_time operation_id repeat_rule habit week_start china_makeup location"},
   {"update_task","task_id title remark important urgent collected"},{"move_task","task_id list_id"},
   {"reschedule_task","task_id start_date end_date start_time end_time"},
   {"complete_occurrence","occurrence_id completed_at"},{"change_progress","occurrence_id action"},
   {"delete_task","task_id scope occurrence_id"}
  };

  static string Digest(string value){
   using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();
  }
  internal static string[] CloudAnchors(Dictionary<string,object> snapshot,int limit=int.MaxValue){
   var anchors=new HashSet<string>(StringComparer.Ordinal);
   foreach(var pair in new[]{new[]{"tasks","task_id"},new[]{"lists","list_id"}}){
    foreach(string id in snapshot.Rows(pair[0]).Select(row=>row.S(pair[1]))){
     long number;
     if(BusinessId(id)&&long.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out number)&&number>=1000000000000L)
      anchors.Add(Digest(pair[1]+"/"+id));
     if(anchors.Count>=limit)return anchors.ToArray();
    }
   }
   return anchors.ToArray();
  }
  string[] BackupAnchors(){
   return Settings.QueueAnchors.Concat(CloudAnchors(Snapshot(),128)).Distinct(StringComparer.Ordinal).Take(128).ToArray();
  }
  internal bool HasBackupAccountRecords{get{return !Demo&&BackupAnchors().Length>0;}}
  static bool BusinessId(string value){
   long id;
   return value.Length>0&&value.Length<=16&&value[0]!='0'&&long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out id)&&id>0&&id<=9007199254740991L;
  }
  internal static void ValidateBackupChange(Dictionary<string,object> row){
   Guid id;string allowed;
   var args=row.D("args");
   string method=row.S("method"),task=row.S("taskId");
   if(!Guid.TryParseExact(row.S("changeId"),"N",out id)||id.ToString("N")!=row.S("changeId")||!BusinessId(task)||args==null||
      !backupArguments.TryGetValue(method,out allowed)||args.Keys.Except(allowed.Split(' ')).Any())
    throw new InvalidDataException("待同步记录包含无效编号、未知操作或参数。");
   foreach(var key in new[]{"task_id","occurrence_id","operation_id","list_id"})
    if(args.ContainsKey(key)&&(!(args[key] is string)||!BusinessId(args.S(key))))throw new InvalidDataException("待同步记录中的业务编号无效。");
   foreach(var key in new[]{"started","sent","boardStage","recoveryTracked"})
    if(row.ContainsKey(key)&&!(row[key] is bool))throw new InvalidDataException("待同步记录状态不正确。");
   foreach(var key in new[]{"important","urgent","collected","habit","china_makeup"})
    if(args.ContainsKey(key)&&!(args[key] is bool))throw new InvalidDataException("待同步记录中的开关值不正确。");
   foreach(var key in new[]{"title","remark","start_time","end_time","completed_at","action","scope"})
    if(args.ContainsKey(key)&&!(args[key] is string))throw new InvalidDataException("待同步记录中的文字格式不正确。");
   foreach(var key in new[]{"start_date","end_date","reminder_date"}){
    DateTime date;
    if(args.ContainsKey(key)&&!DateTime.TryParseExact(args.S(key),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))
     throw new InvalidDataException("待同步记录中的日期无效。");
   }
   bool progress=method=="change_progress"||method=="complete_occurrence";
   if((method=="create_task"?(args.S("operation_id")!=task||string.IsNullOrWhiteSpace(args.S("title"))):
      progress?!BusinessId(args.S("occurrence_id")):args.S("task_id")!=task)||
      (method=="change_progress"&&args.S("action")!="reopen")||
      (method=="move_task"&&!BusinessId(args.S("list_id")))||
      (method=="reschedule_task"&&new[]{"start_date","end_date","start_time","end_time"}.Any(key=>!args.ContainsKey(key)))||
      (method=="delete_task"&&(!new[]{"","series","occurrence"}.Contains(args.S("scope"))||
       (args.S("scope")=="occurrence"&&!BusinessId(args.S("occurrence_id")))))||
      (row.B("sent")&&!row.B("started")))
    throw new InvalidDataException("待同步记录缺少有效的目标或参数。");
   if(method=="create_task"){
    if(args.ContainsKey("location")&&args.D("location")==null)throw new InvalidDataException("待同步记录的地点无效。");
    if(args.ContainsKey("repeat_rule")&&args.D("repeat_rule")==null)throw new InvalidDataException("待同步记录的循环规则无效。");
    if(args.ContainsKey("subtasks")&&(!(args["subtasks"] is IList)||((IList)args["subtasks"]).Cast<object>().Any(value=>!(value is string))))
     throw new InvalidDataException("待同步记录的子任务无效。");
    CreateLocalTask(args,true,new List<Dictionary<string,object>>());
   }
  }

  bool PrepareAuthorization(){
   // The browser handoff can arrive on a pipe or process callback thread. A failed
   // scope rotation blocks the handoff; the saved error is surfaced on the next UI operation.
   try{Settings.RenewAuthorizationScope();return true;}
   catch(Exception){return false;}
  }

  void EnsureQueueAccount(){
   CheckTaskChanges();
   string scope=Settings.AuthorizationScope;
   if(Settings.QueueScope!=scope||(viewScope!=null&&viewScope!=scope))
    throw new McpException("账号授权已更新，请完成登录并刷新云端数据，再处理待同步改动。");
  }
  Dictionary<string,object> QueuePayload(string scope,string[] anchors,IList<Dictionary<string,object>> changes){
   return J.Obj("scope",scope,"anchors",anchors,"changes",changes);
  }
  internal bool InvalidatePreviousAccount(){
   if(Demo||viewScope==null||viewScope==Settings.AuthorizationScope)return false;
   cachedCloudAccount=false;Connected=false;Tasks.Clear();Occurrences.Clear();Lists.Clear();Focus.Clear();
   boardStages=J.Obj();LastSync=null;viewScope=null;
   Publish();return true;
  }
  void PrepareCloudSession(string scope){
   CheckTaskChanges();
   if(scope!=Settings.AuthorizationScope)throw new McpException("账号授权已更新，请重新同步。");
   if(Settings.QueueScope!=scope){
    if(pendingTaskChanges.Count>0){
     var previous=QueuePayload(Settings.QueueScope,Settings.QueueAnchors,pendingTaskChanges);
     Settings.WriteProtected(Settings.ArchivePath(Settings.QueueScope),previous);
    }
    Settings.DetachTaskChanges();pendingTaskChanges.Clear();
    Settings.QueueScope=scope;Settings.QueueAnchors=new string[0];
   }
   InvalidatePreviousAccount();
   viewScope=scope;
  }

  bool MatchesQueue(Dictionary<string,object> payload,HashSet<string> anchors){
   if(payload.S("scope")==Settings.AuthorizationScope)return true;
   return payload.Strings("anchors").Any(anchors.Contains);
  }
  void RestoreMatchingArchives(){
   string[] archives=Directory.GetFiles(Settings.DirectoryPath,"saved-queue-*.bin");
   var anchors=CloudAnchors(Snapshot(),archives.Length==0?128:int.MaxValue);
   Settings.QueueAnchors=anchors.Take(128).ToArray();
   var matches=new HashSet<string>(anchors,StringComparer.Ordinal);
   foreach(string path in archives){
    try{
     var data=Settings.ReadProtected(path);
     if(!MatchesQueue(data,matches))continue;
     MergeQueue(data);
     File.Delete(path);
    }catch(IOException){}catch(UnauthorizedAccessException){}catch(CryptographicException){}
     catch(InvalidDataException){}catch(ArgumentException){}catch(InvalidOperationException){}
     catch(McpException){}catch(FormatException){}catch(InvalidCastException){}catch(OverflowException){}
   }
  }

  int MergeQueue(Dictionary<string,object> payload){
   var rows=Settings.ChangeRows(payload,"changes");
   var existing=new HashSet<string>(pendingTaskChanges.Select(row=>row.S("changeId")),StringComparer.Ordinal);

   var locals=pendingTaskChanges.ToLookup(row=>row.S("taskId"));
   var before=new Dictionary<Dictionary<string,object>,List<Dictionary<string,object>>>();
   var tail=new List<Dictionary<string,object>>();
   foreach(var row in rows){
    ValidateBackupChange(row);
    string id=row.S("changeId");
    if(!existing.Add(id)||Settings.AlreadyRecovered(id))continue;
    row["recoveryTracked"]=true;
    // Preserve the backup order while keeping newer local work last on the same task.
    DateTime incoming;
    bool dated=DateTime.TryParse(row.S("queuedAt"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out incoming);
    var next=locals[row.S("taskId")].FirstOrDefault(local=>{
     DateTime localTime;
     return !dated||!DateTime.TryParse(local.S("queuedAt"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out localTime)||localTime.ToUniversalTime()>=incoming.ToUniversalTime();
    });
    if(next==null)tail.Add(row);
    else{
     List<Dictionary<string,object>> preceding;
     if(!before.TryGetValue(next,out preceding))before[next]=preceding=new List<Dictionary<string,object>>();
     preceding.Add(row);
    }
   }
   var merged=new List<Dictionary<string,object>>();
   foreach(var local in pendingTaskChanges){
    List<Dictionary<string,object>> preceding;
    if(before.TryGetValue(local,out preceding))merged.AddRange(preceding);
    merged.Add(local);
   }
   merged.AddRange(tail);
   int added=merged.Count-pendingTaskChanges.Count;
   if(added>0){SaveTaskChanges(merged);pendingChangedUtc=DateTime.UtcNow;}
   return added;
  }

  void WriteBackup(string path,bool queueOnly){
   if(!Demo)EnsureQueueAccount();
   if(queueOnly&&Demo)throw new McpException("体验模式没有云端待同步队列。");
   if(!Demo){
    var untracked=pendingTaskChanges.Where(row=>!row.B("recoveryTracked")).ToArray();
    if(untracked.Length>0){
     foreach(var row in untracked)row["recoveryTracked"]=true;
     try{Settings.SaveTaskChanges(pendingTaskChanges);}
     catch{foreach(var row in untracked)row.Remove("recoveryTracked");throw;}
    }
   }
   var data=queueOnly?J.Obj():Snapshot();
   data["format"]=queueOnly?"biandan-queue":"biandan-backup";
   data["version"]=2;data["mode"]=Demo?"demo":"cloud";data["exportedAt"]=DateTime.UtcNow.ToString("o");
   data["pendingQueue"]=Demo?QueuePayload("",new string[0],new List<Dictionary<string,object>>()):QueuePayload(Settings.AuthorizationScope,BackupAnchors(),pendingTaskChanges);
   data["sha256"]=Digest(J.Encode(data));
   string text=J.Encode(data);
   var bytes=Encoding.UTF8.GetBytes(text);
   // Use a unique sibling so a failed export cannot damage an existing backup or a same-named temporary file.
   string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllBytes(temp,bytes);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
   finally{if(File.Exists(temp))File.Delete(temp);}
  }

  internal async Task ExportBackupAsync(string path,bool queueOnly){
   await operations.WaitAsync();
   try{WriteBackup(path,queueOnly);}finally{operations.Release();}
  }
  internal async Task<int> RestoreQueueAsync(string path){
   await operations.WaitAsync();
   try{
    if(Demo||!CanWrite)throw new McpException("请先进入要恢复的便单云端账号。");
    EnsureQueueAccount();
    if(new FileInfo(path).Length>4L*32*1024*1024)throw new InvalidDataException("备份文件过大。");
    var data=J.Parse(File.ReadAllText(path,new UTF8Encoding(false,true)));
    if(data==null||data.I("version")!=2||!new[]{"biandan-queue","biandan-backup"}.Contains(data.S("format")))
     throw new InvalidDataException("请选择含待同步队列的便单备份。旧版备份不包含待同步改动。");
    string checksum=data.S("sha256");
    data.Remove("sha256");
    if(checksum!=Digest(J.Encode(data)))throw new InvalidDataException("文件校验失败，请选择一份完整的备份。");
    var queue=data.D("pendingQueue");
    Guid scope;
    if(queue==null||data.S("mode")!="cloud"||!Guid.TryParseExact(queue.S("scope"),"N",out scope))
     throw new InvalidDataException("此文件没有可恢复的云端账号队列。");
    if(!MatchesQueue(queue,new HashSet<string>(CloudAnchors(Snapshot()),StringComparer.Ordinal)))
     throw new McpException("无法确认这份备份属于当前账号。请登录原账号并刷新；跨授权恢复需要仍存在的原云端记录。");
    int added=MergeQueue(queue);
    if(added>0)Publish();
    return added;
   }finally{operations.Release();}
  }
 }
}
