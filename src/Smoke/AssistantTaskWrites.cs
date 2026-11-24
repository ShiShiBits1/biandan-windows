using System;
using System.Linq;
using System.Threading.Tasks;

namespace Biandan {
 public sealed partial class App {
  async Task VerifyAssistantTaskWritesAsync(){
   string folderId=J.NewId(),folderName="小助手验收-"+folderId;
   var draft=new AssistantSuggestion{
    Title="跨日交接",FolderName=folderName,Important=true,Urgent=true,Collected=true,
    Location="会客室",Subtasks=new[]{"核对","交接"},TargetCount=2,Remark="**核对**材料\n交给下一班",
    Date=J.Today,StartTime="23:45",EndDate=J.Date(DateTime.Today.AddDays(1)),EndTime="00:15",
    ReminderDate=J.Today,ReminderTime="23:35"
   };
   var point=new AssistantSuggestion{Title="时间点",Date=J.Today,StartTime="09:15",FolderName="我的收藏",TargetCount=3};
   var undated=new AssistantSuggestion{Title="待安排",FolderName="重要待办"};
   var repeat=new AssistantSuggestion{
    Title="隔日核对",Date=J.Today,EndDate=J.Date(DateTime.Today.AddDays(4)),StartTime="10:00",
    Repeat=AssistantRepeat.Parse(J.Obj("mode","custom","unit","day","interval",2,"dates",new string[0]))
   };
   try{
    bool missing=false;
    try{await Repo.CreateAssistantTaskAsync(draft);}
    catch(McpException error){missing=error.Message.Contains(folderName);}
    if(!missing||Repo.Items().Any(value=>value.Id==draft.OperationId))
     throw new InvalidOperationException("An unknown assistant list should leave the draft available to retry.");
    await Repo.CallAsync("create_list",J.Obj("name",folderName,"operation_id",folderId));
    await Repo.CreateAssistantTaskAsync(draft);
    await Repo.CreateAssistantTaskAsync(draft);
    var item=Repo.Items().Single(value=>value.Id==draft.OperationId);
    var reminder=item.Task.D("time_reminder");
    if(item.ListId!=folderId||item.Task.S("remark")!=draft.Remark||!item.Task.B("important")||
       !item.Task.B("urgent")||!item.Task.B("collected")||item.Task.D("location").S("name")!=draft.Location||
       !item.Task.Strings("subtasks").SequenceEqual(draft.Subtasks)||item.Task.I("target_count")!=2||
       item.Start!=draft.Date||item.End!=draft.EndDate||item.Time!=draft.StartTime||
       item.Occurrence.S("end_time")!=draft.EndTime||reminder.S("date")!=draft.ReminderDate||reminder.S("time")!=draft.ReminderTime)
     throw new InvalidOperationException("Assistant draft fields changed during task creation.");
    await Repo.CreateAssistantTaskAsync(point);
    item=Repo.Items().Single(value=>value.Id==point.OperationId);
    if(item.Time!=point.StartTime||item.Occurrence.S("end_time")!=point.StartTime||!item.Task.B("collected")||item.Task.I("target_count")!=3)
     throw new InvalidOperationException("An assistant point event must keep its exact time and collection flag.");
    await Repo.CreateAssistantTaskAsync(undated);
    item=Repo.Items().Single(value=>value.Id==undated.OperationId);
    if(!item.Undated||!item.Task.B("important"))throw new InvalidOperationException("An undated draft acquired a date or lost its priority.");
    await Repo.CreateAssistantTaskAsync(repeat);
    var starts=Repo.Items().Where(value=>value.Id==repeat.OperationId&&value.Repeating)
     .Select(value=>value.Start).OrderBy(date=>date);
    if(!starts.SequenceEqual(new[]{J.Today,J.Date(DateTime.Today.AddDays(2)),repeat.EndDate}))
     throw new InvalidOperationException("The assistant draft did not create the requested repeat interval.");
   }finally{
    foreach(var suggestion in new[]{draft,point,undated,repeat}){
     if(Repo.Tasks.Any(item=>item.S("task_id")==suggestion.OperationId))Repo.DemoCall("delete_task",J.Obj("task_id",suggestion.OperationId));
    }
    if(Repo.Lists.Any(list=>list.S("list_id")==folderId))Repo.DemoCall("delete_list",J.Obj("list_id",folderId));
    Repo.Persist();Repo.Publish();
   }
  }
 }
}
