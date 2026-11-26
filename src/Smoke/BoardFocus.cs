using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Biandan {
 public sealed partial class App {
  sealed class BoardPressProbe:Button {
   public void Hold(){IsPressed=true;}
  }

  void VerifyBoardCardFocus(){
   var body=VisualControls<Button>(Desktop).First(button=>button.IsVisible&&AutomationProperties.GetName(button).StartsWith("查看待办："));
   var row=(Grid)body.Parent;
   if(!body.Focusable||!body.IsTabStop||Math.Abs(body.ActualWidth-row.ActualWidth)>1||Math.Abs(body.ActualHeight-row.ActualHeight)>1)
    throw new InvalidOperationException("The board detail action must provide a focus target around the whole card.");
   foreach(var action in row.Children.OfType<Button>()){
    var center=action.TranslatePoint(new Point(action.ActualWidth/2,action.ActualHeight/2),row);
    var hit=row.InputHitTest(center) as DependencyObject;
    while(hit!=null&&!(hit is Button))hit=VisualTreeHelper.GetParent(hit);
    if(hit!=action)throw new InvalidOperationException("The board body, completion and menu buttons must keep separate hit targets.");
   }

   // An empty card body should paint no pixels, even while the button is held.
   var size=new Size(96,44);
   var probe=new BoardPressProbe{Style=body.Style};
   probe.Measure(size);probe.Arrange(new Rect(size));probe.UpdateLayout();
   probe.Hold();probe.UpdateLayout();
   var image=new RenderTargetBitmap(96,44,96,96,PixelFormats.Pbgra32);
   image.Render(probe);
   var pixels=new byte[96*44*4];
   image.CopyPixels(pixels,96*4,0);
   if(pixels.Any(value=>value!=0))throw new InvalidOperationException("Holding a card must not paint a second button face.");

   var cue=new Control{Style=body.FocusVisualStyle};
   cue.Measure(size);cue.Arrange(new Rect(size));cue.UpdateLayout();
   var ring=VisualControls<Border>(cue).FirstOrDefault(border=>border.BorderBrush!=null&&border.BorderThickness.Left>0);
   if(ring==null||ring.CornerRadius.TopLeft<=0||Math.Abs(ring.ActualWidth-size.Width)>1)
    throw new InvalidOperationException("Keyboard navigation must have a visible rounded cue around the card.");
  }
 }
}
