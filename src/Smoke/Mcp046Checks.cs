using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace Biandan {
 public sealed partial class App {
  async Task VerifyBatchDeleteDialogAsync(){
   string list=J.NewId(),single=J.NewId(),series=J.NewId();
   Repo.DemoCall("create_list",J.Obj("operation_id",list,"name","删除验收"));
   Repo.DemoCall("create_task",J.Obj("operation_id",single,"list_id",list,"title","单次待办"));
   Repo.DemoCall("create_task",J.Obj("operation_id",series,"list_id",list,"title","重复待办",
    "start_date",J.Today,"end_date",J.Date(DateTime.Today.AddDays(2)),"repeat_rule",J.Obj("mode","daily")));
   Repo.Publish();
   try{
    Desktop.Navigate("list:"+list);
    ClickBulkButton(Desktop,"多选");
    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    Desktop.UpdateLayout();
    var first=Repo.Items().First(item=>item.Id==series);
    var select=VisualControls<CheckBox>(Desktop).FirstOrDefault(check=>Equals(check.Tag,first.OccurrenceId));
    if(select==null)throw new InvalidOperationException("The selected repeat instance was not rendered in multi-select mode.");
    select.IsChecked=true;
    select.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
    Exception failure=null;
    var interaction=Dispatcher.BeginInvoke(new Action(()=>{
     var dialog=Desktop.OwnedWindows.Cast<Window>().Single(window=>window.Title=="批量删除待办");
     try{
      dialog.UpdateLayout();
      var apply=VisualControls<Button>(dialog).Single(button=>Equals(button.Content,"确认删除"));
      if(apply.IsEnabled)throw new InvalidOperationException("Delete must be disabled before preview.");
      ClickBulkButton(dialog,"预览删除");
      if(!apply.IsEnabled||Repo.Items().Count(item=>item.Id==series)!=3)
       throw new InvalidOperationException("Preview must keep all selected instances and permit confirmation.");
      ClickBulkButton(dialog,"确认删除");
      if(Repo.Items().Count(item=>item.Id==series)!=2||Repo.Items().Any(item=>item.OccurrenceId==first.OccurrenceId))
       throw new InvalidOperationException("Default batch scope must delete only the selected repeat instance.");
     }catch(Exception error){failure=error;}
     finally{dialog.Close();}
    }),DispatcherPriority.ApplicationIdle);
    ClickBulkButton(Desktop,"批量删除");
    await interaction.Task;
    if(failure!=null)throw new InvalidOperationException("The batch delete dialog failed.",failure);
    if(!Repo.Items().Any(item=>item.Id==single))throw new InvalidOperationException("An unselected task was deleted.");
   }finally{
    Desktop.Navigate("today");
    Repo.DemoCall("delete_task",J.Obj("task_id",series));
    Repo.DemoCall("delete_task",J.Obj("task_id",single));
    Repo.DemoCall("delete_list",J.Obj("list_id",list));
    Repo.Persist();Repo.Publish();
   }
  }

  static async Task VerifyCloudBatchDeleteAsync(){
   const int size=25;
   var tasks=Enumerable.Range(1,size).Select(id=>J.Obj("task_id",id.ToString(),"title","Batch "+id)).ToArray();
   var occurrences=Enumerable.Range(1,size).Select(id=>J.Obj("task_id",id.ToString(),"occurrence_id",(id+100).ToString(),"status","pending")).ToArray();
   var service=new PaginationService();
   int previews=0,writes=0;
   service.Reply=(name,args)=>{
    switch(name){
     case "biandan_check_connection":return J.Obj();
     case "biandan_list_lists":return J.Obj("items",new[]{J.Obj("list_id","1","name","默认")});
     case "biandan_search_tasks":return PaginationPage(tasks,size,null);
     case "biandan_list_occurrences":return PaginationPage(occurrences,size,null);
     case "biandan_list_focus":return PaginationPage(new object[0],0,null);
     case "biandan_batch_delete_tasks":
      var targets=args.Rows("tasks");
      if(targets.Count!=size||targets.Any(target=>target.S("scope")!="occurrence"||target.ContainsKey("preview")))
       throw new InvalidOperationException("The desktop must send the full selection to a single MCP call.");
      bool preview=args.B("preview");
      if(preview)previews++;else writes++;
      var results=targets.Select((target,index)=>!preview&&index==size-1?
       J.Obj("ok",false,"error","Failed","may_have_written",true):
       J.Obj("ok",true,"result",J.Obj("task_id",target.S("task_id"),"scope","occurrence",
        "occurrence_ids",new[]{target.S("occurrence_id")},"deletes_task_definition",true,preview?"preview":"deleted",true))).ToArray();
      return J.Obj("items",results,"preview",preview,"succeeded",preview?size:size-1);
     default:throw new InvalidOperationException("Unexpected tool: "+name);
    }
   };
   var temp=Path.Combine(Path.GetTempPath(),"biandan-batch-"+Guid.NewGuid().ToString("N"));
   try{
    using(var client=new McpClient(new HttpClient(service){BaseAddress=new Uri("http://localhost/mcp")})){
     var repo=new Repository(new Settings(temp),client);
     await repo.LoginAsync(false);
     var targets=repo.BulkDeleteTargets(repo.Items().Select(item=>item.OccurrenceId),"occurrence");
     var preview=await repo.BatchDeleteAsync(targets,true);
     if(previews!=1||preview.Count!=size||repo.Items().Count!=size)
      throw new InvalidOperationException("Cloud delete preview did not preserve the cached selection.");
     var result=await repo.BatchDeleteAsync(targets,false);
     if(writes!=1||result.Count(row=>row.B("ok"))!=size-1||repo.Items().Count!=1||
        repo.Items().Single().OccurrenceId!=targets.Last().S("occurrence_id"))
      throw new InvalidOperationException("Cloud delete should apply successes and keep the unconfirmed item.");
    }
   }finally{
    foreach(string file in Directory.GetFiles(temp))File.Delete(file);
    Directory.Delete(temp);
   }
  }

  static void VerifyOfficialRemarkFormats(){
   const string sample="<indent level=\"2\"><b>加粗</b> <i>斜体</i> <u>下划线</u> <s>删除线</s> <span data-color=\"lakeBlue\">湖蓝</span>\n"
    +"7. 第一项\n9) 第二项\n  8. 缩进项\n---\nhttps://example.com/path。\n"
    +"````\n```\n<b>字面内容</b>\n````\n```\n## 未闭合围栏后仍可读";
   var doc=((RichTextBox)TaskRemarkMarkdown.Render(sample)).Document;
   var blocks=doc.Blocks.Cast<Block>().ToList();
   var plain=new TextRange(doc.ContentStart,doc.ContentEnd).Text;
   var lists=blocks.OfType<List>().ToList();
   var first=(Paragraph)blocks[0];
   var color=first.Inlines.OfType<Span>().Single(span=>((Run)span.Inlines.FirstInline).Text=="湖蓝");
   if(first.Margin.Left!=24||!first.Inlines.OfType<Bold>().Any()||!first.Inlines.OfType<Italic>().Any()||
      !first.Inlines.OfType<Underline>().Any()||!first.Inlines.OfType<Span>().Any(span=>span.TextDecorations.Count>0)||
      color.Foreground.ToString()!=U.C(ListPalette.Hex(5)).ToString()||
      lists.Count!=2||lists[0].StartIndex!=1||lists[0].ListItems.Count!=2||lists[1].Margin.Left!=12||
      !blocks.OfType<Paragraph>().Any(paragraph=>paragraph.BorderThickness.Bottom==1)||
      Controls<Hyperlink>(doc).Single().NavigateUri.AbsoluteUri!="https://example.com/path"||
      !plain.Contains("<b>字面内容</b>")||!plain.Contains("未闭合围栏后仍可读")||
      plain.Contains("<indent")||plain.Contains("data-color"))
    throw new InvalidOperationException("The desktop preview dropped an official remark format.");
  }
 }
}
