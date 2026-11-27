using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Biandan {
 public sealed partial class App {
  void VerifyBulkToolbarLayout(){
   double width=Desktop.Width,height=Desktop.Height;
   try{
    Desktop.Width=Desktop.MinWidth;Desktop.Height=Desktop.MinHeight;Desktop.UpdateLayout();
    var start=VisualControls<Button>(Desktop).Single(b=>Equals(b.Content,"多选"));
    var label=VisualControls<TextBlock>(Desktop).Single(t=>t.Text=="待办事项");
    var labelOrigin=label.TranslatePoint(new Point(),Desktop);
    var startOrigin=start.TranslatePoint(new Point(),Desktop);
    if(startOrigin.Y>=labelOrigin.Y+label.ActualHeight||startOrigin.X<labelOrigin.X+label.DesiredSize.Width)
     throw new InvalidOperationException("The selection entry must share the section heading without overlapping its title.");
    start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Desktop.UpdateLayout();
    var results=VisualControls<TaskResultsList>(Desktop).Single();
    Rect bounds=new Rect(results.TranslatePoint(new Point(),Desktop),results.RenderSize);
    var actions=VisualControls<Button>(Desktop).Where(b=>b.IsVisible&&new[]{"全选","移到清单","改日期","批量删除","退出多选"}.Contains(b.Content as string)).ToArray();
    if(actions.Length!=5)throw new InvalidOperationException("The selection toolbar lost actions at the minimum window width.");
    foreach(var button in actions){
     var rect=new Rect(button.TranslatePoint(new Point(),Desktop),button.RenderSize);
     if(rect.Left<bounds.Left-1||rect.Right>bounds.Right+1||rect.Top<labelOrigin.Y+label.ActualHeight-1||rect.Bottom>bounds.Top)
      throw new InvalidOperationException("Selection actions must fit below the heading and above the task viewport.");
    }
    actions.Single(b=>Equals(b.Content,"退出多选")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   }finally{Desktop.Width=width;Desktop.Height=height;Desktop.UpdateLayout();}
  }

  void VerifyTaskListVirtualization(){
   const string prefix="virtual-list-smoke-";
   const int count=1200;
   try{
    for(int i=0;i<count;i++){
     string id=prefix+i;
     Repo.Tasks.Add(J.Obj("task_id",id,"title",id,"list_id","1","undated",true,"remark",i==311?"smoke-remark-match":""));
     Repo.Occurrences.Add(J.Obj("task_id",id,"occurrence_id",id,"status",i==0?"completed":"pending"));
    }
    Repo.Publish();
    Desktop.Navigate("all");Desktop.UpdateLayout();VerifyBulkToolbarLayout();
    var list=VisualControls<TaskResultsList>(Desktop).Single();
    if(list.Items.Count<count-1||!VirtualizingPanel.GetIsVirtualizing(list)||VisualControls<ListBoxItem>(list).Count()>100)
     throw new InvalidOperationException("The all-tasks page must realize only the scrolling viewport.");
    var completed=list.Items.OfType<Expander>().First();
    if(completed.IsExpanded||list.Items.Contains(prefix+"0"))throw new InvalidOperationException("A collapsed section materialized completed occurrences.");
    completed.IsExpanded=true;
    if(!list.Items.Contains(prefix+"0"))throw new InvalidOperationException("Expanding the completed section lost its occurrences.");

    var input=Controls<TextBox>(Desktop).Single(box=>AutomationProperties.GetName(box)=="快速添加待办");
    input.Text="draft";input.Focus();input.Select(1,2);
    bool focused=input.IsKeyboardFocused;
    var viewer=VisualControls<ScrollViewer>(list).First();
    viewer.ScrollToVerticalOffset(600);Desktop.UpdateLayout();
    double offset=viewer.VerticalOffset;
    var row=VisualControls<SwipeTaskRow>(list).First();
    Repo.Publish();Desktop.UpdateLayout();
    if(offset<500||offset!=viewer.VerticalOffset||!VisualControls<SwipeTaskRow>(list).Contains(row)||
       !ReferenceEquals(input,Controls<TextBox>(Desktop).Single(box=>AutomationProperties.GetName(box)=="快速添加待办"))||
       input.Text!="draft"||input.SelectionStart!=1||input.SelectionLength!=2||focused&&!input.IsKeyboardFocused)
     throw new InvalidOperationException("Refreshing a list must preserve its realized rows, scroll anchor, and quick-entry selection.");
    input.Clear();
    var anchor=(string)list.Items[(int)viewer.VerticalOffset];
    var inserted=J.Obj("task_id",prefix+"insert","title","new item","list_id","1","undated",true,"important",true);
    var occurrence=J.Obj("task_id",prefix+"insert","occurrence_id",prefix+"insert","status","pending");
    Repo.Tasks.Add(inserted);Repo.Occurrences.Add(occurrence);Repo.Publish();Desktop.UpdateLayout();
    if(!Equals(list.Items[(int)viewer.VerticalOffset],anchor))throw new InvalidOperationException("Inserting before the viewport changed its first occurrence.");
    Repo.Tasks.Remove(inserted);Repo.Occurrences.Remove(occurrence);Repo.Publish();Desktop.UpdateLayout();
    var container=(ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(anchor);
    var more=VisualControls<Button>(container).Single(b=>Equals(b.ToolTip,"更多操作"));
    bool buttonFocused=more.Focus();
    Repo.Tasks.First(task=>task.S("task_id")==anchor)["title"]=prefix+"updated title";
    Repo.Publish();Desktop.UpdateLayout();
    if(!VisualControls<TextBlock>(container).Any(t=>t.Text==prefix+"updated title")||
       buttonFocused&&!VisualControls<Button>(container).Single(b=>Equals(b.ToolTip,"更多操作")).IsKeyboardFocused)
     throw new InvalidOperationException("Updating a visible occurrence must refresh its title and retain the focused action.");

    Exception failure=null;
    Dispatcher.BeginInvoke(new Action(async delegate{
     Window dialog=Desktop.OwnedWindows.Cast<Window>().Single(w=>w.Title=="全局搜索");
     try{
      var query=VisualControls<TextBox>(dialog).Single();
      var results=VisualControls<TaskResultsList>(dialog).Single();
      query.Text=prefix;
      await Task.Delay(220);dialog.UpdateLayout();
      if(results.Items.Count!=count||VisualControls<ListBoxItem>(results).Count()>100)
       throw new InvalidOperationException("Search must virtualize all matching occurrences.");
      var scroll=VisualControls<ScrollViewer>(results).First();scroll.ScrollToVerticalOffset(550);dialog.UpdateLayout();
      double initial=scroll.VerticalOffset;Repo.Publish();dialog.UpdateLayout();
      if(initial<500||scroll.VerticalOffset!=initial||query.Text!=prefix)throw new InvalidOperationException("Refreshing search lost the result position or text.");
      query.Text="smoke-remark-match";await Task.Delay(220);dialog.UpdateLayout();
      if(results.Items.Count!=1||!Equals(results.Items[0],prefix+"311")||scroll.VerticalOffset!=0)
       throw new InvalidOperationException("A replacement query must show the new matches from the top.");
     }catch(Exception error){failure=error;}
     finally{dialog.Close();}
    }),DispatcherPriority.ApplicationIdle);
    VisualControls<Button>(Desktop).Single(b=>AutomationProperties.GetName(b)=="全局搜索").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if(failure!=null)throw failure;
   }finally{
    Repo.Tasks.RemoveAll(t=>t.S("task_id").StartsWith(prefix,StringComparison.Ordinal));
    Repo.Occurrences.RemoveAll(o=>o.S("task_id").StartsWith(prefix,StringComparison.Ordinal));
    Repo.Publish();Desktop.Navigate("today");
   }
  }
 }
}
