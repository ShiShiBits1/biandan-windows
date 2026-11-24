using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Biandan {
 public sealed class WidgetView:Window {
  readonly App app;readonly Repository repo;readonly string kind;readonly ContentControl content;readonly TextBlock status;readonly Border background;readonly Grid foreground;DateTime selected=DateTime.Today,month=DateTime.Today;bool locked;
  public WidgetView(App application,string widgetKind){
   app=application;repo=app.Repo;kind=widgetKind;Title=kind=="calendar"?"便单 · 桌面日历":"便单 · 桌面待办";U.Configure(this,kind=="calendar"?340:370,kind=="calendar"?590:485,10);ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Height=Math.Min(Height,SystemParameters.WorkArea.Height-30);
   Topmost=app.Preferences.GetBool("widget."+kind+".pinned");locked=app.Preferences.GetBool("widget."+kind+".locked");
   var g=foreground=U.Grid("*","44,*,39");var head=U.Grid("*,30,30,30");head.Background=new SolidColorBrush(Color.FromArgb(1,0,0,0));head.Margin=new Thickness(20,2,11,0);var label=U.Text(kind=="calendar"?"桌面日历":"我的一天",12,"#6E5638",true);U.Place(head,label);
   head.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(!locked&&e.ChangedButton==MouseButton.Left&&!(e.Source is Button))try{DragMove();}catch(InvalidOperationException){}};
   var pin=U.IconButton("\uE718",Topmost?"取消置顶":"置顶",null);((TextBlock)pin.Content).Foreground=U.C(Topmost?"#8D560B":"#897859");pin.Click+=delegate{Topmost=!Topmost;app.Preferences.Set("widget."+kind+".pinned",Topmost);((TextBlock)pin.Content).Foreground=U.C(Topmost?"#8D560B":"#897859");pin.ToolTip=Topmost?"取消置顶":"置顶";};U.Place(head,pin,1);
   var lockBtn=U.IconButton(locked?"\uE72E":"\uE785","锁定 / 解锁位置",null);lockBtn.Click+=delegate{locked=!locked;app.Preferences.Set("widget."+kind+".locked",locked);lockBtn.Content=U.Icon(locked?"\uE72E":"\uE785");};U.Place(head,lockBtn,2);U.Place(head,U.IconButton("\uE8BB","关闭小组件",()=>Close()),3);U.Place(g,head);
   content=new ContentControl{Margin=new Thickness(20,6,20,0),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top};U.Place(g,content,0,1);
   var footer=U.Grid("*,Auto");footer.Margin=new Thickness(21,0,16,3);status=U.Text("",10,"#6E5638");U.Place(footer,status);var open=U.Button(kind=="calendar"?"打开日历  ↗":"打开便单  ↗",()=>{app.ShowDesktop();if(kind=="calendar"&&app.Desktop!=null)app.Desktop.OpenCalendarDate(selected);});open.FontSize=10;open.Padding=new Thickness(5,4,5,4);U.Place(footer,open,1);U.Place(g,footer,0,2);
   var options=Motion.Menu();
   var appearance=new MenuItem{Header="小组件透明度…"};
   appearance.Click+=delegate{app.ShowDesktop();if(app.Desktop!=null)app.Desktop.Navigate("settings");};
   options.Items.Add(appearance);head.ContextMenu=options;
   var layers=U.Grid("*");layers.Margin=new Thickness(9);
   background=U.Card(null,"#FFFAED",10,new Thickness(0));
   background.IsHitTestVisible=false;
   background.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,Opacity=.12,BlurRadius=13,ShadowDepth=2};
   U.Place(layers,background);U.Place(layers,g);
   Content=layers;
   SetBackgroundTransparency(app.Preferences.WidgetTransparency);
   repo.Changed+=Changed;Closed+=delegate{repo.Changed-=Changed;app.Preferences.Values["widget."+kind+".x"]=Left;app.Preferences.Values["widget."+kind+".y"]=Top;app.Preferences.Save();};
   Loaded+=delegate{double x,y;var work=SystemParameters.WorkArea;if(double.TryParse(app.Preferences.Values.S("widget."+kind+".x"),out x)&&double.TryParse(app.Preferences.Values.S("widget."+kind+".y"),out y)&&x+Width>SystemParameters.VirtualScreenLeft&&x<SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth&&y+Height>SystemParameters.VirtualScreenTop&&y<SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight){Left=x;Top=y;}else{Left=work.Right-Width-20;Top=work.Top+(kind=="calendar"?30:Height+45);if(Top+Height>work.Bottom)Top=work.Bottom-Height-15;}};
   Render();
  }
  public void SetBackgroundTransparency(int percent){
   byte alpha=(byte)Math.Round(255*(100-percent)/100.0);
   var fill=((SolidColorBrush)U.C("#FFFAED")).Color;
   var line=((SolidColorBrush)U.C("#EFE1C8")).Color;
   fill.A=line.A=alpha;
   background.Background=new SolidColorBrush(fill);
   background.BorderBrush=new SolidColorBrush(line);
   if(percent>=65&&foreground.Effect==null)
    foreground.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.White,Opacity=.82,BlurRadius=6,ShadowDepth=0};
   if(percent<65&&foreground.Effect!=null)foreground.Effect=null;
  }
  void Changed(){Render();}
  void Render(){
   status.Text=repo.Demo?"离线体验":repo.Connected?"与云端同步":"离线缓存";
   var panel=U.Stack();
   if(kind=="calendar"){
    var hero=U.Grid("*,Auto");var date=U.Stack();date.Children.Add(U.Text(selected.ToString("M 月 d 日"),25,"#6E5638",true));U.Gap(date,5);date.Children.Add(U.Text(new[]{"星期日","星期一","星期二","星期三","星期四","星期五","星期六"}[(int)selected.DayOfWeek],11,"#7B6548"));U.Place(hero,date);var today=U.Button("今天",()=>{selected=month=DateTime.Today;Render();},"Secondary");today.VerticalAlignment=VerticalAlignment.Center;today.Padding=new Thickness(9,4,9,4);U.Place(hero,today,1);panel.Children.Add(hero);U.Gap(panel,8);
    var cal=new MonthCalendar(repo,month,selected,d=>{selected=d;month=d;Render();});cal.MonthChanged=m=>month=m;panel.Children.Add(cal);U.Gap(panel,10);panel.Children.Add(new Separator());var items=repo.Items().Where(t=>t.OnDate(J.Date(selected))&&!t.Abandoned).ToList();
    foreach(var item in items.Take(2))panel.Children.Add(Row(item));if(items.Count==0)panel.Children.Add(U.Text("这一天，留一点空白。",12,"#897859"));
   }else{
    panel.Children.Add(U.Text("今天，也要从容一点",22,"#6E5638",true));U.Gap(panel,7);var items=repo.Items().Where(t=>(t.OnDate(J.Today)||t.Overdue)&&!t.Abandoned).ToList();int completed=items.Count(t=>t.Completed);panel.Children.Add(U.Text(DateTime.Today.ToString("M 月 d 日")+"  ·  已完成 "+completed+" / "+items.Count,11,"#7B6548"));U.Gap(panel,15);
    var progress=new ProgressBar{Minimum=0,Maximum=Math.Max(1,items.Count),Value=completed,Height=4,Foreground=U.C("#D28C1C"),Background=U.C("#F5E5C5"),BorderThickness=new Thickness(0)};panel.Children.Add(progress);U.Gap(panel,17);
    var rows=U.Stack();foreach(var item in items.OrderBy(t=>t.Completed).ThenByDescending(t=>t.Task.B("important")))rows.Children.Add(Row(item));if(items.Count==0){var empty=U.Text("今天还没有安排。\n写下一件想做的小事吧。",13,"#897859");empty.LineHeight=26;rows.Children.Add(empty);}panel.Children.Add(new ScrollViewer{Content=rows,MaxHeight=205});
    U.Gap(panel,15);var add=U.Button("＋  添加一项待办",()=>new TaskEditor(this,repo,null,DateTime.Today).ShowDialog(),"Secondary");add.IsEnabled=repo.CanWrite;panel.Children.Add(add);
   }content.Content=kind=="calendar"?(UIElement)new ScrollViewer{Content=panel}:panel;
  }
  FrameworkElement Row(TaskItem item){
   var row=U.Grid("26,*");row.Margin=new Thickness(0,7,0,7);var box=new CheckBox{IsChecked=item.Completed,IsEnabled=repo.CanWrite&&!item.Syncing,VerticalAlignment=VerticalAlignment.Center};box.Click+=async delegate{box.IsEnabled=false;try{await repo.CallAsync(item.Completed?"change_progress":"complete_occurrence",item.Completed?J.Obj("occurrence_id",item.OccurrenceId,"action","reopen"):J.Obj("occurrence_id",item.OccurrenceId));}catch(Exception e){U.Error(this,e);Render();}};U.Place(row,box);
   var t=U.Stack();var title=U.Text(item.Title,12,item.Completed?"#897859":"#6E5638");if(item.Completed)title.TextDecorations=TextDecorations.Strikethrough;t.Children.Add(title);if(item.Time!=""&&!item.Undated){U.Gap(t,4);t.Children.Add(U.Text(item.Time.Substring(0,Math.Min(5,item.Time.Length))+" · "+repo.ListName(item.ListId),10,"#897859"));}if(item.Syncing)t.Children.Add(U.Text("待同步",10,"#8D560B"));else{t.Cursor=Cursors.Hand;t.MouseLeftButtonUp+=delegate{new TaskDetail(this,repo,item).ShowDialog();};}U.Place(row,t,1);return row;
  }
 }
}
