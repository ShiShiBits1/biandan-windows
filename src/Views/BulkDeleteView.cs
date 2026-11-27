using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Biandan {
 public sealed partial class MainView {
  static bool HasDeletions(Dictionary<string,object> row){
   var result=row.D("result");
   return row.B("ok")&&(result.B("deletes_task_definition")||result.Strings("occurrence_ids").Count>0);
  }

  static bool SameDeleteTargets(Dictionary<string,object> first,Dictionary<string,object> second){
   if(!second.B("ok"))return false;
   var before=first.D("result");
   var after=second.D("result");
   return before.B("deletes_task_definition")==after.B("deletes_task_definition")&&
    new HashSet<string>(before.Strings("occurrence_ids"),StringComparer.Ordinal).SetEquals(after.Strings("occurrence_ids"));
  }

  void ShowBulkDeleteDialog(){
   var selected=bulkItems.Where(item=>bulkIds.Contains(item.OccurrenceId)).ToList();
   if(selected.Count==0)return;
   var titles=selected.GroupBy(item=>item.Id).ToDictionary(group=>group.Key,group=>group.First().Title);
   var dialog=U.Dialog(this,"批量删除待办",530,500);
   var panel=U.Stack();
   var scope=new ComboBox();
   string[] scopes={"occurrence","future_pending","series"};
   scope.Items.Add("只删除选中的执行记录");
   scope.Items.Add("删除之后尚未完成的执行记录");
   scope.Items.Add("删除整个重复系列");
   scope.SelectedIndex=0;
   if(selected.Any(item=>item.Repeating))panel.Children.Add(U.Field("重复待办的删除范围",scope));
   var status=U.Text("已选 "+selected.Count+" 条记录。删除前可查看影响范围。",13,"#513D24",true);
   status.TextWrapping=TextWrapping.Wrap;
   panel.Children.Add(status);
   U.Gap(panel,10);
   var details=new ListBox{MaxHeight=220,Visibility=Visibility.Collapsed};
   ScrollViewer.SetHorizontalScrollBarVisibility(details,ScrollBarVisibility.Disabled);
   var detailStyle=new Style(typeof(ListBoxItem));
   detailStyle.Setters.Add(new Setter(ToolTipProperty,new Binding()));
   details.ItemContainerStyle=detailStyle;
   panel.Children.Add(details);
   var buttons=U.Stack(true);
   buttons.HorizontalAlignment=HorizontalAlignment.Right;
   var close=U.Button("关闭",()=>dialog.Close());
   var preview=U.Button("预览删除",null,"Secondary");
   var apply=U.Button("确认删除",null,"Primary");
   foreach(var button in new[]{close,preview,apply}){
    button.Margin=new Thickness(8,0,0,0);
    buttons.Children.Add(button);
   }
   apply.IsEnabled=false;
   bool busy=false,submitted=false;
   Dictionary<string,object>[] targets=null;
   List<Dictionary<string,object>> plan=null;
   var selectedIds=selected.Select(item=>item.OccurrenceId).ToArray();

   Action<bool> setBusy=working=>{
    busy=working;
    scope.IsEnabled=!working&&!submitted;
    close.IsEnabled=!working;
    preview.IsEnabled=!working&&!submitted;
    apply.IsEnabled=!working&&!submitted&&plan!=null&&plan.Any(HasDeletions);
   };
   Action<List<Dictionary<string,object>>,Dictionary<string,object>[],bool> showRows=(rows,inputs,executed)=>{
    details.ItemsSource=rows.Select((row,index)=>{
     string name=titles[inputs[index].S("task_id")];
     if(!row.B("ok"))return name+" · "+(row.B("may_have_written")?"需同步核对 · ":"")+U.Friendly(row.S("error"));
     var result=row.D("result");
     return name+" · "+(executed?"已删除 ":"将删除 ")+result.Strings("occurrence_ids").Count+
      " 次执行"+(result.B("deletes_task_definition")?"，含待办本身":"");
    }).ToArray();
    details.Visibility=Visibility.Visible;
   };
   scope.SelectionChanged+=delegate{
    plan=null;apply.IsEnabled=false;
    details.Visibility=Visibility.Collapsed;
    status.Text="删除范围已更改，请重新预览。";
   };
   dialog.Closing+=delegate(object sender,CancelEventArgs args){args.Cancel=busy;};
   preview.Click+=async delegate{
    if(busy)return;
    plan=null;setBusy(true);
    details.Visibility=Visibility.Collapsed;
    status.Text="正在读取删除预览…";
    try{
     targets=repo.BulkDeleteTargets(selectedIds,scopes[scope.SelectedIndex]);
     plan=await repo.BatchDeleteAsync(targets,true);
     status.Text=plan.Count(HasDeletions)+" 个目标可删除，涉及 "+
      plan.Where(HasDeletions).Sum(row=>(long)row.D("result").Strings("occurrence_ids").Count)+" 次执行。请核对后确认。";
     showRows(plan,targets,false);
    }catch(Exception error){status.Text=U.Friendly(error.Message);}
    finally{setBusy(false);}
   };
   apply.Click+=async delegate{
    if(busy||submitted||plan==null)return;
    setBusy(true);
    status.Text="正在复查删除范围…";
    try{
     var selectedIndices=Enumerable.Range(0,plan.Count).Where(index=>HasDeletions(plan[index])).ToArray();
     var actualTargets=selectedIndices.Select(index=>targets[index]).ToArray();
     var latest=await repo.BatchDeleteAsync(actualTargets,true);
     if(!latest.Select((row,index)=>SameDeleteTargets(plan[selectedIndices[index]],row)).All(same=>same)){
      plan=null;
      details.Visibility=Visibility.Collapsed;
      status.Text="删除范围已变化，请重新预览后确认。";
      return;
     }
     submitted=true;
     status.Text="正在删除待办。选中项较多时需要等待…";
     var results=await repo.BatchDeleteAsync(actualTargets,false);
     int done=results.Count(row=>row.B("ok"));
     showRows(results,actualTargets,true);
     status.Text="已删除 "+done+" 个目标。"+(done==results.Count?"":"另有 "+(results.Count-done)+" 个目标未确认删除，请查看结果。");
    }catch(Exception error){
     status.Text=submitted?"删除结果尚未全部确认。请按 F5 同步后查看剩余待办。":U.Friendly(error.Message);
    }finally{setBusy(false);}
   };
   U.DialogContent(dialog,"批量删除待办",panel,buttons);
   dialog.ShowDialog();
  }
 }
}
