using System;
using System.Linq;

namespace Biandan {
 public sealed partial class App {
  void VerifyDemoProgress(){
   string series=J.NewId(),numeric=J.NewId();
   try{
    Repo.DemoCall("create_task",J.Obj("operation_id",series,"title","多设备子任务进度",
     "start_date",J.Today,"end_date",J.Date(DateTime.Today.AddDays(2)),"repeat_rule",J.Obj("mode","daily"),
     "subtasks",new[]{"核对","发货","留存"}));
    var rows=Repo.Occurrences.Where(row=>row.S("task_id")==series).ToList();
    var first=rows[0];var history=rows[1];var skipped=rows[2];
    string firstId=first.S("occurrence_id"),historyId=history.S("occurrence_id"),skipId=skipped.S("occurrence_id");
    Repo.DemoCall("complete_subtask",J.Obj("occurrence_id",firstId,"subtask_index",2));
    Repo.DemoCall("complete_subtask",J.Obj("occurrence_id",firstId,"subtask_index",0));
    Repo.DemoCall("complete_occurrence",J.Obj("occurrence_id",historyId,"completed_at","2030-06-03T18:45"));
    Repo.DemoCall("complete_occurrence",J.Obj("occurrence_id",historyId));
    Repo.DemoCall("change_progress",J.Obj("occurrence_id",skipId,"action","abandon"));
    if(first.I("completed_count")!=2||first.S("completed_at")!=""||history.S("completed_at")!="2030-06-03 18:45:00")
     throw new InvalidOperationException("Partial checklist progress or idempotent completion changed its history.");
    var original=J.Encode(Repo.Occurrences);
    bool rejected=false;
    try{Repo.DemoCall("edit_task_goal",J.Obj("task_id",series,"subtasks",new[]{"核对","留存"}));}
    catch(McpException){rejected=true;}
    if(!rejected||original!=J.Encode(Repo.Occurrences))throw new InvalidOperationException("Moving a historical checklist position must be rejected before changing instances.");
    var task=Repo.DemoCall("edit_task_goal",J.Obj("task_id",series,"subtasks",new[]{"核对","发货"}));
    if(first.I("completed_count")!=1||!first.Strings("completed_subtask_indices").SequenceEqual(new[]{"0"})||
       history.I("completed_count")!=3||history.S("completed_at")!="2030-06-03 18:45:00"||
       skipped.S("status")!="abandoned"||task.B("completed"))
     throw new InvalidOperationException("Changing a checklist goal lost saved history or retained a removed active position.");
    Repo.DemoCall("edit_task_goal",J.Obj("task_id",series,"subtasks",new[]{"核对","发货","回访","归档"}));
    if(first.I("completed_count")!=1||history.I("target_count")!=4||history.S("status")!="completed"||
       history.I("completed_count")!=3||history.S("completed_at")!="2030-06-03 18:45:00")
     throw new InvalidOperationException("Appending subtasks must retain the original completion of past instances.");
    Repo.DemoCall("delete_task",J.Obj("task_id",series,"scope","occurrence","occurrence_id",firstId));
    if(!Repo.Tasks.Single(row=>row.S("task_id")==series).B("completed"))
     throw new InvalidOperationException("Deleting the last pending instance did not update the task.");
    Repo.DemoCall("change_progress",J.Obj("occurrence_id",skipId,"action","restore"));
    if(skipped.S("status")!="pending"||Repo.Tasks.Single(row=>row.S("task_id")==series).B("completed"))
     throw new InvalidOperationException("Restoring the skipped instance did not reopen its parent.");

    Repo.DemoCall("create_task",J.Obj("operation_id",numeric,"title","次数目标","target_count",5));
    var counter=Repo.Occurrences.Single(row=>row.S("task_id")==numeric);
    string countId=counter.S("occurrence_id");
    Repo.DemoCall("change_progress",J.Obj("occurrence_id",countId,"action","set_count","count",3));
    Repo.DemoCall("change_progress",J.Obj("occurrence_id",countId,"action","restore"));
    rejected=false;
    try{Repo.DemoCall("edit_task_goal",J.Obj("task_id",numeric,"subtasks",new[]{"校对"}));}
    catch(McpException){rejected=true;}
    if(!rejected||counter.I("completed_count")!=3)throw new InvalidOperationException("A mode change must preserve existing count progress.");
    task=Repo.DemoCall("edit_task_goal",J.Obj("task_id",numeric,"target_count",2));
    if(!task.B("completed")||counter.S("completed_at")==""||counter.I("completed_count")!=2)
     throw new InvalidOperationException("Lowering a numeric target must reconcile its active progress and parent.");
    Repo.DemoCall("change_progress",J.Obj("occurrence_id",countId,"action","abandon"));
    if(counter.I("completed_count")!=0||counter.S("completed_at")!=""||counter.S("status")!="abandoned")
     throw new InvalidOperationException("Abandoned progress must match the MCP occurrence view.");
   }finally{
    foreach(string id in new[]{series,numeric})
     if(Repo.Tasks.Any(task=>task.S("task_id")==id))Repo.DemoCall("delete_task",J.Obj("task_id",id));
    Repo.Publish();
   }
  }
 }
}
