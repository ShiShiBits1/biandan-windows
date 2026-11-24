using System;
using System.Linq;
using System.Threading.Tasks;

namespace Biandan {
 public sealed partial class Repository {
  internal async Task CreateAssistantTaskAsync(AssistantSuggestion task){
   if(task==null||string.IsNullOrWhiteSpace(task.Title))throw new McpException("请填写待办名称。");
   var folder=(task.FolderName??"").Trim();
   var args=J.Obj("title",task.Title.Trim(),"operation_id",task.OperationId,
    "list_id",AssistantListId(folder),"important",task.Important||folder=="重要待办");
   args["urgent"]=task.Urgent;
   if(task.Remark!=null)args["remark"]=task.Remark;
   if(!string.IsNullOrWhiteSpace(task.Location))args["location"]=J.Obj("name",task.Location.Trim());
   var subtasks=task.Subtasks??new string[0];
   if(subtasks.Length>20||subtasks.Any(string.IsNullOrWhiteSpace))
    throw new McpException("子任务最多 20 项，每项都需要填写名称。");
   if(task.TargetCount.HasValue&&(task.TargetCount<1||task.TargetCount>99))
    throw new McpException("次数目标请填 1–99。");
   if(subtasks.Length>0){
    if(task.TargetCount.HasValue&&task.TargetCount.Value!=subtasks.Length)
     throw new McpException("子任务数量与次数目标不一致，请核对草稿。");
    args["subtasks"]=subtasks.Select(title=>title.Trim()).ToArray();
   }else if(task.TargetCount.HasValue)args["target_count"]=task.TargetCount.Value;

   string date=(task.Date??"").Trim(),endDate=(task.EndDate??"").Trim();
   string startTime=AssistantClock(task.StartTime),endTime=AssistantClock(task.EndTime);
   string reminderDate=(task.ReminderDate??"").Trim(),reminderTime=AssistantClock(task.ReminderTime);
   if(date==""){
    if(endDate!=""||startTime!=""||endTime!=""||reminderDate!=""||reminderTime!=""||task.Repeat!=null)
     throw new McpException("请先指定开始日期，再安排时段、提醒或循环。");
   }else{
    if(task.Repeat!=null&&endDate=="")throw new McpException("循环待办需要明确的截止日期。");
    var first=DemoCalendar.Date(date);
    var last=endDate==""?first:DemoCalendar.Date(endDate);
    if(last<first)throw new McpException("结束日期不能早于开始日期。");
    args["start_date"]=J.Date(first);
    args["end_date"]=J.Date(last);
    if(startTime==""){
     if(endTime!="")throw new McpException("请补充该时段的开始时间。");
    }else{
     if(endTime==""&&last!=first&&task.Repeat==null)throw new McpException("跨日时段需要明确的结束时间。");
     if(endTime=="")endTime=startTime;
     if((last==first||task.Repeat!=null)&&string.CompareOrdinal(endTime,startTime)<0)
      throw new McpException("结束时间不能早于开始时间。");
     args["start_time"]=startTime;
     args["end_time"]=endTime;
    }
    if(reminderDate!=""||reminderTime!=""){
     if(reminderDate==""||reminderTime=="")throw new McpException("提醒需要同时指定日期和时间。");
     args["reminder_date"]=J.Date(DemoCalendar.Date(reminderDate));
     args["reminder_time"]=reminderTime;
    }
    if(task.Repeat!=null){
     args["repeat_rule"]=task.Repeat.ToRule();
     args["week_start"]=1;
    }
   }
   await CallAsync("create_task",args);
   if(task.Collected||folder=="我的收藏")await CallAsync("update_task",J.Obj("task_id",task.OperationId,"collected",true));
  }

  string AssistantListId(string folder){
   if(folder==""||folder=="未分类"||folder=="全部待办"||folder=="重要待办"||folder=="我的收藏")return "1";
   var matches=Lists.Where(list=>string.Equals(list.S("name").Trim(),folder,StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
   if(matches.Count==0)throw new McpException("找不到清单「"+folder+"」。可先新建该清单，再重试添加。");
   if(matches.Count>1)throw new McpException("有多个清单名为「"+folder+"」。请先区分清单名称，再重试添加。");
   return matches[0].S("list_id");
  }

  static string AssistantClock(string value){
   string text=(value??"").Trim();
   if(text=="")return "";
   TimeSpan clock;
   if(text.Length!=5||!ReadScheduleClock(text,TimeSpan.Zero,out clock))
    throw new McpException("时间请使用 HH:mm 格式，例如 09:30。");
   return text;
  }
 }
}
