using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Biandan {
 public sealed partial class MainView {
  // One non-interactive overlay follows the drag across the calendar. The day
  // columns keep their original brushes, including when they scroll underneath it.
  sealed class WeekDropPreview:Canvas{
   readonly Border glow,rule,chip;
   readonly TextBlock label;
   public FrameworkElement Target{get;private set;}

   public WeekDropPreview(){
    IsHitTestVisible=false;
    ClipToBounds=true;
    Visibility=Visibility.Hidden;
    var fade=new RadialGradientBrush(Color.FromArgb(190,255,255,255),Colors.Transparent){
     Center=new Point(.5,.5),GradientOrigin=new Point(.5,.5),RadiusX=.5,RadiusY=.5
    };
    fade.Freeze();
    glow=new Border{Background=U.C(PlanningPalette.Accent),OpacityMask=fade};
    rule=new Border{Height=2,CornerRadius=new CornerRadius(1)};
    label=U.Text("",11,PlanningPalette.Ink,true);
    label.TextAlignment=TextAlignment.Center;
    var fit=new Viewbox{Child=label,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly};
    chip=new Border{Child=fit,CornerRadius=new CornerRadius(4),Padding=new Thickness(4,0,4,0),BorderThickness=new Thickness(1)};
    Children.Add(glow);Children.Add(rule);Children.Add(chip);
    Unloaded+=delegate{Clear();};
   }

   public void ShowAt(FrameworkElement target,DateTime date,int? slot,DateTime start,ScrollViewer scroll,bool allDay){
    var origin=target.TranslatePoint(new Point(),this);
    double top=origin.Y,bottom=origin.Y+target.ActualHeight;
    if(scroll!=null){
     top=scroll.TranslatePoint(new Point(),this).Y;
     bottom=top+scroll.ViewportHeight;
    }
    double width=Math.Min(160,target.ActualWidth-4);
    if(width<=0||bottom<=top){Clear();return;}
    double left=origin.X+(target.ActualWidth-width)/2;
    double anchor=slot.HasValue?origin.Y+MinuteOffset(slot.Value):(top+bottom)/2;
    anchor=Math.Max(top,Math.Min(bottom,anchor));

    bool highContrast=SystemParameters.HighContrast;
    glow.Visibility=!highContrast&&SystemParameters.ClientAreaAnimation?Visibility.Visible:Visibility.Collapsed;
    glow.Width=width;glow.Height=Math.Min(36,bottom-top);
    SetLeft(glow,left);SetTop(glow,Math.Max(top,Math.Min(bottom-glow.Height,anchor-glow.Height/2)));
    rule.Visibility=slot.HasValue?Visibility.Visible:Visibility.Collapsed;
    rule.Background=highContrast?SystemColors.HighlightBrush:U.C(PlanningPalette.Emphasis);
    rule.Width=width;SetLeft(rule,left);SetTop(rule,Math.Min(anchor,bottom-rule.Height));

    string format=allDay?"M/d 全天":!slot.HasValue||start.Date!=date.Date?"M/d HH:mm":"HH:mm";
    string text=start.ToString(format,CultureInfo.InvariantCulture);
    if(label.Text!=text){
     label.Text=text;
     AutomationProperties.SetName(chip,"移至 "+text);
    }
    // In narrow columns allow the date and clock to occupy two legible lines.
    // Short all-day rows still fit their label within the available height.
    bool canWrap=bottom-top>=36;
    label.TextWrapping=canWrap?TextWrapping.Wrap:TextWrapping.NoWrap;
    label.MaxWidth=canWrap?Math.Max(1,width-10):double.PositiveInfinity;
    label.Foreground=highContrast?SystemColors.HighlightTextBrush:U.C(PlanningPalette.Ink);
    chip.Background=highContrast?SystemColors.HighlightBrush:U.C(PlanningPalette.Card);
    chip.BorderBrush=rule.Background;
    chip.MaxWidth=width;chip.MaxHeight=bottom-top;
    chip.Measure(new Size(width,bottom-top));
    double h=chip.DesiredSize.Height;
    double labelTop=slot.HasValue?anchor+2:anchor-h/2;
    if(slot.HasValue&&labelTop+h>bottom)labelTop=anchor-h;
    SetLeft(chip,left+(width-chip.DesiredSize.Width)/2);
    SetTop(chip,Math.Max(top,Math.Min(labelTop,bottom-h)));
    Target=target;
    Visibility=Visibility.Visible;
   }

   public void Clear(){
    Visibility=Visibility.Hidden;
    Target=null;
   }
  }
 }
}
