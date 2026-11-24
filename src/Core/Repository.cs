using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
namespace Biandan {
 public enum BoardStage { Pending, InProgress, Completed }
 public sealed partial class Repository {
  public readonly Settings Settings;public readonly McpClient Client;
  readonly SemaphoreSlim operations=new SemaphoreSlim(1,1);
  Dictionary<string,object> boardStages=J.Obj();
  public bool Demo{get;private set;} public bool Connected{get;private set;} public bool Syncing{get;private set;}
  public DateTime? LastSync;public event Action Changed;
  public List<Dictionary<string,object>> Tasks=new List<Dictionary<string,object>>(),Occurrences=new List<Dictionary<string,object>>(),Lists=new List<Dictionary<string,object>>(),Focus=new List<Dictionary<string,object>>();
  public Repository(Settings settings,McpClient client){Settings=settings;Client=client;LoadPendingTaskChanges();Client.PrepareAuthorization=PrepareAuthorization;}
  bool cachedCloudAccount;
  public bool CanWrite{get{return Demo||((Connected||cachedCloudAccount)&&viewScope==Settings.AuthorizationScope);}}
  List<TaskItem> viewItems;
  public void Publish(){viewItems=null;if(Changed!=null)Changed();}
  Dictionary<string,object> Snapshot(){return J.Obj("accountScope",Demo?null:viewScope,"tasks",Tasks,"occurrences",Occurrences,"lists",Lists,"focus",Focus,"boardStages",boardStages,"lastSync",LastSync.HasValue?LastSync.Value.ToString("o"):null);}
  void Load(Dictionary<string,object> data){Tasks=data.Rows("tasks");Occurrences=data.Rows("occurrences");Lists=data.Rows("lists");Focus=data.Rows("focus");boardStages=data.D("boardStages")??J.Obj();DateTime d;LastSync=DateTime.TryParse(data.S("lastSync"),out d)?(DateTime?)d:null;}
  public void OpenDemo(){Client.Stop();Demo=true;Connected=false;var saved=Settings.LoadSnapshot(true);if(saved==null){boardStages=J.Obj();LastSync=null;SeedDemo();}else Load(saved);Settings.Set("lastMode","demo");Persist();Publish();}
  public bool OpenCache(){
   Demo=false;Connected=false;cachedCloudAccount=false;
   var saved=Settings.LoadSnapshot(false);
   if(saved==null||saved.S("accountScope",Settings.InitialScope)!=Settings.AuthorizationScope){
    viewScope=null;Tasks.Clear();Occurrences.Clear();Lists.Clear();Focus.Clear();boardStages=J.Obj();LastSync=null;Publish();
    return false;
   }
   viewScope=Settings.AuthorizationScope;Load(saved);cachedCloudAccount=true;
   if(Settings.QueueScope==viewScope&&Settings.QueueAnchors.Length==0)Settings.QueueAnchors=CloudAnchors(saved,128);
   Publish();return true;
  }
  public async Task LeaveAsync(string backupPath=null){
   await operations.WaitAsync();
   try{
    CheckTaskChanges();
    if(pendingTaskChanges.Count>0){
     if(string.IsNullOrEmpty(backupPath))throw new McpException("本机还有待同步改动，请先同步或导出队列再退出。");
     WriteBackup(backupPath,true);
    }
    Settings.DetachTaskChanges();pendingTaskChanges.Clear();Settings.RenewAuthorizationScope();viewScope=null;
    Client.Logout();Settings.ClearSnapshot();Settings.Set("lastMode","");
    Demo=false;Connected=false;cachedCloudAccount=false;pendingLoadError=null;pendingTaskChanges.Clear();Tasks.Clear();Occurrences.Clear();Lists.Clear();Focus.Clear();boardStages=J.Obj();LastSync=null;Publish();
   }finally{operations.Release();}
  }
  public void Persist(){Settings.SaveSnapshot(Snapshot(),Demo);}
  public async Task LoginAsync(bool syncPending=true){
   await operations.WaitAsync();
   try{
    await Client.StartAsync();
    await Client.CallAsync("check_connection");
    await SyncCoreAsync(syncPending);
    Settings.Set("lastMode","cloud");
   }finally{operations.Release();}
  }
  public async Task SyncAsync(){
   await operations.WaitAsync();
   try{
    if(Demo){Publish();return;}
    if(!Client.Running)await Client.StartAsync();
    await SyncCoreAsync();
   }finally{operations.Release();}
  }
  public async Task<bool> TryAutomaticSyncAsync(bool listsOnly=false){
   if(Demo||!await operations.WaitAsync(0))return false;
   try{
    if(!Connected&&!Client.HasCredential)return false;
    if(!Client.Running)await Client.StartAsync();
    if(listsOnly&&viewScope==Settings.AuthorizationScope){
     var freshLists=await ReadCloudListsAsync();EnsureQueueAccount();
     if(ApplyRefreshedLists(freshLists))await SaveCloudSnapshotAsync();
    }else await SyncCoreAsync();
    return true;
   }finally{operations.Release();}
  }

  async Task<List<Dictionary<string,object>>> ReadCloudListsAsync(){
   var result=await Client.CallAsync("list_lists");
   object data;
   var rows=result.Rows("items");
   var ids=new HashSet<string>(StringComparer.Ordinal);
   if(!result.TryGetValue("items",out data)||!(data is IList)||((IList)data).Count!=rows.Count||
      rows.Any(row=>string.IsNullOrWhiteSpace(row.S("name"))||row.S("list_id")==""||!ids.Add(row.S("list_id"))))
    throw new McpException("云端清单返回格式异常，桌面已保留上次读取的清单。");
   if(!ids.Contains("1"))rows.Insert(0,J.Obj("list_id","1","name","未分类","color",0,"sort_index",0));
   return rows;
  }

  bool ApplyRefreshedLists(List<Dictionary<string,object>> lists){
   if(Demo||J.Encode(lists)==J.Encode(Lists))return false;
   Lists=lists;
   Publish();
   return true;
  }

  async Task SyncCoreAsync(bool sendPending=true){
   Syncing=true;
   bool listsChanged=false;
   ExceptionDispatchInfo syncFailure=null;
   try{
    string scope=Settings.AuthorizationScope;PrepareCloudSession(scope);
    Publish();
    List<Dictionary<string,object>> lists=null;
    ExceptionDispatchInfo listFailure=null;
    try{lists=await ReadCloudListsAsync();}
    catch(Exception error){listFailure=ExceptionDispatchInfo.Capture(error);}
    EnsureQueueAccount();
    if(listFailure==null)listsChanged=ApplyRefreshedLists(lists);
    if(sendPending)await SendPendingTaskChangesAsync();
    if(listFailure!=null)listFailure.Throw();

    var tasks=await Client.AllAsync("search_tasks");
    var occurrences=await Client.AllAsync("list_occurrences",OccurrenceQuery());
    var focus=await Client.AllAsync("list_focus",J.Obj("start_date",EarliestSyncDate,"end_date",LatestSyncDate));
    EnsureQueueAccount();
    if(Demo){var saved=Settings.LoadSnapshot(false);boardStages=saved!=null&&saved.S("accountScope",Settings.InitialScope)==scope?(saved.D("boardStages")??J.Obj()):J.Obj();}
    Lists=lists;Tasks=tasks;Occurrences=occurrences;Focus=focus;
    Demo=false;Connected=true;cachedCloudAccount=true;LastSync=DateTime.Now;
    RestoreMatchingArchives();
    var sent=pendingTaskChanges.Where(change=>change.B("sent")).ToList();
    foreach(var change in sent.Where(c=>c.ContainsKey("boardStage")))
     boardStages[change.D("args").S("occurrence_id")]=change.B("boardStage");
    PruneBoardStages();
    await SaveCloudSnapshotAsync(sent.Count>0);
    if(sent.Count>0)SaveTaskChanges(pendingTaskChanges.Where(change=>!sent.Contains(change)).ToList());
   }catch(Exception error){
    Connected=false;
    syncFailure=ExceptionDispatchInfo.Capture(error);
   }
   try{
    if(syncFailure!=null){
     if(listsChanged)await SaveCloudSnapshotAsync();
     syncFailure.Throw();
    }
   }finally{Syncing=false;Publish();}
  }
  public List<TaskItem> Items(){
   if(!Demo&&(viewScope==null||viewScope!=Settings.AuthorizationScope))return new List<TaskItem>();
   if(viewItems!=null)return viewItems;
   var tasks=Tasks.GroupBy(t=>t.S("task_id")).ToDictionary(g=>g.Key,g=>g.First());
   var occurrences=Occurrences.GroupBy(o=>o.S("occurrence_id")).ToDictionary(g=>g.Key,g=>g.First());
   var temporarySeries=new HashSet<string>();
   if(!Demo&&Settings.QueueScope==Settings.AuthorizationScope)ApplyPendingTaskChanges(tasks,occurrences,temporarySeries);
   viewItems=occurrences.Values.Where(o=>tasks.ContainsKey(o.S("task_id")))
    .Select(o=>new TaskItem{Task=tasks[o.S("task_id")],Occurrence=o,Syncing=temporarySeries.Contains(o.S("task_id"))})
    .OrderBy(t=>t.Undated?"9999":t.Start).ThenBy(t=>t.Time).ToList();
   return viewItems;
  }
  public string ListName(string id){var list=Lists.FirstOrDefault(l=>l.S("list_id")==id);return list==null?"未分类":list.S("name");}
  public async Task<Dictionary<string,object>> CallAsync(string name,Dictionary<string,object> args){
   if(!Demo&&IsDeferredTaskWrite(name,args))return QueueTaskChange(name,args);
   await operations.WaitAsync();
   try{
    if(!Demo){
     if(!Connected)throw new McpException("当前离线，请连接云端后再使用此操作。");
     if(!args.B("preview")&&HasPendingDependency(name,args)){
      await SyncCoreAsync();
      if(HasPendingDependency(name,args))throw new McpException("相关待办仍有未同步的改动，请在设置的同步详情中查看后再试。");
     }
    }
    return await CallCoreAsync(name,args);
   }finally{operations.Release();}
  }
  async Task<Dictionary<string,object>> CallCoreAsync(string name,Dictionary<string,object> args){
    if(!CanWrite)throw new McpException("当前为离线缓存，请连接云端后再修改。");
    if(Demo){
     var demoResult=DemoCall(name,args);
     if(!args.B("preview")){PruneBoardStages();Persist();Publish();}
     return demoResult;
    }
    var result=await Client.CallAsync(name,args);
    if(args.B("preview"))return result;
    bool applied=false;
    try{applied=await ApplyMutationAsync(name,result);}
    catch(McpException){/* The confirmed write will be read during the next scheduled refresh. */}
    catch(System.Net.Http.HttpRequestException){}
    catch(TaskCanceledException){}
    if(applied){
     PruneBoardStages();
     Publish();
     await SaveCloudSnapshotAsync();
    }
    return result;
  }
  public BoardStage Stage(TaskItem item){
   if(item.Completed)return BoardStage.Completed;
   bool inProgress=PendingBoardStage(item.OccurrenceId)??(boardStages.ContainsKey(item.OccurrenceId)?boardStages.B(item.OccurrenceId):item.Occurrence.I("completed_count")>0);
   return inProgress?BoardStage.InProgress:BoardStage.Pending;
  }
  public async Task MoveOnBoardAsync(string id,BoardStage destination){
   if(!CanWrite)throw new McpException("请先登录便单，再移动待办。");
   var item=Items().FirstOrDefault(t=>t.OccurrenceId==id);
   if(item==null)throw new McpException("找不到这次执行记录。");
   if(item.Abandoned||item.Syncing||Stage(item)==destination)return;

   if(destination==BoardStage.Completed){
    await CallAsync("complete_occurrence",J.Obj("occurrence_id",id));
    return;
   }
   if(!Demo&&item.Completed){
    QueueTaskChange("change_progress",J.Obj("occurrence_id",id,"action","reopen"),destination);
    return;
   }
   if(ReplacePendingBoardStage(id,destination))return;

   await operations.WaitAsync();
   try{
    if(item.Completed)await CallCoreAsync("change_progress",J.Obj("occurrence_id",id,"action","reopen"));
    var prior=boardStages;
    boardStages=J.Clone(prior);
    boardStages[id]=destination==BoardStage.InProgress;
    try{Persist();}
    catch{boardStages=prior;throw;}
    Publish();
   }finally{operations.Release();}
  }
  void PruneBoardStages(){
   if(boardStages.Count==0)return;
   viewItems=null;
   var active=new HashSet<string>(Items().Where(item=>!item.Completed&&!item.Abandoned).Select(item=>item.OccurrenceId));
   foreach(var id in boardStages.Keys.Where(id=>!active.Contains(id)).ToArray())boardStages.Remove(id);
  }
  Dictionary<string,object> FindTask(string id){var t=Tasks.FirstOrDefault(x=>x.S("task_id")==id);if(t==null)throw new McpException("找不到这项待办。");return t;}
  Dictionary<string,object> FindOccurrence(string id){var t=Occurrences.FirstOrDefault(x=>x.S("occurrence_id")==id);if(t==null)throw new McpException("找不到这次执行记录。");return t;}
  public Dictionary<string,object> DemoCall(string name,Dictionary<string,object> a){
   string tid=a.S("task_id"),oid=a.S("occurrence_id");Dictionary<string,object> t=null,o=null;
   if(tid!="")t=FindTask(tid);if(oid!=""){o=FindOccurrence(oid);if(t==null)t=FindTask(o.S("task_id"));}
   switch(name){
    case "create_task":{
     var id=a.S("operation_id",J.NewId());var old=Tasks.FirstOrDefault(x=>x.S("task_id")==id);if(old!=null)return old;
     t=CreateLocalTask(a,false,Occurrences,id);Tasks.Add(t);
     return t;
    }
    case "update_task":foreach(var k in new[]{"title","remark","important","urgent","collected"})if(a.ContainsKey(k))t[k]=a[k];return t;
    case "move_task":t["list_id"]=a["list_id"];return t;
    case "reschedule_task":case "reschedule_occurrence":{
     if(o==null)o=Occurrences.First(x=>x.S("task_id")==tid);
     if(o.S("status")!="pending")throw new McpException("只有待办状态的任务可以改期。");
     bool detached=t.I("repeat_type")!=0;
     var first=DemoCalendar.Date(a.S("start_date"));
     int span=detached?(DemoCalendar.Date(o.S("end_date"))-DemoCalendar.Date(o.S("start_date"))).Days:0;
     var last=DemoCalendar.Date(a.S("end_date",J.Date(first.AddDays(span))));
     if(last<first)throw new McpException("结束日期不能早于开始日期。");
     if(detached){
      t=J.Clone(t);t["task_id"]=J.NewId();t["repeat_type"]=0;t["recurrence"]=null;t["is_habit"]=false;
      Tasks.Add(t);o["task_id"]=t["task_id"];
     }
     o["start_date"]=t["start_date"]=J.Date(first);o["end_date"]=t["end_date"]=J.Date(last);
     foreach(var k in new[]{"start_time","end_time"})if(a.ContainsKey(k))o[k]=t[k]=a[k];
     t["undated"]=false;return o;
    }
    case "clear_schedule":t["undated"]=true;t["time_reminder"]=null;return t;
    case "complete_occurrence":o["status"]="completed";o["completed_count"]=t.I("target_count",1);o["completed_at"]=a.S("completed_at",DateTime.Now.ToString("yyyy-MM-ddTHH:mm"));o["completed_subtask_indices"]=Enumerable.Range(0,t.Strings("subtasks").Count).ToArray();return o;
    case "complete_subtask":case "change_progress":{
     string action=name=="complete_subtask"?"check":a.S("action");
     if(action=="reopen"||action=="restore"){o["status"]="pending";o["completed_count"]=0;o["completed_subtask_indices"]=new int[0];o["completed_at"]=null;}
     else if(action=="abandon")o["status"]="abandoned";
     else if(action=="set_count"){o["completed_count"]=a.I("count");o["status"]=a.I("count")>=t.I("target_count",1)?"completed":"pending";if(o.S("status")=="completed")o["completed_at"]=DateTime.Now.ToString("yyyy-MM-ddTHH:mm");}
     else{var indices=o.Strings("completed_subtask_indices").Select(int.Parse).ToList();int index=a.I("subtask_index");if(action=="uncheck_subtask")indices.Remove(index);else if(!indices.Contains(index))indices.Add(index);o["completed_subtask_indices"]=indices;o["completed_count"]=indices.Count;o["status"]=indices.Count>=t.Strings("subtasks").Count?"completed":"pending";if(o.S("status")=="completed")o["completed_at"]=DateTime.Now.ToString("yyyy-MM-ddTHH:mm");}
     return o;
    }
    case "edit_task_goal":if(a.ContainsKey("subtasks")){t["subtasks"]=a["subtasks"];t["target_count"]=Math.Max(1,a.Strings("subtasks").Count);}else{t["subtasks"]=new string[0];t["target_count"]=a["target_count"];}foreach(var x in Occurrences.Where(x=>x.S("task_id")==tid))x["target_count"]=t["target_count"];return t;
    case "set_time_reminder":t["time_reminder"]=a.ContainsKey("enabled")&&!a.B("enabled")?null:J.Obj("date",a.S("reminder_date"),"time",a.S("reminder_time"));return t;
    case "set_location":{
     if(a.B("clear")){t["location"]=null;t["has_location_reminder"]=false;return t;}
     var place=t.D("location")??J.Obj();
     foreach(var k in new[]{"name","latitude","longitude"})if(a.ContainsKey(k))place[k]=a[k];
     if(a.B("remind")&&(!place.ContainsKey("latitude")||!place.ContainsKey("longitude")))throw new McpException("到达提醒需要有效坐标。");
     if(a.ContainsKey("remind"))t["has_location_reminder"]=a.B("remind");
     t["location"]=place;return t;
    }
    case "delete_task":{
     var affected=Occurrences.Where(x=>x.S("task_id")==tid&&(a.S("scope")!="occurrence"||x.S("occurrence_id")==oid)).ToList();
     bool deleteDefinition=a.S("scope")!="occurrence"||t.I("repeat_type")==0;
     if(a.B("preview"))return J.Obj("preview",true,"occurrence_ids",affected.Select(x=>x.S("occurrence_id")).ToArray(),"deletes_task_definition",deleteDefinition);
     Occurrences.RemoveAll(x=>affected.Contains(x));if(deleteDefinition)Tasks.Remove(t);return J.Obj("deleted",true);
    }
    case "create_list":var l=J.Obj("list_id",a.S("operation_id",J.NewId()),"name",a.S("name"),"color",a.I("color"),"sort_index",Lists.Count);Lists.Add(l);return l;
    case "update_list":var list=Lists.First(x=>x.S("list_id")==a.S("list_id"));foreach(var k in new[]{"name","color","sort_index"})if(a.ContainsKey(k))list[k]=a[k];return list;
    case "delete_list":var tasks=Tasks.Where(x=>x.S("list_id")==a.S("list_id")).ToList();if(a.B("preview"))return J.Obj("affected_tasks",tasks);foreach(var task in tasks)task["list_id"]="1";Lists.RemoveAll(x=>x.S("list_id")==a.S("list_id"));return J.Obj("deleted",true);
    case "create_focus":var f=J.Clone(a);DateTime started=DateTime.Parse(a.S("started_at"));f["focus_id"]=((started.ToUniversalTime().Ticks-621355968000000000L)/10000).ToString();f["date"]=J.Date(started);Focus.Add(f);return f;
    case "update_focus":var focus=Focus.First(x=>x.S("focus_id")==a.S("focus_id"));foreach(var k in new[]{"title","good_count","bad_count"})if(a.ContainsKey(k))focus[k]=a[k];return focus;
    case "delete_focus":Focus.RemoveAll(x=>x.S("focus_id")==a.S("focus_id"));return J.Obj("deleted",true);
    case "update_recurrence":if(a.B("preview"))return J.Obj("created",0,"rescheduled",0,"removed",0,"protected_dates",Occurrences.Count(x=>x.S("task_id")==tid));throw new McpException("体验模式支持新建循环任务；修改已有循环规则请在登录后使用。");
    default:throw new McpException("体验模式暂不支持此操作。");
   }
  }
  static Dictionary<string,object> CreateLocalTask(Dictionary<string,object> args,bool cloud,
    List<Dictionary<string,object>> occurrences,string id=null){
   string taskId=id??args.S("operation_id");
   bool undated=!args.ContainsKey("start_date");
   var rule=args.D("repeat_rule");
   DateTime first=DemoCalendar.Date(args.S("start_date",cloud?"1970-01-01":J.Today));
   DateTime last=DemoCalendar.Date(args.S("end_date",undated&&cloud?"2100-12-31":J.Date(first)));
   var windows=cloud&&rule!=null&&args.B("china_makeup")?
    new List<Tuple<DateTime,DateTime>>{Tuple.Create(first,first)}:DemoCalendar.Windows(first,last,rule).ToList();
   var task=J.Clone(args);
   task["task_id"]=taskId;task["undated"]=undated;task["list_id"]=args.S("list_id","1");
   task["repeat_type"]=rule==null?0:4;task["recurrence"]=rule;task["is_habit"]=args.B("habit");
   task["target_count"]=Math.Max(args.Strings("subtasks").Count,args.I("target_count",1));
   if(args.ContainsKey("reminder_date"))task["time_reminder"]=J.Obj("date",args.S("reminder_date"),"time",args.S("reminder_time"));
   foreach(var window in windows){
    string start=J.Date(window.Item1);
    string occurrenceId=!cloud?J.NewId():rule==null?taskId:"pending:"+taskId+":"+start;
    occurrences.Add(J.Obj("task_id",taskId,"occurrence_id",occurrenceId,"start_date",start,"end_date",J.Date(window.Item2),
     "start_time",args.S("start_time",cloud&&!undated?"00:00":""),"end_time",args.S("end_time",cloud&&!undated?"23:59":""),
     "status","pending","target_count",task.I("target_count",1),"completed_count",0,"completed_subtask_indices",new int[0]));
   }
   return task;
  }

  void SeedDemo(){
   Lists=new List<Dictionary<string,object>>{J.Obj("list_id","1","name","未分类","color",0),J.Obj("list_id","2","name","工作计划","color",4),J.Obj("list_id","3","name","生活日常","color",2),J.Obj("list_id","4","name","自我成长","color",8)};
   Tasks.Clear();Occurrences.Clear();Focus.Clear();
   AddSeed("整理本周的工作计划","2",0,"09:30",true,false,new[]{"回顾上周进展","确定本周重点","安排执行时间"},"先做重要的事，给每一天留一点余地。",false);
   AddSeed("完成秋冬系列主图设计","2",0,"14:00",true,false,new string[0],"梳理视觉方向，检查商品结构与色彩。",false);
   Tasks.Last()["urgent"]=true;
   AddSeed("去公园散步，看看秋天","3",0,"17:30",false,false,new string[0],"带上耳机，走一条没走过的路。",false);
   AddSeed("确认今天的快递取件","3",0,"16:00",false,false,new string[0],"",false);
   Tasks.Last()["urgent"]=true;
   AddSeed("读书 20 分钟","4",0,"21:00",false,true,new string[0],"每天积累一点点。",true);
   AddSeed("喝一杯水，开启新的一天","3",0,"08:00",false,false,new string[0],"",true);
   AddSeed("准备明天的项目沟通","2",1,"10:00",false,false,new string[0],"",false);
   AddSeed("整理书桌与灵感素材","3",2,"",false,false,new string[0],"",false);
   AddSeed("记录一个新的设计灵感","4",4,"",false,false,new string[0],"",false);
   AddSeed("整理收藏的文章","4",0,"",false,false,new string[0],"找个空闲的下午慢慢读。",false);Tasks.Last()["undated"]=true;
   Focus.Add(J.Obj("focus_id","100","date",J.Today,"minutes",25,"good_count",1,"bad_count",0,"title","专注设计","started_at",J.Today+"T09:00"));
  }
  void AddSeed(string title,string list,int day,string time,bool important,bool habit,string[] subs,string remark,bool done){
   var id=J.NewId();var date=J.Date(DateTime.Today.AddDays(day));var t=J.Obj("task_id",id,"title",title,"list_id",list,"start_date",date,"end_date",date,"start_time",time,"important",important,"urgent",false,"undated",false,"repeat_type",habit?4:0,"is_habit",habit,"subtasks",subs,"remark",remark,"target_count",Math.Max(1,subs.Length),"completed",done);Tasks.Add(t);
   Occurrences.Add(J.Obj("task_id",id,"occurrence_id",J.NewId(),"start_date",date,"end_date",date,"start_time",time,"status",done?"completed":"pending","completed_at",done?J.Today+"T08:00":null,"completed_count",done?Math.Max(1,subs.Length):0,"target_count",Math.Max(1,subs.Length),"completed_subtask_indices",new int[0]));
  }
 }
}
