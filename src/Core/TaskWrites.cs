using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Biandan {
 public sealed partial class Repository {
  static readonly TimeSpan TaskSyncDelay=TimeSpan.FromSeconds(15);
  List<Dictionary<string,object>> pendingTaskChanges=new List<Dictionary<string,object>>();
  Exception pendingLoadError;
  DateTime pendingChangedUtc=DateTime.UtcNow;

  public int PendingTaskCount{get{return !Demo&&pendingTaskChanges.Count>0&&Settings.QueueScope==Settings.AuthorizationScope?pendingTaskChanges.Count:0;}}
  internal bool PendingTaskSyncDue(DateTime utcNow){
   return utcNow>=pendingChangedUtc+TaskSyncDelay&&HasReadyTaskWrites;
  }

  void LoadPendingTaskChanges(){
   try{pendingTaskChanges=Settings.LoadTaskChanges();}
   catch(IOException error){pendingLoadError=error;}
   catch(InvalidDataException error){pendingLoadError=error;}
   catch(McpException error){pendingLoadError=error;}
   catch(FormatException error){pendingLoadError=error;}
   catch(InvalidCastException error){pendingLoadError=error;}
   catch(OverflowException error){pendingLoadError=error;}
   catch(UnauthorizedAccessException error){pendingLoadError=error;}
   catch(CryptographicException error){pendingLoadError=error;}
   catch(ArgumentException error){pendingLoadError=error;}
   catch(InvalidOperationException error){pendingLoadError=error;}
  }

  void CheckTaskChanges(){
   if(pendingLoadError!=null)throw new McpException("无法读取本机待同步记录。请保留数据文件并检查 Windows 用户和磁盘权限。");
  }

  static bool IsDeferredTaskWrite(string method,Dictionary<string,object> args){
   if(method=="delete_task")return args.S("scope")!="future_pending";
   if(args.B("preview"))return false;
   return method=="create_task"||method=="update_task"||method=="move_task"||method=="complete_occurrence"||
    (method=="change_progress"&&args.S("action")=="reopen");
  }

  public Task CreateQuickAsync(string title,string operationId,string date,string listId,bool important){
   var args=J.Obj("title",title,"operation_id",operationId,"list_id",listId,"important",important);
   if(date!=null)args["start_date"]=date;
   return CallAsync("create_task",args);
  }

  internal Task ScheduleUndatedAsync(string id,DateTime date,int minute){
   if(!CanWrite)throw new McpException("请先登录便单，再安排待办。");
   var item=Items().FirstOrDefault(candidate=>candidate.Id==id);
   if(item==null||!item.Undated||item.Completed||item.Abandoned||item.Repeating||item.Syncing)
    throw new McpException("这项待办无法从待安排列表改期。");
   if(minute<0||minute>1425)throw new ArgumentOutOfRangeException("minute");
   return RescheduleItemAsync(item.OccurrenceId,date,minute);
  }

  static string ScheduleClock(int minute){
   return TimeSpan.FromMinutes(minute).ToString(@"hh\:mm",CultureInfo.InvariantCulture);
  }

  public Task SetPriorityAsync(string id,bool important,bool urgent){
   return CallAsync("update_task",J.Obj("task_id",id,"important",important,"urgent",urgent));
  }

  Dictionary<string,object> QueueTaskChange(string method,Dictionary<string,object> source,BoardStage? boardStage=null){
   EnsureQueueAccount();
   if(!CanWrite)throw new McpException("请先登录便单，再修改待办。");
   var args=J.Clone(source);
   string taskId=args.S("task_id"),occurrenceId=args.S("occurrence_id");
   Dictionary<string,object> result;
   if(method=="create_task"){
    taskId=args.S("operation_id",J.NewId());
    args["operation_id"]=taskId;
    result=J.Obj("task_id",taskId,"operation_id",taskId,"occurrence_id",taskId);
    if(Items().Any(item=>item.Id==taskId))return result;
    CreateLocalTask(args,true,new List<Dictionary<string,object>>());
   }else if(method=="delete_task"){
    result=LocalDeletion(args);
    if(args.B("preview"))return result;
    args.Remove("preview");
   }else{
    bool progress=method=="complete_occurrence"||method=="change_progress";
    var item=Items().FirstOrDefault(t=>progress?t.OccurrenceId==occurrenceId:t.Id==taskId);
    if(item==null)throw new McpException("找不到这项待办。");
    taskId=item.Id;
    if(method=="complete_occurrence"&&!args.ContainsKey("completed_at"))
     args["completed_at"]=DateTime.Now.ToString("yyyy-MM-ddTHH:mm",CultureInfo.InvariantCulture);
    if(method=="update_task"&&args.Where(p=>p.Key!="task_id").All(p=>Object.Equals(item.Task.S(p.Key),Convert.ToString(p.Value))))
     return item.Task;
    result=progress?J.Obj("occurrence",item.Occurrence):item.Task;
   }

   var changes=new List<Dictionary<string,object>>(pendingTaskChanges);
   if(method=="delete_task"&&result.B("deletes_task_definition")&&
      changes.Any(c=>c.S("taskId")==taskId&&c.S("method")=="create_task")&&
      !changes.Any(c=>c.S("taskId")==taskId&&c.B("started"))){
    changes.RemoveAll(c=>c.S("taskId")==taskId);
   }else{
    var last=changes.LastOrDefault();
    if(method=="update_task"&&last!=null&&!last.B("started")&&last.S("method")==method&&last.S("taskId")==taskId){
     var combined=J.Clone(last.D("args"));
     foreach(var kv in args)combined[kv.Key]=kv.Value;
     args=combined;
     changes.RemoveAt(changes.Count-1);
    }
    var title=args.S("title");
    if(string.IsNullOrWhiteSpace(title)){
     var item=Items().FirstOrDefault(t=>t.Id==taskId);
     if(item!=null)title=item.Title;
    }
    var entry=J.Obj("method",method,"taskId",taskId,"args",args,"title",title,"queuedAt",DateTime.UtcNow.ToString("o"));
    if(boardStage.HasValue)entry["boardStage"]=boardStage==BoardStage.InProgress;
    changes.Add(entry);
   }
   SaveTaskChanges(changes);
   pendingChangedUtc=DateTime.UtcNow;
   Publish();
   return result;
  }

  Dictionary<string,object> LocalDeletion(Dictionary<string,object> args){
   string taskId=args.S("task_id");
   var items=Items().Where(item=>item.Id==taskId).ToList();
   if(items.Count==0)throw new McpException("找不到这项待办。");
   bool one=args.S("scope")=="occurrence";
   var affected=one?items.Where(item=>item.OccurrenceId==args.S("occurrence_id")).ToList():items;
   if(affected.Count==0)throw new McpException("找不到这次执行记录。");
   return J.Obj("task_id",taskId,"occurrence_ids",affected.Select(item=>item.OccurrenceId).ToArray(),
    "deletes_task_definition",!one||!items[0].Repeating,"preview",args.B("preview"),"deleted",!args.B("preview"));
  }

  bool HasPendingDependency(string method,Dictionary<string,object> args){
   if(pendingTaskChanges.Count==0)return false;
   if(method=="delete_list"){
    string listId=args.S("list_id");
    var affected=new HashSet<string>(Tasks.Where(t=>t.S("list_id","1")==listId).Select(t=>t.S("task_id")));
    return pendingTaskChanges.Any(c=>affected.Contains(c.S("taskId"))||
     c.D("args").S("list_id",c.S("method")=="create_task"?"1":"")==listId);
   }
   string taskId=args.S("task_id"),occurrenceId=args.S("occurrence_id");
   if(taskId==""&&occurrenceId!=""){
    var item=Items().FirstOrDefault(t=>t.OccurrenceId==occurrenceId);
    taskId=item!=null?item.Id:Occurrences.FirstOrDefault(o=>o.S("occurrence_id")==occurrenceId).S("task_id");
    if(taskId=="")taskId=pendingTaskChanges.FirstOrDefault(c=>c.D("args").S("occurrence_id")==occurrenceId).S("taskId");
   }
   return taskId!=""&&pendingTaskChanges.Any(change=>change.S("taskId")==taskId);
  }

  void SaveTaskChanges(List<Dictionary<string,object>> changes){
   Settings.SaveTaskChanges(changes);
   pendingTaskChanges=changes;
  }

  public int FailedTaskWriteCount{get{return Demo?0:pendingTaskChanges.Count(c=>c.S("lastError")!="");}}

  public bool HasReadyTaskWrites{
   get{
    if(PendingTaskCount==0)return false;
    var blocked=new HashSet<string>();
    foreach(var entry in pendingTaskChanges){
     if(entry.B("sent"))return true;
     if(entry.S("lastError")!="")blocked.Add(entry.S("taskId"));
     else if(!blocked.Contains(entry.S("taskId")))return true;
    }
    return false;
   }
  }

  static string OperationName(string method){
   switch(method){
    case "create_task":return "创建待办";
    case "delete_task":return "删除待办";
    case "update_task":return "更新待办";
    case "move_task":return "移动清单";
    case "complete_occurrence":return "完成待办";
    case "change_progress":return "重新打开待办";
    case "reschedule_task":return "调整日期";
    default:return method;
   }
  }

  public List<Dictionary<string,object>> SyncQueueDetails(){
   var rows=new List<Dictionary<string,object>>();
   if(Demo)return rows;
   EnsureQueueAccount();
   var predecessors=new HashSet<string>();
   var failedTasks=new HashSet<string>();
   var titles=new Dictionary<string,string>();
   foreach(var task in Tasks)titles[task.S("task_id")]=task.S("title");
   foreach(var entry in pendingTaskChanges){
    string taskId=entry.S("taskId"),title=entry.S("title",entry.D("args").S("title"));
    if(string.IsNullOrWhiteSpace(title)&&!titles.TryGetValue(taskId,out title))title="待办 "+taskId;
    titles[taskId]=title;
    bool sent=entry.B("sent"),failed=entry.S("lastError")!="",blocked=failedTasks.Contains(taskId);
    string status=sent?"已发送，等待读回":blocked?"等待同一待办的改动重试":failed?"同步失败":"等待空闲同步";
    rows.Add(J.Obj("id",entry.S("changeId"),"title",title,"operation",OperationName(entry.S("method")),
     "status",status,"error",entry.S("lastError"),"queuedAt",entry.S("queuedAt"),
     "canRetry",failed&&!sent&&!predecessors.Contains(taskId)));
    if(!sent)predecessors.Add(taskId);
    if(failed&&!sent)failedTasks.Add(taskId);
   }
   return rows;
  }

  // MCP returns task rejections and shared service errors through the same tools/call envelope.
  static bool IsTaskWriteRejection(McpException error){
   if(error.Details==null||error.Details.ContainsKey("action")&&!error.Details.B("may_have_written"))return false;
   int status=error.Details.I("status");
   if(status!=0)return status==400||status==404||status==409||status==410||status==412||status==422;
   string message=error.Message;
   string[] serviceErrors={"could not reach","timed out","temporarily unavailable","service returned","service is unavailable",
    "access token","authorization","credential","oauth","renewal service","unexpected cloud record or tool data format"};
   return !serviceErrors.Any(fragment=>message.IndexOf(fragment,StringComparison.OrdinalIgnoreCase)>=0);
  }

  async Task<bool> SendTaskWriteAsync(Dictionary<string,object> entry){
   EnsureQueueAccount();
   if(!entry.B("started")){
    entry["started"]=true;
    try{Settings.SaveTaskChanges(pendingTaskChanges);}
    catch{entry.Remove("started");throw;}
   }
   try{await Client.CallAsync(entry.S("method"),entry.D("args"));}
   catch(McpException error){
    if(!IsTaskWriteRejection(error))throw;
    entry["lastError"]=string.IsNullOrWhiteSpace(error.Message)?"云端未接受这项改动":error.Message;
    Settings.SaveTaskChanges(pendingTaskChanges);
    return false;
   }
   entry["sent"]=true;
   entry.Remove("lastError");
   Settings.SaveTaskChanges(pendingTaskChanges);
   return true;
  }

  async Task SendPendingTaskChangesAsync(){
   CheckTaskChanges();
   var blocked=new HashSet<string>();
   foreach(var entry in pendingTaskChanges.Where(c=>!c.B("sent")).ToArray()){
    if(!pendingTaskChanges.Contains(entry))continue;
    string id=entry.S("taskId");
    if(blocked.Contains(id))continue;
    if(entry.S("lastError")!=""||!await SendTaskWriteAsync(entry))blocked.Add(id);
   }
  }

  public async Task RetryTaskWriteAsync(string id){
   await operations.WaitAsync();
   try{
    if(Demo)return;
    EnsureQueueAccount();
    int index=pendingTaskChanges.FindIndex(c=>c.S("changeId")==id);
    if(index<0)return;
    var entry=pendingTaskChanges[index];
    if(entry.B("sent")||entry.S("lastError")=="")return;
    if(pendingTaskChanges.Take(index).Any(c=>!c.B("sent")&&c.S("taskId")==entry.S("taskId")))
     throw new McpException("请先重试这项待办更早的改动。");
    if(!Client.Running)await Client.StartAsync();
    if(await SendTaskWriteAsync(entry))await SyncCoreAsync(false);
   }finally{operations.Release();Publish();}
  }

  void ApplyPendingTaskChanges(Dictionary<string,Dictionary<string,object>> tasks,
    Dictionary<string,Dictionary<string,object>> occurrences,HashSet<string> temporarySeries){
   Dictionary<string,List<string>> occurrenceIdsByTask=null;
   foreach(var change in pendingTaskChanges){
    string id=change.S("taskId"),method=change.S("method");
    var args=change.D("args");
    if(method=="create_task"){
     if(tasks.ContainsKey(id))continue;
     var added=new List<Dictionary<string,object>>();
     var task=CreateLocalTask(args,true,added);
     tasks[id]=task;
     if(task.I("repeat_type")!=0)temporarySeries.Add(id);
     foreach(var occurrence in added)occurrences[occurrence.S("occurrence_id")]=occurrence;
     if(occurrenceIdsByTask!=null){
      List<string> existingIds;
      if(!occurrenceIdsByTask.TryGetValue(id,out existingIds))occurrenceIdsByTask[id]=existingIds=new List<string>();
      existingIds.AddRange(added.Select(row=>row.S("occurrence_id")));
     }
    }else if(method=="delete_task"){
     Dictionary<string,object> current;
     if(args.S("scope")!="occurrence"||(tasks.TryGetValue(id,out current)&&current.I("repeat_type")==0))
      tasks.Remove(id);
     else occurrences.Remove(args.S("occurrence_id"));
    }else if(method=="complete_occurrence"||method=="change_progress"){
     Dictionary<string,object> occurrence,task;
     string occurrenceId=args.S("occurrence_id");
     if(!occurrences.TryGetValue(occurrenceId,out occurrence)||!tasks.TryGetValue(id,out task))continue;
     bool done=method=="complete_occurrence";
     var edited=new Dictionary<string,object>(occurrence);
     edited["status"]=done?"completed":"pending";
     edited["completed_count"]=done?task.I("target_count",1):0;
     edited["completed_at"]=done?(object)args.S("completed_at"):null;
     edited["completed_subtask_indices"]=done?Enumerable.Range(0,task.Strings("subtasks").Count).ToArray():new int[0];
     occurrences[occurrenceId]=edited;
    }else{
     Dictionary<string,object> task;
     if(!tasks.TryGetValue(id,out task))continue;
     var edited=new Dictionary<string,object>(task);
     foreach(var pair in args)if(pair.Key!="task_id")edited[pair.Key]=pair.Value;
     if(method=="reschedule_task"){
      edited["undated"]=false;
      if(occurrenceIdsByTask==null)
       occurrenceIdsByTask=occurrences.Values.GroupBy(row=>row.S("task_id"))
        .ToDictionary(group=>group.Key,group=>group.Select(row=>row.S("occurrence_id")).ToList());
      List<string> scheduledIds;
      if(occurrenceIdsByTask.TryGetValue(id,out scheduledIds))foreach(string occurrenceId in scheduledIds){
       Dictionary<string,object> occurrence;
       if(!occurrences.TryGetValue(occurrenceId,out occurrence)||occurrence.S("task_id")!=id)continue;
       var scheduled=new Dictionary<string,object>(occurrence);
       foreach(var field in new[]{"start_date","end_date","start_time","end_time"})scheduled[field]=args[field];
       occurrences[scheduled.S("occurrence_id")]=scheduled;
      }
     }
     tasks[id]=edited;
    }
   }
  }

  bool? PendingBoardStage(string id){
   if(Demo||Settings.QueueScope!=Settings.AuthorizationScope)return null;
   var change=pendingTaskChanges.LastOrDefault(c=>c.ContainsKey("boardStage")&&c.D("args").S("occurrence_id")==id);
   return change==null?(bool?)null:change.B("boardStage");
  }

  bool ReplacePendingBoardStage(string id,BoardStage destination){
   if(Demo)return false;
   EnsureQueueAccount();
   var change=pendingTaskChanges.LastOrDefault(c=>c.ContainsKey("boardStage")&&c.D("args").S("occurrence_id")==id);
   if(change==null)return false;
   bool previous=change.B("boardStage");
   change["boardStage"]=destination==BoardStage.InProgress;
   try{Settings.SaveTaskChanges(pendingTaskChanges);}
   catch{change["boardStage"]=previous;throw;}
   Publish();
   return true;
  }

  static void Upsert(List<Dictionary<string,object>> records,string key,string id,Dictionary<string,object> value){
   int index=records.FindIndex(record=>record.S(key)==id);
   if(index<0)records.Add(value);
   else records[index]=value;
  }

  bool ApplyUpdatedTask(string id,Dictionary<string,object> result){
   if(id==""||result.S("task_id")!=id||result.S("title")=="")return false;
   Upsert(Tasks,"task_id",id,result);
   return true;
  }

  async Task SaveCloudSnapshotAsync(bool requireSaved=false){
   var snapshot=J.Obj("accountScope",viewScope,"tasks",Tasks.ToArray(),"occurrences",Occurrences.ToArray(),"lists",Lists.ToArray(),
    "focus",Focus.ToArray(),"boardStages",new Dictionary<string,object>(boardStages),
    "lastSync",LastSync.HasValue?LastSync.Value.ToString("o"):null);
   try{await Task.Run(()=>Settings.SaveSnapshot(snapshot,false));}
   catch(IOException){if(requireSaved)throw;}
   catch(UnauthorizedAccessException){if(requireSaved)throw;}
   catch(CryptographicException){if(requireSaved)throw;}
   catch(InvalidOperationException){if(requireSaved)throw;}
  }
 }
}
