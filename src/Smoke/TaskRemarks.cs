using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Biandan {
 public sealed partial class App {
  static void ToggleRemarkPreview(TaskEditor editor,string name){
   var button=VisualControls<Button>(editor).Single(b=>b.IsVisible&&AutomationProperties.GetName(b)==name);
   button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));editor.UpdateLayout();
  }

  static void CheckRenderedRemark(Window window,string container){
   var panel=VisualControls<Border>(window).Single(b=>b.IsVisible&&AutomationProperties.GetName(b)==container);
   var reader=VisualControls<RichTextBox>(panel).Single();
   var document=reader.Document;
   var text=new TextRange(document.ContentStart,document.ContentEnd).Text;
   var links=Controls<Hyperlink>(document).Select(link=>link.NavigateUri.AbsoluteUri).ToArray();
   if(!reader.IsReadOnly||text.Contains("## 核对清单")||!text.Contains("**保留原符号**")||!text.Contains("从零开始")||
      !Controls<Bold>(document).SelectMany(b=>b.Inlines.OfType<Run>()).Any(run=>run.Text=="确认样衣"))
    throw new InvalidOperationException("Task remarks must display lists and emphasis and keep fenced code literal.");
   if(!links.SequenceEqual(new[]{"https://example.com/spec","http://example.com/notes","https://example.com/p.jpg"})||
      Controls<Image>(document).Any())
    throw new InvalidOperationException("Task remarks must expose only explicit web links and must not load pictures automatically.");
  }

  void SaveRemarkEditor(TaskItem item,Action<TaskEditor> configure){
   var editor=new TaskEditor(Desktop,Repo,item);
   Exception failure=null;
   editor.ContentRendered+=delegate{
    try{
     configure(editor);
     ClickBulkButton(editor,"保存待办");
    }catch(Exception error){failure=error;}
    finally{if(editor.IsVisible)editor.Close();}
   };
   var saved=editor.ShowDialog();
   if(failure!=null)throw new InvalidOperationException("Task remark editor interaction failed.",failure);
   if(saved!=true)throw new InvalidOperationException("The task editor did not confirm a demo save.");
  }

  async Task VerifyTaskRemarksAsync(string dir){
   const string title="整理样衣评审记录";
   const string plain="先核对面料  再确认颜色";
   const string legacy="手机记录\n保留  两个空格";
   const string markdown="## 核对清单\r\n- **确认样衣**\r\n- 校对颜色\r\n\r\n0. 从零开始\r\n\r\n[规格资料](https://example.com/spec) · [旧版参考](http://example.com/notes)\r\n\r\n```\r\n**保留原符号**\r\n```\r\n\r\n[本机文件](file:///C:/private.txt) ![示例图](https://example.com/p.jpg)";
   string id=null;
   TaskDetail detail=null;
   try{
    SaveRemarkEditor(null,editor=>{
     VisualControls<TextBox>(editor).Single(box=>box.MaxLength==500).Text=title;
     var raw=VisualControls<TextBox>(editor).Single(box=>AutomationProperties.GetName(box)=="备注原文");
     raw.Text=plain;
     id=editor.BuildCreate().S("operation_id");
     ToggleRemarkPreview(editor,"预览备注");
     var reader=VisualControls<RichTextBox>(editor).Single(box=>box.IsVisible);
     if(!new TextRange(reader.Document.ContentStart,reader.Document.ContentEnd).Text.Contains(plain))
      throw new InvalidOperationException("Plain task remarks lost their spacing in preview.");
    });
    Func<TaskItem> current=()=>Repo.Items().Single(item=>item.Id==id);
    if(current().Task.S("remark")!=plain)throw new InvalidOperationException("The new task lost its plain remark.");
    // A remark originating on another client can use LF instead of Windows CRLF.
    await Repo.CallAsync("update_task",J.Obj("task_id",id,"remark",legacy));
    SaveRemarkEditor(current(),editor=>{
     ToggleRemarkPreview(editor,"预览备注");ToggleRemarkPreview(editor,"编辑备注原文");
     if(editor.BuildCreate().S("remark")!=legacy)throw new InvalidOperationException("Preview changed an existing remark's source.");
     var raw=VisualControls<TextBox>(editor).Single(box=>AutomationProperties.GetName(box)=="备注原文");
     raw.Text=markdown;ToggleRemarkPreview(editor,"预览备注");
     CheckRenderedRemark(editor,"备注预览");
    });
    if(current().Task.S("remark")!=markdown)throw new InvalidOperationException("Saving from preview must keep exact Markdown source.");
    detail=new TaskDetail(Desktop,Repo,current());detail.Show();detail.UpdateLayout();
    CheckRenderedRemark(detail,"待办备注");
    await Task.Delay(250);
    U.Capture(detail,Path.Combine(dir,"task-remark-markdown.png"));
   }finally{
    if(detail!=null)detail.Close();
    if(id!=null&&Repo.Items().Any(item=>item.Id==id)){
     Repo.DemoCall("delete_task",J.Obj("task_id",id));Repo.Persist();Repo.Publish();
    }
   }
  }
 }
}
