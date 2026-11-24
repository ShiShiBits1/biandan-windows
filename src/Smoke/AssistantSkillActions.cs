using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Biandan {
 public sealed partial class App {
  void VerifyAssistantSkillActions(AssistantView view){
   var query=new AssistantQuery{StartDate=J.Today,EndDate=J.Today,FolderName="全部待办"};
   var reply=new AssistantReply{Query=query,Answer="查询中"};
   var item=Repo.Items().First(value=>!value.Undated&&!value.Abandoned&&value.OnDate(J.Today));
   view.ShowReply(reply,false);
   Desktop.UpdateLayout();
   if(!VisualControls<TextBlock>(view).Any(text=>text.Text.Contains("请勾选")))
    throw new InvalidOperationException("Task queries require permission to access local data.");
   view.ShowReply(reply,true);
   Desktop.UpdateLayout();
   if(!VisualControls<TextBlock>(view).Any(text=>text.Text.Contains(item.Title)))
    throw new InvalidOperationException("The assistant must show matching tasks from the current repository.");

   var focus=new AssistantFocus{Minutes=30,Title="整理报告"};
   view.ShowReply(new AssistantReply{Focus=focus,Answer="准备专注"},false);
   Desktop.UpdateLayout();
   var start=VisualControls<Button>(view).Single(button=>ReferenceEquals(button.Tag,focus));
   if(FocusStarted.HasValue||!start.IsEnabled)throw new InvalidOperationException("The focus card requires a click to start.");
   start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   var started=FocusStarted;
   if(!started.HasValue||FocusMinutes!=30||FocusTitle!=focus.Title)
    throw new InvalidOperationException("The assistant did not start the requested focus session.");
   start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if(FocusStarted!=started)throw new InvalidOperationException("The assistant restarted an active focus session.");
   DiscardFocus();
  }
 }
}
