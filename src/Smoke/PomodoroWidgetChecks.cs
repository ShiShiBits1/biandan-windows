using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Biandan {
 public sealed partial class App {
  async Task VerifyPomodoroWidgetAsync(string directory){
   var earlierRecords=Repo.Focus.ToList();
   try{
    Desktop.Navigate("widgets");
    Desktop.UpdateLayout();
    var add=VisualControls<Button>(Desktop).Single(button=>
     AutomationProperties.GetName(button)=="添加番茄钟小组件");
    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    var widget=Widgets["focus"];
    await Task.Delay(150);
    if(!Preferences.GetBool("widget.focus.visible"))
     throw new InvalidOperationException("Opening the Pomodoro widget must remember its visibility.");

    var clock=PomodoroClock(widget);
    var duration=VisualControls<ComboBox>(widget).Single(box=>
     AutomationProperties.GetName(box)=="番茄钟时长");
    if(clock.Text!="25:00")throw new InvalidOperationException("The Pomodoro widget must initially show 25 minutes.");
    U.Capture(widget,Path.Combine(directory,"widget-pomodoro.png"));
    duration.SelectedIndex=0;
    if(clock.Text!="15:00")throw new InvalidOperationException("The widget must preview the selected duration.");
    Repo.Publish();
    if(duration.SelectedIndex!=0||clock.Text!="15:00")
     throw new InvalidOperationException("Refreshing data must not reset a selected widget duration.");

    ClickBulkButton(widget,"开始专注");
    var started=FocusStarted;
    if(!started.HasValue||FocusMinutes!=15||!FocusEnd.HasValue)
     throw new InvalidOperationException("Starting in the widget did not start the shared timer.");
    widget.Close();
    if(!FocusEnd.HasValue||Preferences.GetBool("widget.focus.visible"))
     throw new InvalidOperationException("Closing the widget must keep the timer running and remember that it was closed.");
    ToggleWidget("focus");
    widget=Widgets["focus"];
    Desktop.Navigate("focus");
    Desktop.UpdateLayout();
    FocusEnd=DateTime.Now.AddSeconds(30);
    UpdateFocusTimer();
    string remaining=PomodoroClock(widget).Text;
    if(!remaining.StartsWith("00:")||!VisualControls<TextBlock>(Desktop).Any(text=>text.IsVisible&&text.Text==remaining))
     throw new InvalidOperationException("The widget and the main window must display the same remaining time.");

    FocusStarted=DateTime.Now.AddMinutes(-16);
    FocusEnd=DateTime.Now.AddSeconds(-1);
    UpdateFocusTimer();
    if(FocusEnd.HasValue||PomodoroClock(widget).Text!="00:00"||
       VisualControls<ProgressBar>(widget).Single().Value!=1||
       !VisualControls<TextBlock>(Desktop).Any(text=>text.IsVisible&&text.Text=="完成了"))
     throw new InvalidOperationException("Countdown completion must be visible in both windows.");
    ClickBulkButton(widget,"保存本次专注");
    if(FocusStarted.HasValue||Repo.Focus.Count!=earlierRecords.Count+1||
       Repo.Focus.Last().I("good_count")!=1||Repo.Focus.Last().I("bad_count")!=0)
     throw new InvalidOperationException("Saving from the widget must record one completed Pomodoro and reset both windows.");

    StartFocus(45,"主窗口专注");
    if(PomodoroClock(widget).Text!="45:00"||
       !VisualControls<TextBlock>(widget).Any(text=>text.IsVisible&&text.Text=="主窗口专注"))
     throw new InvalidOperationException("The widget did not pick up the session started in the main window.");
   }finally{
    DiscardFocus();
    Repo.Focus=earlierRecords;
    Repo.Persist();
    Repo.Publish();
    WidgetView open;
    if(Widgets.TryGetValue("focus",out open))open.Close();
    Desktop.Navigate("widgets");
   }
  }

  static TextBlock PomodoroClock(WidgetView widget){
   widget.UpdateLayout();
   return VisualControls<TextBlock>(widget).Single(text=>AutomationProperties.GetName(text)=="番茄钟剩余时间");
  }
 }
}
