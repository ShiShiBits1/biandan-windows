using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Biandan {
 // Painting the border itself keeps the light inside a scrolling or swiping row.
 // All hit targets, keyboard focus visuals and child layout remain native WPF.
 internal sealed class TaskCardSurface : Border {
  RadialGradientBrush light;
  Pen edge,feather;
  int contactId=-1;
  ContextMenu activeMenu;

  internal static void OpenMenu(FrameworkElement anchor,ContextMenu menu){
   menu.PlacementTarget=anchor;
   menu.Placement=PlacementMode.Bottom;
   menu.Closed+=ResetMenuPlacement;
   menu.IsOpen=true;
   if(!menu.IsOpen)return;
   // A keyboard-opened menu and a tap indicate the same task. Its light stays
   // with the button while input moves into the popup's separate visual tree.
   DependencyObject parent=anchor;
   while(parent!=null&&!(parent is TaskCardSurface))parent=VisualTreeHelper.GetParent(parent);
   var card=parent as TaskCardSurface;
   if(card==null)return;
   card.ClearLight();
   var center=new Point(anchor.ActualWidth/2,anchor.ActualHeight/2);
   card.Illuminate(anchor.TranslatePoint(center,card),true);
   if(card.light!=null){
    card.activeMenu=menu;
    menu.Closed+=card.MenuClosed;
   }
  }

  static void ResetMenuPlacement(object sender,RoutedEventArgs e){
   var menu=(ContextMenu)sender;
   menu.Closed-=ResetMenuPlacement;
   // Board cards also use this menu for right-click, which positions at the mouse.
   menu.ClearValue(ContextMenu.PlacementTargetProperty);
   menu.ClearValue(ContextMenu.PlacementProperty);
  }

  void MenuClosed(object sender,RoutedEventArgs e){ClearLight();}

  protected override void OnRender(DrawingContext drawing){
   base.OnRender(drawing);
   if(light==null)return;
   drawing.PushOpacity(.24);
   DrawRim(drawing,feather);
   drawing.Pop();
   DrawRim(drawing,edge);
  }

  void DrawRim(DrawingContext drawing,Pen pen){
   double inset=pen.Thickness/2;
   if(RenderSize.Width<=pen.Thickness||RenderSize.Height<=pen.Thickness)return;
   var rect=new Rect(inset,inset,RenderSize.Width-pen.Thickness,RenderSize.Height-pen.Thickness);
   double radius=Math.Max(0,CornerRadius.TopLeft-inset);
   drawing.DrawRoundedRectangle(null,pen,rect,radius,radius);
  }

  void Illuminate(Point point,bool pressed){
   if(!IsEnabled||SystemParameters.HighContrast||!SystemParameters.ClientAreaAnimation||
      point.X<0||point.Y<0||point.X>RenderSize.Width||point.Y>RenderSize.Height){
    ClearLight();
    return;
   }
   if(light==null){
    light=new RadialGradientBrush{MappingMode=BrushMappingMode.Absolute,RadiusX=118,RadiusY=82};
    light.GradientStops.Add(Theme.ColorStop(Theme.DefaultHex,0));
    light.GradientStops.Add(new GradientStop(Colors.Transparent,1));
    edge=new Pen(light,1.6);
    feather=new Pen(light,5);
    Unloaded+=SurfaceUnloaded;
    SystemParameters.StaticPropertyChanged+=PreferencesChanged;
    InvalidateVisual();
   }
   // The retained drawing observes brush changes directly. No rendering loop or
   // measure/arrange is needed as the pointer moves.
   light.Center=point;
   light.GradientOrigin=point;
   light.Opacity=pressed?1:.78;
  }

  void ClearLight(){
   if(activeMenu!=null){
    activeMenu.Closed-=MenuClosed;
    activeMenu=null;
   }
   contactId=-1;
   if(light==null)return;
   SystemParameters.StaticPropertyChanged-=PreferencesChanged;
   Unloaded-=SurfaceUnloaded;
   light=null;edge=feather=null;
   InvalidateVisual();
  }

  void SurfaceUnloaded(object sender,RoutedEventArgs e){ClearLight();}

  void PreferencesChanged(object sender,PropertyChangedEventArgs e){
   if(SystemParameters.HighContrast||!SystemParameters.ClientAreaAnimation)ClearLight();
  }

  void MouseFeedback(MouseEventArgs e){
   // Touch has its own contact lifetime. Promoted mouse events can arrive after
   // TouchUp, when a tap should already have returned to its resting appearance.
   if(activeMenu==null&&contactId<0&&e.StylusDevice==null)
    Illuminate(e.GetPosition(this),e.LeftButton==MouseButtonState.Pressed);
  }

  protected override void OnMouseEnter(MouseEventArgs e){base.OnMouseEnter(e);MouseFeedback(e);}
  protected override void OnPreviewMouseMove(MouseEventArgs e){base.OnPreviewMouseMove(e);MouseFeedback(e);}
  protected override void OnPreviewMouseDown(MouseButtonEventArgs e){base.OnPreviewMouseDown(e);MouseFeedback(e);}
  protected override void OnPreviewMouseUp(MouseButtonEventArgs e){base.OnPreviewMouseUp(e);MouseFeedback(e);}
  protected override void OnMouseLeave(MouseEventArgs e){
   base.OnMouseLeave(e);
   if(activeMenu==null&&contactId<0)ClearLight();
  }

  protected override void OnPreviewTouchDown(TouchEventArgs e){
   base.OnPreviewTouchDown(e);
   if(activeMenu!=null||contactId>=0)return;
   contactId=e.TouchDevice.Id;
   Illuminate(e.GetTouchPoint(this).Position,true);
  }

  protected override void OnPreviewTouchMove(TouchEventArgs e){
   base.OnPreviewTouchMove(e);
   if(contactId==e.TouchDevice.Id)Illuminate(e.GetTouchPoint(this).Position,true);
  }

  internal void ReleaseContact(int id){if(contactId==id)ClearLight();}

  protected override void OnPreviewTouchUp(TouchEventArgs e){
   base.OnPreviewTouchUp(e);
   ReleaseContact(e.TouchDevice.Id);
  }

  protected override void OnTouchLeave(TouchEventArgs e){
   base.OnTouchLeave(e);
   ReleaseContact(e.TouchDevice.Id);
  }

  protected override void OnLostTouchCapture(TouchEventArgs e){
   base.OnLostTouchCapture(e);
   ReleaseContact(e.TouchDevice.Id);
  }

  protected override void OnGiveFeedback(GiveFeedbackEventArgs e){
   base.OnGiveFeedback(e);
   ClearLight();
  }
 }
}
