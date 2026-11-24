using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Biandan {
 internal static class WindowLighting {
  static Brush navigationFill;

  internal static Brush NavigationFill{
   get{
    if(navigationFill==null){
     var brush=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1)};
     brush.GradientStops.Add(Theme.ColorStop("#FFE5AC",0));
     brush.GradientStops.Add(Theme.ColorStop("#FFF1D5",1));
     navigationFill=brush;
    }
    return navigationFill;
   }
  }

  static RadialGradientBrush Wash(Point origin,double width,double height,string light,string middle,string paper){
   var brush=new RadialGradientBrush{Center=origin,GradientOrigin=origin,RadiusX=width,RadiusY=height};
   brush.GradientStops.Add(Theme.ColorStop(light,0));
   brush.GradientStops.Add(Theme.ColorStop(middle,.52));
   brush.GradientStops.Add(Theme.ColorStop(paper,1));
   return brush;
  }

  internal static void Apply(Border shell,Border sidebar,Grid content){
   var ambient=Wash(new Point(.68,-.17),.86,.6,"#FFE5AC","#FFF4E6","#FFFCF6");
   var sideLight=Wash(new Point(.12,-.05),1.75,.63,"#FFE7AF","#FFF4E6","#FFFCF5");

   // Only this narrow highlight follows the pointer. The large reading surface stays still.
   var rest=new Point(.17,.5);
   var edgeLight=new RadialGradientBrush{Center=rest,GradientOrigin=rest,RadiusX=.28,RadiusY=3};
   edgeLight.GradientStops.Add(Theme.ColorStop(Theme.DefaultHex,0));
   edgeLight.GradientStops.Add(Theme.ColorStop("#00FFE8B8",1));
   var edge=new Border{Height=2,Margin=new Thickness(14,0,14,0),VerticalAlignment=VerticalAlignment.Top,
    Background=edgeLight,Opacity=.64,IsHitTestVisible=false,Focusable=false};
   U.Place(content,edge,0,0,content.ColumnDefinitions.Count,content.RowDefinitions.Count);

   Action reset=delegate{if(edgeLight.Center!=rest)edgeLight.Center=edgeLight.GradientOrigin=rest;};
   Action apply=delegate{
    bool plain=SystemParameters.HighContrast;
    shell.Background=plain?U.C("#FFFCF6"):ambient;
    sidebar.Background=plain?U.C("#FFFCF5"):sideLight;
    edge.Visibility=plain?Visibility.Collapsed:Visibility.Visible;
    reset();
   };
   apply();
   // Subscribe only while the window is attached, so reopening it releases the old brushes.
   PropertyChangedEventHandler changed=delegate(object sender,PropertyChangedEventArgs args){
    if(args.PropertyName=="HighContrast"||args.PropertyName=="ClientAreaAnimation")apply();
   };
   content.Loaded+=delegate{SystemParameters.StaticPropertyChanged+=changed;apply();};
   content.Unloaded+=delegate{SystemParameters.StaticPropertyChanged-=changed;};
   content.MouseMove+=delegate(object sender,System.Windows.Input.MouseEventArgs args){
    if(edge.Visibility!=Visibility.Visible||!SystemParameters.ClientAreaAnimation||edge.ActualWidth<=0)return;
    var pointer=args.GetPosition(edge);
    if(pointer.Y>120){reset();return;}
    double x=Math.Max(0,Math.Min(1,pointer.X/edge.ActualWidth));
    if(Math.Abs(edgeLight.Center.X-x)>.008)edgeLight.Center=edgeLight.GradientOrigin=new Point(x,.5);
   };
   content.MouseLeave+=delegate{reset();};
  }
 }
}
