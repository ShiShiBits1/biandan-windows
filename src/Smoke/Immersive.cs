using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Biandan {
 public sealed partial class App {
  // Route contacts inside the demo window without moving the system cursor.
  sealed class DemoContact : TouchDevice {
   readonly FrameworkElement surface;
   Point position;
   TouchAction action;

   public DemoContact(FrameworkElement surface):base(101){this.surface=surface;}

   public void Send(Point point,TouchAction next,RoutedEvent routedEvent){
    position=point;action=next;
    var hit=surface.InputHitTest(point) as UIElement??surface;
    var args=new TouchEventArgs(this,Environment.TickCount){RoutedEvent=routedEvent};
    hit.RaiseEvent(args);
    if(args.Handled)throw new InvalidOperationException("A stationary or rightward contact on the card intercepted a task gesture.");
   }

   public override TouchPoint GetTouchPoint(IInputElement relativeTo){
    var relative=relativeTo as UIElement;
    Point local=relative==null?position:surface.TranslatePoint(position,relative);
    return new TouchPoint(this,local,new Rect(local,new Size(1,1)),action);
   }

   public override TouchPointCollection GetIntermediateTouchPoints(IInputElement relativeTo){
    return new TouchPointCollection{GetTouchPoint(relativeTo)};
   }
  }

  static RenderTargetBitmap RenderWindowRoot(FrameworkElement root){
   root.UpdateLayout();
   var transform=PresentationSource.FromVisual(root).CompositionTarget.TransformToDevice;
   var frame=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth*transform.M11),
    (int)Math.Ceiling(root.ActualHeight*transform.M22),96*transform.M11,96*transform.M22,PixelFormats.Pbgra32);
   frame.Render(root);
   return frame;
  }

  byte[] CardEdge(Border card,double x){
   var frame=RenderWindowRoot(Desktop);
   var point=card.TranslatePoint(new Point(x-12,0),Desktop);
   var region=new Int32Rect((int)Math.Floor(point.X*frame.DpiX/96),(int)Math.Floor(point.Y*frame.DpiY/96),
    (int)Math.Ceiling(24*frame.DpiX/96),(int)Math.Ceiling(5*frame.DpiY/96));
   var pixels=new byte[region.Width*region.Height*4];
   frame.CopyPixels(region,pixels,region.Width*4,0);
   return pixels;
  }

  // Subtract each theme's resting edge to isolate the tint of the contact rim.
  static int EdgeTint(byte[] pressed,byte[] resting){
   int delta=0;
   for(int i=0;i<pressed.Length;i+=4)delta+=(pressed[i+2]-resting[i+2])-(pressed[i]-resting[i]);
   return delta;
  }

  void CaptureHomeMenu(ContextMenu menu,string path){
   var popupRoot=(FrameworkElement)PresentationSource.FromVisual(menu).RootVisual;
   var windowFrame=RenderWindowRoot(Desktop);
   var menuFrame=RenderWindowRoot(popupRoot);
   var origin=Desktop.PointFromScreen(popupRoot.PointToScreen(new Point()));
   var end=Desktop.PointFromScreen(popupRoot.PointToScreen(new Point(popupRoot.ActualWidth,popupRoot.ActualHeight)));
   var menuRect=new Rect(origin,end);
   var windowRect=new Rect(Desktop.RenderSize);
   if(!windowRect.IntersectsWith(menuRect))throw new InvalidOperationException("The task menu opened outside the home window.");
   var bounds=Rect.Union(windowRect,menuRect);
   var layers=new DrawingVisual();
   using(var dc=layers.RenderOpen()){
    dc.PushTransform(new TranslateTransform(-bounds.X,-bounds.Y));
    dc.DrawImage(windowFrame,windowRect);
    dc.DrawImage(menuFrame,menuRect);
    dc.Pop();
   }
   var image=new RenderTargetBitmap((int)Math.Ceiling(bounds.Width*windowFrame.DpiX/96),
    (int)Math.Ceiling(bounds.Height*windowFrame.DpiY/96),windowFrame.DpiX,windowFrame.DpiY,PixelFormats.Pbgra32);
   image.Render(layers);
   var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
   using(var stream=File.Create(path))png.Save(stream);
  }

  void VerifyMenuOpensDetails(MenuItem detail,string expectedTitle){
   bool opened=false;
   Dispatcher.BeginInvoke(new Action(delegate{
    var window=Desktop.OwnedWindows.OfType<TaskDetail>().SingleOrDefault(w=>w.IsVisible);
    if(window==null)return;
    try{window.UpdateLayout();opened=VisualControls<TextBlock>(window).Any(t=>t.Text==expectedTitle);}
    finally{window.Close();}
   }),DispatcherPriority.ApplicationIdle);
   detail.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
   if(!opened)throw new InvalidOperationException("The task menu did not open the selected occurrence's details.");
  }

  async Task VerifyImmersiveAsync(string directory){
   if(!Smoke||!Repo.Demo)throw new InvalidOperationException("This check requires the isolated smoke-test account.");
   var originalTheme=Theme.Selected;
   ContextMenu menu=null;
   try{
    SetThemeColor(Theme.DefaultHex);Desktop.Navigate("today");
    await Task.Delay(350);Desktop.UpdateLayout();
    var list=VisualControls<TaskResultsList>(Desktop).Single();
    var container=(ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
    var row=VisualControls<SwipeTaskRow>(container).Single();
    var card=row.Card;
    var more=VisualControls<Button>(card).Single(b=>Equals(b.ToolTip,"更多操作"));
    var expected=Repo.Items().Single(t=>t.OccurrenceId==(string)list.Items[0]);
    var left=new Point(40,4);
    var right=new Point(card.ActualWidth-44,4);
    var contact=new DemoContact(card);
    var restLeft=CardEdge(card,left.X);
    var restRight=CardEdge(card,right.X);
    contact.Send(left,TouchAction.Down,UIElement.PreviewTouchDownEvent);
    var first=CardEdge(card,left.X);
    var far=CardEdge(card,right.X);
    contact.Send(right,TouchAction.Move,UIElement.PreviewTouchMoveEvent);
    var moved=CardEdge(card,right.X);
    bool effects=SystemParameters.ClientAreaAnimation&&!SystemParameters.HighContrast;
    if(!far.SequenceEqual(restRight)||!CardEdge(card,left.X).SequenceEqual(restLeft)||
       (effects?(EdgeTint(first,restLeft)<100||EdgeTint(moved,restRight)<100):!moved.SequenceEqual(restRight)))
     throw new InvalidOperationException("The card edge did not follow the routed touch contact.");
    SetThemeColor("#76B0E8");
    var blue=CardEdge(card,right.X);
    contact.Send(right,TouchAction.Up,UIElement.PreviewTouchUpEvent);
    var blueRest=CardEdge(card,right.X);
    if(effects&&EdgeTint(blue,blueRest)>-100)
     throw new InvalidOperationException("The active card edge did not follow the new theme and release on touch-up.");
    SetThemeColor(Theme.DefaultHex);
    if(!CardEdge(card,right.X).SequenceEqual(restRight))
     throw new InvalidOperationException("The released card retained its contact light.");

    more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await Task.Delay(360);
    menu=PresentationSource.CurrentSources.OfType<PresentationSource>().Where(s=>!s.IsDisposed&&s.RootVisual!=null)
     .SelectMany(s=>VisualControls<ContextMenu>(s.RootVisual)).Single(m=>m.IsOpen&&m.PlacementTarget==more);
    var detail=menu.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,"打开详情与子任务"));
    if(effects&&EdgeTint(CardEdge(card,right.X),restRight)<100)
     throw new InvalidOperationException("The active task menu lost its originating card highlight.");
    CaptureHomeMenu(menu,Path.Combine(directory,"immersive-home.png"));

    VerifyMenuOpensDetails(detail,expected.Title);
   }finally{
    if(menu!=null)menu.IsOpen=false;
    SetThemeColor(originalTheme);
   }
  }
 }
}
