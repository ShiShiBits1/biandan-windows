using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Biandan {
 internal sealed partial class AssistantView {
  internal void ShowReply(AssistantReply reply,bool canQuery){
   string text=reply.Answer;
   if(reply.Query!=null){
    text=canQuery?QuerySummary(app.Repo,reply.Query):"请勾选「附上近期待办并允许查询本机清单」，再发送查询。";
   }
   AddMessage("便单小助手",text,reply.Suggestions,reply.Focus);
  }

  internal static string QuerySummary(Repository repo,AssistantQuery query){
   string first=query.StartDate+" "+(query.StartTime??"00:00");
   string last=query.EndDate+" "+(query.EndTime??"23:59");
   DateTime start,end;
   if(!DateTime.TryParseExact(first,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out start)
      ||!DateTime.TryParseExact(last,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out end)||end<start)
    return "查询的起止日期或时间无效，请重新指定时间范围。";

   string folder=string.IsNullOrWhiteSpace(query.FolderName)?"全部待办":query.FolderName;
   IEnumerable<TaskItem> items=repo.Items().Where(item=>!item.Abandoned&&!item.Undated);
   switch(folder){
    case "全部待办":break;
    case "未分类":items=items.Where(item=>item.ListId=="1");break;
    case "重要待办":items=items.Where(item=>item.Task.B("important"));break;
    case "我的收藏":items=items.Where(item=>item.Task.B("collected"));break;
    default:
     var lists=repo.Lists.Where(list=>string.Equals(list.S("name"),folder,StringComparison.OrdinalIgnoreCase)).ToList();
     if(lists.Count==0)return "本机清单中没有找到「"+folder+"」。";
     if(lists.Count>1)return "本机有多个名为「"+folder+"」的清单，请先区分清单名称。";
     string id=lists[0].S("list_id");items=items.Where(item=>item.ListId==id);break;
   }

   var lines=new StringBuilder();
   int count=0;
   foreach(var item in items){
    DateTime itemStart,itemEnd;
    if(!Repository.TryScheduleWindow(item,out itemStart,out itemEnd)||itemEnd<start||itemStart>end)continue;
    string when=item.Start+(string.IsNullOrEmpty(item.Time)?"":" "+item.Time);
    if(item.End!=item.Start)when+=" 至 "+item.End;
    string endTime=item.Occurrence.S("end_time",item.Task.S("end_time"));
    if(!string.IsNullOrEmpty(endTime)&&(endTime!=item.Time||item.End!=item.Start))
     when+=(item.End==item.Start?" 至 ":" ")+endTime;
    lines.AppendLine().Append(when).Append(" · ").Append(item.Title).Append(item.Completed?"（已完成）":"（待完成）");
    count++;
   }
   return "本机"+(repo.Demo?"体验数据":"已载入的日程")+" · "+folder+"\n"+first+" 至 "+last+"\n"+
    (count==0?"该范围内没有匹配的事项。":"共 "+count+" 条"+lines.ToString());
  }

  static string DraftDetails(AssistantSuggestion task){
   var details=new List<string>();
   string schedule=task.Date??"未安排日期";
   if(task.Repeat!=null){
    if(task.EndDate!=null&&task.EndDate!=task.Date)schedule+=" 至 "+task.EndDate;
    if(task.StartTime!=null)schedule+=" · 每次 "+task.StartTime;
    if(task.EndTime!=null&&task.EndTime!=task.StartTime)schedule+=" 至 "+task.EndTime;
   }else{
    if(task.StartTime!=null)schedule+=" "+task.StartTime;
    if(task.EndDate!=null&&task.EndDate!=task.Date)schedule+=" 至 "+task.EndDate+(task.EndTime==null?"":" "+task.EndTime);
    else if(task.EndTime!=null&&task.EndTime!=task.StartTime)schedule+=" 至 "+task.EndTime;
   }
   details.Add(schedule+(task.Important?" · 重要":"")+(task.Urgent?" · 紧急":"")+(task.Collected?" · 收藏":""));
   if(task.FolderName!=null)details.Add("清单 · "+task.FolderName);
   if(task.ReminderDate!=null)details.Add("提醒 · "+task.ReminderDate+" "+task.ReminderTime);
   if(task.Repeat!=null)details.Add("循环 · "+task.Repeat.Description);
   if(task.Location!=null)details.Add("地点 · "+task.Location);
   if(task.TargetCount.HasValue)details.Add("完成次数 · "+task.TargetCount.Value);
   if(task.Subtasks!=null&&task.Subtasks.Length>0)details.Add("子任务\n· "+string.Join("\n· ",task.Subtasks));
   if(task.Remark!=null)details.Add("备注 · "+task.Remark);
   return string.Join("\n",details);
  }

  FrameworkElement FocusCard(AssistantFocus focus){
   var row=U.Grid("*,Auto");
   var label=U.Text(focus.Title+" · "+focus.Minutes+" 分钟",13,"#513D24",true);
   label.TextWrapping=TextWrapping.Wrap;U.Place(row,label);
   var start=U.Button("开始专注",null,"Secondary");
   start.Tag=focus;start.Margin=new Thickness(9,0,0,0);U.Place(row,start,1);
   ToggleSuggestionButtons(start);
   start.Click+=delegate{
    if(!start.IsEnabled)return;
    if(app.FocusStarted.HasValue){
     ToggleSuggestionButtons(start);
     status.Text="请先在「专注时刻」保存或结束当前专注。";return;
    }
    start.IsEnabled=false;start.Content="已开始";
    app.StartFocus(focus.Minutes,focus.Title);
    status.Text="已开始「"+focus.Title+"」专注，可在「专注时刻」查看。";
    UpdateAvailability();
   };
   return U.Card(row,"#FFFBF3",10,new Thickness(9));
  }
 }
}
