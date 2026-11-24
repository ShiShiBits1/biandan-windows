using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Biandan {
 public sealed partial class Repository {
  const string EarliestSyncDate="1900-01-31",LatestSyncDate="2100-12-31";

  static Dictionary<string,object> OccurrenceQuery(){
   return J.Obj("start_date",EarliestSyncDate,"end_date",LatestSyncDate,"status","all","include_undated",true);
  }

  async Task<bool> ApplyMutationAsync(string method,Dictionary<string,object> result){
   switch(method){
    case "update_task":
    case "move_task":
    case "set_location":
    case "set_time_reminder":
     return ApplyUpdatedTask(result.S("task_id"),result);
    case "reschedule_task":
    case "reschedule_occurrence":
    case "clear_schedule":
     return ApplyTaskOccurrence(result);
    case "complete_occurrence":
    case "complete_subtask":
    case "change_progress":{
     var occurrence=result.D("occurrence");
     if(!ApplyOccurrence(occurrence))return false;
     if(result.ContainsKey("task_completed"))SetTaskCompletion(occurrence.S("task_id"),result.B("task_completed"));
     return true;
    }
    case "create_task":{
     string id=result.S("task_id"),occurrenceId=result.S("occurrence_id");
     if(id==""||occurrenceId=="")return false;
     var first=await Client.CallAsync("get_occurrence",J.Obj("occurrence_id",occurrenceId));
     if(result.I("occurrences_created")==1)return ApplyTaskOccurrence(first);
     return await ApplyTaskSeriesAsync(first.D("task"));
    }
    case "edit_task_goal":
     return await ApplyTaskSeriesAsync(result);
    case "update_recurrence":
     return await ApplyTaskSeriesAsync(result.D("task"));
    case "delete_task":
     return ApplyDeletedTask(result);
    case "create_list":
    case "update_list":{
     string id=result.S("list_id");
     if(id=="")return false;
     Upsert(Lists,"list_id",id,result);
     Lists=Lists.OrderBy(list=>list.I("sort_index")).ThenBy(list=>{
      long value;return long.TryParse(list.S("list_id"),out value)?value:long.MaxValue;
     }).ToList();
     return true;
    }
    case "delete_list":
     return ApplyDeletedList(result);
    case "create_focus":
    case "update_focus":{
     string id=result.S("focus_id");
     if(id=="")return false;
     Upsert(Focus,"focus_id",id,result);
     return true;
    }
    case "delete_focus":
     if(!result.B("deleted")||result.S("focus_id")=="")return false;
     Focus.RemoveAll(entry=>entry.S("focus_id")==result.S("focus_id"));
     return true;
    default:
     return false;
   }
  }

  bool ApplyTaskOccurrence(Dictionary<string,object> result){
   var task=result.D("task");
   var occurrence=result.D("occurrence");
   string taskId=task.S("task_id");
   if(taskId==""||taskId!=occurrence.S("task_id")||occurrence.S("occurrence_id")=="")return false;
   ApplyUpdatedTask(taskId,task);
   ApplyOccurrence(occurrence);
   string detachedFrom=result.S("series_task_id");
   if(detachedFrom!=""&&detachedFrom!=taskId)RecomputeTaskCompletion(detachedFrom);
   return true;
  }

  bool ApplyOccurrence(Dictionary<string,object> occurrence){
   string id=occurrence.S("occurrence_id");
   if(id==""||occurrence.S("task_id")=="")return false;
   Upsert(Occurrences,"occurrence_id",id,occurrence);
   return true;
  }

  async Task<bool> ApplyTaskSeriesAsync(Dictionary<string,object> task){
   string id=task.S("task_id");
   if(id==""||task.S("title")=="")return false;
   var query=OccurrenceQuery();
   query["task_id"]=id;
   var items=await Client.AllAsync("list_occurrences",query);
   if(items.Any(occurrence=>occurrence.S("occurrence_id")==""||occurrence.S("task_id")!=id))return false;
   ApplyUpdatedTask(id,task);
   Occurrences.RemoveAll(occurrence=>occurrence.S("task_id")==id);
   Occurrences.AddRange(items);
   return true;
  }

  bool ApplyDeletedTask(Dictionary<string,object> result){
   string id=result.S("task_id");
   if(!result.B("deleted")||id=="")return false;
   var removed=new HashSet<string>(result.Strings("occurrence_ids"));
   if(result.B("deletes_task_definition")){
    Tasks.RemoveAll(task=>task.S("task_id")==id);
    Occurrences.RemoveAll(occurrence=>occurrence.S("task_id")==id);
   }else{
    Occurrences.RemoveAll(occurrence=>occurrence.S("task_id")==id&&removed.Contains(occurrence.S("occurrence_id")));
    RecomputeTaskCompletion(id);
   }
   return true;
  }

  bool ApplyDeletedList(Dictionary<string,object> result){
   string id=result.S("list_id"),action=result.S("task_action");
   if(!result.B("deleted")||id==""||(action!="move_to_default"&&action!="delete_tasks"))return false;
   Lists.RemoveAll(list=>list.S("list_id")==id);
   var affected=new HashSet<string>(result.Rows("affected_tasks").Select(task=>task.S("task_id")));
   if(action=="delete_tasks"){
    Tasks.RemoveAll(task=>affected.Contains(task.S("task_id")));
    Occurrences.RemoveAll(occurrence=>affected.Contains(occurrence.S("task_id")));
   }else{
    for(int index=0;index<Tasks.Count;index++){
     var task=Tasks[index];
     if(!affected.Contains(task.S("task_id")))continue;
     var updated=new Dictionary<string,object>(task);
     updated["list_id"]="1";
     Tasks[index]=updated;
    }
   }
   return true;
  }

  void SetTaskCompletion(string id,bool completed){
   int index=Tasks.FindIndex(task=>task.S("task_id")==id);
   if(index<0)return;
   var updated=new Dictionary<string,object>(Tasks[index]);
   updated["completed"]=completed;
   Tasks[index]=updated;
  }

  void RecomputeTaskCompletion(string id){
   var statuses=Occurrences.Where(occurrence=>occurrence.S("task_id")==id).Select(occurrence=>occurrence.S("status")).ToList();
   SetTaskCompletion(id,statuses.Count>0&&statuses.All(status=>status=="completed"||status=="abandoned"));
  }
 }
}