using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Biandan {
 public sealed class SyncDetails {
  const int PageSize=40;
  readonly Repository repo;
  readonly Window window;
  readonly StackPanel items=U.Stack();
  readonly TextBlock feedback=U.Text("",12,"#B96B51");
  string retryingId;
  int limit=PageSize;
  int updateQueued;
  bool closed;

  SyncDetails(Window owner,Repository repository){
   repo=repository;
   window=U.Dialog(owner,"同步详情",540,520);
   var content=U.Stack();
   feedback.TextWrapping=TextWrapping.Wrap;
   feedback.Visibility=Visibility.Collapsed;
   feedback.Margin=new Thickness(0,0,0,10);
   content.Children.Add(feedback);
   content.Children.Add(items);
   U.DialogContent(window,"同步详情",content);
  }

  public static void Show(Window owner,Repository repository){
   var view=new SyncDetails(owner,repository);
   repository.Changed+=view.QueueUpdate;
   try{
    view.Render();
    view.window.ShowDialog();
   }finally{
    view.closed=true;
    repository.Changed-=view.QueueUpdate;
   }
  }

  void QueueUpdate(){
   if(Interlocked.Exchange(ref updateQueued,1)!=0)return;
   window.Dispatcher.BeginInvoke(new Action(delegate{
    Interlocked.Exchange(ref updateQueued,0);
    if(!closed)Render();
   }));
  }

  void Render(){
   items.Children.Clear();
   List<Dictionary<string,object>> entries;
   try{
    entries=repo.SyncQueueDetails();
   }catch(McpException e){
    var failure=U.Text(U.Friendly(e.Message),13,"#B96B51");
    failure.TextWrapping=TextWrapping.Wrap;
    items.Children.Add(failure);
    return;
   }
   if(entries.Count==0){
    items.Children.Add(U.Text("当前没有待同步的任务改动",14,"#6E5638"));
    return;
   }
   items.Children.Add(U.Text("本机有 "+entries.Count+" 条待同步改动",15,"#513D24",true));
   U.Gap(items,12);
   for(int index=0;index<Math.Min(limit,entries.Count);index++){
    AddEntry(entries[index]);
   }
   if(limit<entries.Count){
    var more=U.Button("显示更多",delegate{limit+=PageSize;Render();},"Secondary");
    more.HorizontalAlignment=HorizontalAlignment.Left;
    items.Children.Add(more);
   }
  }

  void AddEntry(Dictionary<string,object> entry){
   string id=entry.S("id");
   var row=U.Grid("*,Auto");
   var description=U.Stack();
   var title=U.Text(entry.S("title"),14,"#513D24",true);
   title.TextWrapping=TextWrapping.Wrap;
   description.Children.Add(title);
   string metadata=entry.S("operation")+"  ·  "+entry.S("status");
   DateTimeOffset queued;
   if(DateTimeOffset.TryParse(entry.S("queuedAt"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out queued)){
    metadata+="  ·  "+queued.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
   }
   var status=U.Text(metadata,12,"#7B6548");
   status.TextWrapping=TextWrapping.Wrap;
   status.Margin=new Thickness(0,4,0,0);
   description.Children.Add(status);
   string error=entry.S("error");
   if(!string.IsNullOrWhiteSpace(error)){
    var reason=U.Text(U.Friendly(error),12,"#B96B51");
    reason.TextWrapping=TextWrapping.Wrap;
    reason.Margin=new Thickness(0,7,0,0);
    description.Children.Add(reason);
   }
   U.Place(row,description);
   if(entry.B("canRetry")){
    var retry=U.Button(id==retryingId?"重试中…":"重试",null,"Secondary");
    retry.Margin=new Thickness(12,0,0,0);
    retry.VerticalAlignment=VerticalAlignment.Center;
    retry.IsEnabled=retryingId==null;
    System.Windows.Automation.AutomationProperties.SetName(retry,"重试"+entry.S("operation")+" "+entry.S("title"));
    retry.Click+=async delegate{await RetryAsync(id);};
    U.Place(row,retry,1);
   }
   var card=U.Card(row,"#FFFAF0",12,new Thickness(14));
   card.Margin=new Thickness(0,0,0,8);
   items.Children.Add(card);
  }

  async Task RetryAsync(string id){
   if(retryingId!=null)return;
   retryingId=id;
   feedback.Visibility=Visibility.Collapsed;
   Render();
   try{
    await repo.RetryTaskWriteAsync(id);
   }catch(Exception e){
    if(!closed){
     feedback.Text=U.Friendly(e.Message);
     feedback.Visibility=Visibility.Visible;
    }
   }finally{
    retryingId=null;
    if(!closed)Render();
   }
  }
 }
}
