using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
namespace Biandan {
 public sealed class TaskEditor:Window{
  readonly Repository repo;readonly TaskItem item;readonly string operationId=J.NewId();readonly TextBox name,remark,startTime,endTime,subtasks,target,location,reminderTime,ruleDates;readonly DatePicker start,end,reminderDate;readonly CheckBox dated,important,urgent,collected,habit,remind,makeup;readonly ComboBox list,repeat;readonly TextBlock error;readonly Button save;bool saving;
  readonly List<Dictionary<string,object>> listItems;readonly CheckBox[] weekdays;readonly StackPanel repeatFields;
  readonly string originalSubtaskText;
  public TaskEditor(Window owner,Repository repository,TaskItem existing,DateTime? initialDate=null,string initialList="1",TimeSpan? initialTime=null){
   repo=repository;item=existing;Title=existing==null?"新建待办":"编辑待办";Owner=owner;ShowInTaskbar=false;U.Configure(this,580,800);Height=Math.Min(800,SystemParameters.WorkArea.Height-35);ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
   var t=existing==null?J.Obj():existing.Task;var panel=U.Stack();panel.Children.Add(U.Text(existing==null?"写下一件小事":"编辑这项待办",23,"#4A3316",true));U.Gap(panel,19);
   name=U.Input(t.S("title"));name.MaxLength=500;panel.Children.Add(U.Field("待办名称",name));
   remark=U.Input(t.S("remark"),true);panel.Children.Add(RemarkField());
   listItems=repo.Lists.ToList();if(listItems.Count==0)listItems.Add(J.Obj("list_id","1","name","未分类"));list=new ComboBox();foreach(var l in listItems)list.Items.Add(l.S("name"));int li=listItems.FindIndex(l=>l.S("list_id")==t.S("list_id",initialList));list.SelectedIndex=Math.Max(0,li);
   panel.Children.Add(U.Field("所属清单",list));
   var flags=U.Stack(true);important=new CheckBox{Content="重要",IsChecked=t.B("important")};urgent=new CheckBox{Content="紧急",IsChecked=t.B("urgent"),Margin=new Thickness(24,0,0,0)};collected=new CheckBox{Content="收藏",IsChecked=t.B("collected"),Margin=new Thickness(24,0,0,0)};flags.Children.Add(important);flags.Children.Add(urgent);flags.Children.Add(collected);panel.Children.Add(flags);U.Gap(panel,20);
   dated=new CheckBox{Content="安排日期",IsChecked=existing==null||!t.B("undated")};panel.Children.Add(dated);U.Gap(panel,10);
   DateTime parsed;start=new DatePicker{SelectedDate=DateTime.TryParse(existing==null?"":existing.Start,out parsed)?parsed:initialDate??DateTime.Today};end=new DatePicker{SelectedDate=DateTime.TryParse(existing==null?"":existing.End,out parsed)?parsed:start.SelectedDate};
   startTime=U.Input(existing==null?(initialTime.HasValue?initialTime.Value.ToString(@"hh\:mm"):""):existing.Time);
   endTime=U.Input(existing==null?(initialTime.HasValue?TimeSpan.FromMinutes(Math.Min(1439,initialTime.Value.TotalMinutes+30)).ToString(@"hh\:mm"):""):existing.Occurrence.S("end_time"));if(startTime.Text.Length>5)startTime.Text=startTime.Text.Substring(0,5);if(endTime.Text.Length>5)endTime.Text=endTime.Text.Substring(0,5);
   startTime.ToolTip="HH:mm，可留空";endTime.ToolTip="HH:mm，可留空";
   var dates=U.Grid("*,12,*");U.Place(dates,U.Field("开始日期",start));U.Place(dates,U.Field("结束日期 / 循环截止",end),2);panel.Children.Add(dates);
   var times=U.Grid("*,12,*");U.Place(times,U.Field("开始时间（HH:mm）",startTime));U.Place(times,U.Field("结束时间（HH:mm）",endTime),2);panel.Children.Add(times);
   dated.Click+=delegate{dates.IsEnabled=times.IsEnabled=dated.IsChecked==true;};dates.IsEnabled=times.IsEnabled=dated.IsChecked==true;
   repeat=new ComboBox();foreach(var s in new[]{"不重复","每天","工作日","休息日","每周指定日期","每月指定日期","每年指定日期","艾宾浩斯记忆","每周周期","每月周期","每年周期"})repeat.Items.Add(s);repeat.SelectedIndex=0;
   if(existing!=null){repeat.Items.Clear();repeat.Items.Add(existing.Repeating?"已有循环 · 请在详情中调整规则":"不重复");repeat.SelectedIndex=0;repeat.IsEnabled=false;if(existing.Repeating){dated.IsEnabled=false;dates.IsEnabled=false;times.IsEnabled=false;}}
   panel.Children.Add(U.Field("重复规则",repeat));repeatFields=U.Stack();weekdays=new CheckBox[7];var weeks=U.Stack(true);for(int i=0;i<7;i++){int day=i;weekdays[i]=new CheckBox{Content=new[]{"一","二","三","四","五","六","日"}[i],Margin=new Thickness(0,0,10,0),IsChecked=i==((int)DateTime.Today.DayOfWeek+6)%7};weeks.Children.Add(weekdays[i]);}
   repeatFields.Children.Add(weeks);ruleDates=U.Input("1,15");repeatFields.Children.Add(U.Field("日期（逗号分隔；每月 32 为月底；每年如 01-01）",ruleDates));habit=new CheckBox{Content="作为习惯打卡",IsChecked=t.B("is_habit"),Margin=new Thickness(0,8,0,10)};repeatFields.Children.Add(habit);
   makeup=new CheckBox{Content="使用中国节假日调休（2024–2026）",IsEnabled=!repo.Demo,Margin=new Thickness(0,4,0,15)};repeatFields.Children.Add(makeup);panel.Children.Add(repeatFields);repeatFields.Visibility=Visibility.Collapsed;
   repeat.SelectionChanged+=delegate{repeatFields.Visibility=repeat.SelectedIndex>0?Visibility.Visible:Visibility.Collapsed;weeks.Visibility=repeat.SelectedIndex==4?Visibility.Visible:Visibility.Collapsed;((FrameworkElement)ruleDates.Parent).Visibility=repeat.SelectedIndex==5||repeat.SelectedIndex==6?Visibility.Visible:Visibility.Collapsed;if(repeat.SelectedIndex>0){dated.IsChecked=true;dates.IsEnabled=true;if(end.SelectedDate<=start.SelectedDate)end.SelectedDate=start.SelectedDate.Value.AddMonths(1);}};
   subtasks=U.Input(string.Join("\n",t.Strings("subtasks")),true);subtasks.ToolTip="每行一项，最多 20 项；已有进度时请保留原有顺序";panel.Children.Add(U.Field("子任务（每行一项）",subtasks));target=U.Input(t.I("target_count",1).ToString());panel.Children.Add(U.Field("次数目标（没有子任务时生效，1–99）",target));
   originalSubtaskText=subtasks.Text;
   target.IsEnabled=t.Strings("subtasks").Count==0;subtasks.TextChanged+=delegate{target.IsEnabled=string.IsNullOrWhiteSpace(subtasks.Text);};
   var reminder=t.D("time_reminder");remind=new CheckBox{Content="时间提醒",IsChecked=reminder!=null};panel.Children.Add(remind);U.Gap(panel,8);reminderDate=new DatePicker{SelectedDate=reminder!=null&&DateTime.TryParse(reminder.S("date"),out parsed)?parsed:start.SelectedDate};reminderTime=U.Input(reminder==null?"09:00":reminder.S("time"));if(reminderTime.Text.Length>5)reminderTime.Text=reminderTime.Text.Substring(0,5);
   var rr=U.Grid("*,12,*");U.Place(rr,reminderDate);U.Place(rr,reminderTime,2);rr.IsEnabled=remind.IsChecked==true;remind.Click+=delegate{rr.IsEnabled=remind.IsChecked==true;};panel.Children.Add(rr);U.Gap(panel,16);
   location=U.Input(t.D("location").S("name"));panel.Children.Add(U.Field("地点",location));
   error=U.Text("",12,"#B47D5D");error.TextWrapping=TextWrapping.Wrap;panel.Children.Add(error);
   var footer=U.Grid("*,Auto,Auto");U.Place(footer,U.Text(repo.Demo?"仅保存到本机体验数据":"先保存到本机，空闲时同步",11,"#897859"));U.Place(footer,U.Button("取消",()=>Close()),1);save=U.Button("保存待办",()=>Save(),"Primary");save.Margin=new Thickness(10,0,0,0);save.IsEnabled=repo.CanWrite;U.Place(footer,save,2);U.DialogContent(this,Title,panel,footer);
   Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(saving)e.Cancel=true;};Loaded+=delegate{name.Focus();};
  }
  FrameworkElement RemarkField(){
   AutomationProperties.SetName(remark,"备注原文");
   var field=U.Stack();
   field.Margin=new Thickness(0,0,0,15);
   var header=U.Grid("*,Auto");
   header.Margin=new Thickness(0,0,0,7);
   var label=U.Text("备注（Markdown）",12,"#79684F");
   label.VerticalAlignment=VerticalAlignment.Center;
   U.Place(header,label);

   var toggle=U.Button("预览",null,"Secondary");
   toggle.MinHeight=26;
   toggle.Padding=new Thickness(10,2,10,2);
   AutomationProperties.SetName(toggle,"预览备注");
   U.Place(header,toggle,1);
   field.Children.Add(header);
   field.Children.Add(remark);

   var preview=U.Card(null,"#FFFDF8",10,new Thickness(12,9,12,9));
   preview.MinHeight=remark.MinHeight;
   preview.Visibility=Visibility.Collapsed;
   AutomationProperties.SetName(preview,"备注预览");
   field.Children.Add(preview);
   toggle.Click+=delegate{
    bool showPreview=preview.Visibility!=Visibility.Visible;
    if(showPreview){
     preview.Child=string.IsNullOrWhiteSpace(remark.Text)?U.Text("暂无备注",13,"#79684F"):TaskRemarkMarkdown.Render(remark.Text);
    }else preview.Child=null;
    preview.Visibility=showPreview?Visibility.Visible:Visibility.Collapsed;
    remark.Visibility=showPreview?Visibility.Collapsed:Visibility.Visible;
    toggle.Content=showPreview?"编辑原文":"预览";
    AutomationProperties.SetName(toggle,showPreview?"编辑备注原文":"预览备注");
    if(!showPreview)remark.Focus();
   };
   return field;
  }
  public Dictionary<string,object> BuildCreate(){
   if(string.IsNullOrWhiteSpace(name.Text))throw new McpException("请填写待办名称。");
   var a=J.Obj("title",name.Text.Trim(),"remark",remark.Text,"list_id",listItems[list.SelectedIndex].S("list_id"),"important",important.IsChecked==true,"urgent",urgent.IsChecked==true,"operation_id",operationId);
   // Collection is an update-only field in the supplied MCP schema.
   if(dated.IsChecked==true){
    if(!start.SelectedDate.HasValue||!end.SelectedDate.HasValue)throw new McpException("请选择完整日期。");if(end.SelectedDate<start.SelectedDate)throw new McpException("结束日期不能早于开始日期。");
    a["start_date"]=J.Date(start.SelectedDate.Value);a["end_date"]=J.Date(end.SelectedDate.Value);
    ValidateTime(startTime.Text);ValidateTime(endTime.Text);
    if(startTime.Text.Trim()!="")a["start_time"]=startTime.Text.Trim();if(endTime.Text.Trim()!="")a["end_time"]=endTime.Text.Trim();
    if(start.SelectedDate==end.SelectedDate&&startTime.Text.Trim()!=""&&endTime.Text.Trim()!=""&&string.Compare(startTime.Text.Trim(),endTime.Text.Trim(),StringComparison.Ordinal)>0)throw new McpException("结束时间不能早于开始时间。");
   }
   if(repeat.SelectedIndex>0){
    if(dated.IsChecked!=true)throw new McpException("循环任务需要安排日期和截止日期。");Dictionary<string,object> rule;
    string[] modes={"","daily","workdays","days_off","custom","custom","custom","ebbinghaus","weekly_period","monthly_period","yearly_period"};rule=J.Obj("mode",modes[repeat.SelectedIndex]);
    if(repeat.SelectedIndex==4){var days=Enumerable.Range(0,7).Where(i=>weekdays[i].IsChecked==true).Select(i=>((i+1)%7).ToString()).ToArray();if(days.Length==0)throw new McpException("请至少选择一个星期。");rule["unit"]="week";rule["dates"]=days;}
    if(repeat.SelectedIndex==5||repeat.SelectedIndex==6){rule["unit"]=repeat.SelectedIndex==5?"month":"year";rule["dates"]=ruleDates.Text.Split(new[]{',','，'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).ToArray();}
    a["repeat_rule"]=rule;a["habit"]=habit.IsChecked==true;a["week_start"]=1;a["china_makeup"]=makeup.IsChecked==true;
   }
   string[] subs;
   if(item!=null&&subtasks.Text==originalSubtaskText){
    // Each stored title owns a progress bit, including titles containing a line break.
    subs=item.Task.Strings("subtasks").ToArray();
   }else{
    subs=subtasks.Text.Split(new[]{"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).Where(s=>s!="").ToArray();
    if(subs.Length>20)throw new McpException("子任务最多 20 项。");
   }
   a["subtasks"]=subs;
   if(subs.Length==0){int n;if(!int.TryParse(target.Text,out n)||n<1||n>99)throw new McpException("次数目标请填 1–99。");a["target_count"]=n;}
   if(remind.IsChecked==true){if(dated.IsChecked!=true)throw new McpException("请先安排日期，再开启提醒。");if(!reminderDate.SelectedDate.HasValue)throw new McpException("请选择提醒日期。");ValidateTime(reminderTime.Text,true);a["reminder_date"]=J.Date(reminderDate.SelectedDate.Value);a["reminder_time"]=reminderTime.Text.Trim();}
   if(!string.IsNullOrWhiteSpace(location.Text))a["location"]=J.Obj("name",location.Text.Trim());return a;
  }
  public static void ValidateTime(string value,bool required=false){if(value.Trim()==""&&!required)return;if(!Regex.IsMatch(value.Trim(),@"^([01]\d|2[0-3]):[0-5]\d$"))throw new McpException("时间请使用 HH:mm 格式，例如 09:30。");}
  async void Save(){
   if(saving)return;saving=true;save.IsEnabled=false;save.Content="正在保存…";error.Text="";
   try{
    var a=BuildCreate();
    if(item==null){
     await repo.CallAsync("create_task",a);if(collected.IsChecked==true)await repo.CallAsync("update_task",J.Obj("task_id",operationId,"collected",true));
    }else{
     var changes=J.Obj("task_id",item.Id);foreach(var k in new[]{"title","remark","important","urgent"})if(J.Encode(a[k])!=J.Encode(item.Task.ContainsKey(k)?item.Task[k]:k=="important"||k=="urgent"?(object)false:""))changes[k]=a[k];if((collected.IsChecked==true)!=item.Task.B("collected"))changes["collected"]=collected.IsChecked==true;
     if(changes.Count>1)await repo.CallAsync("update_task",changes);
     if(a.S("list_id")!=item.ListId)await repo.CallAsync("move_task",J.Obj("task_id",item.Id,"list_id",a["list_id"]));
     if(!item.Repeating){
      if(dated.IsChecked!=true&&!item.Undated)await repo.CallAsync("clear_schedule",J.Obj("task_id",item.Id));
      else if(dated.IsChecked==true&&(a.S("start_date")!=item.Start||a.S("end_date")!=item.End||a.S("start_time")!=Short(item.Time)||a.S("end_time")!=Short(item.Occurrence.S("end_time")))){
       if(item.Completed||item.Abandoned)throw new McpException("已有进度的任务需要先恢复为待办，才能调整日期。");
       var schedule=J.Obj("task_id",item.Id,"start_date",a.S("start_date"),"end_date",a.S("end_date"),"start_time",a.S("start_time","00:00"),"end_time",a.S("end_time","23:59"));await repo.CallAsync("reschedule_task",schedule);
      }
     }
     if(J.Encode(a["subtasks"])!=J.Encode(item.Task.Strings("subtasks"))||a.Strings("subtasks").Count==0&&a.I("target_count",1)!=item.Task.I("target_count",1)){
      var goal=J.Obj("task_id",item.Id);if(a.Strings("subtasks").Count>0)goal["subtasks"]=a["subtasks"];else goal["target_count"]=a["target_count"];await repo.CallAsync("edit_task_goal",goal);
     }
     var oldReminder=item.Task.D("time_reminder");
     if(a.ContainsKey("reminder_date")&&(oldReminder==null||a.S("reminder_date")!=oldReminder.S("date")||a.S("reminder_time")!=Short(oldReminder.S("time"))))await repo.CallAsync("set_time_reminder",J.Obj("task_id",item.Id,"reminder_date",a["reminder_date"],"reminder_time",a["reminder_time"]));
     else if(!a.ContainsKey("reminder_date")&&oldReminder!=null&&dated.IsChecked==true)await repo.CallAsync("set_time_reminder",J.Obj("task_id",item.Id,"enabled",false));
     if(location.Text.Trim()!=item.Task.D("location").S("name"))await repo.CallAsync("set_location",string.IsNullOrWhiteSpace(location.Text)?J.Obj("task_id",item.Id,"clear",true):J.Obj("task_id",item.Id,"name",location.Text.Trim()));
    }
    saving=false;DialogResult=true;
   }catch(Exception ex){error.Text=U.Friendly(ex.Message)+(item==null?"\n可保留此窗口重试，便单会沿用同一创建编号。":"\n已成功的改动会保留，可重新打开详情查看。");}
   finally{saving=false;save.IsEnabled=repo.CanWrite;save.Content="保存待办";}
  }
  static string Short(string s){return s.Length>5?s.Substring(0,5):s;}
 }
 public static class ListEditor {
  public static void Show(Window owner,Repository repo,Dictionary<string,object> list){
   var w=U.Dialog(owner,list==null?"新建清单":"编辑清单",440,380);var panel=U.Stack();panel.Children.Add(U.Text(list==null?"给事情分个类":"编辑清单",23,"#4A3316",true));U.Gap(panel,22);
   string initialName=list.S("name");
   var name=U.Input(initialName);AutomationProperties.SetName(name,"清单名称");panel.Children.Add(U.Field("清单名称",name));
   int initialColor=list==null?4:ListPalette.IndexOrDefault(list.I("color"));
   var colors=ListPalette.Picker(initialColor);panel.Children.Add(U.Field("标记颜色",colors));
   string operationId=J.NewId();bool busy=false;
   var save=U.Button("保存清单",null,"Primary");
   save.Click+=async delegate{
    if(busy)return;
    if(string.IsNullOrWhiteSpace(name.Text)){U.Error(w,new McpException("请填写清单名称。"));return;}
    busy=true;save.IsEnabled=false;
    try{
     var a=J.Obj();
     if(list==null||name.Text!=initialName)a["name"]=name.Text.Trim();
     if(list==null||colors.SelectedIndex!=initialColor)a["color"]=ListPalette.IndexOrDefault(colors.SelectedIndex);
     if(a.Count>0){
      a[list==null?"operation_id":"list_id"]=list==null?operationId:list.S("list_id");
      await repo.CallAsync(list==null?"create_list":"update_list",a);
     }
    }catch(Exception e){
     U.Error(w,e);
     return;
    }finally{busy=false;save.IsEnabled=true;}
    w.DialogResult=true;
   };
   w.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(busy)e.Cancel=true;};var footer=U.Stack(true);footer.HorizontalAlignment=HorizontalAlignment.Right;footer.Children.Add(U.Button("取消",()=>w.Close()));footer.Children.Add(save);U.DialogContent(w,"我的清单",panel,footer);w.ShowDialog();
  }
 }
}
