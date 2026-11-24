using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Biandan {
 public sealed partial class App {
  static void ClickBulkButton(Window window,string label){
   window.UpdateLayout();
   var button=VisualControls<Button>(window).Single(b=>b.IsVisible&&Equals(b.Content,label));
   if(!button.IsEnabled)throw new InvalidOperationException("Disabled action: "+label);
   button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  }

  void SubmitBulkDialog(string command,string title,string accept,Action<Window> configure){
   ClickBulkButton(Desktop,"多选");
   ClickBulkButton(Desktop,"全选");
   Exception problem=null;
   Dispatcher.BeginInvoke(new Action(delegate{
    var dialog=Desktop.OwnedWindows.Cast<Window>().SingleOrDefault(w=>w.Title==title&&w.IsVisible);
    try{
     if(dialog==null)throw new InvalidOperationException("Missing dialog: "+title);
     dialog.UpdateLayout();
     configure(dialog);
     ClickBulkButton(dialog,accept);
    }catch(Exception error){
     problem=error;
     if(dialog!=null&&dialog.IsVisible)dialog.Close();
    }
   }),DispatcherPriority.ApplicationIdle);
   ClickBulkButton(Desktop,command);
   if(problem!=null)throw new InvalidOperationException("Could not submit "+title,problem);
  }

  void VerifyBulkActions(){
   string list=J.NewId(),first=J.NewId(),second=J.NewId();
   Repo.DemoCall("create_list",J.Obj("operation_id",list,"name","批量验收"));
   Repo.DemoCall("create_task",J.Obj("operation_id",first,"list_id",list,"title","跨日待办","start_date",J.Today,
    "end_date",J.Date(DateTime.Today.AddDays(1)),"start_time","23:00","end_time","01:30"));
   Repo.DemoCall("create_task",J.Obj("operation_id",second,"list_id",list,"title","无日期待办"));
   Repo.Publish();
   try{
    Desktop.Navigate("list:"+list);
    var target=DateTime.Today.AddDays(4);
    SubmitBulkDialog("改日期","批量改日期","确认改期",dialog=>VisualControls<DatePicker>(dialog).Single().SelectedDate=target);
    var moved=Repo.Items().Single(item=>item.Id==first);
    if(moved.Start!=J.Date(target)||moved.End!=J.Date(target.AddDays(1))||
      moved.Time!="23:00"||moved.Occurrence.S("end_time")!="01:30"||
      Repo.Items().Single(item=>item.Id==second).Start!=J.Date(target))
     throw new InvalidOperationException("Batch date change must preserve intervals and schedule undated tasks.");
    SubmitBulkDialog("移到清单","批量移到清单","确认移动",dialog=>{
     VisualControls<ComboBox>(dialog).Single().SelectedIndex=Repo.Lists.FindIndex(row=>row.S("list_id")=="1");
    });
    if(Repo.Items().Any(item=>(item.Id==first||item.Id==second)&&item.ListId!="1"))
     throw new InvalidOperationException("Batch move must update every selected task.");
   }finally{
    Desktop.Navigate("today");
    Repo.DemoCall("delete_task",J.Obj("task_id",first));
    Repo.DemoCall("delete_task",J.Obj("task_id",second));
    Repo.DemoCall("delete_list",J.Obj("list_id",list));
    Repo.Persist();
    Repo.Publish();
   }
  }
 }
}