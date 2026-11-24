using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Biandan {
 internal static class Motion {
  static bool Enabled{get{return SystemParameters.ClientAreaAnimation&&!SystemParameters.HighContrast;}}
  static ScaleTransform Transform(FrameworkElement element){
   var scale=element.RenderTransform as ScaleTransform;
   if(scale!=null&&!scale.IsFrozen)return scale;
   scale=new ScaleTransform(1,1);
   element.RenderTransformOrigin=new Point(.5,.5);
   element.RenderTransform=scale;
   return scale;
  }

  public static void EnablePress(Button button){
   button.PreviewMouseLeftButtonDown+=delegate{if(Enabled)Scale(button,.96,90,false);};
   button.PreviewMouseLeftButtonUp+=delegate{if(Enabled)Scale(button,1,210,true);};
   button.LostMouseCapture+=delegate{if(Enabled)Scale(button,1,210,true);};
   button.IsEnabledChanged+=delegate{if(Enabled&&!button.IsEnabled)Scale(button,1,140,false);};
  }

  public static void EnterWhenLoaded(FrameworkElement element){
   RoutedEventHandler handler=null;
   handler=delegate{element.Loaded-=handler;Enter(element,.978,true);};
   element.Loaded+=handler;
  }

  public static void Enter(FrameworkElement element,double initial,bool fade){
   if(!Enabled)return;
   var scale=Transform(element);
   scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);
   scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
   scale.ScaleX=scale.ScaleY=initial;
   Scale(element,1,220,true);
   if(fade)element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.50,1,TimeSpan.FromMilliseconds(145)){FillBehavior=FillBehavior.Stop});
  }

  static void Scale(FrameworkElement element,double target,int duration,bool spring){
   var scale=Transform(element);
   double x=scale.ScaleX,y=scale.ScaleY;
   scale.ScaleX=scale.ScaleY=target;
   IEasingFunction easing=spring?(IEasingFunction)new BackEase{Amplitude=.65,EasingMode=EasingMode.EaseOut}:new QuadraticEase{EasingMode=EasingMode.EaseOut};
   var time=TimeSpan.FromMilliseconds(duration);
   scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(x,target,time){EasingFunction=easing,FillBehavior=FillBehavior.Stop});
   scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(y,target,time){EasingFunction=easing,FillBehavior=FillBehavior.Stop});
  }

  public static ContextMenu Menu(){
   var menu=new ContextMenu();
   menu.Opened+=delegate{
    menu.ApplyTemplate();
    var frame=menu.Template.FindName("MenuSurface",menu) as FrameworkElement;
    if(frame!=null)Enter(frame,.955,true);
   };
   return menu;
  }

  public static void LightSurface(Border surface,string lit,string baseColor,Point resting){
   var light=new RadialGradientBrush{Center=resting,GradientOrigin=resting,RadiusX=.73,RadiusY=.81};
   light.GradientStops.Add(Theme.ColorStop(lit,0));
   light.GradientStops.Add(Theme.ColorStop(baseColor,1));
   surface.Background=light;
   surface.MouseMove+=delegate(object sender,MouseEventArgs e){
    if(!Enabled||surface.ActualWidth<=0||surface.ActualHeight<=0)return;
    var cursor=e.GetPosition(surface);
    var point=new Point(resting.X*.62+cursor.X/surface.ActualWidth*.38,resting.Y*.62+cursor.Y/surface.ActualHeight*.38);
    if(Math.Abs(light.Center.X-point.X)+Math.Abs(light.Center.Y-point.Y)>.012)
     light.Center=light.GradientOrigin=point;
   };
   surface.MouseLeave+=delegate{light.Center=light.GradientOrigin=resting;};
  }
 }
}