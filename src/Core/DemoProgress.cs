using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Biandan {
 public sealed partial class Repository {
  // MCP exposes checklist progress as checked indices. Use their bits while editing
  // so removing a trailing goal never assigns its progress to a different title.
  static int DemoProgress(Dictionary<string,object> row,bool checklist){
   if(row.S("status")=="abandoned")return -1;
   if(!checklist)return Math.Max(0,row.I("completed_count"));
   int mask=0;
   foreach(string value in row.Strings("completed_subtask_indices")){
    int index;
    if(int.TryParse(value,out index)&&index>=0&&index<20)mask|=1<<index;
   }
   return mask;
  }

  static void SetDemoProgress(Dictionary<string,object> row,int value,int target,bool checklist,string completedAt=null){
   bool finished=value>=target;
   bool keepTime=finished&&row.S("status")=="completed"&&value==DemoProgress(row,checklist)&&row.S("completed_at")!="";
   int positive=Math.Max(value,0);
   int[] indices=checklist?Enumerable.Range(0,20).Where(index=>(positive&(1<<index))!=0).ToArray():new int[0];
   row["completed_subtask_indices"]=indices;
   row["completed_count"]=checklist?indices.Length:positive;
   row["status"]=value<0?"abandoned":finished?"completed":"pending";
   if(!finished)row["completed_at"]=null;
   else if(completedAt!=null||!keepTime)row["completed_at"]=completedAt??DateTime.Now.ToString("yyyy-MM-dd HH:mm:00",CultureInfo.InvariantCulture);
  }

  Dictionary<string,object> UpdateDemoProgress(string method,Dictionary<string,object> task,
    Dictionary<string,object> occurrence,Dictionary<string,object> args){
   if(occurrence==null||task==null)throw new McpException("找不到这次执行记录。");
   int subtaskCount=Math.Min(20,task.Strings("subtasks").Count);
   bool checklist=subtaskCount>0;
   int target=checklist?(1<<subtaskCount)-1:Math.Max(1,task.I("target_count",1));
   int previous=DemoProgress(occurrence,checklist);
   string action=method=="change_progress"?args.S("action"):method;
   int value;
   switch(action){
    case "complete_occurrence":value=target;break;
    case "reopen":value=0;break;
    case "restore":value=Math.Max(previous,0);break;
    case "abandon":value=-1;break;
    case "set_count":
     value=args.I("count",-1);
     if(checklist||value<0||value>target)throw new McpException("次数进度须在该待办的次数目标范围内。");
     break;
    case "complete_subtask":case "uncheck_subtask":
     int index=args.I("subtask_index",-1);
     if(index<0||index>=subtaskCount)throw new McpException("找不到这个子任务。");
     int bit=1<<index;
     int recorded=Math.Max(0,previous);
     value=action=="uncheck_subtask"?(recorded&~bit):(recorded|bit);
     break;
    default:throw new McpException("无法识别这项进度操作。");
   }
   string completedAt=null;
   if(args.ContainsKey("completed_at")){
    DateTime date;
    if((action!="complete_occurrence"&&action!="set_count")||
       !DateTime.TryParseExact(args.S("completed_at"),"yyyy-MM-ddTHH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))
     throw new McpException("完成时间应为本地日期和时刻，例如 2030-06-03T09:30。");
    completedAt=date.ToString("yyyy-MM-dd HH:mm:00",CultureInfo.InvariantCulture);
   }
   SetDemoProgress(occurrence,value,target,checklist,completedAt);
   RecomputeTaskCompletion(task.S("task_id"));
   return occurrence;
  }

  Dictionary<string,object> EditDemoGoal(Dictionary<string,object> task,Dictionary<string,object> args){
   bool hasTitles=args.ContainsKey("subtasks");
   if(hasTitles==args.ContainsKey("target_count"))throw new McpException("请选择子任务或次数目标。");
   var titles=args.Strings("subtasks").Select(value=>value.Trim()).ToList();
   if(titles.Count>20||titles.Any(string.IsNullOrEmpty))throw new McpException("最多可设置 20 个子任务，每项名称不能为空。");
   bool checklist=titles.Count>0;
   int count=checklist?titles.Count:args.I("target_count",hasTitles?1:0);
   if(count<1||count>99)throw new McpException("次数目标请填 1–99。");

   var previousTitles=task.Strings("subtasks");
   var instances=Occurrences.Where(row=>row.S("task_id")==task.S("task_id")).ToList();
   if(instances.Any(row=>row.S("status")=="completed"||(row.S("status")!="abandoned"&&row.I("completed_count")>0))){
    if(checklist!=(previousTitles.Count>0))throw new McpException("已有进度的待办无法切换子任务和次数目标。");
    for(int index=0;index<titles.Count;index++){
     if(previousTitles.Contains(titles[index])&&(index>=previousTitles.Count||titles[index]!=previousTitles[index]))
      throw new McpException("已有进度，请保留现有子任务的顺序。可在原位置改名或在末尾增加子任务。");
    }
   }
   int target=checklist?(1<<count)-1:count;
   foreach(var row in instances){
    row["target_count"]=count;
    if(row.S("status")=="completed")continue;
    int progress=DemoProgress(row,checklist);
    if(progress>=0)progress=checklist?progress&target:Math.Min(progress,target);
    SetDemoProgress(row,progress,target,checklist);
   }
   task["subtasks"]=titles.ToArray();task["target_count"]=count;
   RecomputeTaskCompletion(task.S("task_id"));
   return FindTask(task.S("task_id"));
  }
 }
}
