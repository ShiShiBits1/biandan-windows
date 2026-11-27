using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Biandan {
 public sealed partial class App {
  void VerifyRailDateFilter(){
   string spans=J.NewId(),tomorrowOnly=J.NewId(),undated=J.NewId();
   var tomorrow=DateTime.Today.AddDays(1);
   string date=J.Date(tomorrow);
   Repo.DemoCall("create_task",J.Obj("operation_id",spans,"title","跨日筛选验收","start_date",J.Today,"end_date",date));
   Repo.DemoCall("create_task",J.Obj("operation_id",tomorrowOnly,"title","明日筛选验收","start_date",date));
   Repo.DemoCall("create_task",J.Obj("operation_id",undated,"title","无日期筛选验收"));
   Repo.Publish();
   try{
    var expected=new HashSet<string>(Repo.Items()
     .Where(item=>item.OnDate(date)&&!item.Completed&&!item.Abandoned).Select(item=>item.OccurrenceId));
    var pages=new[]{"today","upcoming","all"};
    var title=new[]{"我的一天","最近七天","全部待办"};
    for(int i=0;i<pages.Length;i++){
     Desktop.Navigate(pages[i]);
     Desktop.UpdateLayout();
     var list=VisualControls<TaskResultsList>(Desktop).Single();
     var original=list.Items.OfType<string>().ToArray();
     var calendar=VisualControls<MonthCalendar>(Desktop).Single(c=>AutomationProperties.GetName(c)=="右侧小日历");
     var day=VisualControls<Button>(calendar).Single(button=>AutomationProperties.GetName(button)==tomorrow.ToString("yyyy 年 M 月 d 日"));
     day.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     Desktop.UpdateLayout();
     if(!DesktopContainsPageTitle(title[i])||!expected.SetEquals(list.Items.OfType<string>())||calendar.Selected!=tomorrow)
      throw new InvalidOperationException("Using the side calendar must filter in place, including tasks spanning the selected date.");
     ClickBulkButton(Desktop,"清除日期筛选");
     if(!original.SequenceEqual(list.Items.OfType<string>())||calendar.Selected.HasValue)
      throw new InvalidOperationException("Clearing the selected date must restore the current page.");
    }
   }finally{
    foreach(string id in new[]{spans,tomorrowOnly,undated})Repo.DemoCall("delete_task",J.Obj("task_id",id));
    Repo.Persist();
    Repo.Publish();
    Desktop.Navigate("today");
   }
  }

  bool DesktopContainsPageTitle(string title){
   return VisualControls<TextBlock>(Desktop).Any(text=>text.IsVisible&&text.FontSize>=28&&text.Text==title);
  }
 }
}
