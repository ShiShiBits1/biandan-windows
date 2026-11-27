using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace Biandan {
 public sealed partial class MainView{
  int focusLimit=30;

  void RenderFocus(){
   var p=U.Stack();var hero=U.Stack();hero.HorizontalAlignment=HorizontalAlignment.Center;hero.Margin=new Thickness(0,22,0,18);
   hero.Children.Add(U.Text("把这一刻，还给自己",21,"#6E5638",true));U.Gap(hero,17);focusClock=U.Text("25:00",72,"#8D560B",true);focusClock.HorizontalAlignment=HorizontalAlignment.Center;hero.Children.Add(focusClock);U.Gap(hero,13);
   if(app.FocusStarted.HasValue){var caption=U.Text(app.FocusTitle,13,"#7B6548");caption.HorizontalAlignment=HorizontalAlignment.Center;hero.Children.Add(caption);U.Gap(hero,20);var actions=U.Stack(true);actions.HorizontalAlignment=HorizontalAlignment.Center;
    var save=U.Button(app.SavingFocus?"正在保存…":"保存本次专注",async()=>await app.RunAction(app.SaveFocusAsync),"Primary");
    var discard=U.Button("放弃本次",()=>app.ConfirmDiscardFocus(this));
    save.IsEnabled=discard.IsEnabled=!app.SavingFocus;
    discard.Margin=new Thickness(10,0,0,0);
    actions.Children.Add(save);actions.Children.Add(discard);hero.Children.Add(actions);
   }else{var fields=U.Grid("230,16,90");var name=U.Input("专注当下");name.ToolTip="本次专注名称";var duration=new ComboBox();foreach(var n in FocusDuration.Presets)duration.Items.Add(n+" 分钟");duration.SelectedIndex=1;U.Place(fields,name);U.Place(fields,duration,2);hero.Children.Add(fields);U.Gap(hero,18);var start=U.Button("开始一段专注",()=>{app.StartFocus(FocusDuration.Presets[duration.SelectedIndex],string.IsNullOrWhiteSpace(name.Text)?"专注当下":name.Text.Trim());},"Primary");start.HorizontalAlignment=HorizontalAlignment.Center;hero.Children.Add(start);}
   p.Children.Add(U.Card(hero,"#FEEDD0",20,new Thickness(25)));TickFocus();U.Gap(p,24);
   var stats=U.Grid("*,*,*");var today=FocusDuration.Today(repo.Focus).ToList();U.Place(stats,Stat("今日专注",FocusDuration.Format(today.Sum(record=>FocusDuration.Minutes(record)))+" 分钟","#8D560B"));U.Place(stats,Stat("完成番茄",FocusDuration.Tomatoes(today).ToString(),"#C2A47C"),1);U.Place(stats,Stat("累计记录",repo.Focus.Count.ToString(),"#7B6548"),2);p.Children.Add(stats);U.Gap(p,23);
   var heading=U.Grid("*,Auto");U.Place(heading,U.Text("专注记录",15,"#6E5638",true));U.Place(heading,U.Button("＋ 补记专注",()=>FocusEditor.Show(this,repo,null),"Secondary"),1);p.Children.Add(heading);U.Gap(p,13);
   foreach(var f in repo.Focus.OrderByDescending(x=>x.S("started_at")).Take(focusLimit)){
    var focus=f;var row=U.Grid("*,90,Auto,Auto");var text=U.Stack();text.Children.Add(U.Text(f.S("title","专注时刻"),13,"#6E5638",true));U.Gap(text,6);text.Children.Add(U.Text(f.S("date")+"  "+(f.S("started_at").Length>=16?f.S("started_at").Substring(11,5):""),11,"#897859"));U.Place(row,text);U.Place(row,U.Text(FocusDuration.Format(FocusDuration.Minutes(f))+" 分钟",14,"#6E5638",true),1);
    U.Place(row,U.IconButton("\uE70F","编辑记录",()=>FocusEditor.Show(this,repo,focus)),2);U.Place(row,U.IconButton("\uE74D","删除记录",async()=>{if(U.Confirm(this,"删除专注记录","将永久删除这条专注记录。","删除"))await app.RunAction(async()=>await repo.CallAsync("delete_focus",J.Obj("focus_id",focus.S("focus_id"))));}),3);var card=U.Card(row,"#FFFAF0",12,new Thickness(18,14,12,14));card.Margin=new Thickness(0,0,0,8);p.Children.Add(card);
   }
   if(repo.Focus.Count==0){var t=U.Text("从一次小小的专注开始。",13,"#897859");t.Margin=new Thickness(0,20,0,0);p.Children.Add(t);}
   if(repo.Focus.Count>focusLimit)p.Children.Add(U.Button("显示更多记录",()=>{focusLimit+=30;RenderFocus();},"Secondary"));
   body.Content=new ScrollViewer{Content=p};
  }
  public void TickFocus(){
   if(focusClock==null)return;
   if(app.FocusEnd.HasValue)focusClock.Text=App.FormatFocusClock(app.FocusSecondsLeft);
   else focusClock.Text=app.FocusStarted.HasValue?"完成了":App.FormatFocusClock(app.FocusMinutes*60);
  }
  internal void RefreshWidgetsPage(){
   if(page=="widgets")RenderWidgets();
  }

  void RenderWidgets(){
   var p=U.Stack();var intro=U.Stack();intro.Children.Add(U.Text("把重要的事，留在桌面。",23,"#513D24",true));U.Gap(intro,10);intro.Children.Add(U.Text("独立窗口，轻巧常驻。拖到顺手的位置，按你的节奏安排。",12,"#7B6548"));p.Children.Add(U.Card(intro,"#FFF1D2",18,new Thickness(25)));U.Gap(p,24);
   var grid=U.Grid("*,20,*,20,*");var cal=U.Stack();var mini=new MonthCalendar(repo,DateTime.Today,DateTime.Today,null);cal.Children.Add(U.Card(mini,"#FFFAF0",18,new Thickness(23)));U.Gap(cal,18);cal.Children.Add(U.Text("桌面日历",18,"#6E5638",true));U.Gap(cal,9);var cd=U.Text("月历与每日安排一眼可见，点击日期查看待办。",12,"#7B6548");cd.TextWrapping=TextWrapping.Wrap;cal.Children.Add(cd);U.Gap(cal,18);cal.Children.Add(U.Button(app.Widgets.ContainsKey("calendar")?"关闭日历小组件":"添加到桌面",()=>{app.ToggleWidget("calendar");},"Primary"));U.Place(grid,cal);
   var todo=U.Stack();var preview=U.Stack();preview.Children.Add(U.Text("今天，也要从容一点",19,"#513D24",true));U.Gap(preview,10);preview.Children.Add(U.Text(DateTime.Today.ToString("M 月 d 日")+"  ·  我的待办",11,"#7B6548"));U.Gap(preview,20);
   foreach(var task in repo.Items().Where(t=>t.OnDate(J.Today)).Take(4)){var row=U.Grid("28,*");var cb=new CheckBox{IsChecked=task.Completed,IsHitTestVisible=false};U.Place(row,cb);var tt=U.Text(task.Title,12,"#6E5638");U.Place(row,tt,1);row.Margin=new Thickness(0,9,0,9);preview.Children.Add(row);}
   if(repo.Items().Count==0)preview.Children.Add(U.Text("待办会显示在这里",13,"#7B6548"));var previewCard=U.Card(preview,"#FFFAF0",18,new Thickness(23));previewCard.MinHeight=325;todo.Children.Add(previewCard);U.Gap(todo,18);todo.Children.Add(U.Text("桌面待办",18,"#6E5638",true));U.Gap(todo,9);var td=U.Text("直接勾选、快速新增，完成的小事都算数。",12,"#7B6548");td.TextWrapping=TextWrapping.Wrap;todo.Children.Add(td);U.Gap(todo,18);todo.Children.Add(U.Button(app.Widgets.ContainsKey("todo")?"关闭待办小组件":"添加到桌面",()=>{app.ToggleWidget("todo");},"Primary"));U.Place(grid,todo,2);
   var focus=U.Stack();
   var focusPreview=U.Grid("*","*,Auto,Auto,*");
   var clock=U.Text("25:00",42,"#8D560B",true);
   clock.HorizontalAlignment=HorizontalAlignment.Center;
   U.Place(focusPreview,clock,0,1);
   var startPreview=U.Pill("开始专注");
   startPreview.HorizontalAlignment=HorizontalAlignment.Center;
   startPreview.Margin=new Thickness(0,10,0,0);
   U.Place(focusPreview,startPreview,0,2);
   var focusCard=U.Card(focusPreview,"#FFFAF0",18,new Thickness(23));
   focusCard.MinHeight=325;
   focus.Children.Add(focusCard);
   U.Gap(focus,18);
   focus.Children.Add(U.Text("桌面番茄钟",18,"#6E5638",true));
   U.Gap(focus,9);
   var description=U.Text("在桌面开始倒计时，结束后直接保存专注记录。",12,"#7B6548");
   description.TextWrapping=TextWrapping.Wrap;
   focus.Children.Add(description);
   U.Gap(focus,18);
   bool focusOpen=app.Widgets.ContainsKey("focus");
   var toggleFocus=U.Button(focusOpen?"关闭番茄钟小组件":"添加到桌面",()=>app.ToggleWidget("focus"),"Primary");
   System.Windows.Automation.AutomationProperties.SetName(toggleFocus,focusOpen?"关闭番茄钟小组件":"添加番茄钟小组件");
   focus.Children.Add(toggleFocus);
   U.Place(grid,focus,4);
   p.Children.Add(grid);
   U.Gap(p,26);var hint=U.Text("小组件右上角可切换置顶，拖动顶部移动位置。关闭主窗口后，小组件仍可继续使用。",12,"#7B6548");hint.TextWrapping=TextWrapping.Wrap;p.Children.Add(hint);body.Content=new ScrollViewer{Content=p};
  }
  void RenderSettings(){
   var p=U.Stack();p.Children.Add(U.Text("按自己的习惯，安排便单。",21,"#6E5638",true));U.Gap(p,23);
   var account=U.Grid("*,Auto");var info=U.Stack();info.Children.Add(U.Text(repo.Demo?"正在体验便单":repo.Connected?"便单账号已连接":"便单账号待连接",17,"#513D24",true));U.Gap(info,8);info.Children.Add(U.Text(repo.Demo?"体验数据保存在本机，登录后可使用云端清单。":"使用华为账号授权，令牌由 Windows 当前用户加密保护。",11,"#7B6548"));U.Place(account,info);
   U.Place(account,U.Button(repo.Demo?"登录账号":"退出账号",()=>LeaveAccount(),"Secondary"),1);p.Children.Add(U.Card(account,"#FFFAF0",16,new Thickness(22)));U.Gap(p,20);
   p.Children.Add(BuildThemeSettings());U.Gap(p,20);
 var settings=U.Stack();settings.Children.Add(U.Text("桌面与通知",15,"#6E5638",true));U.Gap(settings,17);
   settings.Children.Add(SettingToggle("关闭主窗口后留在系统托盘","桌面小组件和番茄钟计时继续运行。","minimizeToTray",true));settings.Children.Add(new Separator());
   settings.Children.Add(SettingToggle("桌面提醒","在任务提醒时间和专注结束时显示通知。程序需保持运行。","notifications",true));settings.Children.Add(new Separator());
   var auto=new CheckBox{Content="登录 Windows 后自动启动",IsChecked=IsAutostart(),Margin=new Thickness(0,12,0,12)};auto.Click+=delegate{try{using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(auto.IsChecked==true)key.SetValue("BiandanDesktop","\""+System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName+"\" --autostart");else key.DeleteValue("BiandanDesktop",false);}}catch(Exception e){auto.IsChecked=IsAutostart();U.Error(this,e);}};settings.Children.Add(auto);
   U.Gap(settings,11);
 var transparency=new Slider{Minimum=0,Maximum=100,Value=app.Preferences.WidgetTransparency,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,8,0,4)};
 System.Windows.Automation.AutomationProperties.SetName(transparency,"小组件背景透明度");
 var appearance=U.Grid("*,Auto");
 U.Place(appearance,U.Text("小组件背景透明度",13,"#513D24",true));
 var percentage=U.Text(app.Preferences.WidgetTransparency+"%",12,"#8D560B",true);U.Place(appearance,percentage,1);
 settings.Children.Add(appearance);settings.Children.Add(transparency);
 var scale=U.Grid("*,Auto");
 U.Place(scale,U.Text("实色",11,"#7B6548"));U.Place(scale,U.Text("透明",11,"#7B6548"),1);
 settings.Children.Add(scale);
 transparency.ValueChanged+=delegate{
  var percent=(int)transparency.Value;
  percentage.Text=percent+"%";app.SetWidgetTransparency(percent);
 };
   p.Children.Add(U.Card(settings,"#FFFAF0",16,new Thickness(22)));U.Gap(p,20);
   var sync=U.Stack();sync.Children.Add(U.Text("数据与同步",15,"#6E5638",true));U.Gap(sync,12);
 var intervalRow=U.Grid("*,142");
 U.Place(intervalRow,U.Text("自动同步间隔",13,"#513D24",true));
 var interval=new ComboBox();
 foreach(int minutes in Settings.SyncIntervals)interval.Items.Add("每 "+minutes+" 分钟");
 System.Windows.Automation.AutomationProperties.SetName(interval,"自动同步间隔");
 interval.SelectedIndex=Array.IndexOf(Settings.SyncIntervals,app.Preferences.SyncIntervalMinutes);
 interval.SelectionChanged+=delegate{
  if(interval.SelectedIndex>=0)app.SetSyncInterval(Settings.SyncIntervals[interval.SelectedIndex]);
 };
 U.Place(intervalRow,interval,1);
 sync.Children.Add(intervalRow);
 U.Gap(sync,10);
 var description=U.Text("新增、拖动和删除即时保存在本机，停止操作 15 秒后集中同步。其他设备的改动按以上间隔拉取，按 F5 可立即提交。",12,"#7B6548");
 description.TextWrapping=TextWrapping.Wrap;description.LineHeight=24;sync.Children.Add(description);
 U.Gap(sync,12);
 var controls=U.Stack(true);controls.Children.Add(U.Button("立即同步",()=>Refresh(),"Secondary"));
 var details=U.Button("同步详情",()=>SyncDetails.Show(this,repo),"Secondary");
 details.Margin=new Thickness(10,0,0,0);controls.Children.Add(details);
 sync.Children.Add(controls);U.Gap(sync,10);sync.Children.Add(BackupControls());
 p.Children.Add(U.Card(sync,"#FFFAF0",16,new Thickness(22)));U.Gap(p,23);
   var about=U.Text("便单 Windows 桌面版 "+GetType().Assembly.GetName().Version.ToString(3)+"   ·   内置便单 MCP "+BundledBackend.Version+"\n快捷键：Ctrl + N 新建待办    Ctrl + K 全局搜索    F5 同步\n本客户端为独立开发版本，账号授权及云端能力由原便单服务提供。",11,"#897859");about.LineHeight=25;p.Children.Add(about);body.Content=new ScrollViewer{Content=p};
  }
  FrameworkElement SettingToggle(string label,string description,string key,bool fallback){
   var row=U.Stack();var box=new CheckBox{Content=label,IsChecked=app.Preferences.GetBool(key,fallback)};box.Click+=delegate{app.Preferences.Set(key,box.IsChecked==true);};row.Children.Add(box);var help=U.Text(description,11,"#897859");help.TextWrapping=TextWrapping.Wrap;help.Margin=new Thickness(28,8,0,5);row.Children.Add(help);return row;
  }
  bool IsAutostart(){using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return key!=null&&key.GetValue("BiandanDesktop")!=null;}

 }
 public static class FocusEditor{
  public static void Show(Window owner,Repository repo,System.Collections.Generic.Dictionary<string,object> existing){
   var w=U.Dialog(owner,existing==null?"补记专注":"编辑专注记录",460,620);var p=U.Stack();p.Children.Add(U.Text(existing==null?"把认真付出的时间记下来":"编辑这次专注",22,"#513D24",true));U.Gap(p,22);
   var name=U.Input(existing==null?"":existing.S("title"));p.Children.Add(U.Field("专注内容",name));var date=new DatePicker{SelectedDate=DateTime.Today};var clock=U.Input(DateTime.Now.AddMinutes(-25).ToString("HH:mm"));var minutes=U.Input("25");
   if(existing==null){p.Children.Add(U.Field("开始日期",date));p.Children.Add(U.Field("开始时间（HH:mm）",clock));p.Children.Add(U.Field("专注分钟（1–1440）",minutes));}
   var good=U.Input(existing==null?"1":existing.I("good_count").ToString());var bad=U.Input(existing==null?"0":existing.I("bad_count").ToString());p.Children.Add(U.Field("完成番茄数",good));p.Children.Add(U.Field("中断番茄数",bad));var error=U.Text("",12,"#B17D5B");error.TextWrapping=TextWrapping.Wrap;p.Children.Add(error);bool busy=false;string startedAt=null;
   var save=U.Button("保存记录",null,"Primary");save.IsEnabled=repo.CanWrite;save.Click+=async delegate{if(busy)return;busy=true;save.IsEnabled=false;try{int g,b;if(!int.TryParse(good.Text,out g)||g<0||!int.TryParse(bad.Text,out b)||b<0)throw new McpException("番茄数请填写非负整数。");var a=J.Obj("title",name.Text.Trim(),"good_count",g,"bad_count",b);if(existing==null){int n;if(!int.TryParse(minutes.Text,out n)||n<1||n>1440)throw new McpException("分钟数请填 1–1440。");TaskEditor.ValidateTime(clock.Text,true);if(!date.SelectedDate.HasValue)throw new McpException("请选择日期。");if(startedAt==null)startedAt=J.Date(date.SelectedDate.Value)+"T"+clock.Text.Trim()+":"+DateTime.Now.Second.ToString("00");a["started_at"]=startedAt;a["minutes"]=n;}else a["focus_id"]=existing.S("focus_id");await repo.CallAsync(existing==null?"create_focus":"update_focus",a);busy=false;w.DialogResult=true;}catch(Exception e){error.Text=U.Friendly(e.Message);}finally{busy=false;save.IsEnabled=repo.CanWrite;}};
   w.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(busy)e.Cancel=true;};U.DialogContent(w,"专注记录",p,save);w.ShowDialog();
  }
 }
}
