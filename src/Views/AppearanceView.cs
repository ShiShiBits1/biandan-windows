using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Biandan {
 public sealed partial class MainView {
  FrameworkElement BuildThemeSettings(){
   var panel=U.Stack();
   panel.Children.Add(U.Text("主题色",15,"#6E5638",true));
   U.Gap(panel,12);
   var options=new WrapPanel();
   var input=U.Input(Theme.Selected);
   input.MaxLength=7;input.MinHeight=38;
   AutomationProperties.SetName(input,"自定义主题色");
   var feedback=U.Text("",11,"#A94727");

   foreach(var preset in Theme.Presets){
    var key=preset.Item2;
    var tag=U.Stack(true);
    tag.Children.Add(new Ellipse{Width=13,Height=13,Fill=RawColor(key),VerticalAlignment=VerticalAlignment.Center});
    var label=U.Text(preset.Item1,12,"#513D24");
    label.Margin=new Thickness(7,0,0,0);tag.Children.Add(label);
    var button=U.Button("",()=>{
     app.SetThemeColor(key);
     RenderSettings();
    },"Secondary");
    button.Content=tag;button.ToolTip=preset.Item1;
    AutomationProperties.SetName(button,preset.Item1);
    if(Theme.Selected==key)button.BorderBrush=U.C("#CE8B24");
    button.Margin=new Thickness(0,0,8,7);options.Children.Add(button);
   }
   panel.Children.Add(options);
   var field=U.Grid("Auto,12,128,8,Auto,*");
   U.Place(field,U.Text("自定义",12,"#79684F"));
   U.Place(field,input,2);
   Action apply=()=>{
    string hex;
    if(!Theme.TryHex(input.Text,out hex)){
     feedback.Text="请输入 6 位色值，例如 #76B0E8。";return;
    }
    app.SetThemeColor(hex);
    RenderSettings();
   };
   input.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;apply();}};
   U.Place(field,U.Button("应用",apply,"Secondary"),4);
   panel.Children.Add(field);U.Gap(panel,5);panel.Children.Add(feedback);
   return U.Card(panel,"#FFFAF0",16,new Thickness(22));
  }

  static Brush RawColor(string hex){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));}
 }
}