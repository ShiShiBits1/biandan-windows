using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Biandan {
 public sealed partial class MainView {
  AssistantView assistantView;
  Window assistantDialog;

  void RenderAssistant(){
   if(assistantView==null)assistantView=new AssistantView(this,app);
   if(assistantDialog==null)body.Content=assistantView;
   assistantView.UpdateAvailability();
  }

  void ShowAssistant(){
   if(assistantDialog!=null){assistantDialog.Activate();return;}
   var dialog=U.Dialog(this,"便单小助手",800,682);
   if(assistantView==null)assistantView=new AssistantView(dialog,app);
   var view=assistantView;
   assistantDialog=dialog;
   if(ReferenceEquals(body.Content,view))body.Content=null;
   view.SetOwner(dialog);
   view.UpdateAvailability();
   U.DialogContent(dialog,"便单小助手",view,null,false);
   try{dialog.ShowDialog();}
   finally{
    var container=view.Parent as Border;
    if(container!=null)container.Child=null;
    view.SetOwner(this);
    assistantDialog=null;
    if(page=="assistant")body.Content=view;
   }
  }

  void ClearAssistant(){
   if(assistantView==null)return;
   assistantView.Stop();
   assistantView=null;
  }
 }

 internal sealed partial class AssistantView:UserControl{
  Window owner;
  readonly App app;
  readonly CodexAssistant cli;
  readonly TextBox input;
  readonly TextBlock status;
  readonly CheckBox includeTasks;
  readonly StackPanel messages;
  readonly ScrollViewer history;
  readonly Button send,stop,statusButton,loginButton,chooseButton;
  readonly Queue<string> previousQuestions=new Queue<string>();
  CancellationTokenSource current;

  public AssistantView(Window owner,App app){
   this.owner=owner;this.app=app;cli=new CodexAssistant(app.Preferences);
   var root=U.Grid("*","Auto,*,Auto");
   var header=U.Grid("*,Auto");
   status=U.Text("使用本机 Codex CLI 的 ChatGPT 登录。",12,"#79684F");
   status.TextWrapping=TextWrapping.Wrap;U.Place(header,status);
   var actions=U.Stack(true);
   statusButton=U.Button("检查登录",async()=>await CheckLogin(),"Secondary");
   loginButton=U.Button("登录 Codex",OpenLogin,"Secondary");
   chooseButton=U.Button("选择 CLI",ChooseCli,"Secondary");
   foreach(var button in new[]{statusButton,loginButton,chooseButton}){
    button.Margin=new Thickness(7,0,0,0);actions.Children.Add(button);
   }
   U.Place(header,actions,1);
   var headerCard=GlassCard(header,15,new Thickness(16,12,16,12));
   headerCard.Margin=new Thickness(0,0,0,12);U.Place(root,headerCard);

   messages=U.Stack();
   var intro=U.Stack();
   intro.Children.Add(U.Text("今天要安排什么？",21,"#513D24",true));
   U.Gap(intro,12);
   var examples=new WrapPanel();
   Suggest(examples,"查询今天的待办","查询今天的全部待办。");
   Suggest(examples,"拆解一项任务","帮我把这件事拆成几条可以直接执行的待办：");
   Suggest(examples,"启动番茄钟","开始 25 分钟专注。");
   intro.Children.Add(examples);
   messages.Children.Add(GlassCard(intro,17,new Thickness(19)));
   U.Gap(messages,12);
   history=new ScrollViewer{Content=messages,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
   U.Place(root,history,0,1);

   var compose=U.Stack();
   input=U.Input("",true);
   input.Style=(Style)FindResource("AssistantInput");
   input.MaxLength=3000;input.MinHeight=58;input.MaxHeight=132;
   input.ToolTip="输入问题，Ctrl + Enter 发送";
   AutomationProperties.SetName(input,"小助手问题");
   input.PreviewKeyDown+=async delegate(object sender,KeyEventArgs e){
    if(e.Key==Key.Enter&&Keyboard.Modifiers==ModifierKeys.Control){e.Handled=true;await Send();}
   };
   compose.Children.Add(input);U.Gap(compose,5);
   var bottom=U.Grid("*,Auto,Auto");
   includeTasks=new CheckBox{Content="附上近期待办并允许查询本机清单",VerticalAlignment=VerticalAlignment.Center};
   includeTasks.ToolTip="勾选后将最多 30 条近期待办的标题、日期、优先级和状态附到本次提问。按日期查询的结果仅在本机显示。";
   U.Place(bottom,includeTasks);
   stop=U.Button("停止",()=>{if(current!=null)current.Cancel();},"Secondary");
   stop.Margin=new Thickness(0,0,8,0);U.Place(bottom,stop,1);
   send=U.Button("发送",async()=>await Send(),"AssistantSend");U.Place(bottom,send,2);
   compose.Children.Add(bottom);
   var composer=GlassCard(compose,23,new Thickness(12));
   composer.Margin=new Thickness(0,10,0,0);
   U.Place(root,composer,0,2);
   Content=root;
   SetBusy(false);
  }

  Border GlassCard(UIElement child,double radius,Thickness padding){
   return new Border{Style=(Style)FindResource("FrostedCard"),Child=child,CornerRadius=new CornerRadius(radius),Padding=padding};
  }

  public void SetOwner(Window window){owner=window;}

  void Suggest(Panel panel,string label,string prompt){
   var button=U.Button(label,()=>{
    input.Text=prompt;input.Focus();input.CaretIndex=prompt.Length;
   },"Secondary");
   button.Margin=new Thickness(0,0,7,4);panel.Children.Add(button);
  }

  public void UpdateAvailability(){
   foreach(var card in messages.Children.OfType<Border>())
    ToggleSuggestionButtons(card);
  }

  void ToggleSuggestionButtons(DependencyObject parent){
   var button=parent as Button;
   if(button!=null&&button.Tag is AssistantSuggestion&&Equals(button.Content,"添加待办"))
    button.IsEnabled=app.Repo.CanWrite;
   if(button!=null&&button.Tag is AssistantFocus&&!Equals(button.Content,"已开始")){
    button.IsEnabled=!app.FocusStarted.HasValue;
    button.Content=button.IsEnabled?"开始专注":"已有专注";
    button.ToolTip=button.IsEnabled?null:"请先在「专注时刻」保存或结束当前专注。";
   }
   foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
    ToggleSuggestionButtons(child);
  }

  void SetBusy(bool busy){
   send.IsEnabled=statusButton.IsEnabled=chooseButton.IsEnabled=loginButton.IsEnabled=!busy;
   stop.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
  }

  public void Stop(){if(current!=null)current.Cancel();}

  void ChooseCli(){
   var dialog=new OpenFileDialog{Title="选择 Codex CLI",FileName="codex.exe",Filter="Windows 可执行文件 (*.exe)|*.exe",CheckFileExists=true};
   if(dialog.ShowDialog(owner)!=true)return;
   app.Preferences.Set("codexExecutable",dialog.FileName);
   status.Text="已选择 CLI，可以检查登录状态。";
  }

  void OpenLogin(){
   try{cli.StartLogin();status.Text="请在打开的窗口完成 ChatGPT 登录，再点击检查登录。";}
   catch(Exception error){status.Text=U.Friendly(error.Message);}
  }

  async Task CheckLogin(){
   if(current!=null)return;
   using(var job=new CancellationTokenSource(TimeSpan.FromSeconds(20))){
    current=job;SetBusy(true);status.Text="正在检查 Codex 登录…";
    try{await cli.CheckLoginAsync(job.Token);status.Text="Codex CLI 已登录 ChatGPT。";}
    catch(OperationCanceledException){status.Text="登录检查已停止。";}
    catch(Exception error){status.Text=U.Friendly(error.Message);}
    finally{current=null;SetBusy(false);}
   }
  }

  async Task Send(){
   string question=input.Text.Trim();
   if(current!=null||question.Length==0)return;
   bool include=includeTasks.IsChecked==true;
   var context=include?app.Repo.Items().Where(task=>!task.Abandoned&&!task.Syncing&&
    (task.Undated&&!task.Completed||task.Overdue||task.OnDate(J.Today)||
     !task.Undated&&!task.Completed&&string.CompareOrdinal(task.Start,J.Date(DateTime.Today.AddDays(6)))<=0))
    .OrderBy(task=>task.Completed).ThenByDescending(task=>task.Task.B("important")).Take(30).ToArray():new TaskItem[0];
   using(var job=new CancellationTokenSource(TimeSpan.FromMinutes(3))){
    current=job;SetBusy(true);
    input.Clear();
    AddMessage("你",question,null,null);
    status.Text="Codex 正在回答…";
    try{
     var reply=await cli.AskAsync(question,context,job.Token,include,previousQuestions.ToArray());
     job.Token.ThrowIfCancellationRequested();
     previousQuestions.Enqueue(question);
     if(previousQuestions.Count>3)previousQuestions.Dequeue();
     ShowReply(reply,include&&includeTasks.IsChecked==true);
     status.Text="Codex CLI 已登录 ChatGPT。";
    }catch(OperationCanceledException){
     status.Text="本次请求已停止。";if(input.Text.Length==0)input.Text=question;
    }catch(Exception error){
     status.Text=U.Friendly(error.Message);if(input.Text.Length==0)input.Text=question;
    }finally{current=null;SetBusy(false);}
   }
  }

  void AddMessage(string label,string content,IEnumerable<AssistantSuggestion> suggestions,AssistantFocus focus){
   var panel=U.Stack();
   panel.Children.Add(U.Text(label,11,"#8D560B",true));U.Gap(panel,7);
   var text=U.Text(content,14,"#2E2418");
   text.TextWrapping=TextWrapping.Wrap;text.TextTrimming=TextTrimming.None;text.LineHeight=22;
   panel.Children.Add(text);
   if(suggestions!=null)
    foreach(var suggested in suggestions){
     U.Gap(panel,8);panel.Children.Add(SuggestionCard(suggested));
    }
   if(focus!=null){U.Gap(panel,8);panel.Children.Add(FocusCard(focus));}
   var card=GlassCard(panel,15,new Thickness(16,12,16,13));
   card.Margin=new Thickness(0,0,0,11);messages.Children.Add(card);
   Dispatcher.BeginInvoke(new Action(history.ScrollToEnd),System.Windows.Threading.DispatcherPriority.Background);
  }

  FrameworkElement SuggestionCard(AssistantSuggestion task){
   var row=U.Grid("*,Auto");
   var information=U.Stack();
   var title=U.Text(task.Title,13,"#513D24",true);title.TextWrapping=TextWrapping.Wrap;
   information.Children.Add(title);U.Gap(information,3);
   var description=U.Text(DraftDetails(task),12,"#79684F");
   description.TextWrapping=TextWrapping.Wrap;description.TextTrimming=TextTrimming.None;
   information.Children.Add(description);
   U.Place(row,information);
   var add=U.Button("添加待办",null,"Secondary");
   add.Tag=task;add.IsEnabled=app.Repo.CanWrite;
   add.Margin=new Thickness(9,0,0,0);U.Place(row,add,1);
   add.Click+=async delegate{
    if(!add.IsEnabled)return;
    add.IsEnabled=false;add.Content="添加中…";
    try{
     await app.Repo.CreateAssistantTaskAsync(task);
     add.Content="已添加";
    }catch(Exception error){
     status.Text=U.Friendly(error.Message);
     add.Content="添加待办";add.IsEnabled=app.Repo.CanWrite;
    }
   };
   return U.Card(row,"#FFFBF3",10,new Thickness(9));
  }
 }
}