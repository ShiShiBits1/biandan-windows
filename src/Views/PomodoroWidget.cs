using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Biandan {
 public sealed partial class WidgetView {
  TextBlock pomodoroClock,pomodoroCaption,pomodoroCount;
  ComboBox pomodoroDuration;
  Grid pomodoroIdle,pomodoroActive;
  ProgressBar pomodoroProgress;
  Button pomodoroSave,pomodoroDiscard;

  FrameworkElement BuildPomodoro(){
   var panel=U.Stack();
   var summary=U.Grid("*,Auto");
   pomodoroCaption=U.Text("",12,"#6E5638",true);
   pomodoroCaption.VerticalAlignment=VerticalAlignment.Center;
   pomodoroCaption.Margin=new Thickness(0,0,8,0);
   U.Place(summary,pomodoroCaption);
   pomodoroCount=U.Text("",11,"#8D560B",true);
   pomodoroCount.VerticalAlignment=VerticalAlignment.Center;
   U.Place(summary,pomodoroCount,1);
   panel.Children.Add(summary);

   pomodoroClock=U.Text("",50,"#8D560B",true);
   pomodoroClock.HorizontalAlignment=HorizontalAlignment.Center;
   pomodoroClock.Margin=new Thickness(0,7,0,7);
   Typography.SetNumeralAlignment(pomodoroClock,FontNumeralAlignment.Tabular);
   AutomationProperties.SetName(pomodoroClock,"番茄钟剩余时间");
   panel.Children.Add(pomodoroClock);

   pomodoroProgress=new ProgressBar{
    Height=5,Minimum=0,Maximum=1,
    Foreground=U.C("#D28C1C"),Background=U.C("#F5E5C5"),BorderThickness=new Thickness(0)
   };
   AutomationProperties.SetName(pomodoroProgress,"专注进度");
   panel.Children.Add(pomodoroProgress);
   U.Gap(panel,14);

   pomodoroIdle=U.Grid("Auto,8,*");
   pomodoroDuration=new ComboBox{MinWidth=92};
   foreach(int minutes in FocusDuration.Presets)pomodoroDuration.Items.Add(minutes+" 分钟");
   pomodoroDuration.SelectedIndex=Array.IndexOf(FocusDuration.Presets,25);
   AutomationProperties.SetName(pomodoroDuration,"番茄钟时长");
   pomodoroDuration.SelectionChanged+=delegate{TickPomodoro();};
   U.Place(pomodoroIdle,pomodoroDuration);
   var start=U.Button("开始专注",()=>{
    if(pomodoroDuration.SelectedIndex<0)return;
    app.StartFocus(FocusDuration.Presets[pomodoroDuration.SelectedIndex],"专注当下");
   },"Primary");
   U.Place(pomodoroIdle,start,2);
   panel.Children.Add(pomodoroIdle);

   pomodoroActive=U.Grid("*,8,Auto");
   pomodoroSave=U.Button("保存本次专注",async()=>{
    try{await app.SaveFocusAsync();}
    catch(Exception error){U.Error(IsVisible?(Window)this:app.Desktop,error);}
   },"Primary");
   U.Place(pomodoroActive,pomodoroSave);
   pomodoroDiscard=U.Button("放弃本次",()=>app.ConfirmDiscardFocus(this),"Secondary");
   U.Place(pomodoroActive,pomodoroDiscard,2);
   panel.Children.Add(pomodoroActive);
   return panel;
  }

  void RefreshPomodoro(){
   bool active=app.FocusStarted.HasValue;
   pomodoroIdle.Visibility=active?Visibility.Collapsed:Visibility.Visible;
   pomodoroActive.Visibility=active?Visibility.Visible:Visibility.Collapsed;
   pomodoroSave.Content=app.SavingFocus?"正在保存…":"保存本次专注";
   pomodoroSave.IsEnabled=pomodoroDiscard.IsEnabled=!app.SavingFocus;
   pomodoroCaption.ToolTip=active?app.FocusTitle:null;
   pomodoroCount.Text="今日 "+FocusDuration.Tomatoes(FocusDuration.Today(repo.Focus))+" 颗";
   TickPomodoro();
  }

  internal void TickPomodoro(){
   bool active=app.FocusStarted.HasValue;
   bool finished=active&&!app.FocusEnd.HasValue;
   int seconds=active?app.FocusSecondsLeft:
    pomodoroDuration.SelectedIndex>=0?FocusDuration.Presets[pomodoroDuration.SelectedIndex]*60:0;
   pomodoroCaption.Text=finished?"计时结束 · "+app.FocusTitle:active?app.FocusTitle:"准备专注";
   pomodoroClock.Text=App.FormatFocusClock(seconds);
   pomodoroProgress.Value=active?1-(double)seconds/(app.FocusMinutes*60):0;
  }
 }
}
