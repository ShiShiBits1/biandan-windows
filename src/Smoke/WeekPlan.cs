using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Biandan {
 public sealed partial class App {
  async Task VerifyWeekReschedulingAsync(){
   var monday=MainView.WeekStart(DateTime.Today);
   string title="跨午夜排期验收";
   var created=new HashSet<string>();
   try{
    string one=J.NewId();created.Add(one);
    await Repo.CallAsync("create_task",J.Obj("title",title,"operation_id",one,"start_date",J.Date(monday),
     "start_time","11:00","end_time","12:30","reminder_date",J.Date(monday),"reminder_time","10:00"));
    var occurrence=Repo.Items().Single(i=>i.Id==one).OccurrenceId;
    await Repo.RescheduleItemAsync(occurrence,monday.AddDays(1),23*60+30);
    Func<TaskItem> current=()=>Repo.Items().Single(i=>i.OccurrenceId==occurrence);
    var moved=current();
    if(moved.Start!=J.Date(monday.AddDays(1))||moved.End!=J.Date(monday.AddDays(2))||moved.Time!="23:30"||
       moved.Occurrence.S("end_time")!="01:00"||moved.Task.D("time_reminder").S("date")!=J.Date(monday))
     throw new InvalidOperationException("An overnight move must keep its duration and original reminder.");
    Desktop.UpdateLayout();
    Func<List<Button>> blocks=()=>VisualControls<Button>(Desktop).Where(b=>AutomationProperties.GetName(b)=="周计划："+title).ToList();
    if(blocks().Count!=2||!blocks().Any(b=>Convert.ToString(b.ToolTip).Contains("23:30–24:00"))||
       !blocks().Any(b=>Convert.ToString(b.ToolTip).Contains("00:00–01:00")))
     throw new InvalidOperationException("An overnight appointment must be split at midnight on the timeline.");
    await Repo.RescheduleItemAsync(occurrence,monday.AddDays(2));
    if(current().Time!="23:30"||current().End!=J.Date(monday.AddDays(3)))throw new InvalidOperationException("Moving only the date must retain both clocks.");
    await Repo.RescheduleItemAsync(occurrence,monday,22*60+30);
    Desktop.UpdateLayout();
    if(blocks().Count!=1||current().Occurrence.S("end_time")!="00:00")throw new InvalidOperationException("The midnight endpoint must not appear twice.");
    await Repo.CallAsync("complete_occurrence",J.Obj("occurrence_id",occurrence));
    if(Repo.CanRescheduleItem(current()))throw new InvalidOperationException("Completed appointments must not be draggable.");

    string undated=J.NewId();created.Add(undated);
    await Repo.CallAsync("create_task",J.Obj("title","待安排验收","operation_id",undated));
    await Repo.ScheduleUndatedAsync(undated,monday,1425);
    var late=Repo.Items().Single(i=>i.Id==undated);
    if(late.Start!=late.End||late.Occurrence.S("end_time")!="23:59")throw new InvalidOperationException("A new late-night appointment ends on the chosen day.");

    string series=J.NewId();created.Add(series);
    await Repo.CallAsync("create_task",J.Obj("title","循环改期验收","operation_id",series,"start_date",J.Date(monday),
     "end_date",J.Date(monday.AddDays(2)),"start_time","10:00","end_time","11:00","repeat_rule",J.Obj("mode","daily")));
    var selected=Repo.Items().First(i=>i.Id==series);
    await Repo.RescheduleItemAsync(selected.OccurrenceId,monday,600);
    if(!Repo.Items().Single(i=>i.OccurrenceId==selected.OccurrenceId).Repeating)throw new InvalidOperationException("An unchanged drop must not detach an occurrence.");
    await Repo.RescheduleItemAsync(selected.OccurrenceId,monday.AddDays(4),660);
    var detached=Repo.Items().Single(i=>i.OccurrenceId==selected.OccurrenceId);created.Add(detached.Id);
    if(detached.Id==series||detached.Repeating||detached.Time!="11:00"||Repo.Items().Count(i=>i.Id==series)!=2)
     throw new InvalidOperationException("Rescheduling a repeat occurrence must move only that occurrence.");
   }finally{
    Repo.Tasks.RemoveAll(t=>created.Contains(t.S("task_id")));
    Repo.Occurrences.RemoveAll(o=>created.Contains(o.S("task_id")));
    Repo.Persist();Repo.Publish();
   }
  }
 }
}
