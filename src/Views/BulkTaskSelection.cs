using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Biandan {
 public sealed partial class MainView {
  readonly HashSet<string> bulkIds=new HashSet<string>();
  readonly HashSet<CheckBox> visibleBulkChecks=new HashSet<CheckBox>();
  List<TaskItem> bulkItems=new List<TaskItem>();
  Button bulkStart,bulkAll,bulkMove,bulkDate,bulkCancel;
  TextBlock bulkCount;
  bool BulkSelecting{get;set;}

  void UpdateBulkTasks(List<TaskItem> items){
   bulkItems=items.Where(item=>!item.Syncing).ToList();
   if(bulkIds.Count>0)bulkIds.IntersectWith(bulkItems.Select(item=>item.OccurrenceId));
   RefreshBulkToolbar();
  }

  void LeaveBulkPage(){
   BulkSelecting=false;
   bulkIds.Clear();
   bulkItems.Clear();
   visibleBulkChecks.Clear();
   PreviewKeyDown-=BulkKeyDown;
   bulkStart=bulkAll=bulkMove=bulkDate=bulkCancel=null;bulkCount=null;
  }

  FrameworkElement BulkTaskToolbar(){
   var bar=new WrapPanel{Margin=new Thickness(0,0,0,7)};
   bulkStart=U.Button("多选",()=>{BulkSelecting=true;RefreshBulkToolbar();RenderPage();},"Secondary");
   bulkStart.ToolTip="选择多项待办";
   bulkCount=U.Text("",12,"#513D24",true);
   bulkCount.VerticalAlignment=VerticalAlignment.Center;
   bulkCount.Margin=new Thickness(0,2,10,2);
   bulkAll=U.Button("全选",ToggleBulkAll);
   bulkAll.ToolTip="选择本页全部记录，包括已收起的分组";
   bulkMove=U.Button("移到清单",()=>ShowBulkDialog(false),"Secondary");
   bulkDate=U.Button("改日期",()=>ShowBulkDialog(true),"Secondary");
   bulkCancel=U.Button("退出多选",EndBulkSelection);
   foreach(var button in new[]{bulkStart,bulkAll,bulkMove,bulkDate,bulkCancel}){
    button.Padding=new Thickness(10,5,10,5);
    button.MinHeight=29;
    button.Margin=new Thickness(0,2,6,2);
   }
   bar.Children.Add(bulkStart);bar.Children.Add(bulkCount);
   foreach(var button in new[]{bulkAll,bulkMove,bulkDate,bulkCancel})bar.Children.Add(button);
   PreviewKeyDown-=BulkKeyDown;PreviewKeyDown+=BulkKeyDown;
   RefreshBulkToolbar();
   return bar;
  }

  void RefreshBulkToolbar(){
   if(bulkStart==null)return;
   bulkStart.Visibility=BulkSelecting?Visibility.Collapsed:Visibility.Visible;
   bulkStart.IsEnabled=repo.CanWrite&&bulkItems.Count>0;
   var visibility=BulkSelecting?Visibility.Visible:Visibility.Collapsed;
   bulkCount.Visibility=bulkAll.Visibility=bulkMove.Visibility=bulkDate.Visibility=bulkCancel.Visibility=visibility;
   bulkCount.Text="已选 "+bulkIds.Count+" 项";
   bulkAll.Content=bulkIds.Count>0&&bulkIds.Count==bulkItems.Count?"清空选择":"全选";
   bulkAll.IsEnabled=bulkItems.Count>0;
   bulkMove.IsEnabled=bulkDate.IsEnabled=bulkIds.Count>0&&repo.CanWrite;
  }

  void EndBulkSelection(){
   BulkSelecting=false;bulkIds.Clear();RefreshBulkToolbar();RenderPage();
  }

  void BulkKeyDown(object sender,KeyEventArgs args){
   if(!BulkSelecting||args.Handled)return;
   if(args.Key==Key.Escape){args.Handled=true;EndBulkSelection();}
   if(args.Key==Key.A&&Keyboard.Modifiers==ModifierKeys.Control&&!(Keyboard.FocusedElement is TextBox)){
    args.Handled=true;SelectBulkAll(true);
   }
  }

  void ToggleBulkAll(){SelectBulkAll(bulkIds.Count!=bulkItems.Count);}

  void SelectBulkAll(bool select){
   bulkIds.Clear();
   if(select)bulkIds.UnionWith(bulkItems.Select(item=>item.OccurrenceId));
   foreach(var check in visibleBulkChecks)check.IsChecked=bulkIds.Contains((string)check.Tag);
   RefreshBulkToolbar();
  }

  void ToggleBulkRow(CheckBox check){
   string id=(string)check.Tag;
   if(check.IsChecked==true)bulkIds.Add(id);else bulkIds.Remove(id);
   RefreshBulkToolbar();
  }

  FrameworkElement BulkTaskRow(TaskItem item){
   if(!BulkSelecting||item.Syncing)return TaskRow(item);
   var row=U.Grid("30,*");
   string id=item.OccurrenceId;
   var check=new CheckBox{Tag=id,IsChecked=bulkIds.Contains(id),VerticalAlignment=VerticalAlignment.Center};
   AutomationProperties.SetName(check,"选择待办："+item.Title);
   check.Click+=delegate{ToggleBulkRow(check);};
   check.Loaded+=delegate{visibleBulkChecks.Add(check);check.IsChecked=bulkIds.Contains(id);};
   check.Unloaded+=delegate{visibleBulkChecks.Remove(check);};
   U.Place(row,check);
   var text=U.Stack();text.Cursor=Cursors.Hand;text.Background=Brushes.Transparent;
   var name=U.Text(item.Title,13,"#513D24",true);
   if(item.Completed)name.TextDecorations=TextDecorations.Strikethrough;
   text.Children.Add(name);U.Gap(text,5);
   string when=item.Undated?"未安排":item.Start+(item.Time.Length==0?"":" "+item.Time.Substring(0,Math.Min(5,item.Time.Length)));
   string state=item.Completed?"  ·  已完成":item.Abandoned?"  ·  已跳过":"";
   text.Children.Add(U.Text(repo.ListName(item.ListId)+"  ·  "+when+(item.Repeating?"  ·  重复":"")+state,11,"#79684F"));
   U.Place(row,text,1);
   var card=U.TaskCard(row,"#FFFAF0",12,new Thickness(14,10,12,10));
   card.Margin=new Thickness(0,0,0,8);
   card.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){
    if(e.Handled)return;
    check.Focus();check.IsChecked=check.IsChecked!=true;ToggleBulkRow(check);e.Handled=true;
   };
   return card;
  }

  void ShowBulkDialog(bool dateChange){
   var selected=bulkItems.Where(item=>bulkIds.Contains(item.OccurrenceId)).ToList();
   if(selected.Count==0)return;
   var eligible=dateChange?repo.BulkDateCandidates(selected):selected;
   int skipped=selected.Count-eligible.Count;
   var targetIds=eligible.Select(item=>item.OccurrenceId).ToArray();
   string title=dateChange?"批量改日期":"批量移到清单";
   var dialog=U.Dialog(this,title,450,330);
   var panel=U.Stack();
   var info=U.Text("",13,"#513D24");info.TextWrapping=TextWrapping.Wrap;
   if(dateChange){
    info.Text=eligible.Count+" 项单次待办将使用新的开始日期，保留原有时段和跨日天数。";
    if(skipped>0)info.Text+=" 另有 "+skipped+" 项无法批量改期。仅支持待完成且只有一条执行记录的单次待办。";
   }else{
    info.Text="将 "+eligible.Select(item=>item.Id).Distinct().Count()+" 项待办移到同一清单。";
    if(eligible.Any(item=>item.Repeating))info.Text+=" 选中的重复待办将连同整个系列一起移动。";
   }
   panel.Children.Add(info);U.Gap(panel,16);
   var lists=repo.Lists.ToList();
   var list=new ComboBox();
   var date=new DatePicker{SelectedDate=DateTime.Today};
   if(eligible.Count>0){
    if(dateChange)panel.Children.Add(U.Field("新的开始日期",date));
    else{
     foreach(var entry in lists)list.Items.Add(entry.S("name"));
     list.SelectedIndex=-1;
     panel.Children.Add(U.Field("目标清单",list));
    }
   }
   var footer=U.Stack(true);footer.HorizontalAlignment=HorizontalAlignment.Right;
   footer.Children.Add(U.Button(eligible.Count==0?"关闭":"取消",()=>dialog.DialogResult=false));
   int changed=0;
   if(eligible.Count>0){
    var apply=U.Button(dateChange?"确认改期":"确认移动",null,"Primary");
    apply.Margin=new Thickness(8,0,0,0);
    apply.IsEnabled=dateChange;
    if(!dateChange)list.SelectionChanged+=delegate{apply.IsEnabled=list.SelectedIndex>=0;};
    apply.Click+=delegate{
     try{
      if(dateChange&&!date.SelectedDate.HasValue)throw new McpException("请选择新的开始日期。");
      changed=dateChange?repo.RescheduleBulkTasks(targetIds,date.SelectedDate.Value):
       repo.MoveBulkTasks(targetIds,lists[list.SelectedIndex].S("list_id"));
      dialog.DialogResult=true;
     }catch(Exception error){U.Error(dialog,error);}
    };
    footer.Children.Add(apply);
   }
   U.DialogContent(dialog,title,panel,footer);
   if(dialog.ShowDialog()==true){
    EndBulkSelection();
    string message=changed==0?"所选待办已在目标"+(dateChange?"日期":"清单"):
     (dateChange?"已改期 ":"已移动 ")+changed+" 项待办";
    if(skipped>0)message+="，跳过 "+skipped+" 项";
    ShowToast(message);
   }
  }
 }
}
