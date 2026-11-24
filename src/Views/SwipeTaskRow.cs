using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Biandan {
 internal sealed class SwipeTaskRow:Grid{
  const double RevealWidth=64;
  readonly Button delete;
  readonly TranslateTransform translate=new TranslateTransform();
  readonly Action<SwipeTaskRow> reveal;
  int touchId=-1;
  bool swiping,opened,suppressTouchClick;
  Point start;
  double initialOffset;
  public Border Card{get;private set;}
  internal bool ActionsOpen{get{return opened;}}

  public SwipeTaskRow(Border card,Action<SwipeTaskRow> reveal,Action remove){
   Card=card;this.reveal=reveal;
   Background=Brushes.Transparent;
   ClipToBounds=true;
   Margin=new Thickness(0,0,0,8);
   delete=U.Button("",()=>{CloseActions();remove();});
   delete.Content=U.Icon("\uE74D",18,"#FFFFFF");
   delete.Background=U.C("#AD412C");
   delete.Width=RevealWidth+10;
   delete.HorizontalAlignment=HorizontalAlignment.Right;
   delete.Padding=new Thickness(12,0,2,0);
   delete.Focusable=false;
   delete.ToolTip="删除待办";
   AutomationProperties.SetName(delete,"删除待办");
   delete.Visibility=Visibility.Hidden;
   Children.Add(delete);
   card.RenderTransform=translate;
   Children.Add(card);
   AddHandler(PreviewTouchDownEvent,new EventHandler<TouchEventArgs>(TouchStarted),true);
   AddHandler(PreviewTouchMoveEvent,new EventHandler<TouchEventArgs>(TouchMoved),true);
   AddHandler(PreviewTouchUpEvent,new EventHandler<TouchEventArgs>(TouchEnded),true);
   LostTouchCapture+=delegate(object sender,TouchEventArgs e){
    if(e.TouchDevice.Id==touchId&&ReferenceEquals(e.OriginalSource,this))ResetGesture();
   };
   PreviewMouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){
    if(suppressTouchClick&&e.StylusDevice!=null)e.Handled=true;
   };
  }

  void TouchStarted(object sender,TouchEventArgs e){
   if(swiping&&touchId!=e.TouchDevice.Id)return;
   touchId=e.TouchDevice.Id;
   start=e.GetTouchPoint(this).Position;
   initialOffset=opened?-RevealWidth:0;
   swiping=false;
   suppressTouchClick=false;

  }

  void TouchMoved(object sender,TouchEventArgs e){
   if(touchId!=e.TouchDevice.Id)return;
   var delta=e.GetTouchPoint(this).Position-start;
   if(!swiping){
    if(Math.Abs(delta.Y)>12&&Math.Abs(delta.Y)>=Math.Abs(delta.X)){ResetGesture();return;}
    if(Math.Abs(delta.X)<12||Math.Abs(delta.X)<Math.Abs(delta.Y)*1.3||(!opened&&delta.X>=0))return;
    swiping=true;suppressTouchClick=true;
    if(!e.TouchDevice.Capture(this)){ResetGesture();return;}
    // Capturing the row reroutes subsequent touch events away from the card.
    var surface=Card as TaskCardSurface;
    if(surface!=null)surface.ReleaseContact(touchId);
    reveal(this);
    delete.Visibility=Visibility.Visible;
    delete.IsHitTestVisible=false;
    translate.BeginAnimation(TranslateTransform.XProperty,null);
   }
   translate.X=Math.Max(-RevealWidth,Math.Min(0,initialOffset+delta.X));
   e.Handled=true;
  }

  void TouchEnded(object sender,TouchEventArgs e){
   if(touchId!=e.TouchDevice.Id)return;
   bool dragged=swiping;
   touchId=-1;swiping=false;
   if(!dragged)return;
   e.Handled=true;
   if(e.TouchDevice.Captured==this)e.TouchDevice.Capture(null);
   SetActions(translate.X<-RevealWidth*.55);
  }

  void ResetGesture(){
   touchId=-1;
   if(swiping){swiping=false;SetActions(opened);}
  }

  public void CloseActions(){SetActions(false);}

  internal void SetActions(bool open){
   opened=open;
   delete.Visibility=Visibility.Visible;
   delete.IsHitTestVisible=open;
   if(open)reveal(this);
   double old=translate.X,next=open?-RevealWidth:0;
   translate.BeginAnimation(TranslateTransform.XProperty,null);
   translate.X=next;
   if(!SystemParameters.ClientAreaAnimation||Math.Abs(old-next)<.1){
    if(!open)delete.Visibility=Visibility.Hidden;
    return;
   }
   var animation=new DoubleAnimation(old,next,TimeSpan.FromMilliseconds(155)){
    EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop
   };
   animation.Completed+=delegate{if(!opened&&!swiping)delete.Visibility=Visibility.Hidden;};
   translate.BeginAnimation(TranslateTransform.XProperty,animation);
  }
 }
}