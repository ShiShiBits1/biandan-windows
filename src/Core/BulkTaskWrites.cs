using System;
using System.Collections.Generic;
using System.Linq;

namespace Biandan {
 public sealed partial class Repository {
  internal List<TaskItem> BulkDateCandidates(IEnumerable<TaskItem> selected){
   var singleTasks=new HashSet<string>(Items().GroupBy(item=>item.Id)
    .Where(group=>group.Count()==1).Select(group=>group.Key));
   return selected.Where(item=>!item.Syncing&&!item.Repeating&&item.Occurrence.S("status")=="pending"&&
    item.Occurrence.I("completed_count")>=0&&singleTasks.Contains(item.Id)).ToList();
  }

  List<TaskItem> ResolveBulkSelection(IEnumerable<string> occurrenceIds){
   if(!CanWrite)throw new McpException("请先登录便单，再修改待办。");
   var ids=new HashSet<string>(occurrenceIds);
   var items=Items().Where(item=>ids.Contains(item.OccurrenceId)&&!item.Syncing).ToList();
   if(items.Count!=ids.Count)throw new McpException("部分所选记录已变化，请重新选择。");
   return items;
  }

  internal int MoveBulkTasks(IEnumerable<string> occurrenceIds,string listId){
   if(!Lists.Any(list=>list.S("list_id")==listId))throw new McpException("目标清单已变化，请重新选择。");
   var changed=ResolveBulkSelection(occurrenceIds).Where(item=>item.ListId!=listId)
    .GroupBy(item=>item.Id).Select(group=>group.First());
   return SaveBulkEdits("move_task",changed,item=>J.Obj("task_id",item.Id,"list_id",listId));
  }

  internal int RescheduleBulkTasks(IEnumerable<string> occurrenceIds,DateTime firstDay){
   var items=ResolveBulkSelection(occurrenceIds);
   if(BulkDateCandidates(items).Count!=items.Count)
    throw new McpException("部分所选待办已无法改期，请重新选择。");
   string day=J.Date(firstDay);
   return SaveBulkEdits("reschedule_task",items.Where(item=>item.Undated||item.Start!=day),
    item=>ScheduleArguments(item,firstDay));
  }

  int SaveBulkEdits(string method,IEnumerable<TaskItem> items,Func<TaskItem,Dictionary<string,object>> buildArgs){
   var entries=new List<Dictionary<string,object>>();
   string queuedAt=DateTime.UtcNow.ToString("o");
   foreach(var item in items){
    entries.Add(J.Obj("method",method,"title",item.Title,"taskId",item.Id,"queuedAt",queuedAt,"args",buildArgs(item)));
   }
   if(entries.Count==0)return 0;
   if(Demo){
    SaveBulkDemo(method,entries);
   }else{
    EnsureQueueAccount();
    var changes=new List<Dictionary<string,object>>(pendingTaskChanges);
    changes.AddRange(entries);
    SaveTaskChanges(changes);
    pendingChangedUtc=DateTime.UtcNow;
   }
   Publish();
   return entries.Count;
  }

  void SaveBulkDemo(string method,List<Dictionary<string,object>> entries){
   var patches=entries.ToDictionary(entry=>entry.S("taskId"),entry=>entry.D("args"));
   var priorTasks=Tasks;
   var priorOccurrences=Occurrences;
   bool scheduling=method=="reschedule_task";
   var updatedTasks=CopyBulkRecords(Tasks,patches,scheduling);
   if(scheduling)Occurrences=CopyBulkRecords(Occurrences,patches,false);
   Tasks=updatedTasks;
   try{Persist();}
   catch{Tasks=priorTasks;Occurrences=priorOccurrences;throw;}
  }

  static List<Dictionary<string,object>> CopyBulkRecords(List<Dictionary<string,object>> records,
    Dictionary<string,Dictionary<string,object>> patches,bool markDated){
   var updated=new List<Dictionary<string,object>>(records.Count);
   foreach(var original in records){
    Dictionary<string,object> patch;
    var row=original;
    if(patches.TryGetValue(original.S("task_id"),out patch)){
     row=new Dictionary<string,object>(original);
     foreach(var pair in patch)if(pair.Key!="task_id")row[pair.Key]=pair.Value;
     if(markDated)row["undated"]=false;
    }
    updated.Add(row);
   }
   return updated;
  }
 }
}
