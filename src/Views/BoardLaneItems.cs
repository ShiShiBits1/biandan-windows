using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Biandan {
 internal class RecyclingListBox:ListBox {
  static readonly Style ItemStyle=BuildItemStyle();
  protected RecyclingListBox(){
   Background=Brushes.Transparent;BorderThickness=new Thickness(0);Padding=new Thickness(0);
   IsTabStop=false;
   ScrollViewer.SetCanContentScroll(this,true);
   ScrollViewer.SetVerticalScrollBarVisibility(this,ScrollBarVisibility.Auto);
   ScrollViewer.SetHorizontalScrollBarVisibility(this,ScrollBarVisibility.Disabled);
   VirtualizingPanel.SetIsVirtualizing(this,true);
   VirtualizingPanel.SetVirtualizationMode(this,VirtualizationMode.Recycling);
   ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
   ItemContainerStyle=ItemStyle;
  }

  static Style BuildItemStyle(){
   var style=new Style(typeof(ListBoxItem));
   style.Setters.Add(new Setter(FocusableProperty,false));
   style.Setters.Add(new Setter(IsTabStopProperty,false));
   var template=new ControlTemplate(typeof(ListBoxItem)){VisualTree=new FrameworkElementFactory(typeof(ContentPresenter))};
   style.Setters.Add(new Setter(TemplateProperty,template));
   style.Seal();
   return style;
  }

  protected override void ClearContainerForItemOverride(DependencyObject element,object item){
   ((ListBoxItem)element).ClearValue(ContentControl.ContentProperty);
   base.ClearContainerForItemOverride(element,item);
  }

  public double Offset{
   get{var viewer=FindViewer(this);return viewer==null?0:viewer.VerticalOffset;}
  }
  protected void ScrollToOffset(double offset){
   var viewer=FindViewer(this);
   if(viewer!=null)viewer.ScrollToVerticalOffset(offset);
  }
  static ScrollViewer FindViewer(DependencyObject root){
   var viewer=root as ScrollViewer;
   if(viewer!=null)return viewer;
   for(int index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){
    viewer=FindViewer(VisualTreeHelper.GetChild(root,index));
    if(viewer!=null)return viewer;
   }
   return null;
  }
 }

 internal sealed class BoardLaneItems:RecyclingListBox {
  readonly Func<TaskItem,FrameworkElement> render;
  public BoardLaneItems(List<TaskItem> items,Func<TaskItem,FrameworkElement> render,double offset){
   this.render=render;
   ItemsSource=items;
   if(offset>0)Loaded+=delegate{ScrollToOffset(offset);};
  }
  protected override void PrepareContainerForItemOverride(DependencyObject element,object item){
   base.PrepareContainerForItemOverride(element,item);
   ((ListBoxItem)element).Content=render((TaskItem)item);
  }
 }
}
