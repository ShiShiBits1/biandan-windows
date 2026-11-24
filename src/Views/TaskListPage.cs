using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Biandan {
 public sealed partial class MainView {
  Grid taskPageFrame;
  TaskResultsList taskResults;
  TextBlock pendingCount,completedCount,focusMinutes,totalCount,emptyLabel;
  FrameworkElement emptyTasks,bulkToolbar;
  Expander completedGroup,skippedGroup;
  List<TaskItem> activeRows,completedRows,skippedRows;

  void RenderTasks(List<TaskItem> items){
   UpdateBulkTasks(items);
   if(taskPageFrame==null)BuildTaskListPage();
   Grid.SetRow(bulkToolbar,BulkSelecting?1:0);
   Grid.SetColumn(bulkToolbar,BulkSelecting?0:1);
   Grid.SetColumnSpan(bulkToolbar,BulkSelecting?2:1);
   bulkToolbar.Margin=BulkSelecting?new Thickness(0,4,0,0):new Thickness(0);
   activeRows=items.Where(t=>!t.Completed&&!t.Abandoned).OrderByDescending(t=>t.Overdue).ThenByDescending(t=>t.Task.B("important")).ToList();
   completedRows=items.Where(t=>t.Completed).ToList();
   skippedRows=items.Where(t=>t.Abandoned).ToList();
   pendingCount.Text=activeRows.Count.ToString();
   completedCount.Text=completedRows.Count.ToString();
   focusMinutes.Text=FocusDuration.Format(repo.Focus.Where(f=>f.S("date")==J.Today).Sum(record=>FocusDuration.Minutes(record)));
   totalCount.Text=items.Count+" 项";
   if(quickInput!=null)quickInput.IsEnabled=repo.CanWrite;
   completedGroup.Header="已完成  ·  "+completedRows.Count;
   skippedGroup.Header="已跳过  ·  "+skippedRows.Count;
   emptyLabel.Text=items.Count>0?"没有待完成事项":page=="today"?"今天还没有待办":page=="completed"?"还没有完成记录":"暂无待办";
   UpdateGroupedRows();
  }

  void BuildTaskListPage(){
   var panel=U.Stack();
   if(page=="today"){
    panel.Children.Add(QuickAddBox());
    var summary=new WrapPanel{Margin=new Thickness(2,12,0,16)};
    pendingCount=AddDayMetric(summary,"待完成");
    completedCount=AddDayMetric(summary,"已完成");
    focusMinutes=AddDayMetric(summary,"专注分钟");
    panel.Children.Add(summary);
   }else{
    var stats=U.Grid("*,*,*");
    pendingCount=U.Text("",25,"#8D560B",true);completedCount=U.Text("",25,"#8D560B",true);focusMinutes=U.Text("",25,"#7B6548",true);
    U.Place(stats,Stat("待完成",pendingCount));U.Place(stats,Stat("已完成",completedCount),1);U.Place(stats,Stat("专注分钟",focusMinutes),2);panel.Children.Add(stats);U.Gap(panel,20);
   }
   var section=U.Grid("*,Auto","Auto,Auto");
   var heading=U.Stack(true);
   heading.Children.Add(U.Text(page=="habits"?"今天的好习惯":page=="completed"?"完成记录":"待办事项",16,"#2E2418",true));
   totalCount=U.Text("",12,"#79684F");totalCount.Margin=new Thickness(10,0,0,0);heading.Children.Add(totalCount);U.Place(section,heading);
   bulkToolbar=BulkTaskToolbar();U.Place(section,bulkToolbar,1);
   panel.Children.Add(section);U.Gap(panel,8);
   if(page!="today"&&page!="completed"&&page!="habits"){panel.Children.Add(QuickAddBox());U.Gap(panel,9);}
   var empty=U.Stack();empty.HorizontalAlignment=HorizontalAlignment.Center;empty.Margin=new Thickness(0,26,0,26);
   var icon=U.Icon("\uE73E",30,"#A89570");icon.HorizontalAlignment=HorizontalAlignment.Center;
   empty.Children.Add(icon);U.Gap(empty,13);emptyLabel=U.Text("",13,"#7B6548");empty.Children.Add(emptyLabel);emptyTasks=empty;
   completedGroup=TaskGroup(page=="completed");skippedGroup=TaskGroup(false);
   taskResults=new TaskResultsList(BulkTaskRow,t=>RowSignature(t,BulkSelecting));
   System.Windows.Automation.AutomationProperties.SetName(taskResults,"待办列表");
   taskPageFrame=U.Grid("*","Auto,*");U.Place(taskPageFrame,panel);U.Place(taskPageFrame,taskResults,0,1);
   body.Content=taskPageFrame;
  }

  static TextBlock AddDayMetric(Panel summary,string label){
   var metric=U.Stack(true);
   if(summary.Children.Count>0)
    metric.Children.Add(new Border{Width=1,Height=12,Background=U.C("#DCCBA9"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(13,0,13,0)});
   metric.Children.Add(U.Text(label,12,"#79684F"));
   var value=U.Text("",14,"#513D24",true);value.Margin=new Thickness(6,0,0,0);
   metric.Children.Add(value);summary.Children.Add(metric);return value;
  }

  Expander TaskGroup(bool expanded){
   var group=new Expander{IsExpanded=expanded,Foreground=U.C("#7B6548"),FontSize=12,Margin=new Thickness(0,9,0,5)};
   group.Expanded+=delegate{UpdateGroupedRows();};
   group.Collapsed+=delegate{UpdateGroupedRows();};
   return group;
  }

  void UpdateGroupedRows(){
   if(taskResults==null||activeRows==null)return;
   var rows=new List<object>(activeRows);
   if(activeRows.Count==0&&(page!="completed"||completedRows.Count==0))rows.Add(emptyTasks);
   if(completedRows.Count>0){rows.Add(completedGroup);if(completedGroup.IsExpanded)rows.AddRange(completedRows);}
   if(skippedRows.Count>0){rows.Add(skippedGroup);if(skippedGroup.IsExpanded)rows.AddRange(skippedRows);}
   taskResults.UpdateRows(rows);
  }

  string RowSignature(TaskItem item,bool selecting){
   var list=repo.Lists.FirstOrDefault(l=>l.S("list_id")==item.ListId);
   return J.Encode(new object[]{item.Task,item.Occurrence,item.Syncing,item.Overdue,repo.CanWrite,list.S("name"),list.I("color"),selecting});
  }

  void ShowSearch(){
   if(searchDialog!=null){searchDialog.Activate();return;}
   var dialog=U.Dialog(this,"全局搜索",700,570);searchDialog=dialog;
   var panel=U.Grid("*","Auto,Auto,*");
   U.Place(panel,U.Text("搜索标题、备注和子任务",12,"#79684F"));
   var input=U.Input();input.Margin=new Thickness(0,8,0,12);input.ToolTip="搜索标题、备注和子任务";
   System.Windows.Automation.AutomationProperties.SetName(input,"搜索标题、备注和子任务");U.Place(panel,input,0,1);
   var results=new TaskResultsList(t=>TaskRow(t),t=>RowSignature(t,false));
   System.Windows.Automation.AutomationProperties.SetName(results,"搜索结果");U.Place(panel,results,0,2);
   var empty=U.Text("没有找到匹配的待办",13,"#79684F");
   var delay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};
   List<TaskItem> indexedItems=null;
   var searchable=new Dictionary<string,string>();
   string lastTerm=null;
   updateSearch=delegate{
    delay.Stop();
    string term=input.Text.Trim();
    var rows=new List<object>();
    if(term.Length>0){
     var items=repo.Items();
     if(!ReferenceEquals(items,indexedItems)){
      searchable.Clear();
      foreach(var item in items)if(!searchable.ContainsKey(item.Id))
       searchable.Add(item.Id,item.Title+" "+item.Task.S("remark")+" "+string.Join(" ",item.Task.Strings("subtasks")));
      indexedItems=items;
     }
     foreach(var item in items)if(searchable[item.Id].IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0)rows.Add(item);
     if(rows.Count==0)rows.Add(empty);
    }
    results.UpdateRows(rows,term!=lastTerm);lastTerm=term;
   };
   delay.Tick+=delegate{updateSearch();};
   input.TextChanged+=delegate{delay.Stop();if(input.Text.Trim().Length==0)updateSearch();else delay.Start();};
   dialog.Loaded+=delegate{input.Focus();};
   U.DialogContent(dialog,"全局搜索",panel,null,false);
   try{dialog.ShowDialog();}
   finally{delay.Stop();searchDialog=null;updateSearch=null;}
  }
 }
}
