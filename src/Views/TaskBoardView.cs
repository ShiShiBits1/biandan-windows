using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Biandan {
 public sealed partial class MainView {
  const string QuadrantDrag="Biandan.QuadrantOccurrence";
  const string BoardDrag="Biandan.BoardOccurrence";
  static readonly string[] quadrantTitles={"重要且紧急","重要不紧急","紧急不重要","不重要不紧急"};
  static readonly string[] boardTitles={"待办","进行中","已完成"};
  static readonly string[] quadrantColors={"#A94727","#886019","#9B531F","#70634F"};
  static readonly string[] boardColors={"#A89570",PlanningPalette.Accent,"#568462"};
  static readonly string[] quadrantBackgrounds={"#FFF1E6","#FFF4D9","#FFF1E3","#FBF5E9"};
  static readonly string[] boardRanges={"今天","最近七天","全部"};
  int boardRange=2,quadrantRange;
  string boardListId,renderedBoardListId;
  bool movingCard;
  readonly HashSet<string> movingPriorities=new HashSet<string>();
  TaskItem draggedCard;
  BoardLaneItems[] visibleLanes;
  int renderedBoardRange=-1;
  bool renderedQuadrants;

  internal static int Quadrant(TaskItem item){
   return (item.Task.B("important")?0:2)+(item.Task.B("urgent")?0:1);
  }

  int Lane(TaskItem item,bool quadrant){
   return quadrant?Quadrant(item):(int)repo.Stage(item);
  }

  void RenderTaskBoard(bool quadrant){
   int rangeIndex=quadrant?quadrantRange:boardRange;
   if(!quadrant&&boardListId!=null&&!repo.Lists.Any(list=>list.S("list_id")==boardListId))boardListId=null;
   var items=Filtered(rangeIndex==0?"today":rangeIndex==1?"upcoming":"all")
    .Where(item=>!item.Abandoned&&(quadrant?!item.Completed:boardListId==null||item.ListId==boardListId))
    .OrderByDescending(item=>item.Overdue).ThenBy(item=>item.Undated).ThenBy(item=>item.Start).ThenBy(item=>item.Time)
    .ToList();
   var root=U.Grid("*","Auto,*");
   var bar=U.Grid("*,Auto");
   if(quadrant){
    var hint=U.Text("拖动卡片调整优先级。循环待办的优先级应用到整个系列。",12,"#735F42");
    hint.TextWrapping=TextWrapping.Wrap;
    U.Place(bar,hint);
   }else U.Place(bar,BoardListFilter());
   var scopes=U.Stack(true);
   for(int i=0;i<boardRanges.Length;i++){
    int range=i;
    Action select=()=>{if(quadrant)quadrantRange=range;else boardRange=range;RenderTaskBoard(quadrant);};
    var button=quadrant?U.Button(boardRanges[i],select):PlanningPalette.Button(boardRanges[i],select,true);
    bool active=i==rangeIndex;
    button.Background=U.C(quadrant?(active?"#FFE5AC":"#FFFCF6"):(active?PlanningPalette.Selection:PlanningPalette.Panel));
    button.BorderBrush=U.C(quadrant?(active?"#EBC87C":"#EFE1C8"):PlanningPalette.Line);
    button.Margin=new Thickness(5,0,0,0);
    AutomationProperties.SetName(button,"范围："+boardRanges[i]);
    scopes.Children.Add(button);
   }
   U.Place(bar,scopes,1);
   bar.Margin=new Thickness(0,0,0,15);
   U.Place(root,bar);

   var lanes=quadrant?U.Grid("*,14,*","*,14,*"):U.Grid("*,12,*,12,*");
   var labels=quadrant?quadrantTitles:boardTitles;
   var offsets=visibleLanes!=null&&renderedBoardRange==rangeIndex&&renderedQuadrants==quadrant&&(quadrant||renderedBoardListId==boardListId)
    ?visibleLanes.Select(list=>list.Offset).ToArray():new double[labels.Length];
   visibleLanes=new BoardLaneItems[labels.Length];
   renderedBoardRange=rangeIndex;renderedQuadrants=quadrant;renderedBoardListId=boardListId;
   var grouped=items.ToLookup(item=>Lane(item,quadrant));
   for(int i=0;i<labels.Length;i++){
    var column=RenderLane(quadrant,i,labels,grouped[i].ToList(),offsets[i]);
    U.Place(lanes,column,quadrant?(i%2)*2:i*2,quadrant?(i/2)*2:0);
   }
   U.Place(root,lanes,0,1);
   body.Content=root;
  }

  Button BoardListFilter(){
   string label=boardListId==null?"全部清单":repo.ListName(boardListId);
   var button=PlanningPalette.Button("",null,true);
   button.HorizontalAlignment=HorizontalAlignment.Left;
   var content=U.Grid("*,24");
   content.MinWidth=121;
   U.Place(content,U.Text(label,13,PlanningPalette.Ink,true));
   var arrow=U.Icon("\uE70D",11,PlanningPalette.Muted);
   arrow.HorizontalAlignment=HorizontalAlignment.Right;
   U.Place(content,arrow,1);
   button.Content=content;
   AutomationProperties.SetName(button,"筛选清单："+label);
   var menu=Motion.Menu();
   Menu(menu,"全部清单",()=>{boardListId=null;RenderTaskBoard(false);});
   foreach(var list in repo.Lists){
    string id=list.S("list_id");
    Menu(menu,list.S("name"),()=>{boardListId=id;RenderTaskBoard(false);});
   }
   button.ContextMenu=menu;
   button.Click+=delegate{menu.PlacementTarget=button;menu.IsOpen=true;};
   return button;
  }

  FrameworkElement RenderLane(bool quadrant,int lane,string[] titles,List<TaskItem> items,double offset){
   string accent=(quadrant?quadrantColors:boardColors)[lane];
   var layout=U.Grid("*","Auto,*,Auto");
   var header=U.Grid("*,Auto");
   if(quadrant){
    U.Place(header,U.Text(titles[lane],15,accent,true));
    U.Place(header,U.Pill(items.Count.ToString(),accent,"#FFFCF6"),1);
   }else{
    var heading=U.Stack(true);
    heading.Children.Add(new Border{Width=10,Height=10,CornerRadius=new CornerRadius(3),Background=U.C(accent),Margin=new Thickness(1,0,10,0)});
    heading.Children.Add(U.Text(titles[lane],18,PlanningPalette.Ink,true));
    U.Place(header,heading);
    U.Place(header,U.Text(items.Count.ToString(),13,PlanningPalette.Muted),1);
   }
   header.Margin=new Thickness(0,0,0,10);
   U.Place(layout,header);
   var rows=new BoardLaneItems(items,item=>quadrant?RenderBoardCard(item,true,lane,titles):RenderKanbanCard(item,lane),offset);
   if(!quadrant)PlanningPalette.StyleScrollbars(rows);
   visibleLanes[lane]=rows;
   U.Place(layout,rows,0,1);
   if(items.Count==0){
    var empty=U.Text(quadrant?"暂无待办":"暂无任务",12,quadrant?"#89734F":PlanningPalette.Muted);
    empty.Margin=new Thickness(3,10,0,0);
    empty.VerticalAlignment=VerticalAlignment.Top;empty.IsHitTestVisible=false;
    U.Place(layout,empty,0,1);
   }
   if(!quadrant&&lane==0){
    var add=PlanningPalette.Button("＋ 添加任务",()=>NewTask());
    add.Foreground=U.C(PlanningPalette.Emphasis);
    add.HorizontalAlignment=HorizontalAlignment.Left;
    U.Place(layout,add,0,2);
   }
   var surface=quadrant?U.Card(layout,quadrantBackgrounds[lane],17,new Thickness(13,12,13,11))
    :U.Card(layout,PlanningPalette.Panel,12,new Thickness(12,13,10,9));
   var normal=surface.BorderBrush;
   var destination=U.C(quadrant?"#B87819":PlanningPalette.Accent);
   surface.AllowDrop=true;
   surface.DragOver+=delegate(object sender,DragEventArgs e){
    string id=DraggedOccurrence(e,quadrant);
    bool valid=draggedCard!=null&&draggedCard.OccurrenceId==id&&CanMoveCard(draggedCard,quadrant)&&Lane(draggedCard,quadrant)!=lane;
    e.Effects=valid?DragDropEffects.Move:DragDropEffects.None;
    surface.BorderBrush=valid?destination:normal;
    e.Handled=true;
   };
   surface.DragLeave+=delegate{surface.BorderBrush=normal;};
   surface.Drop+=delegate(object sender,DragEventArgs e){
    surface.BorderBrush=normal;
    string id=DraggedOccurrence(e,quadrant);
    e.Handled=true;
    if(draggedCard==null||draggedCard.OccurrenceId!=id)return;
    Dispatcher.BeginInvoke(new Action(async()=>await MoveBoardCard(id,quadrant,lane)),System.Windows.Threading.DispatcherPriority.Input);
   };
   return surface;
  }

  FrameworkElement RenderBoardCard(TaskItem item,bool quadrant,int lane,string[] titles){
   var fields=U.Stack();
   var title=U.Text(item.Title,13,"#322416",true);
   title.TextWrapping=TextWrapping.Wrap;
   if(item.Completed)title.TextDecorations=TextDecorations.Strikethrough;
   fields.Children.Add(title);
   U.Gap(fields,5);
   DateTime date;
   string when=item.Undated?"未安排":DateTime.TryParse(item.Start,out date)?date.ToString("M月d日"):item.Start;
   var meta=U.Text(item.Syncing?"待同步":when+"  ·  "+repo.ListName(item.ListId),11,item.Syncing?"#8D560B":"#796547");
   meta.ToolTip=meta.Text;
   fields.Children.Add(meta);
   int target=item.Task.I("target_count",1);
   if(target>1){
    U.Gap(fields,5);
    fields.Children.Add(U.Text("进度 "+item.Occurrence.I("completed_count")+"/"+target,10,"#886019"));
   }

   var row=U.Grid("*,29");
   var open=U.Button("",()=>EditTask(item));
   open.Padding=new Thickness(10,9,4,9);
   open.HorizontalContentAlignment=HorizontalAlignment.Stretch;
   open.Content=fields;
   AutomationProperties.SetName(open,"查看待办："+item.Title);
   if(item.Syncing)open.IsEnabled=false;
   else TrackCardDrag(open,item,quadrant);
   U.Place(row,open);
   var more=U.IconButton("\uE712","移动卡片",null);
   more.Width=27;
   more.Height=29;
   more.Margin=new Thickness(0,3,2,0);
   more.VerticalAlignment=VerticalAlignment.Top;
   more.IsEnabled=repo.CanWrite&&!item.Syncing&&!item.Abandoned;
   AutomationProperties.SetName(more,"移动待办："+item.Title);
   var menu=Motion.Menu();
   for(int i=0;i<titles.Length;i++){
    if(i==lane)continue;
    int destination=i;
    Menu(menu,"移到「"+titles[i]+"」",async()=>await MoveBoardCard(item.OccurrenceId,quadrant,destination));
   }
   more.Click+=delegate{TaskCardSurface.OpenMenu(more,menu);};
   if(more.IsEnabled)open.ContextMenu=menu;
   U.Place(row,more,1);
   var card=U.TaskCard(row,"#FFFAF0",10,new Thickness(0));
   card.Margin=new Thickness(0,0,2,7);
   return card;
  }

  FrameworkElement RenderKanbanCard(TaskItem item,int lane){
   var row=U.Grid("28,*,27");
   var check=PlanningPalette.Icon(item.Completed?"\uE73E":"\uE739",item.Completed?"撤销完成："+item.Title:"完成待办："+item.Title,
    async()=>await MoveBoardCard(item.OccurrenceId,false,item.Completed?0:2));
   check.Width=26;
   check.VerticalAlignment=VerticalAlignment.Top;
   check.Margin=new Thickness(0,4,0,0);
   check.IsEnabled=repo.CanWrite&&!item.Syncing;
   if(item.Completed){
    var tick=U.Icon("\uE73E",10,"#182D23");tick.HorizontalAlignment=HorizontalAlignment.Center;
    check.Content=new Border{Child=tick,Background=U.C("#D4E8D9"),Width=16,Height=16,CornerRadius=new CornerRadius(4)};
   }
   U.Place(row,check);

   var fields=U.Stack();
   var heading=U.Grid("*,Auto");
   var titleText=U.Text(item.Title,14,PlanningPalette.Ink,true);
   titleText.TextWrapping=TextWrapping.Wrap;
   U.Place(heading,titleText);
   if(item.Task.B("important")){
    var star=U.Icon("\uE735",13,PlanningPalette.Emphasis);star.Margin=new Thickness(5,0,1,0);
    U.Place(heading,star,1);
   }
   fields.Children.Add(heading);
   var list=repo.Lists.FirstOrDefault(entry=>entry.S("list_id")==item.ListId);
   var listRow=U.Grid("14,*");
   listRow.Margin=new Thickness(0,6,0,0);
   U.Place(listRow,new Border{Width=7,Height=7,HorizontalAlignment=HorizontalAlignment.Left,
    CornerRadius=new CornerRadius(2),Background=U.C(ListColor(list.I("color")))});
   U.Place(listRow,U.Text(list==null?"未分类":list.S("name"),11,PlanningPalette.Muted),1);
   fields.Children.Add(listRow);

   DateTime date;
   bool parsed=DateTime.TryParseExact(item.Start,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out date);
   string when=item.Syncing?"待同步":item.Undated?"未安排":parsed?date.ToString("M月d日"):item.Start;
   if(!item.Syncing&&!item.Undated&&parsed&&date==DateTime.Today)when+=" · 今天";
   int count=item.Occurrence.I("completed_count");
   int target=item.Task.I("target_count",1);
   var meta=U.Grid("15,*,Auto");
   meta.Margin=new Thickness(0,8,0,0);
   string dateColor=item.Overdue?PlanningPalette.Emphasis:PlanningPalette.Muted;
   U.Place(meta,U.Icon("\uE787",11,dateColor));
   U.Place(meta,U.Text(when,11,dateColor),1);
   if(target>1){
    var ratio=U.Text(Math.Max(0,count)+"/"+target,11,PlanningPalette.Muted);
    ratio.Margin=new Thickness(5,0,0,0);ratio.ToolTip="子任务或次数进度";
    U.Place(meta,ratio,2);
   }
   fields.Children.Add(meta);
   if(lane==1&&target>1&&count>0){
    var progress=new ProgressBar{Minimum=0,Maximum=target,Value=Math.Min(count,target),
     Height=4,Margin=new Thickness(0,8,0,0),Foreground=U.C(PlanningPalette.Accent),Background=U.C(PlanningPalette.Line),BorderThickness=new Thickness(0)};
    fields.Children.Add(progress);
   }

   var open=PlanningPalette.Button("",()=>EditTask(item));
   open.Content=fields;
   open.Padding=new Thickness(0,9,2,9);
   open.HorizontalContentAlignment=HorizontalAlignment.Stretch;
   open.IsEnabled=!item.Syncing;
   AutomationProperties.SetName(open,"查看待办："+item.Title);
   TrackCardDrag(open,item,false);
   U.Place(row,open,1);
   var more=PlanningPalette.Icon("\uE712","移动待办："+item.Title,null);
   more.Width=25;
   more.VerticalAlignment=VerticalAlignment.Top;
   more.Margin=new Thickness(0,4,0,0);
   more.IsEnabled=repo.CanWrite&&!item.Syncing;
   var menu=Motion.Menu();
   for(int index=0;index<boardTitles.Length;index++){
    if(index==lane)continue;
    int destination=index;
    Menu(menu,"移到「"+boardTitles[index]+"」",async()=>await MoveBoardCard(item.OccurrenceId,false,destination));
   }
   more.Click+=delegate{TaskCardSurface.OpenMenu(more,menu);};
   if(more.IsEnabled)open.ContextMenu=menu;
   U.Place(row,more,2);
   var card=U.TaskCard(row,item.Completed?"#FFF9EC":PlanningPalette.Card,9,new Thickness(5,0,2,0));
   if(item.Task.B("important"))card.BorderBrush=U.C("#EBC87C");
   card.Margin=new Thickness(0,0,2,8);
   return card;
  }

  void TrackCardDrag(Button button,TaskItem item,bool quadrant){
   Point origin=new Point();
   bool pressing=false;
   button.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){origin=e.GetPosition(this);pressing=true;};
   button.PreviewMouseLeftButtonUp+=delegate{pressing=false;};
   button.PreviewMouseMove+=delegate(object sender,MouseEventArgs e){
    if(!pressing||e.LeftButton!=MouseButtonState.Pressed||!CanMoveCard(item,quadrant))return;
    Point now=e.GetPosition(this);
    if(Math.Abs(now.X-origin.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(now.Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
    pressing=false;
    e.Handled=true;
    button.ReleaseMouseCapture();
    draggedCard=item;
    try{DragDrop.DoDragDrop(button,new DataObject(quadrant?QuadrantDrag:BoardDrag,item.OccurrenceId),DragDropEffects.Move);}
    finally{draggedCard=null;}
   };
  }

  static string DraggedOccurrence(DragEventArgs e,bool quadrant){
   string format=quadrant?QuadrantDrag:BoardDrag;
   return e.Data.GetDataPresent(format,false)?e.Data.GetData(format,false) as string:null;
  }

  bool CanMoveCard(TaskItem item,bool quadrant){
   return repo.CanWrite&&!item.Syncing&&!item.Abandoned&&(quadrant?!movingPriorities.Contains(item.Id):!movingCard);
  }

  async Task MoveBoardCard(string id,bool quadrant,int destination){
   var item=repo.Items().FirstOrDefault(candidate=>candidate.OccurrenceId==id);
   if(item==null||!CanMoveCard(item,quadrant)||Lane(item,quadrant)==destination)return;
   if(quadrant)movingPriorities.Add(item.Id);
   else movingCard=true;
   try{
    if(quadrant)await repo.SetPriorityAsync(item.Id,destination<2,destination%2==0);
    else await repo.MoveOnBoardAsync(id,(BoardStage)destination);
    ShowToast("已移到「"+(quadrant?quadrantTitles:boardTitles)[destination]+"」");
   }catch(Exception error){
    SetStatus("移动未确认。"+U.Friendly(error.Message),true);
   }finally{
    if(quadrant)movingPriorities.Remove(item.Id);
    else movingCard=false;
   }
  }
 }
}