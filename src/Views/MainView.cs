using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
namespace Biandan {
 public sealed partial class MainView:Window{
  readonly App app;readonly Repository repo;readonly StackPanel nav;readonly ContentControl body,rail;readonly Grid layout;readonly TextBlock title,subtitle,status;Window searchDialog; Action updateSearch;
  readonly Dictionary<string,Button> navButtons=new Dictionary<string,Button>();string page="today";bool busy;DateTime calendarDate=DateTime.Today,calendarMonth=DateTime.Today;
  TextBlock focusClock;System.Windows.Threading.DispatcherTimer toastTimer;
  TextBox quickInput;
  string quickInputPage;
  readonly Dictionary<Tuple<string,string,string,bool>,string> quickRequests=new Dictionary<Tuple<string,string,string,bool>,string>();
  readonly HashSet<Tuple<string,string,string,bool>> quickInFlight=new HashSet<Tuple<string,string,string,bool>>();
  readonly Dictionary<string,TextBlock> navCounts=new Dictionary<string,TextBlock>();
  string listSignature;
  readonly ColumnDefinition sidebarColumn;
  readonly FrameworkElement brandImage,brandName;
  readonly Button sidebarToggle,newListButton,settingsButton;
  bool sidebarCollapsed;
  SwipeTaskRow openedTaskRow;
  bool calendarWeek;
  public MainView(App application){
   app=application;repo=app.Repo;Title="便单";U.Configure(this,1260,850);WindowWorkArea.Attach(this);MinWidth=1060;MinHeight=720;
   var root=U.Grid("214,*","42,*");
   var clip=new RectangleGeometry{RadiusX=18,RadiusY=18};
   root.Clip=clip;
   root.SizeChanged+=delegate{clip.Rect=new Rect(new Point(),root.RenderSize);};
   sidebarColumn=root.ColumnDefinitions[0];
   sidebarCollapsed=app.Preferences.GetBool("sidebarCollapsed");
   var side=U.Grid("*","80,*,Auto");
   var brand=U.Grid("Auto,*,Auto");brand.Margin=new Thickness(11,0,11,0);brand.VerticalAlignment=VerticalAlignment.Center;
   brandImage=U.Logo(35);brandName=U.Text("便单",24,"#513D24",true);
   brandName.Margin=new Thickness(8,0,0,0);
   U.Place(brand,brandImage);U.Place(brand,brandName,1);
   sidebarToggle=U.IconButton("\uE76B","折叠导航",ToggleSidebar);
   U.Place(brand,sidebarToggle,2);U.Place(side,brand);
   nav=U.Stack();
   var navigationScroll=new ScrollViewer{Content=nav,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
   PlanningPalette.StyleScrollbars(navigationScroll);U.Place(side,navigationScroll,0,1);
   var bottom=U.Stack();bottom.Margin=new Thickness(10,12,10,22);
   newListButton=U.Button("",()=>EditList(null));
   newListButton.ToolTip="新建清单";
   System.Windows.Automation.AutomationProperties.SetName(newListButton,"新建清单");
   bottom.Children.Add(newListButton);U.Gap(bottom,9);
   settingsButton=U.Button("",()=>Navigate("settings"));
   bottom.Children.Add(settingsButton);U.Place(side,bottom,0,2);
   var sidebar=U.Card(side,"#FFFCF5",18,new Thickness(0));
   sidebar.CornerRadius=new CornerRadius(18,0,0,18);
   sidebar.BorderThickness=new Thickness(0,0,1,0);
   U.Place(root,sidebar,0,0,1,2);
   U.Place(root,U.TitleBar(this,"便单  /  Windows 桌面版"),1);
   var main=U.Grid("*","94,*,36");var heading=U.Grid("*,Auto,Auto");heading.Margin=new Thickness(32,8,30,0);
   var tt=U.Stack();tt.VerticalAlignment=VerticalAlignment.Center;title=U.Text("我的一天",28,"#2E2418",true);subtitle=U.Text("",13,"#7B6548");subtitle.Margin=new Thickness(0,6,0,0);tt.Children.Add(title);tt.Children.Add(subtitle);U.Place(heading,tt);
   var toolbar=U.Stack(true);
   toolbar.Margin=new Thickness(0,0,16,0);toolbar.VerticalAlignment=VerticalAlignment.Center;
   var assistant=U.IconButton("\uE8BD","便单小助手",ShowAssistant);
   var search=U.IconButton("\uE721","全局搜索 (Ctrl+K)",ShowSearch);
   System.Windows.Automation.AutomationProperties.SetName(assistant,"便单小助手");
   System.Windows.Automation.AutomationProperties.SetName(search,"全局搜索");
   toolbar.Children.Add(assistant);toolbar.Children.Add(search);U.Place(heading,toolbar,1);
   var add=U.Button("＋  新建待办",()=>NewTask(),"Primary");add.VerticalAlignment=VerticalAlignment.Center;U.Place(heading,add,2);U.Place(main,heading);
   layout=U.Grid("*,286");layout.Margin=new Thickness(32,8,30,6);body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};rail=new ContentControl{Margin=new Thickness(22,0,0,0),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top};U.Place(layout,body);U.Place(layout,rail,1);U.Place(main,layout,0,1);
   var foot=U.Grid("*,Auto");foot.Margin=new Thickness(33,0,30,0);status=U.Text("",10,"#7B6548");U.Place(foot,status);var sync=U.Button("↻  同步",()=>Refresh());sync.FontSize=11;sync.Padding=new Thickness(8,1,8,1);sync.MinHeight=20;U.Place(foot,sync,1);U.Place(main,foot,0,2);U.Place(root,main,1,1);
   var surface=U.Card(root,"#FFFCF6",18,new Thickness(0));
   WindowLighting.Apply(surface,sidebar,root);
   calendarWeek=app.Preferences.GetBool("calendarWeek");
   Content=surface;repo.Changed+=OnChanged;
   Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!app.Quitting&&app.Preferences.GetBool("minimizeToTray",true)){e.Cancel=true;Hide();}else if(!app.Quitting){e.Cancel=true;Dispatcher.BeginInvoke(new Action(app.Quit));}};
   Closed+=delegate{ClearAssistant();repo.Changed-=OnChanged;};PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.N&&Keyboard.Modifiers==ModifierKeys.Control){NewTask();e.Handled=true;}if((e.Key==Key.K||e.Key==Key.F)&&Keyboard.Modifiers==ModifierKeys.Control){e.Handled=true;ShowSearch();}if(e.Key==Key.F5){Refresh();e.Handled=true;}};
   ApplySidebar();BuildNav();RenderPage();
  }
  void ToggleSidebar(){
   sidebarCollapsed=!sidebarCollapsed;
   app.Preferences.Set("sidebarCollapsed",sidebarCollapsed);
   ApplySidebar();
   BuildNav();
  }
  void ApplySidebar(){
   sidebarColumn.Width=new GridLength(sidebarCollapsed?64:214);
   nav.Margin=sidebarCollapsed?new Thickness(8,2,8,0):new Thickness(14,2,14,0);
   var visibility=sidebarCollapsed?Visibility.Collapsed:Visibility.Visible;
   brandImage.Visibility=brandName.Visibility=visibility;
   Grid.SetColumn(sidebarToggle,sidebarCollapsed?0:2);
   Grid.SetColumnSpan(sidebarToggle,sidebarCollapsed?3:1);
   sidebarToggle.HorizontalAlignment=HorizontalAlignment.Center;
   sidebarToggle.Content=U.Icon(sidebarCollapsed?"\uE76C":"\uE76B",16,"#6E5638");
   string label=sidebarCollapsed?"展开导航":"折叠导航";
   sidebarToggle.ToolTip=label;
   System.Windows.Automation.AutomationProperties.SetName(sidebarToggle,label);
   newListButton.Content=sidebarCollapsed?(object)U.Icon("\uE710",17,"#6E5638"):"＋  新建清单";
   newListButton.Padding=sidebarCollapsed?new Thickness(0,6,0,6):new Thickness(13,9,13,9);
  }
  void OnChanged(){
   if(!Dispatcher.CheckAccess()){Dispatcher.Invoke(OnChanged);return;}
   if(page.StartsWith("list:",StringComparison.Ordinal)&&!repo.Lists.Any(list=>list.S("list_id")==page.Substring(5))){
    Navigate("all");
    if(updateSearch!=null)updateSearch();
    return;
   }
   if(listSignature!=J.Encode(repo.Lists))BuildNav();
   else UpdateNavCounts();
   RenderPage();
   if(updateSearch!=null)updateSearch();
  }
  void BuildNav(){
   listSignature=J.Encode(repo.Lists);
   nav.Children.Clear();navButtons.Clear();navCounts.Clear();
   AddNav("today","我的一天","\uE706");AddNav("upcoming","最近七天","\uE787");AddNav("all","全部待办","\uE8FD");AddNav("quadrants","四象限","\uECA5");AddNav("board","看板","\uE8A9");AddNav("important","重要事项","\uE734");AddNav("completed","已完成","\uE73E");
   nav.Children.Add(new Separator{Margin=new Thickness(10,17,10,13)});AddNav("calendar","日历视图","\uE8BF");AddNav("habits","习惯打卡","\uE8F2");AddNav("focus","专注时刻","\uE916");
   if(sidebarCollapsed)nav.Children.Add(new Separator{Margin=new Thickness(7,12,7,8)});
   else{
    var label=U.Text("我的清单",11,"#7B6548");
    label.Margin=new Thickness(14,24,0,9);nav.Children.Add(label);
   }
   foreach(var list in repo.Lists){var id=list.S("list_id");AddNav("list:"+id,list.S("name"),"\uE8F1",ListColor(list.I("color")));}
   U.Gap(nav,22);AddNav("widgets","桌面小组件","\uE80A");
   StyleNavButton(settingsButton,"settings","设置","\uE713");
   UpdateNavCounts();
  }
  void AddNav(string key,string label,string glyph,string color=null){
   var b=U.Button("",()=>Navigate(key));
   StyleNavButton(b,key,label,glyph,color);
   if(key.StartsWith("list:")&&key!="list:1"){var menu=Motion.Menu();var edit=new MenuItem{Header="编辑清单"};var list=repo.Lists.First(l=>l.S("list_id")==key.Substring(5));edit.Click+=delegate{EditList(list);};menu.Items.Add(edit);var del=new MenuItem{Header="删除清单（任务移回未分类）"};del.Click+=async delegate{await app.RunAction(async()=>{var args=J.Obj("list_id",key.Substring(5),"preview",true);var preview=await repo.CallAsync("delete_list",args);if(U.Confirm(this,"删除清单","清单中的 "+preview.Rows("affected_tasks").Count+" 项任务将移到「未分类」。","删除")){args.Remove("preview");await repo.CallAsync("delete_list",args);Navigate("all");}});};menu.Items.Add(del);b.ContextMenu=menu;}
   nav.Children.Add(b);
  }
  void StyleNavButton(Button b,string key,string label,string glyph,string color=null){
   b.Margin=new Thickness(0,2,0,2);
   b.Padding=sidebarCollapsed?new Thickness(0,10,0,10):new Thickness(13,10,12,10);
   b.HorizontalContentAlignment=sidebarCollapsed?HorizontalAlignment.Center:HorizontalAlignment.Stretch;
   b.ToolTip=label;
   System.Windows.Automation.AutomationProperties.SetName(b,label);
   var row=U.Grid("25,*,Auto");
   var icon=U.Icon(glyph,sidebarCollapsed?18:16,color??(page==key?"#6E5638":"#7B6548"));
   if(sidebarCollapsed)icon.HorizontalAlignment=HorizontalAlignment.Center;
   U.Place(row,icon);
   if(!sidebarCollapsed)U.Place(row,U.Text(label,13,page==key?"#513D24":"#6E5638",page==key),1);
   if(!sidebarCollapsed&&(key.StartsWith("list:")||key=="today"||key=="upcoming"||key=="all"||key=="important"||key=="habits")){
    var count=U.Text("",10,page==key?"#8D560B":"#7B6548");
    navCounts[key]=count;U.Place(row,count,2);
   }
   b.Content=row;
   b.Background=page==key?WindowLighting.NavigationFill:Brushes.Transparent;
   navButtons[key]=b;
  }
  void UpdateNavCounts(){
   var counts=new Dictionary<string,int>();
   string today=J.Today,latest=J.Date(DateTime.Today.AddDays(6));
   foreach(var item in repo.Items()){
    if(item.Completed||item.Abandoned)continue;
    Increment(counts,"all");
    Increment(counts,"list:"+item.ListId);
    if(item.Task.B("important"))Increment(counts,"important");
    if(item.Undated)continue;
    if(string.CompareOrdinal(item.Start,today)<=0)Increment(counts,"today");
    if(string.CompareOrdinal(item.Start,latest)<=0&&string.CompareOrdinal(item.End,today)>=0)Increment(counts,"upcoming");
    if(item.Task.B("is_habit")&&item.OnDate(today))Increment(counts,"habits");
   }
   foreach(var pair in navCounts){
    int count;
    pair.Value.Text=counts.TryGetValue(pair.Key,out count)?count.ToString():"";
   }
  }
  static void Increment(Dictionary<string,int> counts,string key){
   int count;counts.TryGetValue(key,out count);counts[key]=count+1;
  }
  public void Navigate(string key){
   if(page!=key){openedTaskRow=null;LeaveBulkPage();taskPageFrame=null;taskResults=null;quickInput=null;quickInputPage=null;if(page=="calendar")weekScroll=null;}
   page=key;BuildNav();RenderPage();
   Button selected;if(navButtons.TryGetValue(key,out selected))Motion.Enter(selected,0.95,false);
  }
  List<TaskItem> Filtered(string key){
   IEnumerable<TaskItem> items=repo.Items();string today=J.Today;
   switch(key){
    case "today":items=items.Where(t=>t.OnDate(today)||t.Overdue);break;
    case "upcoming":var end=J.Date(DateTime.Today.AddDays(6));items=items.Where(t=>!t.Undated&&string.Compare(t.Start,end,StringComparison.Ordinal)<=0&&string.Compare(t.End,today,StringComparison.Ordinal)>=0);break;
    case "important":items=items.Where(t=>t.Task.B("important"));break;
    case "completed":items=items.Where(t=>t.Completed);break;
    case "habits":items=items.Where(t=>t.Task.B("is_habit")&&t.OnDate(today));break;
    case "calendar":items=items.Where(t=>t.OnDate(J.Date(calendarDate)));break;
    default:if(key.StartsWith("list:"))items=items.Where(t=>t.ListId==key.Substring(5));break;
   }
   return items.ToList();
  }
  public void SetStatus(string text,bool error=false){
   status.Text=text;
   status.Foreground=U.C(error?"#B77C5C":"#7B6548");
  }
  public void RenderPage(){
   if(body==null)return;focusClock=null;string pageTitle=page.StartsWith("list:")?repo.ListName(page.Substring(5)):new Dictionary<string,string>{{"today","我的一天"},{"upcoming","最近七天"},{"all","全部待办"},{"important","重要事项"},{"completed","已完成"},{"calendar","日历视图"},{"quadrants","四象限"},{"board","任务看板"},{"habits","习惯打卡"},{"focus","专注时刻"},{"assistant","便单小助手"},{"widgets","桌面小组件"},{"settings","设置"}}[page];
   title.FontSize=page=="board"||page=="calendar"&&calendarWeek?33:28;
   title.Text=pageTitle;subtitle.Text=DateTime.Today.ToString("M 月 d 日")+"  ·  "+new[]{"星期日","星期一","星期二","星期三","星期四","星期五","星期六"}[(int)DateTime.Today.DayOfWeek]+(repo.Demo?"  /  离线体验":"");
   bool taskBoard=page=="quadrants"||page=="board";bool wide=taskBoard||new[]{"calendar","widgets","settings","focus","assistant"}.Contains(page);layout.ColumnDefinitions[1].Width=new GridLength(wide?0:286);rail.Visibility=wide?Visibility.Collapsed:Visibility.Visible;
   if(!wide)RenderRail();
   switch(page){case "quadrants":RenderTaskBoard(true);break;case "board":RenderTaskBoard(false);break;case "calendar":if(calendarWeek)RenderWeekPlan();else RenderCalendar();break;case "widgets":RenderWidgets();break;case "settings":RenderSettings();break;case "focus":RenderFocus();break;case "assistant":RenderAssistant();break;default:RenderTasks(Filtered(page));break;}
   SetSyncStatus();
  }
  void SetSyncStatus(){
   var failed=repo.FailedTaskWriteCount;
   if(!repo.Demo&&!repo.Syncing&&failed>0){
    SetStatus("● "+failed+" 条任务改动同步失败，请在「设置 → 同步详情」查看并重试",true);
    return;
   }
   SetStatus(repo.Demo?"● 离线体验 · 修改仅保存在本机":repo.Syncing?"● 正在同步…":repo.PendingTaskCount>0?"● "+repo.PendingTaskCount+" 项改动待同步，已保存在本机":repo.Connected?"● 已与便单云端同步"+(repo.LastSync.HasValue?"  "+repo.LastSync.Value.ToString("HH:mm"):""):repo.CanWrite?"○ 当前离线 · 可在本机修改待办，联网后同步":"○ 当前离线 · 暂时无法连接便单云端",!repo.Demo&&!repo.Connected);
  }
  FrameworkElement QuickAddBox(){
   var oldInput=quickInputPage==page?quickInput:null;
   string draft=oldInput==null?"":oldInput.Text;
   bool keepFocus=oldInput!=null&&oldInput.IsKeyboardFocusWithin;
   int caret=oldInput==null?0:oldInput.CaretIndex;
   var layout=U.Grid("25,*,Auto");
   U.Place(layout,U.Icon("\uE710",17,"#8D560B"));
   var input=U.Input(draft);
   input.IsEnabled=repo.CanWrite;input.MaxLength=500;
   input.ToolTip="输入待办，按 Enter 添加";
   input.BorderThickness=new Thickness(0);input.Background=Brushes.Transparent;
   input.Padding=new Thickness(4,7,4,7);input.MinHeight=32;
   System.Windows.Automation.AutomationProperties.SetName(input,"快速添加待办");
   quickInput=input;quickInputPage=page;
   input.KeyDown+=async delegate(object sender,KeyEventArgs e){
    if(e.Key!=Key.Enter||!repo.CanWrite||string.IsNullOrWhiteSpace(input.Text))return;
    e.Handled=true;
    await SubmitQuickTask(input);
   };
   var hint=U.Text("快速添加待办",14,"#79684F");
   hint.Margin=new Thickness(5,0,0,0);hint.IsHitTestVisible=false;
   hint.Visibility=draft.Length==0?Visibility.Visible:Visibility.Collapsed;
   input.TextChanged+=delegate{hint.Visibility=input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;};
   U.Place(layout,input,1);U.Place(layout,hint,1);
   var enter=U.Pill("Enter ↵","#79684F","#FFF9EE");
   enter.VerticalAlignment=VerticalAlignment.Center;enter.IsHitTestVisible=false;
   U.Place(layout,enter,2);
   if(keepFocus){
    Dispatcher.BeginInvoke(new Action(()=>{
     if(IsActive&&ReferenceEquals(quickInput,input)&&input.IsEnabled){input.Focus();input.CaretIndex=Math.Min(caret,input.Text.Length);}
    }),System.Windows.Threading.DispatcherPriority.Input);
   }
   var card=U.Card(layout,"#FFFDF8",12,new Thickness(16,7,14,7));
   card.Cursor=Cursors.IBeam;
   card.MouseLeftButtonDown+=delegate{if(input.IsEnabled&&!input.IsKeyboardFocused)input.Focus();};
   input.GotKeyboardFocus+=delegate{card.BorderBrush=U.C("#CE8B24");};
   input.LostKeyboardFocus+=delegate{card.BorderBrush=U.C("#EFE1C8");};
   return card;
  }
  async System.Threading.Tasks.Task SubmitQuickTask(TextBox input){
   string value=input.Text.Trim(),origin=page;
   string date=page=="all"||page.StartsWith("list:")?null:J.Today;
   string listId=page.StartsWith("list:")?page.Substring(5):"1";
   bool important=page=="important";
   var key=Tuple.Create(value,date,listId,important);
   if(!quickInFlight.Add(key))return;
   string operationId;
   if(!quickRequests.TryGetValue(key,out operationId)){
    operationId=J.NewId();quickRequests[key]=operationId;
   }
   input.Clear();
   try{
    await repo.CreateQuickAsync(value,operationId,date,listId,important);
    quickRequests.Remove(key);
    ShowToast("已添加到便单");
   }catch(Exception error){
    if(page==origin&&quickInput!=null&&quickInput.Text.Length==0){
     quickInput.Text=value;quickInput.CaretIndex=value.Length;
    }
    SetStatus("「"+value+"」添加未确认，可用相同标题重试。"+U.Friendly(error.Message),true);
   }finally{quickInFlight.Remove(key);}
  }
  FrameworkElement Stat(string label,string value,string color){return Stat(label,U.Text(value,25,color,true));}
  FrameworkElement Stat(string label,TextBlock value){var g=U.Grid("*,Auto");var s=U.Stack();s.Children.Add(value);U.Gap(s,5);s.Children.Add(U.Text(label,11,"#7B6548"));U.Place(g,s);var c=U.Card(g,"#FFFAF0",13,new Thickness(15,12,12,12));c.Margin=new Thickness(0,0,8,0);return c;}
  public FrameworkElement TaskRow(TaskItem item,bool small=false){
   if(item.Syncing){
    var pending=U.Grid("*,Auto");
    U.Place(pending,U.Text(item.Title,13,"#513D24",true));
    U.Place(pending,U.Text("待同步",11,"#8D560B"),1);
    var pendingCard=U.TaskCard(pending,"#FFFDF8",12,new Thickness(16,14,10,14));
    pendingCard.Margin=new Thickness(0,0,0,8);
    return pendingCard;
   }
   var g=U.Grid("28,*,28,28,28");var check=new CheckBox{IsChecked=item.Completed,VerticalAlignment=VerticalAlignment.Center,IsEnabled=repo.CanWrite&&!item.Abandoned,ToolTip=item.Completed?"撤销完成":"完成本次待办"};
   check.Click+=async delegate{check.IsEnabled=false;await app.RunAction(async()=>{await repo.CallAsync(item.Completed?"change_progress":"complete_occurrence",item.Completed?J.Obj("occurrence_id",item.OccurrenceId,"action","reopen"):J.Obj("occurrence_id",item.OccurrenceId));ShowToast(item.Completed?"已重新打开":"完成一件小事，做得很好。");});check.IsChecked=item.Completed;check.IsEnabled=repo.CanWrite&&!item.Abandoned;};U.Place(g,check);
   var text=U.Stack();text.Margin=new Thickness(3,0,7,0);
   var name=U.Text(item.Title,small?12:15,item.Completed||item.Abandoned?"#79684F":"#2E2418",!small&&!item.Completed&&!item.Abandoned);
   if(item.Completed)name.TextDecorations=TextDecorations.Strikethrough;
   text.Children.Add(name);U.Gap(text,5);
   var meta=U.Stack(true);double metaSize=small?10:11;
   if(item.Time!=""&&!item.Undated){
    var time=U.Text(item.Time.Substring(0,Math.Min(5,item.Time.Length)),metaSize,"#79684F");
    time.Margin=new Thickness(0,0,9,0);meta.Children.Add(time);
   }
   if(item.Overdue)meta.Children.Add(U.Text("已逾期  ",metaSize,"#A94727"));
   string listColor=ListColor(repo.Lists.FirstOrDefault(l=>l.S("list_id")==item.ListId).I("color"));
   meta.Children.Add(new Ellipse{Width=6,Height=6,Fill=U.C(listColor),Margin=new Thickness(0,0,5,0),VerticalAlignment=VerticalAlignment.Center});
   meta.Children.Add(U.Text(repo.ListName(item.ListId),metaSize,"#79684F"));
   if(item.Repeating)meta.Children.Add(U.Text("   ↻ 重复",metaSize,"#79684F"));if(item.Task.B("is_habit"))meta.Children.Add(U.Text("   习惯",metaSize,"#79684F"));
   if(item.Task.Strings("subtasks").Count>0)meta.Children.Add(U.Text("   "+item.Occurrence.I("completed_count")+"/"+item.Task.Strings("subtasks").Count,metaSize,"#79684F"));
   if(item.Undated)meta.Children.Add(U.Text("   未安排",metaSize,"#79684F"));text.Children.Add(meta);text.Cursor=Cursors.Hand;text.MouseLeftButtonUp+=delegate{EditTask(item);};U.Place(g,text,1);
   var star=U.IconButton(item.Task.B("important")?"\uE735":"\uE734","标记重要",async()=>{await app.RunAction(async()=>await repo.CallAsync("update_task",J.Obj("task_id",item.Id,"important",!item.Task.B("important"))));});if(item.Task.B("important"))((TextBlock)star.Content).Foreground=U.C("#B7A36E");star.Width=28;U.Place(g,star,2);
   var focus=U.Button("",()=>{
    if(app.FocusStarted.HasValue){ShowToast("已有专注正在进行");return;}
    app.StartFocus(25,item.Title,item.OccurrenceId);
    Navigate("focus");
   },"IconButton");
   focus.Width=28;
   focus.Content=TomatoClockIcon();
   focus.ToolTip="开始 25 分钟专注";
   System.Windows.Automation.AutomationProperties.SetName(focus,"开始 25 分钟专注："+item.Title);
   U.Place(g,focus,3);
   var more=U.IconButton("\uE712","更多操作",null);more.Click+=delegate{var menu=Motion.Menu();Menu(menu,"编辑待办",()=>EditTask(item));Menu(menu,"打开详情与子任务",()=>new TaskDetail(this,repo,item).ShowDialog());Menu(menu,item.Abandoned?"恢复本次":"跳过本次",async()=>await app.RunAction(async()=>await repo.CallAsync("change_progress",J.Obj("occurrence_id",item.OccurrenceId,"action",item.Abandoned?"restore":"abandon"))));
   menu.Items.Add(new Separator());Menu(menu,item.Repeating?"删除本次…":"删除待办…",async()=>await DeleteTask(item,false));if(item.Repeating)Menu(menu,"删除整个重复系列…",async()=>await DeleteTask(item,true));TaskCardSurface.OpenMenu(more,menu);};more.Width=28;U.Place(g,more,4);
   var card=U.TaskCard(g,"#FFFDF8",12,new Thickness(16,12,10,12));
   if(!repo.CanWrite){card.Margin=new Thickness(0,0,0,8);return card;}
   return new SwipeTaskRow(card,RevealTaskDelete,async()=>await DeleteTask(item,false));
  }
  void RevealTaskDelete(SwipeTaskRow row){
   if(openedTaskRow!=null&&!ReferenceEquals(openedTaskRow,row))openedTaskRow.CloseActions();
   openedTaskRow=row;
  }
  static FrameworkElement TomatoClockIcon(){
   var icon=new Canvas{Width=17,Height=17,IsHitTestVisible=false};
   icon.Children.Add(new System.Windows.Shapes.Path{
    Data=Geometry.Parse("M8.5,5 C5.2,3.6 1.6,5.1 1.6,9.2 C1.6,13.3 4.5,15.7 8.5,15.7 C12.5,15.7 15.4,13.3 15.4,9.2 C15.4,5.1 11.8,3.6 8.5,5 Z"),
    Fill=U.C("#C15E43")
   });
   icon.Children.Add(new System.Windows.Shapes.Path{
    Data=Geometry.Parse("M8.5,4.7 C7.3,3 5.7,2.8 4.6,3.4 L6.3,4.9 L5.5,6.1 C6.8,6.1 7.7,5.8 8.5,5.1 C9.3,5.8 10.2,6.1 11.5,6.1 L10.7,4.9 L12.4,3.4 C11.4,2.9 10.1,3 9,3.8 L9.4,1.7 L8.3,1.5 Z"),
    Fill=U.C("#677348")
   });
   icon.Children.Add(new System.Windows.Shapes.Path{
    Data=Geometry.Parse("M8.5,7.4 L8.5,10.4 L10.7,10.4"),
    Stroke=Brushes.White,StrokeThickness=1.2,StrokeStartLineCap=PenLineCap.Round,
    StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round
   });
   return icon;
  }
  static void Menu(ContextMenu menu,string label,Action action){var item=new MenuItem{Header=label};item.Click+=delegate{action();};menu.Items.Add(item);}
  async System.Threading.Tasks.Task DeleteTask(TaskItem item,bool series){
   await app.RunAction(async()=>{var a=J.Obj("task_id",item.Id,"preview",true);if(item.Repeating){a["scope"]=series?"series":"occurrence";if(!series)a["occurrence_id"]=item.OccurrenceId;}
    var p=await repo.CallAsync("delete_task",a);if(U.Confirm(this,series?"删除重复系列":"删除待办","将永久删除「"+item.Title+"」"+(series?"及其 "+p.Strings("occurrence_ids").Count+" 次执行记录":"的选中记录")+"。","删除")){a.Remove("preview");await repo.CallAsync("delete_task",a);ShowToast("已删除");}});
  }
  MonthCalendar railCalendar;
  TextBlock railClock;
  Button railFocusButton;
  void RenderRail(){
   if(railCalendar==null){
    var panel=U.Stack();
    var cal=U.Stack();var head=U.Grid("*,Auto");
    U.Place(head,U.Text("日历",16,"#2E2418",true));
    var widget=U.IconButton("\uE8A7","打开日历小组件",()=>app.ToggleWidget("calendar"));
    System.Windows.Automation.AutomationProperties.SetName(widget,"打开日历小组件");
    U.Place(head,widget,1);cal.Children.Add(head);U.Gap(cal,5);
    railCalendar=new MonthCalendar(repo,DateTime.Today,DateTime.Today,d=>{calendarDate=d;calendarMonth=d;Navigate("calendar");});
    cal.Children.Add(railCalendar);panel.Children.Add(U.Card(cal,"#FFFDF8",16,new Thickness(18,13,18,16)));
    U.Gap(panel,14);

    var timer=U.Stack();
    var timerTitle=U.Stack(true);
    timerTitle.Children.Add(U.Icon("\uE916",16,"#8D560B"));
    var label=U.Text("番茄专注",16,"#2E2418",true);label.Margin=new Thickness(8,0,0,0);timerTitle.Children.Add(label);
    timer.Children.Add(timerTitle);U.Gap(timer,9);
    railClock=U.Text("",41,"#513D24");railClock.FontFamily=new FontFamily("Segoe UI");railClock.FontWeight=FontWeights.SemiBold;
    timer.Children.Add(railClock);U.Gap(timer,12);
    railFocusButton=U.Button("",()=>Navigate("focus"),"Secondary");
    railFocusButton.Background=U.C("#FFF3DB");
    timer.Children.Add(railFocusButton);panel.Children.Add(U.Card(timer,"#FFFDF8",16,new Thickness(19,17,19,17)));
    rail.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
   }else railCalendar.Render();
   railFocusButton.Content=app.FocusStarted.HasValue?"查看本次专注":"进入专注";
   if(app.FocusStarted.HasValue){focusClock=railClock;TickFocus();}
   else railClock.Text="25:00";
  }
  void RenderCalendar(){
   var frame=U.Grid("*","Auto,*");
   var modes=CalendarModes();modes.Margin=new Thickness(0,0,0,12);U.Place(frame,modes);
   var g=U.Grid("*,265");var panel=U.Stack();var c=new MonthCalendar(repo,calendarMonth,calendarDate,d=>{calendarDate=d;RenderCalendar();},false);c.MonthChanged=m=>calendarMonth=m;panel.Children.Add(U.Card(c,"#FFFAF0",16,new Thickness(22)));
   var actions=U.Stack(true);actions.Margin=new Thickness(0,14,0,0);actions.Children.Add(U.Button("回到今天",()=>{calendarDate=DateTime.Today;calendarMonth=DateTime.Today;RenderCalendar();},"Secondary"));var add=U.Button("＋ 添加当天待办",()=>NewTask(calendarDate),"Primary");add.Margin=new Thickness(10,0,0,0);actions.Children.Add(add);panel.Children.Add(actions);U.Place(g,panel);
   var day=U.Stack();day.Margin=new Thickness(20,0,0,0);day.Children.Add(U.Text(calendarDate.ToString("M 月 d 日"),20,"#513D24",true));U.Gap(day,8);day.Children.Add(U.Text("这一天的安排",11,"#897859"));U.Gap(day,18);var items=repo.Items().Where(t=>t.OnDate(J.Date(calendarDate))).ToList();foreach(var item in items)day.Children.Add(TaskRow(item,true));if(items.Count==0){var t=U.Text("还没有安排\n留白，也是一种美好。",12,"#897859");t.LineHeight=26;day.Children.Add(t);}U.Place(g,day,1);U.Place(frame,new ScrollViewer{Content=g},0,1);body.Content=frame;
  }
  public void OpenCalendarDate(DateTime date){calendarDate=date;calendarMonth=date;Navigate("calendar");}
  void NewTask(DateTime? date=null,string listId=null,TimeSpan? time=null){
   new TaskEditor(this,repo,null,date??(page=="calendar"?calendarDate:DateTime.Today),
    listId??(page.StartsWith("list:")?page.Substring(5):page=="board"?(boardListId??"1"):"1"),time).ShowDialog();
  }
  void EditTask(TaskItem item){new TaskDetail(this,repo,item).ShowDialog();}
  void EditList(Dictionary<string,object> list){if(!repo.CanWrite){ShowToast("请先登录后再修改");return;}ListEditor.Show(this,repo,list);}
  async void Refresh(){if(busy||repo.Syncing)return;busy=true;SetStatus("正在同步…");try{await repo.SyncAsync();if(repo.FailedTaskWriteCount>0)SetSyncStatus();else ShowToast(repo.Demo?"当前为本机体验数据":"已同步");}catch(Exception e){SetStatus(U.Friendly(e.Message),true);}finally{busy=false;}}
  public void ShowToast(string message){SetStatus("✓ "+message);if(toastTimer==null){toastTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(4)};toastTimer.Tick+=delegate{toastTimer.Stop();SetSyncStatus();};}toastTimer.Stop();toastTimer.Start();}
  public static string ListColor(int color){string[] colors={"#927B54","#CA8C84","#C7A17D","#B9AB72","#82A277","#78A6A1","#90ACBE","#7D8EA9","#A38EAF"};return colors[color==0?0:((color-1)%8)+1];}
 }
}

