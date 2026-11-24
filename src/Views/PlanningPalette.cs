using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Biandan {
 internal static class PlanningPalette {
  // Resolve these shades through Theme so they track the selected app color.
  public const string Ink="#2E2418",Muted="#79684F",Accent=Theme.DefaultHex,Emphasis="#8D560B",
   Panel="#FFFAF0",Card="#FFFDF8",Subtle="#FFF3DB",Line="#EFE1C8",Selection="#FFE5AC";

  public static void StyleScrollbars(FrameworkElement element){
   element.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=Application.Current.FindResource("PlanningScrollbar");
  }

  public static Button Button(string caption,Action action,bool outlined=false){
   var button=U.Button(caption,action,"PlanningButton");
   button.Foreground=U.C(Ink);
   button.Background=outlined?U.C(Panel):Brushes.Transparent;
   if(outlined)button.BorderBrush=U.C(Line);
   return button;
  }

  public static Button Icon(string glyph,string label,Action action){
   var button=Button("",action);
   button.Content=U.Icon(glyph,14,Ink);
   button.ToolTip=label;
   button.Width=31;
   button.Height=32;
   button.MinHeight=0;
   button.Padding=new Thickness(0);
   System.Windows.Automation.AutomationProperties.SetName(button,label);
   return button;
  }

 }
}
