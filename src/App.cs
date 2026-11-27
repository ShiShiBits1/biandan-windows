using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Windows.Media;
using Forms=System.Windows.Forms;
namespace Biandan {
 public sealed partial class App:Application {
  public static readonly string BasePath=AppDomain.CurrentDomain.BaseDirectory;
  public static App CurrentApp;
  public Repository Repo;public Settings Preferences;public MainView Desktop;public LoginView Login;
  static readonly string[] widgetKinds={"calendar","todo","focus"};
  public readonly Dictionary<string,WidgetView> Widgets=new Dictionary<string,WidgetView>();
  public bool Quitting,Smoke;public DateTime? FocusEnd,FocusStarted;public int FocusMinutes=25;public string FocusTitle="专注当下";public string FocusOccurrence;
  internal bool SavingFocus{get;private set;}
  Forms.NotifyIcon tray;Mutex mutex;DispatcherTimer timer,appearanceTimer;int ticks;
  DateTime nextAutomaticSync,lastInputUtc=DateTime.UtcNow,nextTaskSyncRetry;
  bool automaticSyncRunning;readonly HashSet<string> notified=new HashSet<string>();
  [STAThread]public static void Main(string[] args){
   if(args.Length>0&&args[0]==BrowserHandoff.Argument){Environment.ExitCode=BrowserHandoff.Send(args);return;}
   var app=new App();CurrentApp=app;app.Smoke=args.Contains("--smoke");
   bool created=true;if(!app.Smoke){app.mutex=new Mutex(true,@"Local\BiandanDesktop-ShiShiBits",out created);if(!created){MessageBox.Show("便单已经在运行，请从系统托盘打开。","便单");return;}}
   app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
   Theme.Apply(app.Resources,Theme.DefaultHex);
   using(var stream=typeof(App).Assembly.GetManifestResourceStream("Biandan.Theme.xaml"))app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
   app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){try{if(app.Desktop!=null)U.Error(app.Desktop,e.Exception);else MessageBox.Show(U.Friendly(e.Exception.Message),"便单");}finally{e.Handled=true;}};
   string state=app.Smoke?Path.Combine(BasePath,"smoke-state"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BiandanDesktop");
   app.Preferences=new Settings(state);Theme.Apply(app.Resources,app.Preferences.Values.S("themeColor",Theme.DefaultHex));app.Repo=new Repository(app.Preferences,new McpClient(Path.Combine(state,"mcp")));
   app.Repo.Client.AuthorizationRequested+=address=>{
    if(app.Smoke)return;
    app.Dispatcher.BeginInvoke(new Action(()=>{
     if(app.Quitting||!Equals(address,app.Repo.Client.AuthorizationUri))return;
     if(app.Desktop!=null&&!app.Repo.Demo)app.Desktop.CloseAccountDetails();
     app.Repo.InvalidatePreviousAccount();
     if(app.Login==null||!app.Login.IsVisible)app.ShowLogin();
     app.Login.ReceiveAuthorization(address);
    }));
   };
   app.Startup+=async delegate{
    if(!app.Smoke)app.CreateTray();
    app.ScheduleAutomaticSync();
    InputManager.Current.PreProcessInput+=delegate{app.lastInputUtc=DateTime.UtcNow;};
    app.timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};app.timer.Tick+=app.Tick;app.timer.Start();
    if(args.Contains("--demo")||app.Smoke||app.Preferences.Values.S("lastMode")=="demo"){app.Repo.OpenDemo();app.ShowDesktop();}
    else if(app.Repo.Client.HasCredential){app.Repo.OpenCache();app.ShowDesktop();try{await app.Repo.LoginAsync(false);}catch(Exception e){app.Desktop.SetStatus(U.Friendly(e.Message),true);}}
    else app.ShowLogin();
    if(app.Smoke){
     try{await app.SmokeAsync(args.Contains("--smoke-auth"));}
     catch(Exception error){
      string directory=Path.Combine(BasePath,"smoke");
      Directory.CreateDirectory(directory);
      File.WriteAllText(Path.Combine(directory,"failed.txt"),error.ToString());
      app.Quitting=true;
      app.Shutdown(1);
     }
    }
    if(args.Contains("--autostart")&&app.Desktop!=null)app.Desktop.Hide();
   };
   app.Exit+=delegate{if(app.timer!=null)app.timer.Stop();if(app.appearanceTimer!=null&&app.appearanceTimer.IsEnabled){app.appearanceTimer.Stop();app.Preferences.Save();}app.Repo.Client.Dispose();if(app.tray!=null){var icon=app.tray.Icon;app.tray.Dispose();if(icon!=null)icon.Dispose();}if(app.mutex!=null){app.mutex.ReleaseMutex();app.mutex.Dispose();}};
   app.Run();
  }
  void CreateTray(){
   using(var iconStream=BundledBackend.OpenResource("Biandan.App.ico"))
   using(var icon=new System.Drawing.Icon(iconStream))
    tray=new Forms.NotifyIcon{Icon=(System.Drawing.Icon)icon.Clone(),Text="便单",Visible=true};
   var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开便单",null,delegate{Dispatcher.Invoke(ShowDesktop);});
   menu.Items.Add("桌面日历",null,delegate{Dispatcher.Invoke(()=>ToggleWidget("calendar"));});
   menu.Items.Add("桌面待办",null,delegate{Dispatcher.Invoke(()=>ToggleWidget("todo"));});
   menu.Items.Add("桌面番茄钟",null,delegate{Dispatcher.Invoke(()=>ToggleWidget("focus"));});
   menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出便单",null,delegate{Dispatcher.Invoke(Quit);});
   tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{Dispatcher.Invoke(ShowDesktop);};
  }
  public void ShowLogin(){
   if(Login==null){Login=new LoginView(this);Login.Closed+=delegate{Login=null;};}
   Login.Show();Login.Activate();if(Desktop!=null)Desktop.Hide();
  }
  public void ShowDesktop(){
   if(Repo.Tasks.Count==0&&!Repo.Demo&&!Repo.Client.HasCredential&&!Repo.Connected){ShowLogin();return;}
   if(Desktop==null||!Desktop.IsLoaded){Desktop=new MainView(this);MainWindow=Desktop;}
   Desktop.Show();Desktop.WindowState=WindowState.Normal;Desktop.Activate();
   if(Login!=null)Login.Hide();
   foreach(var kind in widgetKinds)if(Preferences.GetBool("widget."+kind+".visible")&&!Widgets.ContainsKey(kind))ToggleWidget(kind);
  }
  public void ToggleWidget(string kind){
   WidgetView w;if(Widgets.TryGetValue(kind,out w)){w.Close();return;}
   if(!Repo.Demo&&!Repo.Connected&&Repo.Tasks.Count==0){ShowLogin();return;}
   w=new WidgetView(this,kind);
   Widgets[kind]=w;
   Preferences.Set("widget."+kind+".visible",true);
   w.Closed+=delegate{
    Widgets.Remove(kind);
    if(Quitting)return;
    Preferences.Set("widget."+kind+".visible",false);
    if(Desktop!=null)Desktop.RefreshWidgetsPage();
   };
   w.Show();
   if(Desktop!=null)Desktop.RefreshWidgetsPage();
  }
  public void SetThemeColor(string hex){
   Preferences.Set("themeColor",hex);
   Theme.Apply(Resources,hex);
   foreach(var widget in Widgets.Values)widget.SetBackgroundTransparency(Preferences.WidgetTransparency);
  }
  public void SetWidgetTransparency(int percent){
   if(Preferences.WidgetTransparency==percent)return;
   Preferences.Values["widgetTransparency"]=percent;
   foreach(var widget in Widgets.Values)widget.SetBackgroundTransparency(percent);
   if(appearanceTimer==null){
    appearanceTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(220)};
    appearanceTimer.Tick+=delegate{appearanceTimer.Stop();Preferences.Save();};
   }
   appearanceTimer.Stop();appearanceTimer.Start();
  }
  public void Quit(){
   if(SavingFocus)return;
   if(FocusStarted.HasValue&&!U.Confirm(Desktop,"还有一条专注记录","退出后计时将结束，未保存的本次专注不会自动写入云端。","退出"))return;
   Quitting=true;if(Login!=null)Login.Close();foreach(var w in Widgets.Values.ToArray())w.Close();if(Desktop!=null)Desktop.Close();Shutdown();
  }
  public void Notify(string title,string body){if(Preferences.GetBool("notifications",true)&&tray!=null){tray.BalloonTipTitle=title;tray.BalloonTipText=body;tray.ShowBalloonTip(5000);}}
  public void StartFocus(int minutes,string title,string occurrence=null){
   if(FocusStarted.HasValue)return;
   var now=DateTime.Now;
   FocusStarted=now;FocusEnd=now.AddMinutes(minutes);FocusMinutes=minutes;FocusTitle=title;FocusOccurrence=occurrence;
   Repo.Publish();
  }
  internal int FocusSecondsLeft{
   get{return FocusEnd.HasValue?(int)Math.Ceiling(Math.Max(0,(FocusEnd.Value-DateTime.Now).TotalSeconds)):0;}
  }
  internal static string FormatFocusClock(int seconds){
   return (seconds/60).ToString("00")+":"+(seconds%60).ToString("00");
  }
  public async Task SaveFocusAsync(){
   if(!FocusStarted.HasValue||SavingFocus)return;
   var started=FocusStarted.Value;
   int minutes=(int)Math.Floor((DateTime.Now-started).TotalMinutes);
   if(minutes<1)throw new McpException("已专注不足 1 分钟，继续一会儿再保存。");
   var args=J.Obj("started_at",started.ToString("yyyy-MM-ddTHH:mm:ss"),"minutes",Math.Min(minutes,1440),"title",FocusTitle,"good_count",minutes>=FocusMinutes?1:0,"bad_count",minutes<FocusMinutes?1:0);
   if(!string.IsNullOrEmpty(FocusOccurrence))args["occurrence_id"]=FocusOccurrence;
   SavingFocus=true;
   try{
    Repo.Publish();
    await Repo.CallAsync("create_focus",args);
    FocusStarted=FocusEnd=null;
   }finally{
    SavingFocus=false;
    Repo.Publish();
   }
  }
  internal void ConfirmDiscardFocus(Window owner){
   var started=FocusStarted;
   if(started.HasValue&&!SavingFocus&&U.Confirm(owner,"放弃本次专注","本次计时将清除，不写入专注记录。","放弃")&&FocusStarted==started)
    DiscardFocus();
  }
  public void DiscardFocus(){
   if(SavingFocus||!FocusStarted.HasValue)return;
   FocusStarted=FocusEnd=null;
   Repo.Publish();
  }
  void UpdateFocusTimer(){
   if(FocusEnd.HasValue&&DateTime.Now>=FocusEnd.Value){
    FocusEnd=null;
    Notify("专注结束",FocusTitle+" · "+FocusMinutes+" 分钟。可在番茄钟或专注时刻保存本次记录。");
   }
   if(Desktop!=null)Desktop.TickFocus();
   WidgetView widget;
   if(Widgets.TryGetValue("focus",out widget))widget.TickPomodoro();
  }
  void Tick(object sender,EventArgs e){
   ticks++;
   UpdateFocusTimer();
   if(ticks%20==0&&Repo.CanWrite)CheckReminders();
   if(!Repo.Demo&&!automaticSyncRunning&&!Repo.Syncing&&AutomaticSyncDue(DateTime.UtcNow))AutoSync();
   if(ticks%60==0)Repo.Publish();
  }
  void ScheduleAutomaticSync(){
   nextAutomaticSync=DateTime.UtcNow.AddMinutes(Preferences.SyncIntervalMinutes);
  }
  public void SetSyncInterval(int minutes){
   if(Preferences.SyncIntervalMinutes==minutes)return;
   if(Array.IndexOf(Settings.SyncIntervals,minutes)<0)throw new ArgumentOutOfRangeException("minutes");
   Preferences.Set("syncIntervalMinutes",minutes);
   ScheduleAutomaticSync();
  }
  bool PendingTaskUploadDue(DateTime utcNow){
   return Repo.PendingTaskCount>0&&utcNow>=nextTaskSyncRetry&&utcNow>=lastInputUtc.AddSeconds(5)&&Repo.PendingTaskSyncDue(utcNow);
  }
  bool AutomaticSyncDue(DateTime utcNow){
   if(PendingTaskUploadDue(utcNow))return true;
   if(utcNow<nextAutomaticSync)return false;
   return !Repo.LastSync.HasValue||utcNow>=Repo.LastSync.Value.ToUniversalTime().AddMinutes(Preferences.SyncIntervalMinutes);
  }
  async void AutoSync(){
   automaticSyncRunning=true;
   try{
    if(!Repo.Connected&&!Repo.Client.HasCredential){nextTaskSyncRetry=DateTime.UtcNow.AddMinutes(1);ScheduleAutomaticSync();return;}
    bool listsOnly=Repo.HasReadyTaskWrites&&!PendingTaskUploadDue(DateTime.UtcNow);
    if(await Repo.TryAutomaticSyncAsync(listsOnly)){
     if(!listsOnly)nextTaskSyncRetry=DateTime.MinValue;
     ScheduleAutomaticSync();
    }
   }catch{
    ScheduleAutomaticSync();
    nextTaskSyncRetry=DateTime.UtcNow.AddMinutes(1);
    if(Desktop!=null)Desktop.SetStatus(Repo.PendingTaskCount>0?"● "+Repo.PendingTaskCount+" 项改动已保存在本机，稍后重试同步":"本轮同步未全部完成，稍后重试",true);
   }finally{automaticSyncRunning=false;}
  }
  void CheckReminders(){
   var past=Preferences.Values.Strings("notified");
   foreach(var item in Repo.Items().Where(t=>!t.Completed&&!t.Abandoned)){
    var r=item.Task.D("time_reminder");if(r==null)continue;DateTime at;if(!DateTime.TryParse(r.S("date")+" "+r.S("time"),out at))continue;
    string key=item.Id+"|"+at.ToString("s");if(DateTime.Now<at||DateTime.Now-at>TimeSpan.FromMinutes(2)||notified.Contains(key)||past.Contains(key))continue;
    notified.Add(key);past.Add(key);Notify("便单提醒",item.Title);
   }
   if(past.Count>300)past=past.Skip(past.Count-300).ToList();Preferences.Values["notified"]=past;Preferences.Save();
  }
  public async Task RunAction(Func<Task> action){try{await action();}catch(Exception e){U.Error(Desktop??(Window)Login,e);}}
  async Task VerifyDemoAsync(){
   if(Repo.Tasks.Select(entry=>entry.S("task_id")).Distinct().Count()!=Repo.Tasks.Count)
    throw new InvalidOperationException("Demo task IDs are duplicated.");
   if(Repo.Occurrences.Select(entry=>entry.S("occurrence_id")).Distinct().Count()!=Repo.Occurrences.Count)
    throw new InvalidOperationException("Demo occurrence IDs are duplicated.");

   string taskId=J.NewId();
   var task=J.Obj("operation_id",taskId,"title","验收循环任务","start_date",J.Today,
    "end_date",J.Date(DateTime.Today.AddDays(2)),"repeat_rule",J.Obj("mode","daily"),"subtasks",new[]{"第一步","第二步"});
   await Repo.CallAsync("create_task",task);
   await Repo.CallAsync("create_task",task);
   var series=Repo.Items().Where(item=>item.Id==taskId).ToList();
   if(series.Count!=3)throw new InvalidOperationException("Repeated creation must retain three distinct occurrences.");
   var first=series[0];
   string id=first.OccurrenceId;
   await Repo.CallAsync("complete_subtask",J.Obj("occurrence_id",id,"subtask_index",0));
   await Repo.CallAsync("complete_subtask",J.Obj("occurrence_id",id,"subtask_index",1));
   if(!Repo.Items().Single(item=>item.OccurrenceId==id).Completed||series.Skip(1).Any(item=>item.Completed))
    throw new InvalidOperationException("Checklist completion must affect exactly the chosen occurrence.");
   await Repo.CallAsync("change_progress",J.Obj("occurrence_id",id,"action","uncheck_subtask","subtask_index",1));
   if(first.Completed||first.Occurrence.I("completed_count")!=1)
    throw new InvalidOperationException("Unchecking must preserve the other completed subtask.");

   string next=J.Date(DateTime.Today.AddDays(5));
   await Repo.CallAsync("reschedule_occurrence",J.Obj("occurrence_id",id,"start_date",next));
   var moved=Repo.Items().Single(item=>item.OccurrenceId==id);
   if(moved.Repeating||moved.Id==taskId||moved.Start!=next||series[1].Start!=J.Date(DateTime.Today.AddDays(1)))
    throw new InvalidOperationException("Rescheduling must detach the chosen occurrence and preserve the series.");

   var deletion=J.Obj("task_id",taskId,"scope","series","preview",true);
   var preview=await Repo.CallAsync("delete_task",deletion);
   if(preview.Strings("occurrence_ids").Count!=2||Repo.Items().Count(item=>item.Id==taskId)!=2)
    throw new InvalidOperationException("The deletion preview must be read-only and exclude the detached occurrence.");
   deletion.Remove("preview");
   await Repo.CallAsync("delete_task",deletion);
   await Repo.CallAsync("delete_task",J.Obj("task_id",moved.Id));

   var cache=Preferences.LoadSnapshot(true);
   if(cache==null||cache.Rows("tasks").Count!=Repo.Tasks.Count)
    throw new InvalidOperationException("Demo changes did not persist.");
  }
  static IEnumerable<T> Controls<T>(DependencyObject root) where T:DependencyObject{
   if(root is T)yield return (T)root;
   foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
    foreach(var match in Controls<T>(child))yield return match;
  }

  static IEnumerable<T> VisualControls<T>(DependencyObject root) where T:DependencyObject{
   if(root is T)yield return (T)root;
   for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
    foreach(var match in VisualControls<T>(System.Windows.Media.VisualTreeHelper.GetChild(root,i)))yield return match;
  }
  void VerifyQuickEntry(){
   Desktop.Navigate("today");
   Desktop.UpdateLayout();
   var input=Controls<TextBox>(Desktop).Single(box=>AutomationProperties.GetName(box)=="快速添加待办");
   const string title="回车快录验收";
   input.Text=title;
   var args=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(input),Environment.TickCount,System.Windows.Input.Key.Enter){
    RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent
   };
   input.RaiseEvent(args);
   if(!args.Handled||input.Text.Length!=0||!Repo.Items().Any(item=>item.Title==title))
    throw new InvalidOperationException("Fast entry must clear the draft and show the new task on Enter.");
   var itemId=Repo.Items().Single(item=>item.Title==title).Id;
   Repo.DemoCall("delete_task",J.Obj("task_id",itemId));
   Repo.Publish();
  }

  void VerifyPlanningTheme(string directory,string capture,Button control,Brush highlight){
   var panel=(SolidColorBrush)control.Background;
   Func<Color> backdropColor=delegate{
    var background=((Border)Desktop.Content).Background;
    var wash=background as RadialGradientBrush;
    return wash==null?((SolidColorBrush)background).Color:wash.GradientStops[1].Color;
   };
   string selected=Theme.Selected;
   try{
    SetThemeColor(Theme.DefaultHex);
    var originalPanel=panel.Color;
    var originalShell=backdropColor();
    SetThemeColor("#7DC9A5");
    Desktop.UpdateLayout();
    if(panel.Color==originalPanel||backdropColor()==originalShell||
       ((SolidColorBrush)highlight).Color!=Color.FromRgb(125,201,165))
     throw new InvalidOperationException("The planning view must follow preset theme colors without reopening.");
    var mintPanel=panel.Color;
    SetThemeColor("#174963");
    Desktop.UpdateLayout();
    if(panel.Color==mintPanel||((SolidColorBrush)highlight).Color!=Color.FromRgb(23,73,99))
     throw new InvalidOperationException("The planning view must follow custom theme colors.");
    U.Capture(Desktop,Path.Combine(directory,capture));
   }finally{SetThemeColor(selected);}
  }

  async Task VerifyWeeklyPlanAsync(string directory){
   Desktop.Navigate("calendar");
   Action<string> click=label=>{
    Desktop.UpdateLayout();
    var button=VisualControls<Button>(Desktop).SingleOrDefault(control=>AutomationProperties.GetName(control)==label);
    if(button==null)throw new InvalidOperationException("Calendar action missing: "+label);
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Desktop.UpdateLayout();
   };
   click("周计划");
   if(!Preferences.GetBool("calendarWeek")||MainView.WeekStart(new DateTime(2020,1,1))!=new DateTime(2019,12,30))
    throw new InvalidOperationException("The week view must use Mondays and remember its selection.");
   var monday=MainView.WeekStart(DateTime.Today);
   click("下一周");
   string mondayLabel=monday.AddDays(7).ToString("M月d日")+" 周一";
   if(!VisualControls<Button>(Desktop).Any(control=>AutomationProperties.GetName(control)==mondayLabel))
    throw new InvalidOperationException("Next week must show the following Monday.");
   click("上一周");
   click("今天");
   var undated=Repo.Items().First(item=>item.Undated&&!item.Completed&&!item.Abandoned);
   if(!VisualControls<Button>(Desktop).Any(control=>AutomationProperties.GetName(control)=="待安排："+undated.Title))
    throw new InvalidOperationException("Undated pending tasks must appear in the scheduling rail.");

   await Repo.ScheduleUndatedAsync(undated.Id,DateTime.Today,630);
   Desktop.UpdateLayout();
   var scheduled=Repo.Items().Single(item=>item.Id==undated.Id);
   if(scheduled.Undated||scheduled.Time!="10:30"||
      !VisualControls<Button>(Desktop).Any(control=>AutomationProperties.GetName(control)=="周计划："+undated.Title)||
      VisualControls<Button>(Desktop).Any(control=>AutomationProperties.GetName(control)=="待安排："+undated.Title))
    throw new InvalidOperationException("Scheduling must move the task from the rail to the appropriate day.");
   await Repo.CallAsync("clear_schedule",J.Obj("task_id",undated.Id));
   await Task.Delay(180);
   U.Capture(Desktop,Path.Combine(directory,"18-week-plan.png"));
   var todayButton=VisualControls<Button>(Desktop).Single(button=>AutomationProperties.GetName(button)=="今天");
   var todayChip=VisualControls<Border>(Desktop).First(border=>border.Width==25&&border.Height==25&&
    border.Child is TextBlock&&((TextBlock)border.Child).Text==DateTime.Today.Day.ToString()&&
    border.Background!=Brushes.Transparent);
   VerifyPlanningTheme(directory,"19-week-plan-custom.png",todayButton,todayChip.Background);
   await VerifyWeekReschedulingAsync();
   click("月历");
   if(!VisualControls<MonthCalendar>(Desktop).Any())
    throw new InvalidOperationException("Switching back must restore the month calendar.");

  }

  async Task VerifyTaskBoardsAsync(string directory){
   string title="看板验收待办";
   string id=J.NewId();
   await Repo.CallAsync("create_task",J.Obj("title",title,"operation_id",id,"start_date",J.Today,"subtasks",new[]{"整理","校对"}));
   Func<TaskItem> current=()=>Repo.Items().Single(item=>item.Id==id);
   if(MainView.Quadrant(current())!=3||Repo.Stage(current())!=BoardStage.Pending)
    throw new InvalidOperationException("New tasks must start in the ordinary priority and pending lanes.");

   Desktop.Navigate("quadrants");
   await Task.Delay(200);
   var card=VisualControls<Button>(Desktop).Single(button=>AutomationProperties.GetName(button)=="查看待办："+title);
   var menu=card.ContextMenu;
   menu.PlacementTarget=card;
   menu.IsOpen=true;
   await Task.Delay(330);
   if(menu.Template.FindName("MenuSurface",menu)==null)
    throw new InvalidOperationException("The animated context menu template was not applied.");
   U.Capture(menu,Path.Combine(directory,"11-move-menu.png"));
   var urgent=menu.Items.OfType<MenuItem>().Single(entry=>Convert.ToString(entry.Header).Contains("重要且紧急"));
   menu.IsOpen=false;
   urgent.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
   if(MainView.Quadrant(current())!=0)
    throw new InvalidOperationException("Moving a quadrant card must update both priority flags.");
   await Task.Delay(150);
   U.Capture(Desktop,Path.Combine(directory,"09-quadrants.png"));

   var bulk=new HashSet<string>();
   for(int i=0;i<320;i++){
    string entryId=J.NewId();bulk.Add(entryId);
    Repo.DemoCall("create_task",J.Obj("title","虚拟化验收 "+i,"operation_id",entryId,"start_date",J.Today));
   }
   Repo.Publish();
   Desktop.UpdateLayout();
   int realized=VisualControls<Button>(Desktop).Count(button=>AutomationProperties.GetName(button).StartsWith("查看待办：虚拟化验收 "));
   if(realized<1||realized>=80)throw new InvalidOperationException("Quadrants should only realize the visible task cards. Count: "+realized);
   Repo.Tasks.RemoveAll(task=>bulk.Contains(task.S("task_id")));
   Repo.Occurrences.RemoveAll(occurrence=>bulk.Contains(occurrence.S("task_id")));
   Repo.Publish();

   string occurrenceId=current().OccurrenceId;
   await Repo.CallAsync("complete_subtask",J.Obj("occurrence_id",occurrenceId,"subtask_index",0));
   if(Repo.Stage(current())!=BoardStage.InProgress)
    throw new InvalidOperationException("Partial checklist progress must appear in the in-progress lane.");
   await Repo.MoveOnBoardAsync(occurrenceId,BoardStage.Pending);
   Repo.OpenDemo();
   if(Repo.Stage(current())!=BoardStage.Pending||current().Occurrence.I("completed_count")!=1)
    throw new InvalidOperationException("The persisted board lane must retain checklist progress.");
   await Repo.MoveOnBoardAsync(occurrenceId,BoardStage.InProgress);

   Desktop.Navigate("board");
   await Task.Delay(300);
   var progress=VisualControls<ProgressBar>(Desktop).FirstOrDefault(bar=>bar.Maximum==2&&bar.Value==1);
   if(progress==null)throw new InvalidOperationException("The in-progress card must show the actual checklist progress.");
   VerifyBoardCardFocus();
   U.Capture(Desktop,Path.Combine(directory,"10-board.png"));
   var filter=VisualControls<Button>(Desktop).Single(button=>AutomationProperties.GetName(button)=="筛选清单：全部清单");
   VerifyPlanningTheme(directory,"20-board-custom.png",filter,progress.Foreground);
   var list=filter.ContextMenu.Items.OfType<MenuItem>().Single(entry=>Convert.ToString(entry.Header)=="工作计划");
   list.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
   Desktop.UpdateLayout();
   if(VisualControls<Button>(Desktop).Any(button=>AutomationProperties.GetName(button)=="查看待办："+title))
    throw new InvalidOperationException("Filtering by list must hide tasks in other lists.");
   filter=VisualControls<Button>(Desktop).Single(button=>AutomationProperties.GetName(button)=="筛选清单：工作计划");
   filter.ContextMenu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
   await Repo.MoveOnBoardAsync(occurrenceId,BoardStage.Completed);
   if(!current().Completed||Repo.Stage(current())!=BoardStage.Completed)
    throw new InvalidOperationException("The completed lane must complete the selected occurrence.");
   await Repo.MoveOnBoardAsync(occurrenceId,BoardStage.InProgress);
   if(current().Completed||Repo.Stage(current())!=BoardStage.InProgress)
    throw new InvalidOperationException("Moving a completed card must reopen its occurrence.");
   await Repo.CallAsync("delete_task",J.Obj("task_id",id));
  }

  static void PressEscape(FrameworkElement target){
   var args=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
    PresentationSource.FromVisual(target),Environment.TickCount,System.Windows.Input.Key.Escape){
    RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent
   };
   target.RaiseEvent(args);
  }

  async Task VerifyRoundedPickers(Window editor,string directory){
   editor.UpdateLayout();
   var combo=Controls<ComboBox>(editor).First();
   var date=Controls<DatePicker>(editor).First();
   combo.ApplyTemplate();date.ApplyTemplate();
   var comboFrame=(Border)combo.Template.FindName("PickerFrame",combo);
   var dateFrame=(Border)date.Template.FindName("DateFrame",date);
   if(comboFrame.CornerRadius.TopLeft!=10||dateFrame.CornerRadius.TopLeft!=10)
    throw new InvalidOperationException("Form dropdowns must use the rounded input treatment.");
   var comboPopup=(System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup",combo);
   combo.Focus();combo.IsDropDownOpen=true;
   await Task.Delay(180);
   var menu=(Border)combo.Template.FindName("PickerOptions",combo);
   if(!comboPopup.IsOpen||menu.CornerRadius.TopLeft!=11||menu.ActualHeight<40)
    throw new InvalidOperationException("Rounded dropdown popup did not present its options.");
   U.Capture(menu,Path.Combine(directory,"13-rounded-options.png"));
   combo.SelectedIndex=1;
   PressEscape(combo);
   if(combo.IsDropDownOpen||!editor.IsVisible||combo.SelectedIndex!=1)
    throw new InvalidOperationException("Escape should dismiss the option menu first.");

   date.BringIntoView();
   date.UpdateLayout();
   var button=(Button)date.Template.FindName("PART_Button",date);
   button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   await Task.Delay(180);
   var popup=(System.Windows.Controls.Primitives.Popup)date.Template.FindName("PART_Popup",date);
   var calendar=(Calendar)popup.Child;
   calendar.ApplyTemplate();
   var surface=(Border)calendar.Template.FindName("CalendarSurface",calendar);
   if(!popup.IsOpen||surface.CornerRadius.TopLeft!=11||surface.ActualHeight<100)
    throw new InvalidOperationException("Rounded date popup did not present a calendar.");
   U.Capture(surface,Path.Combine(directory,"14-rounded-calendar.png"));
   var day=DateTime.Today.AddDays(1);
   PressEscape(date);
   if(date.IsDropDownOpen||!editor.IsVisible)
    throw new InvalidOperationException("Escape should dismiss the date picker first.");
   calendar.SelectedDate=day;
   if(date.SelectedDate!=day)throw new InvalidOperationException("Date selection was not propagated to the editor.");
  }

  void VerifyTaskFocusButton(){
   var item=Repo.Items().First(task=>task.OnDate(J.Today)&&!task.Completed&&!task.Abandoned);
   var row=(Grid)((SwipeTaskRow)Desktop.TaskRow(item)).Card.Child;
   var buttons=row.Children.OfType<Button>().ToList();
   var star=buttons.Single(button=>Equals(button.ToolTip,"标记重要"));
   var focus=buttons.Single(button=>Equals(button.ToolTip,"开始 25 分钟专注"));
   var more=buttons.Single(button=>Equals(button.ToolTip,"更多操作"));
   if(Grid.GetColumn(focus)!=Grid.GetColumn(star)+1||Grid.GetColumn(more)!=Grid.GetColumn(focus)+1||!(focus.Content is Canvas))
    throw new InvalidOperationException("The tomato clock must sit between the star and the task menu.");
   focus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if(FocusMinutes!=25||!FocusEnd.HasValue||FocusOccurrence!=item.OccurrenceId||FocusTitle!=item.Title)
    throw new InvalidOperationException("The tomato clock did not start a 25-minute timer for the selected task.");
   var started=FocusStarted;
   focus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if(FocusStarted!=started)throw new InvalidOperationException("A second click must keep the active timer.");
   DiscardFocus();
   Desktop.Navigate("today");
  }

  async Task VerifyListNavigationAsync(){
   Desktop.Navigate("today");
   string added="清单同步验收-"+J.NewId();
   const string renamed="清单改名验收";
   Exception failure=null;
   var submit=Dispatcher.BeginInvoke(new Action(delegate{
    var dialog=Desktop.OwnedWindows.Cast<Window>().Single(w=>w.IsVisible&&w.Title=="新建清单");
    try{
     dialog.UpdateLayout();
     VisualControls<TextBox>(dialog).Single(box=>AutomationProperties.GetName(box)=="清单名称").Text=added;
     ClickBulkButton(dialog,"保存清单");
     if(dialog.IsVisible)throw new InvalidOperationException("The list editor stayed open after saving.");
    }catch(Exception error){failure=error;}
    finally{if(dialog.IsVisible)dialog.Close();}
   }),DispatcherPriority.ApplicationIdle);
   var newList=VisualControls<Button>(Desktop).Single(control=>AutomationProperties.GetName(control)=="新建清单");
   newList.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   await submit.Task;
   if(failure!=null)throw new InvalidOperationException("Creating a list from the sidebar failed.",failure);
   string id=Repo.Lists.Single(list=>list.S("name")==added).S("list_id");
   Desktop.UpdateLayout();
   var button=VisualControls<Button>(Desktop).Single(control=>AutomationProperties.GetName(control)==added);
   button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   await Repo.CallAsync("update_list",J.Obj("list_id",id,"name",renamed));
   Desktop.UpdateLayout();
   if(!VisualControls<Button>(Desktop).Any(control=>AutomationProperties.GetName(control)==renamed)||
      !VisualControls<TextBlock>(Desktop).Any(text=>text.Text==renamed&&text.FontSize==28))
    throw new InvalidOperationException("Renaming a synced list must update both its navigation item and heading.");
   await Repo.CallAsync("delete_list",J.Obj("list_id",id));
   Desktop.UpdateLayout();
   if(VisualControls<Button>(Desktop).Any(control=>AutomationProperties.GetName(control)==renamed)||
      !VisualControls<TextBlock>(Desktop).Any(text=>text.Text=="全部待办"&&text.FontSize==28))
    throw new InvalidOperationException("Deleting the active list must return to all tasks.");
  }

  void VerifySyncSettings(){
   Desktop.Navigate("settings");
   Desktop.UpdateLayout();
   var interval=Controls<ComboBox>(Desktop).Single(box=>AutomationProperties.GetName(box)=="自动同步间隔");
   interval.SelectedIndex=Array.IndexOf(Settings.SyncIntervals,5);
   var now=DateTime.UtcNow;
   if(Preferences.SyncIntervalMinutes!=5||new Settings(Preferences.DirectoryPath).SyncIntervalMinutes!=5||
      AutomaticSyncDue(now.AddMinutes(4))||!AutomaticSyncDue(now.AddMinutes(5).AddSeconds(2)))
    throw new InvalidOperationException("The synchronization interval must persist and set the next refresh deadline.");
   interval.SelectedIndex=Array.IndexOf(Settings.SyncIntervals,3);
   Desktop.Navigate("today");
  }

  async Task VerifyAppearanceAndAssistantAsync(string directory){
   Desktop.Navigate("today");
   Desktop.UpdateLayout();
   var collapse=VisualControls<Button>(Desktop).Single(button=>AutomationProperties.GetName(button)=="折叠导航");
   collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Desktop.UpdateLayout();
   var expand=VisualControls<Button>(Desktop).Single(button=>AutomationProperties.GetName(button)=="展开导航");
   if(!new Settings(Preferences.DirectoryPath).GetBool("sidebarCollapsed")||
      !VisualControls<Button>(Desktop).Any(button=>AutomationProperties.GetName(button)=="便单小助手"))
    throw new InvalidOperationException("Collapsing navigation must keep its destinations and persist the setting.");
   expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

   string color=Theme.Selected;
   var accent=(SolidColorBrush)U.C(Theme.DefaultHex);
   SetThemeColor("#76B0E8");
   if(accent.Color!=Color.FromRgb(118,176,232)||new Settings(Preferences.DirectoryPath).Values.S("themeColor")!="#76B0E8")
    throw new InvalidOperationException("Theme selection must update existing brushes and persist.");
   Desktop.UpdateLayout();
   U.Capture(Desktop,Path.Combine(directory,"15-blue-theme.png"));
   SetThemeColor("#915B9D");
   if(accent.Color!=Color.FromRgb(145,91,157))throw new InvalidOperationException("Custom theme colors must use the exact requested accent.");
   SetThemeColor(color);

   var row=VisualControls<SwipeTaskRow>(Desktop).First();
   using(var device=new TestTouchDevice(row)){
    device.Send(UIElement.PreviewTouchDownEvent,TouchAction.Down,new Point(190,20));
    device.Send(UIElement.PreviewTouchMoveEvent,TouchAction.Move,new Point(115,21));
    device.Send(UIElement.PreviewTouchUpEvent,TouchAction.Up,new Point(115,21));
   }
   if(!row.ActionsOpen)throw new InvalidOperationException("A left swipe must reveal the task delete action.");
   await Task.Delay(180);
   U.Capture(Desktop,Path.Combine(directory,"16-swipe.png"));
   row.CloseActions();

   Desktop.Navigate("assistant");
   Desktop.UpdateLayout();
   if(!Controls<TextBox>(Desktop).Any(field=>AutomationProperties.GetName(field)=="小助手问题"))
    throw new InvalidOperationException("The assistant page did not expose its input.");
   U.Capture(Desktop,Path.Combine(directory,"17-assistant.png"));
   VerifyAssistantSkillActions(VisualControls<AssistantView>(Desktop).Single());
   var payload=J.Encode(J.Obj("answer","建议先处理紧急待办。",
    "tasks",new[]{J.Obj("title","确认任务","date",J.Today,"important",true)}));
   var reply=CodexAssistant.Parse(J.Encode(J.Obj("type","item.completed","item",J.Obj("type","agent_message","text",payload))));
   if(reply.Suggestions.Count!=1||reply.Suggestions[0].Date!=J.Today||!reply.Suggestions[0].Important)
    throw new InvalidOperationException("Codex response parsing must preserve structured task suggestions.");
   Desktop.Navigate("today");
  }

  sealed class TestTouchDevice:System.Windows.Input.TouchDevice,IDisposable{
   readonly FrameworkElement target;
   Point point;
   System.Windows.Input.TouchAction action;
   public TestTouchDevice(FrameworkElement target):base(9051){
    this.target=target;SetActiveSource(PresentationSource.FromVisual(target));Activate();
   }
   public void Send(RoutedEvent route,System.Windows.Input.TouchAction action,Point point){
    this.action=action;this.point=point;
    target.RaiseEvent(new System.Windows.Input.TouchEventArgs(this,Environment.TickCount){RoutedEvent=route});
   }
   public override System.Windows.Input.TouchPoint GetTouchPoint(IInputElement relativeTo){
    return new System.Windows.Input.TouchPoint(this,point,new Rect(point,new Size(1,1)),action);
   }
   public override System.Windows.Input.TouchPointCollection GetIntermediateTouchPoints(IInputElement relativeTo){
    return new System.Windows.Input.TouchPointCollection{GetTouchPoint(relativeTo)};
   }
   public void Dispose(){Deactivate();}
  }
  async Task SmokeAsync(bool verifyAuthorization){
   string dir=Path.Combine(BasePath,"smoke");Directory.CreateDirectory(dir);
   await Task.Delay(700);U.Capture(Desktop,Path.Combine(dir,"01-today.png"));
   await VerifyImmersiveAsync(dir);
   Desktop.Navigate("calendar");await Task.Delay(200);U.Capture(Desktop,Path.Combine(dir,"02-calendar.png"));
   Desktop.Navigate("widgets");await Task.Delay(200);U.Capture(Desktop,Path.Combine(dir,"03-widgets.png"));
   foreach(var kind in new[]{"calendar","todo"}){if(!Widgets.ContainsKey(kind))ToggleWidget(kind);await Task.Delay(150);U.Capture(Widgets[kind],Path.Combine(dir,"widget-"+kind+".png"));}
   SetWidgetTransparency(72);
   await Task.Delay(310);
   var widget=Widgets["calendar"];
   var layers=(Grid)widget.Content;
   if(((Border)layers.Children[0]).CornerRadius.TopLeft!=10||
      ((SolidColorBrush)((Border)layers.Children[0]).Background).Color.A!=71||
      layers.Children[1].Opacity!=1||new Settings(Preferences.DirectoryPath).WidgetTransparency!=72)
    throw new InvalidOperationException("Widget transparency must affect only the rounded background and persist.");
   U.Capture(widget,Path.Combine(dir,"12-widget-transparency.png"));
   SetWidgetTransparency(2);
   VerifyQuickEntry();
   VerifyTaskListVirtualization();
   VerifyRailDateFilter();
   VerifyTaskFocusButton();
   VerifySyncSettings();
   VerifyFocusDurations();
   VerifyTomatoSummary();
   await VerifyPomodoroWidgetAsync(dir);
   await VerifyCloudPaginationAsync();
   await VerifyListPresentationAsync();
   await VerifyListNavigationAsync();
   VerifyBulkActions();
   await VerifyBatchDeleteDialogAsync();
   await VerifyCloudBatchDeleteAsync();
   VerifyBundledAssistantSkill();
   await VerifyAssistantTaskWritesAsync();
   await VerifyAppearanceAndAssistantAsync(dir);
   await VerifyDemoAsync();
   VerifyDemoProgress();
   await VerifyTaskRemarksAsync(dir);
   VerifyOfficialRemarkFormats();
   await VerifyWeeklyPlanAsync(dir);
   await VerifyTaskBoardsAsync(dir);
   var bundled=BundledBackend.Prepare(Path.Combine(Preferences.DirectoryPath,"mcp"));
   File.WriteAllText(bundled,"invalid cached service");
   await Repo.Client.StartAsync(true);
   try{
    var initialized=await Repo.Client.RpcAsync("initialize",J.Obj("protocolVersion","2025-06-18",
     "capabilities",J.Obj(),"clientInfo",J.Obj("name","Biandan Desktop smoke","version","1")));
    if(initialized.D("serverInfo").S("version")!=BundledBackend.Version)
     throw new InvalidOperationException("The embedded MCP version does not match the desktop UI.");
    var tools=await Repo.Client.RpcAsync("tools/list",J.Obj());
    var batch=tools.Rows("tools").Single(tool=>tool.S("name")=="biandan_batch_delete_tasks");
    if(batch.D("inputSchema").D("properties").D("tasks").ContainsKey("maxItems"))
     throw new InvalidOperationException("The embedded MCP must accept selections above the former 20-item limit.");
    var local=await Repo.Client.CallAsync("local_today");
    if(local.S("date")!=J.Today)throw new InvalidOperationException("Unexpected bundled MCP response.");
   }finally{Repo.Client.Stop();}
   if(verifyAuthorization){
    await Repo.Client.StartAsync();
    try{
     // The bundled MCP starts browser authorization on the first cloud request.
     try{await Repo.Client.CallAsync("check_connection");}
     catch(McpException error){if(!error.WaitingForAuthorization)throw;}
     for(int attempt=0;attempt<100&&Repo.Client.AuthorizationUri==null;attempt++)await Task.Delay(100);
     if(Repo.Client.AuthorizationUri==null)
      throw new InvalidOperationException("The Huawei authorization link was not delivered to the desktop.");
     File.WriteAllText(Path.Combine(dir,"oauth-passed.txt"),"Huawei authorization URI received through the bundled service.");
    }finally{Repo.Client.Stop();}
   }
   var detail=new TaskDetail(Desktop,Repo,Repo.Items().First(task=>task.Task.Strings("subtasks").Count>0));detail.Show();
   await Task.Delay(350);U.Capture(detail,Path.Combine(dir,"08-detail.png"));detail.Close();
   var editor=new TaskEditor(Desktop,Repo,null);editor.Show();await Task.Delay(350);U.Capture(editor,Path.Combine(dir,"04-editor.png"));await VerifyRoundedPickers(editor,dir);PressEscape(Controls<TextBox>(editor).First());if(editor.IsVisible)throw new InvalidOperationException("Escape must close the task editor.");
   var login=new LoginView(this);login.Show();await Task.Delay(350);U.Capture(login,Path.Combine(dir,"05-login.png"));login.Close();
   Desktop.Navigate("focus");await Task.Delay(150);U.Capture(Desktop,Path.Combine(dir,"06-focus.png"));
   Desktop.Navigate("settings");await Task.Delay(150);U.Capture(Desktop,Path.Combine(dir,"07-settings.png"));
   File.WriteAllText(Path.Combine(dir,"passed.txt"),"All views rendered at "+DateTime.Now.ToString("o"));
   Quitting=true;foreach(var w in Widgets.Values.ToArray())w.Close();Desktop.Close();Shutdown();
  }
 }
}
