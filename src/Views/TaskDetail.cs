using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
namespace Biandan {
 public sealed class TaskDetail:Window{
  readonly Repository repo;TaskItem item;bool busy;
  public TaskDetail(Window owner,Repository repository,TaskItem existing){
   repo=repository;item=existing;Title="待办详情";Owner=owner;ShowInTaskbar=false;U.Configure(this,550,710);Height=Math.Min(710,SystemParameters.WorkArea.Height-35);ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;repo.Changed+=Changed;Closed+=delegate{repo.Changed-=Changed;};Render();
  }
  void Changed(){var current=repo.Items().FirstOrDefault(x=>x.OccurrenceId==item.OccurrenceId);if(current!=null)item=current;Render();}
  void Render(){
   var panel=U.Stack();var title=U.Text(item.Title,24,"#4A3316",true);title.TextWrapping=TextWrapping.Wrap;panel.Children.Add(title);U.Gap(panel,13);var tags=U.Stack(true);tags.Children.Add(U.Pill(repo.ListName(item.ListId)));if(item.Repeating){var tag=U.Pill(item.Task.B("is_habit")?"循环习惯":"循环待办","#9C987A","#FBF1DD");tag.Margin=new Thickness(8,0,0,0);tags.Children.Add(tag);}panel.Children.Add(tags);U.Gap(panel,23);
   panel.Children.Add(U.Text(item.Undated?"未安排日期":item.Start+(item.End!=item.Start?" → "+item.End:"")+(item.Time!=""?"  "+item.Time:""),13,"#7B6548"));U.Gap(panel,17);
   var remark=item.Task.S("remark");
   if(!string.IsNullOrWhiteSpace(remark)){
    var note=U.Card(TaskRemarkMarkdown.Render(remark),"#FFF3DB",12,new Thickness(15));
    AutomationProperties.SetName(note,"待办备注");
    panel.Children.Add(note);U.Gap(panel,20);
   }
   var subs=item.Task.Strings("subtasks");var completed=item.Occurrence.Strings("completed_subtask_indices");
   if(subs.Count>0){panel.Children.Add(U.Text("子任务 · "+item.Occurrence.I("completed_count")+"/"+subs.Count,13,"#6E5638",true));U.Gap(panel,12);for(int i=0;i<subs.Count;i++){int index=i;bool isDone=completed.Contains(i.ToString());var box=new CheckBox{Content=subs[i],IsChecked=isDone,Margin=new Thickness(0,8,0,8),IsEnabled=repo.CanWrite&&!busy&&!item.Abandoned};box.Click+=async delegate{await Act(async()=>await repo.CallAsync(isDone?"change_progress":"complete_subtask",isDone?J.Obj("occurrence_id",item.OccurrenceId,"action","uncheck_subtask","subtask_index",index):J.Obj("occurrence_id",item.OccurrenceId,"subtask_index",index)));};panel.Children.Add(box);}U.Gap(panel,15);}
   else if(item.Task.I("target_count",1)>1){panel.Children.Add(U.Text("本次进度  "+item.Occurrence.I("completed_count")+" / "+item.Task.I("target_count"),14,"#6E5638",true));U.Gap(panel,10);var row=U.Grid("*,Auto");var count=U.Input(item.Occurrence.I("completed_count").ToString());U.Place(row,count);U.Place(row,U.Button("记录次数",async()=>{int n;if(!int.TryParse(count.Text,out n)||n<0||n>item.Task.I("target_count")){U.Error(this,new McpException("请填写目标范围内的次数。"));return;}await Act(async()=>await repo.CallAsync("change_progress",J.Obj("occurrence_id",item.OccurrenceId,"action","set_count","count",n)));},"Secondary"),1);panel.Children.Add(row);U.Gap(panel,18);}
   var reminder=item.Task.D("time_reminder");if(reminder!=null){panel.Children.Add(U.Text("提醒  "+reminder.S("date")+" "+reminder.S("time"),12,"#7B6548"));U.Gap(panel,12);}
   if(item.Task.D("location")!=null){panel.Children.Add(U.Text("地点  "+item.Task.D("location").S("name"),12,"#7B6548"));U.Gap(panel,12);}
   var actions=U.Stack();actions.Children.Add(U.Button("编辑待办",()=>{new TaskEditor(this,repo,item).ShowDialog();Changed();},"Secondary"));U.Gap(actions,8);
   if(!item.Completed&&!item.Abandoned){actions.Children.Add(U.Button(item.Repeating?"调整本次日期":"调整日期",()=>Reschedule(),"Secondary"));U.Gap(actions,8);}
   if(item.Repeating){var adjust=U.Button("调整循环规则",()=>RecurrenceEditor.Show(this,repo,item),"Secondary");adjust.IsEnabled=repo.CanWrite&&!repo.Demo;actions.Children.Add(adjust);U.Gap(actions,8);}
   actions.Children.Add(U.Button("设置地点与到达提醒",()=>LocationDialog(),"Secondary"));U.Gap(panel,6);panel.Children.Add(actions);
   var footer=U.Grid("*,Auto");var hint=U.Text(item.Completed?"本次已经完成":item.Abandoned?"本次已经跳过":"一小步，也值得认真对待",11,"#897859");U.Place(footer,hint);
   var finish=U.Button(item.Completed?"撤销完成":item.Abandoned?"恢复本次":"完成本次",async()=>await Act(async()=>await repo.CallAsync(item.Completed||item.Abandoned?"change_progress":"complete_occurrence",item.Completed||item.Abandoned?J.Obj("occurrence_id",item.OccurrenceId,"action",item.Abandoned?"restore":"reopen"):J.Obj("occurrence_id",item.OccurrenceId))),"Primary");finish.IsEnabled=repo.CanWrite&&!busy;U.Place(footer,finish,1);U.DialogContent(this,"待办详情",panel,footer);
  }
  async Task Act(Func<Task> action){if(busy)return;busy=true;try{await action();}catch(Exception e){U.Error(this,e);}finally{busy=false;Changed();}}
  void Reschedule(){
   var w=U.Dialog(this,"调整本次日期",430,410);var p=U.Stack();DateTime d;var date=new DatePicker{SelectedDate=DateTime.TryParse(item.Start,out d)?d:DateTime.Today};var time=U.Input(item.Time.Length>5?item.Time.Substring(0,5):item.Time);p.Children.Add(U.Text("给这件事换个时间",21,"#4A3316",true));U.Gap(p,20);p.Children.Add(U.Field("新的开始日期",date));p.Children.Add(U.Field("开始时刻（留空保留原时刻）",time));var note=U.Text(item.Repeating?"调整后，本次将从循环中分离。原有持续时间保留。":"原有提醒时间不会自动改变，请按需修改提醒。",12,"#7B6548");note.TextWrapping=TextWrapping.Wrap;p.Children.Add(note);
   var save=U.Button("调整日期",null,"Primary");save.Click+=async delegate{save.IsEnabled=false;try{if(!date.SelectedDate.HasValue)throw new McpException("请选择日期。");TaskEditor.ValidateTime(time.Text);var a=J.Obj("occurrence_id",item.OccurrenceId,"start_date",J.Date(date.SelectedDate.Value));if(time.Text.Trim()!="")a["start_time"]=time.Text.Trim();DateTime first,last;if(!item.Repeating&&!item.Undated&&DateTime.TryParse(item.Start,out first)&&DateTime.TryParse(item.End,out last))a["end_date"]=J.Date(date.SelectedDate.Value.AddDays(Math.Max(0,(last-first).Days)));await repo.CallAsync("reschedule_occurrence",a);w.DialogResult=true;}catch(Exception e){U.Error(w,e);}finally{save.IsEnabled=true;}};U.DialogContent(w,"调整日期",p,save);w.ShowDialog();
  }
  void LocationDialog(){
   var w=U.Dialog(this,"地点提醒",460,530);var p=U.Stack();var loc=item.Task.D("location");var name=U.Input(loc.S("name"));var lat=U.Input(loc.S("latitude"));var lng=U.Input(loc.S("longitude"));var remind=new CheckBox{Content="到达时提醒",IsChecked=item.Task.B("has_location_reminder")};p.Children.Add(U.Text("给待办加一个地点",22,"#4A3316",true));U.Gap(p,18);p.Children.Add(U.Field("地点名称",name));p.Children.Add(U.Field("纬度（GCJ-02）",lat));p.Children.Add(U.Field("经度（GCJ-02）",lng));p.Children.Add(remind);U.Gap(p,15);var note=U.Text("到达提醒需要有效坐标，由已授予位置权限的鸿蒙客户端触发。",12,"#7B6548");note.TextWrapping=TextWrapping.Wrap;p.Children.Add(note);
   var buttons=U.Stack(true);buttons.HorizontalAlignment=HorizontalAlignment.Right;buttons.Children.Add(U.Button("清除地点",async()=>{try{await repo.CallAsync("set_location",J.Obj("task_id",item.Id,"clear",true));w.DialogResult=true;}catch(Exception e){U.Error(w,e);}}));var save=U.Button("保存地点",null,"Primary");save.Click+=async delegate{save.IsEnabled=false;try{var a=J.Obj("task_id",item.Id,"name",name.Text.Trim(),"remind",remind.IsChecked==true);if(lat.Text.Trim()!=""||lng.Text.Trim()!=""){double la,lo;if(!double.TryParse(lat.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out la)||!double.TryParse(lng.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out lo)||la< -90||la>90||lo< -180||lo>180)throw new McpException("请填写有效的经纬度。");a["latitude"]=la;a["longitude"]=lo;}await repo.CallAsync("set_location",a);w.DialogResult=true;}catch(Exception e){U.Error(w,e);}finally{save.IsEnabled=true;}};buttons.Children.Add(save);U.DialogContent(w,"地点提醒",p,buttons);w.ShowDialog();
  }
 }
 public static class RecurrenceEditor{
  public static void Show(Window owner,Repository repo,TaskItem item){
   var w=U.Dialog(owner,"调整循环规则",480,650);var p=U.Stack();p.Children.Add(U.Text("调整接下来的安排",22,"#4A3316",true));U.Gap(p,18);var existing=item.Task.D("recurrence");
   string[] modes={"daily","workdays","days_off","custom","weekly_period","monthly_period","yearly_period","ebbinghaus"};var mode=new ComboBox();foreach(var s in new[]{"每天","工作日","休息日","自定义","每周周期","每月周期","每年周期","艾宾浩斯记忆"})mode.Items.Add(s);mode.SelectedIndex=Math.Max(0,Array.IndexOf(modes,existing.S("mode","daily")));p.Children.Add(U.Field("循环方式",mode));
   var unit=new ComboBox();string[] units={"day","week","month","year","lunar_month","lunar_year"};foreach(var s in new[]{"天","周","月","年","农历月","农历年"})unit.Items.Add(s);unit.SelectedIndex=Math.Max(0,Array.IndexOf(units,existing.S("unit","day")));p.Children.Add(U.Field("自定义周期单位（仅自定义时生效）",unit));
   var interval=U.Input(existing.I("interval",1).ToString());p.Children.Add(U.Field("间隔（1–366）",interval));var dates=U.Input(string.Join(",",existing.Strings("dates")));dates.ToolTip="周：0=周日，1=周一；月：1–31，32=月底；年：MM-DD";p.Children.Add(U.Field("指定日期（天无需填写）",dates));
   DateTime endDate;var end=new DatePicker{SelectedDate=DateTime.TryParse(item.Task.S("end_date"),out endDate)?endDate:DateTime.Today.AddMonths(1)};p.Children.Add(U.Field("循环结束日期",end));var from=new DatePicker{SelectedDate=DateTime.Today};p.Children.Add(U.Field("从哪天开始应用新规则",from));var habit=new CheckBox{Content="作为习惯打卡",IsChecked=item.Task.B("is_habit")};p.Children.Add(habit);U.Gap(p,12);var note=U.Text("将保留已有完成记录、有进度及已跳过的实例。保存前会显示变更数量。",12,"#897859");note.TextWrapping=TextWrapping.Wrap;p.Children.Add(note);
   bool saving=false;var save=U.Button("预览变更并保存",null,"Primary");save.Click+=async delegate{if(saving)return;saving=true;save.IsEnabled=false;try{
    if(!end.SelectedDate.HasValue||!from.SelectedDate.HasValue)throw new McpException("请填写日期。");var rule=J.Obj("mode",modes[mode.SelectedIndex],"duration_days",existing.I("duration_days",1));if(mode.SelectedIndex==3){int n;if(!int.TryParse(interval.Text,out n)||n<1||n>366)throw new McpException("间隔请填 1–366。");rule["unit"]=units[unit.SelectedIndex];rule["interval"]=n;string filter=existing.S("day_filter","all");if(filter=="workdays"||filter=="days_off")rule["day_filter"]=filter;if(unit.SelectedIndex!=0)rule["dates"]=dates.Text.Split(new[]{',','，'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).ToArray();}
    var a=J.Obj("task_id",item.Id,"repeat_rule",rule,"from_date",J.Date(from.SelectedDate.Value),"end_date",J.Date(end.SelectedDate.Value),"habit",habit.IsChecked==true,"week_start",1,"preview",true);var preview=await repo.CallAsync("update_recurrence",a);
    if(U.Confirm(w,"确认循环变更","新增 "+preview.I("created")+" 次，调整 "+preview.I("rescheduled")+" 次，移除 "+preview.I("removed")+" 次，保留 "+preview.I("protected_dates")+" 次历史。","应用变更")){a.Remove("preview");await repo.CallAsync("update_recurrence",a);w.DialogResult=true;}
   }catch(Exception e){U.Error(w,e);}finally{saving=false;save.IsEnabled=true;}};w.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(saving)e.Cancel=true;};U.DialogContent(w,"循环规则",p,save);w.ShowDialog();
  }
 }
}
