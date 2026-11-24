using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Biandan {
 public sealed partial class MainView {
  const string WeekDrag="Biandan.WeekOccurrence";
  const double HourHeight=55;
  const double TimelineScrollbar=17;
  static readonly string[] Weekdays={"周一","周二","周三","周四","周五","周六","周日"};
  ScrollViewer weekScroll;
  DateTime renderedWeek;
  double weekScrollOffset;
  sealed class WeekDragState{
   public TaskItem Item;
   public DateTime? ShownDay;
   public int? GrabSlot;
   public DateTime LastScroll;
   public WeekDropPreview Preview;
  }
  WeekDragState weekDrag;
  bool schedulingTask;

  internal static DateTime WeekStart(DateTime date){
   return date.Date.AddDays(-((int)date.DayOfWeek+6)%7);
  }

  void SetCalendarWeek(bool week){
   if(calendarWeek==week)return;
   app.Preferences.Set("calendarWeek",week);
   calendarWeek=week;
   calendarMonth=calendarDate;
   RenderPage();
  }

  FrameworkElement CalendarModes(){
   var panel=U.Stack(true);
   foreach(bool week in new[]{false,true}){
    string label=week?"周计划":"月历";
    var button=PlanningPalette.Button(label,()=>SetCalendarWeek(week),true);
    if(week==calendarWeek)button.Background=U.C(PlanningPalette.Selection);
    button.Margin=new Thickness(0,0,5,0);
    button.ToolTip=week==calendarWeek?label+"（当前视图）":label;
    AutomationProperties.SetName(button,label);
    panel.Children.Add(button);
   }
   return panel;
  }

  void MoveWeek(DateTime selected,bool resetScroll){
   calendarDate=calendarMonth=selected.Date;
   if(resetScroll)weekScroll=null;
   RenderPage();
  }

  void RenderWeekPlan(){
   DateTime first=WeekStart(calendarDate);
   DateTime last=first.AddDays(6);
   title.Text="周计划";
   subtitle.Text=first.ToString("yyyy年 M月d日")+" – "+last.ToString("M月d日");

   var items=repo.Items().Where(item=>!item.Abandoned).ToList();
   string firstKey=J.Date(first),lastKey=J.Date(last);
   var scheduled=items.Where(item=>!item.Undated&&string.CompareOrdinal(item.Start,lastKey)<=0&&string.CompareOrdinal(item.End,firstKey)>=0).ToList();
   var listColors=repo.Lists.GroupBy(list=>list.S("list_id")).ToDictionary(group=>group.Key,group=>group.First().I("color"));
   var root=U.Grid("*","Auto,*");
   var bar=U.Grid("*,Auto");
   U.Place(bar,CalendarModes());
   var navigation=U.Stack(true);
   var previous=PlanningPalette.Icon("\uE76B","上一周",()=>MoveWeek(calendarDate.AddDays(-7),true));
   var next=PlanningPalette.Icon("\uE76C","下一周",()=>MoveWeek(calendarDate.AddDays(7),true));
   var today=PlanningPalette.Button("今天",()=>MoveWeek(DateTime.Today,true),true);
   AutomationProperties.SetName(today,"今天");
   previous.Background=next.Background=U.C(PlanningPalette.Panel);
   previous.BorderBrush=next.BorderBrush=U.C(PlanningPalette.Line);
   next.Margin=new Thickness(5,0,8,0);
   navigation.Children.Add(previous);navigation.Children.Add(next);navigation.Children.Add(today);
   U.Place(bar,navigation,1);
   bar.Margin=new Thickness(0,0,0,12);
   U.Place(root,bar);

   var content=U.Grid("*,14,190");
   var agenda=U.Grid("*","Auto,Auto,*");
   var dropPreview=new WeekDropPreview();
   var dates=U.Grid("59,*,*,*,*,*,*,*");
   dates.Margin=new Thickness(0,0,TimelineScrollbar,0);
   U.Place(agenda,dates);

   var allDay=U.Grid("59,*,*,*,*,*,*,*");
   allDay.Margin=new Thickness(0,0,TimelineScrollbar,0);
   var allDayLabel=U.Text("全天",11,PlanningPalette.Muted);
   allDayLabel.HorizontalAlignment=HorizontalAlignment.Center;
   U.Place(allDay,allDayLabel);
   U.Place(agenda,allDay,0,1);

   var timeline=U.Grid("59,*,*,*,*,*,*,*");
   timeline.Height=HourHeight*24;
   timeline.Background=U.C(PlanningPalette.Card);
   var times=new Canvas{IsHitTestVisible=false};
   for(int hour=0;hour<24;hour++){
    var label=U.Text(hour.ToString("00")+":00",11,PlanningPalette.Muted);
    Canvas.SetLeft(label,10);
    Canvas.SetTop(label,HourHeight*hour+3);
    times.Children.Add(label);
   }
   U.Place(timeline,times);

   var clockLabel=U.Text("",9,PlanningPalette.Ink,true);
   clockLabel.SetResourceReference(TextBlock.ForegroundProperty,"AccentInk");
   var clockChip=new Border{Child=clockLabel,Background=U.C(PlanningPalette.Accent),CornerRadius=new CornerRadius(5),
    HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(4,1,4,1),Margin=new Thickness(3,0,0,0)};
   var nowLine=new Grid{VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false,Visibility=Visibility.Collapsed};
   nowLine.Children.Add(new Border{Height=1.5,Background=U.C(PlanningPalette.Accent)});
   nowLine.Children.Add(clockChip);
   bool hasAllDay=false;
   double earliest=9*60;
   for(int day=0;day<7;day++){
    DateTime date=first.AddDays(day);
    bool selected=date==calendarDate, isToday=date==DateTime.Today;
    var label=U.Stack(true);
    label.Children.Add(U.Text(Weekdays[day],12,selected?PlanningPalette.Emphasis:PlanningPalette.Muted,true));
    var number=U.Text(date.Day.ToString(),14,PlanningPalette.Ink,true);
    if(isToday)number.SetResourceReference(TextBlock.ForegroundProperty,"AccentInk");
    number.HorizontalAlignment=HorizontalAlignment.Center;
    var numberChip=new Border{Child=number,Width=25,Height=25,CornerRadius=new CornerRadius(12.5),
     Background=isToday?U.C(PlanningPalette.Accent):Brushes.Transparent,Margin=new Thickness(4,0,0,0)};
    label.Children.Add(numberChip);
    var header=PlanningPalette.Button("",()=>MoveWeek(date,false));
    header.Content=label;
    header.Padding=new Thickness(0,8,0,8);
    header.Background=U.C(selected?PlanningPalette.Selection:day>=5?PlanningPalette.Subtle:PlanningPalette.Panel);
    AutomationProperties.SetName(header,date.ToString("M月d日")+" "+Weekdays[day]);
    WireWeekDrop(header,date,dropPreview,false);
    U.Place(dates,new Border{Child=header,BorderBrush=U.C(PlanningPalette.Line),BorderThickness=new Thickness(1,0,0,1)},day+1);

    string key=J.Date(date);
    var timed=new List<WeekEvent>();
    var untimed=U.Stack();
    foreach(var item in scheduled.Where(candidate=>candidate.OnDate(key))){
     DateTime begin,finish;
     bool valid=Repository.TryScheduleWindow(item,out begin,out finish);
     int color;
     if(!listColors.TryGetValue(item.ListId,out color)||color<0)color=0;
     if(!valid||begin.TimeOfDay==TimeSpan.Zero&&finish.TimeOfDay==TimeSpan.FromMinutes(1439)){
      untimed.Children.Add(WeekEventButton(item,color,null,date,null));
      continue;
     }
     // Midnight ends belong to the preceding column. A zero-duration event still has a clickable block.
     if(finish==date&&finish>begin)continue;
     int start=date==begin.Date?(int)begin.TimeOfDay.TotalMinutes:0;
     int end=date==finish.Date?(int)finish.TimeOfDay.TotalMinutes:1440;
     timed.Add(new WeekEvent{Item=item,Start=start,End=end,Color=color,
      Label=MinuteText(start)+(end>start?"–"+MinuteText(end):"")});
     earliest=Math.Min(earliest,start);
    }
    if(untimed.Children.Count>0)hasAllDay=true;
    var allDayItems=new ScrollViewer{Content=untimed,MaxHeight=76,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden};
    var allDayCell=new Border{Child=allDayItems,Background=Brushes.Transparent,BorderBrush=U.C(PlanningPalette.Line),BorderThickness=new Thickness(1,0,0,1),Padding=new Thickness(2)};
    WireWeekDrop(allDayCell,date,dropPreview,false);
    U.Place(allDay,allDayCell,day+1);

    var dayColumn=U.Grid("*",string.Join(",",Enumerable.Repeat("1*",24)));
    dayColumn.Background=U.C(selected?"#FFF1D5":day>=5?PlanningPalette.Panel:PlanningPalette.Card);
    for(int hour=0;hour<24;hour++){
     int minute=hour*60;
     var cell=new Border{BorderBrush=U.C(PlanningPalette.Line),BorderThickness=new Thickness(1,1,0,0),Background=Brushes.Transparent};
     cell.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){
      if(e.ClickCount!=2||!repo.CanWrite)return;
      int start=minute+15*Math.Max(0,Math.Min(3,(int)(e.GetPosition(cell).Y/(HourHeight/4))));
      NewTask(date,null,TimeSpan.FromMinutes(start));e.Handled=true;
     };
     U.Place(dayColumn,cell,0,hour);
    }
    var blocks=new Grid{ClipToBounds=true};
    // The overlay receives clicks only on event blocks, leaving free slots clickable.
    U.Place(dayColumn,blocks,0,0,1,24);
    PlaceWeekEvents(blocks,timed,date);
    U.Place(timeline,dayColumn,day+1);
    WireWeekDrop(dayColumn,date,dropPreview,true);
   }
   allDay.Visibility=hasAllDay?Visibility.Visible:Visibility.Collapsed;
   U.Place(timeline,nowLine,1,0,7);
   var clock=new DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};
   Action updateClock=delegate{
    var now=DateTime.Now;
    int index=(now.Date-first).Days;
    nowLine.Visibility=index>=0&&index<7?Visibility.Visible:Visibility.Collapsed;
    if(nowLine.Visibility==Visibility.Visible){
     Grid.SetColumn(nowLine,index+1);Grid.SetColumnSpan(nowLine,1);
     nowLine.Margin=new Thickness(0,Math.Max(0,MinuteOffset(now.TimeOfDay.TotalMinutes)-7),0,0);
     clockLabel.Text=now.ToString("HH:mm",CultureInfo.InvariantCulture);
    }
   };
   clock.Tick+=delegate{updateClock();};
   timeline.Loaded+=delegate{updateClock();clock.Start();};
   timeline.Unloaded+=delegate{clock.Stop();};

   double offset=weekScroll!=null&&renderedWeek==first?weekScrollOffset:MinuteOffset(Math.Max(0,Math.Floor(earliest/60)*60-1));
   weekScrollOffset=offset;
   weekScroll=new ScrollViewer{Content=timeline,VerticalScrollBarVisibility=ScrollBarVisibility.Visible};
   var scroll=weekScroll;
   PlanningPalette.StyleScrollbars(scroll);
   scroll.ScrollChanged+=delegate(object sender,ScrollChangedEventArgs e){
    if(weekScroll==scroll&&e.VerticalChange!=0)weekScrollOffset=e.VerticalOffset;
   };
   scroll.Loaded+=delegate{scroll.ScrollToVerticalOffset(offset);};
   renderedWeek=first;
   U.Place(agenda,scroll,0,2);
   U.Place(agenda,dropPreview,0,0,1,3);
   U.Place(content,U.Card(agenda,PlanningPalette.Panel,12,new Thickness(8,7,0,0)));
   U.Place(content,RenderUnscheduled(items),2);
   U.Place(root,content,0,1);
   body.Content=root;
  }

  static string MinuteText(int minutes){
   return minutes==1440?"24:00":TimeSpan.FromMinutes(minutes).ToString(@"hh\:mm",CultureInfo.InvariantCulture);
  }

  static double MinuteOffset(double minutes){return minutes*HourHeight/60;}

  sealed class WeekEvent{
   public TaskItem Item;
   public int Start,End,Color,Column;
   public string Label;
  }

  void PlaceWeekEvents(Grid day,List<WeekEvent> events,DateTime date){
   var sorted=events.OrderBy(entry=>entry.Start).ThenByDescending(entry=>entry.End).ToList();
   var group=new List<WeekEvent>();
   var ends=new List<double>();
   double last=-1;
   foreach(var entry in sorted){
    if(entry.Start>=last&&group.Count>0){
     AddWeekEventGroup(day,group,ends.Count,date);
     group.Clear();ends.Clear();
    }
    // Include the minimum visual height when allocating columns for close starts.
    double end=Math.Max(entry.End,entry.Start+25);
    int column=ends.FindIndex(value=>value<=entry.Start);
    if(column<0){column=ends.Count;ends.Add(end);}
    else ends[column]=end;
    entry.Column=column;group.Add(entry);
    last=ends.Max();
   }
   if(group.Count>0)AddWeekEventGroup(day,group,ends.Count,date);
  }

  void AddWeekEventGroup(Grid day,List<WeekEvent> group,int columns,DateTime date){
   var grid=U.Grid(string.Join(",",Enumerable.Repeat("*",columns)));
   foreach(var entry in group){
    var block=WeekEventButton(entry.Item,entry.Color,entry.Label,date,day);
    block.Height=Math.Min(Math.Max(1,MinuteOffset(1440-entry.Start)-1),Math.Max(22,MinuteOffset(entry.End-entry.Start)-2));
    block.Margin=new Thickness(2,MinuteOffset(entry.Start)+1,2,0);
    block.VerticalAlignment=VerticalAlignment.Top;
    U.Place(grid,block,entry.Column);
   }
   U.Place(day,grid);
  }

  Button WeekEventButton(TaskItem item,int color,string time,DateTime date,Grid timeline){
   var button=PlanningPalette.Button("",()=>EditTask(item));
   button.Background=U.C(item.Completed?PlanningPalette.Subtle:PlanningPalette.Selection);
   button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
   button.Padding=new Thickness(0);
   button.BorderThickness=new Thickness(0);
   button.MinHeight=0;
   var panel=U.Grid("4,*");
   panel.ClipToBounds=true;
   U.Place(panel,new Border{Background=U.C(ListColor(color)),CornerRadius=new CornerRadius(2),Margin=new Thickness(1,2,0,2)});
   var text=U.Stack();text.Margin=new Thickness(4,2,3,2);text.VerticalAlignment=VerticalAlignment.Center;
   var name=U.Text(item.Title,11,PlanningPalette.Ink,true);
   if(item.Completed)name.TextDecorations=TextDecorations.Strikethrough;
   text.Children.Add(name);
   if(time!=null)text.Children.Add(U.Text(time,10,PlanningPalette.Muted));
   else button.Margin=new Thickness(1,1,1,2);
   U.Place(panel,text,1);
   button.Content=panel;
   button.ToolTip=item.Title+" · "+(time??"全天");
   button.IsEnabled=!item.Syncing;
   if(WireWeekDrag(button,item,date,timeline))button.ToolTip+="\n拖到时间格调整日程，拖到日期保留时刻。"+(item.Repeating?"仅移动本次，将另存为单次待办。":"");
   AutomationProperties.SetName(button,"周计划："+item.Title);
   return button;
  }

  FrameworkElement RenderUnscheduled(List<TaskItem> items){
   var pending=items.Where(item=>item.Undated&&!item.Completed).ToList();
   var rail=U.Grid("*","Auto,Auto,*");
   var header=U.Grid("*,Auto");
   U.Place(header,U.Text("待安排",18,PlanningPalette.Ink,true));
   U.Place(header,U.Text(pending.Count.ToString(),12,PlanningPalette.Muted),1);
   U.Place(rail,header);
   var hint=U.Text(pending.Count==0?"没有未安排的待办":repo.CanWrite?"拖到日历安排时间":"点击待办查看详情",11,PlanningPalette.Muted);
   hint.Margin=new Thickness(0,9,0,9);
   U.Place(rail,hint,0,1);
   var rows=new BoardLaneItems(pending,item=>UnscheduledRow(item),0);
   PlanningPalette.StyleScrollbars(rows);
   U.Place(rail,rows,0,2);
   return U.Card(rail,PlanningPalette.Panel,12,new Thickness(12,15,10,10));
  }

  FrameworkElement UnscheduledRow(TaskItem item){
   var row=U.Grid("26,*");
   var check=PlanningPalette.Icon("\uE739","完成待办："+item.Title,async()=>await app.RunAction(async()=>{
    await repo.CallAsync("complete_occurrence",J.Obj("occurrence_id",item.OccurrenceId));
   }));
   check.IsEnabled=repo.CanWrite&&!item.Syncing;
   check.Width=25;
   U.Place(row,check);
   var task=PlanningPalette.Button("",()=>EditTask(item));
   task.Padding=new Thickness(4,7,1,7);
   task.Content=U.Text(item.Title,12,PlanningPalette.Ink,true);
   task.HorizontalContentAlignment=HorizontalAlignment.Stretch;
   task.ToolTip=item.Title;
   AutomationProperties.SetName(task,"待安排："+item.Title);
   WireWeekDrag(task,item,null,null);
   U.Place(row,task,1);
   return new Border{Child=row,BorderThickness=new Thickness(0,0,0,1),BorderBrush=U.C(PlanningPalette.Line)};
  }

  static int WeekSlot(double y){return Math.Max(0,Math.Min(1425,(int)(y/MinuteOffset(15))*15));}

  bool WireWeekDrag(Button button,TaskItem item,DateTime? shownDay,Grid timeline){
   if(!repo.CanWrite||item.Completed||item.Abandoned||item.Syncing||item.Repeating&&(item.Undated||!repo.Demo&&!repo.Connected))return false;
   Point origin=new Point();
   int? grabSlot=null;
   bool pressing=false;
   button.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){
    origin=e.GetPosition(this);pressing=true;
    grabSlot=timeline==null?(int?)null:WeekSlot(e.GetPosition(timeline).Y);
   };
   button.PreviewMouseLeftButtonUp+=delegate{pressing=false;};
   button.PreviewMouseMove+=delegate(object sender,MouseEventArgs e){
    if(!pressing||e.LeftButton!=MouseButtonState.Pressed||schedulingTask)return;
    Point point=e.GetPosition(this);
    if(Math.Abs(point.X-origin.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
    pressing=false;e.Handled=true;button.ReleaseMouseCapture();
    if(!repo.CanRescheduleItem(item)){ShowToast("这项待办当前无法改期");return;}
    weekDrag=new WeekDragState{Item=item,ShownDay=shownDay,GrabSlot=grabSlot};
    try{DragDrop.DoDragDrop(button,new DataObject(WeekDrag,item.OccurrenceId),DragDropEffects.Move);}
    finally{
     if(weekDrag.Preview!=null)weekDrag.Preview.Clear();
     weekDrag=null;
    }
   };
   return true;
  }

  static DateTime WeekDropStart(WeekDragState drag,DateTime date,int? slot){
   DateTime first,last;
   if(!drag.ShownDay.HasValue||!Repository.TryScheduleWindow(drag.Item,out first,out last))
    return date.AddMinutes(slot??0);
   // The offset keeps the grabbed portion of a long or overnight appointment under the pointer.
   DateTime shifted=first.AddDays((date-drag.ShownDay.Value).Days);
   if(!slot.HasValue)return shifted;
   if(drag.GrabSlot.HasValue)return shifted.AddMinutes(slot.Value-drag.GrabSlot.Value);
   return shifted.Date.AddMinutes(slot.Value);
  }

  bool OwnWeekDrag(DragEventArgs e){
   return !schedulingTask&&weekDrag!=null&&e.Data.GetDataPresent(WeekDrag,false)&&
    (e.Data.GetData(WeekDrag,false) as string)==weekDrag.Item.OccurrenceId;
  }

  void WireWeekDrop(FrameworkElement target,DateTime date,WeekDropPreview preview,bool timed){
   if(!repo.CanWrite)return;
   target.AllowDrop=true;
   DragEventHandler update=delegate(object sender,DragEventArgs e){
    bool valid=OwnWeekDrag(e);
    if(valid){
     if(weekDrag.Preview!=null&&weekDrag.Preview!=preview)weekDrag.Preview.Clear();
     weekDrag.Preview=preview;
     int? slot=timed?(int?)WeekSlot(e.GetPosition(target).Y):null;
     DateTime start=WeekDropStart(weekDrag,date,slot);
     preview.ShowAt(target,date,slot,start,timed?weekScroll:null,!timed&&!weekDrag.GrabSlot.HasValue);
     if(timed){
      double y=e.GetPosition(weekScroll).Y;
      if((DateTime.UtcNow-weekDrag.LastScroll).TotalMilliseconds>=90&&(y<22||y>weekScroll.ViewportHeight-22)){
       weekScroll.ScrollToVerticalOffset(weekScroll.VerticalOffset+(y<22?-1:1)*HourHeight/2);
       weekDrag.LastScroll=DateTime.UtcNow;
      }
     }
    }else preview.Clear();
    e.Effects=valid?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;
   };
   target.PreviewDragEnter+=update;
   target.PreviewDragOver+=update;
   target.PreviewDragLeave+=delegate{if(preview.Target==target)preview.Clear();};
   target.PreviewDrop+=delegate(object sender,DragEventArgs e){
    preview.Clear();
    if(!OwnWeekDrag(e)){e.Effects=DragDropEffects.None;e.Handled=true;return;}
    var drag=weekDrag;
    int? slot=timed?(int?)WeekSlot(e.GetPosition(target).Y):null;
    DateTime start=WeekDropStart(drag,date,slot);
    e.Effects=DragDropEffects.Move;e.Handled=true;
    Dispatcher.BeginInvoke(new Action(async()=>await ScheduleWeekTask(drag.Item,date,start,slot.HasValue)),DispatcherPriority.Input);
   };
  }

  async Task ScheduleWeekTask(TaskItem item,DateTime targetDay,DateTime start,bool timed){
   if(schedulingTask)return;
   DateTime first,last;
   if(Repository.TryScheduleWindow(item,out first,out last)&&first==start)return;
   schedulingTask=true;
   try{
    if(item.Repeating&&!U.Confirm(this,"移动本次循环待办","「"+item.Title+"」的这一次将移至 "+start.ToString("M月d日 HH:mm")+"，并另存为单次待办。","移动本次"))return;
    await repo.RescheduleItemAsync(item.OccurrenceId,start.Date,timed?(int?)start.TimeOfDay.TotalMinutes:null);
    if(page=="calendar"&&calendarWeek&&WeekStart(calendarDate)==WeekStart(targetDay))MoveWeek(targetDay,false);
    ShowToast("已安排至 "+start.ToString(timed?"M月d日 HH:mm":"M月d日"));
   }catch(Exception error){SetStatus("安排未保存。"+U.Friendly(error.Message),true);}
   finally{schedulingTask=false;}
  }
 }
}
