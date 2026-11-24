using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Biandan {
 public sealed class LoginView:Window{
  readonly App app;readonly TextBlock status;readonly Button login,check,copy;bool waiting,busy,loggedIn;DateTime deadline;Uri address;readonly DispatcherTimer timer;
  public LoginView(App application){
   app=application;Title="登录便单";U.Configure(this,920,620);ResizeMode=ResizeMode.NoResize;
   var g=U.Grid("420,*","*");var left=U.Stack();left.Margin=new Thickness(44,52,40,30);
   var brand=U.Stack(true);brand.Children.Add(U.Logo(44));var name=U.Text("便单",25,"#2E2418",true);name.Margin=new Thickness(12,0,0,0);brand.Children.Add(name);left.Children.Add(brand);
   U.Gap(left,58);left.Children.Add(U.Text("把日子过得",34,"#513D24",true));U.Gap(left,5);left.Children.Add(U.Text("有条有理。",34,"#513D24",true));U.Gap(left,18);
   var intro=U.Text("待办、日历与每一刻专注。\n从桌面出发，让生活慢慢有序。",14,"#7B6548");intro.LineHeight=27;left.Children.Add(intro);U.Gap(left,40);
   var mini=U.Stack();var h=U.Stack(true);h.Children.Add(U.Icon("\uE787",17,"#8D560B"));var tx=U.Text("今天，也有小小的进步",13,"#8D560B",true);tx.Margin=new Thickness(9,0,0,0);h.Children.Add(tx);mini.Children.Add(h);U.Gap(mini,14);
   foreach(string s in new[]{"梳理今天的计划","留一点时间给自己","完成一件重要的小事"}){var row=U.Stack(true);row.Margin=new Thickness(0,5,0,5);row.Children.Add(new CheckBox{IsChecked=s.StartsWith("梳理"),IsHitTestVisible=false,VerticalAlignment=VerticalAlignment.Center});var t=U.Text(s,12,"#6E5638");t.Margin=new Thickness(9,0,0,0);row.Children.Add(t);mini.Children.Add(row);}
   left.Children.Add(U.Card(mini,"#FFF9EC",18,new Thickness(20)));
   var brandPanel=U.Card(left,"#FFE7AF",18,new Thickness(0));Motion.LightSurface(brandPanel,"#FFD477","#FFF1CC",new Point(0.13,0.22));U.Place(g,brandPanel);var right=U.Grid("*","42,*,Auto");U.Place(right,U.TitleBar(this,"",false));var form=U.Stack();form.Margin=new Thickness(42,36,42,0);
   form.Children.Add(U.Pill("WINDOWS 桌面版"));U.Gap(form,24);form.Children.Add(U.Text("欢迎回到便单",27,"#2E2418",true));U.Gap(form,10);form.Children.Add(U.Text("登录后，与你的便单云端保持同步",13,"#79684F"));U.Gap(form,34);
   var notes=U.Text("使用华为账号安全授权\n在系统浏览器中登录，授权后自动同步。",13,"#6E5638");notes.LineHeight=25;form.Children.Add(notes);U.Gap(form,24);
   login=U.Button("使用华为账号登录",()=>Begin(),"Primary");login.Height=46;form.Children.Add(login);U.Gap(form,10);
   var links=U.Grid("*,8,*");
   check=U.Button("我已授权，检查登录",()=>Check(),"Secondary");check.Visibility=Visibility.Collapsed;U.Place(links,check);
   copy=U.Button("复制授权链接",CopyAddress,"Secondary");copy.Visibility=Visibility.Collapsed;U.Place(links,copy,2);
   form.Children.Add(links);U.Gap(form,12);
   status=U.Text("账号授权信息仅保存在此设备。",12,"#7B6548");status.TextWrapping=TextWrapping.Wrap;status.LineHeight=22;form.Children.Add(status);U.Gap(form,24);
   var demo=U.Button("先体验一下  →",()=>{if(busy)return;waiting=false;timer.Stop();app.Repo.OpenDemo();app.ShowDesktop();},"Secondary");form.Children.Add(demo);
   U.Place(right,form,0,1);var foot=U.Text("简单记录 · 从容生活",11,"#897859");foot.HorizontalAlignment=HorizontalAlignment.Center;foot.Margin=new Thickness(0,0,0,30);U.Place(right,foot,0,2);U.Place(g,right,1);
   var surface=U.Card(g,"#FFFAF0",18,new Thickness(0));Motion.LightSurface(surface,"#FFF1D0","#FFFDF8",new Point(0.96,0.12));Content=surface;Motion.EnterWhenLoaded(surface);timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};timer.Tick+=delegate{if(waiting&&!busy)Check();};
   Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){
    if(busy&&!app.Quitting){e.Cancel=true;return;}
    timer.Stop();
    if(loggedIn||app.Quitting||app.Smoke)return;
    app.Repo.Client.Stop();
    if(app.Desktop==null){app.Quitting=true;app.Shutdown();}
    else if(app.Repo.Demo)Dispatcher.BeginInvoke(new Action(app.ShowDesktop));
   };
  }
  async void Begin(){
   if(busy)return;
   if(waiting&&address!=null&&DateTime.Now<=deadline){OpenAddress();return;}
   ResetAuthorization();
   busy=true;login.IsEnabled=false;
   waiting=true;deadline=DateTime.Now.AddMinutes(3);
   status.Text="正在准备华为授权页面…";
   try{
    app.Repo.Client.Stop();
    await app.Repo.Client.StartAsync();
    if(address==null)status.Text="正在获取授权链接…";
    timer.Start();
   }catch(Exception e){
    ResetAuthorization();status.Text=U.Friendly(e.Message);
   }finally{busy=false;login.IsEnabled=true;}
   if(waiting)Check();
  }

  internal void ReceiveAuthorization(Uri uri){
   if(Equals(address,uri))return;
   address=uri;loggedIn=false;waiting=true;deadline=DateTime.Now.AddMinutes(3);
   check.Visibility=copy.Visibility=Visibility.Visible;
   login.Content="重新打开授权页面";
   timer.Start();
   OpenAddress();
  }

  void OpenAddress(){
   if(address==null)return;
   try{
    using(var launched=Process.Start(new ProcessStartInfo(address.AbsoluteUri){UseShellExecute=true})){}
    status.Text="请在浏览器完成授权。如果页面未出现，可以复制链接手动打开。";
   }catch(Win32Exception){
    status.Text="Windows 未能打开浏览器。请复制授权链接，在浏览器地址栏打开。";
   }catch(InvalidOperationException){
    status.Text="浏览器启动失败。请复制授权链接手动打开。";
   }
  }

  void CopyAddress(){
   if(address==null||!waiting||DateTime.Now>deadline)return;
   try{
    Clipboard.SetText(address.AbsoluteUri);
    status.Text="授权链接已复制。请粘贴到浏览器地址栏打开。";
   }catch(ExternalException){
    status.Text="剪贴板暂不可用，请稍后重试。";
   }
  }

  void ResetAuthorization(){
   waiting=false;address=null;timer.Stop();
   check.Visibility=copy.Visibility=Visibility.Collapsed;
   login.Content="使用华为账号登录";
  }

  async void Check(){
   if(busy||!waiting)return;
   if(DateTime.Now>deadline){
    ResetAuthorization();status.Text="授权链接已过期，请重新发起授权。";return;
   }
   busy=true;check.IsEnabled=false;
   try{
    await app.Repo.LoginAsync();
    ResetAuthorization();loggedIn=true;app.ShowDesktop();Hide();
   }catch(Exception e){
    var message=e.Message;
    if(!app.Repo.Client.Running||message.IndexOf("OAuth callback",StringComparison.OrdinalIgnoreCase)>=0||
       message.IndexOf("Timed out waiting",StringComparison.OrdinalIgnoreCase)>=0||
       message.IndexOf("did not grant authorization",StringComparison.OrdinalIgnoreCase)>=0){
     ResetAuthorization();status.Text=U.Friendly(message);
    }else if(e is McpException&&((McpException)e).WaitingForAuthorization){
     if(address==null)status.Text="仍在等待授权链接。可以点击登录按钮重新发起。";
    }else status.Text=U.Friendly(message);
   }finally{busy=false;check.IsEnabled=true;}
  }
 }
}
