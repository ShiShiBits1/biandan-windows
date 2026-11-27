using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Biandan {
 public sealed partial class Repository {
  internal Dictionary<string,object>[] BulkDeleteTargets(IEnumerable<string> occurrenceIds,string scope){
   var items=ResolveBulkSelection(occurrenceIds);
   if(scope!="occurrence")items=items.GroupBy(item=>item.Id).Select(group=>group.First()).ToList();
   return items.Select(item=>scope=="occurrence"
    ?J.Obj("task_id",item.Id,"scope",scope,"occurrence_id",item.OccurrenceId)
    :J.Obj("task_id",item.Id,"scope",item.Repeating?scope:"series")).ToArray();
  }

  internal async Task<List<Dictionary<string,object>>> BatchDeleteAsync(
   Dictionary<string,object>[] targets,bool preview){
   await operations.WaitAsync();
   try{
    if(targets.Length==0)return new List<Dictionary<string,object>>();
    if(Demo)return DemoBatchDelete(targets,preview);
    EnsureQueueAccount();
    if(!Connected||!CanWrite)throw new McpException("连接云端后才能预览和批量删除。");
    if(targets.Any(target=>HasPendingDependency("delete_task",target)))
     throw new McpException("所选待办还有未同步的改动。请先同步，再预览删除范围。");

    var response=await Client.CallAsync("batch_delete_tasks",J.Obj("tasks",targets,"preview",preview));
    EnsureQueueAccount();
    var results=response.Rows("items");
    if(!response.ContainsKey("preview")||!(response["preview"] is bool)||response.B("preview")!=preview||
       results.Count!=targets.Length||
       !results.Select((row,index)=>ValidBatchDeleteResult(row,targets[index],preview)).All(valid=>valid))
     throw new McpException(preview?"服务未返回完整的删除预览。":"部分删除结果尚未确认，请同步后查看剩余待办。");
    if(!preview){
     bool changed=false;
     foreach(var row in results.Where(result=>result.B("ok")))
      changed=ApplyDeletedTask(row.D("result"))||changed;
     if(changed){
      PruneBoardStages();
      Publish();
      await SaveCloudSnapshotAsync();
     }
    }
    return results;
   }finally{operations.Release();}
  }

  static bool ValidBatchDeleteResult(Dictionary<string,object> row,Dictionary<string,object> target,bool preview){
   object ok;
   if(!row.TryGetValue("ok",out ok)||!(ok is bool))return false;
   if(!(bool)ok)return row.S("error")!="";
   var result=row.D("result");
   if(result==null||result.S("task_id")!=target.S("task_id")||result.S("scope")!=target.S("scope"))return false;
   object flag,definition,ids;
   if(!result.TryGetValue(preview?"preview":"deleted",out flag)||!(flag is bool)||!(bool)flag||
      !result.TryGetValue("deletes_task_definition",out definition)||!(definition is bool)||
      !result.TryGetValue("occurrence_ids",out ids)||!(ids is IList))return false;
   if(((IList)ids).Cast<object>().Any(id=>string.IsNullOrWhiteSpace(id as string)))return false;
   return target.S("scope")!="occurrence"||result.Strings("occurrence_ids").All(id=>id==target.S("occurrence_id"));
  }

  List<Dictionary<string,object>> DemoBatchDelete(Dictionary<string,object>[] targets,bool preview){
   var priorTasks=Tasks;
   var priorOccurrences=Occurrences;
   var priorStages=boardStages;
   if(!preview){
    Tasks=Tasks.ToList();
    Occurrences=Occurrences.ToList();
    boardStages=new Dictionary<string,object>(boardStages);
   }
   var results=new List<Dictionary<string,object>>(targets.Length);
   try{
    foreach(var target in targets){
     var args=J.Clone(target);
     args["preview"]=preview;
     try{results.Add(J.Obj("ok",true,"result",DemoCall("delete_task",args)));}
     catch(McpException error){results.Add(J.Obj("ok",false,"error",error.Message));}
    }
    if(!preview){PruneBoardStages();Persist();}
   }catch{
    Tasks=priorTasks;
    Occurrences=priorOccurrences;boardStages=priorStages;viewItems=null;
    throw;
   }
   if(!preview)Publish();
   return results;
  }
 }
}
