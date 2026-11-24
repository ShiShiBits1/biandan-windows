using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Biandan {
 // The source contains occurrence IDs and a few permanent group markers. Only
 // containers in the viewport and WPF's small scrolling cache own task controls.
 internal sealed class TaskResultsList:RecyclingListBox {
  readonly Func<TaskItem,FrameworkElement> render;
  readonly Func<TaskItem,string> signature;
  readonly Dictionary<ListBoxItem,string> realized=new Dictionary<ListBoxItem,string>();
  Dictionary<string,TaskItem> tasks=new Dictionary<string,TaskItem>();
  ObservableCollection<object> keys=new ObservableCollection<object>();

  internal TaskResultsList(Func<TaskItem,FrameworkElement> render,Func<TaskItem,string> signature){
   this.render=render;this.signature=signature;
   PlanningPalette.StyleScrollbars(this);
   VirtualizingPanel.SetScrollUnit(this,ScrollUnit.Item);
   ItemsSource=keys;
  }

  internal void UpdateRows(IEnumerable<object> rows,bool startAtTop=false){
   var nextTasks=new Dictionary<string,TaskItem>();
   var next=new List<object>();
   foreach(object row in rows){
    var task=row as TaskItem;
    if(task==null)next.Add(row);
    else{nextTasks.Add(task.OccurrenceId,task);next.Add(task.OccurrenceId);}
   }
   tasks=nextTasks;
   int prefix=0,oldEnd=keys.Count,newEnd=next.Count;
   while(prefix<oldEnd&&prefix<newEnd&&Equals(keys[prefix],next[prefix]))prefix++;
   while(oldEnd>prefix&&newEnd>prefix&&Equals(keys[oldEnd-1],next[newEnd-1])){oldEnd--;newEnd--;}
   if(oldEnd!=prefix||newEnd!=prefix){
    double offset=Offset;
    int first=(int)offset;
    object anchor=first<keys.Count?keys[first]:null;
    if(oldEnd-prefix+newEnd-prefix<=32){
     for(int i=oldEnd;i>prefix;i--)keys.RemoveAt(i-1);
     for(int i=prefix;i<newEnd;i++)keys.Insert(i,next[i]);
    }else{
     keys=new ObservableCollection<object>(next);
     ItemsSource=keys;
    }
    if(!startAtTop&&offset>0){
     int index=anchor==null?-1:next.IndexOf(anchor);
     ScrollToOffset(index<0?first:index);
    }
   }
   if(startAtTop)ScrollToOffset(0);
   foreach(var pair in realized.ToArray())UpdateRow(pair.Key,pair.Value);
  }

  protected override void PrepareContainerForItemOverride(DependencyObject element,object item){
   base.PrepareContainerForItemOverride(element,item);
   var container=(ListBoxItem)element;
   var id=item as string;
   if(id==null){container.Content=item;return;}
   realized[container]=id;
   UpdateRow(container,id);
  }

  void UpdateRow(ListBoxItem container,string id){
   TaskItem task;
   if(!tasks.TryGetValue(id,out task))return;
   string current=signature(task);
   if(Equals(container.Tag,current))return;
   var focused=container.IsKeyboardFocusWithin?Keyboard.FocusedElement as ButtonBase:null;
   var replacement=render(task);
   container.Content=replacement;
   container.Tag=current;
   if(focused!=null){
    var next=FindAction(replacement,focused);
    if(next!=null){container.UpdateLayout();next.Focus();}
   }
  }

  // The checkbox, star, focus button and menu have distinct grid columns.
  static ButtonBase FindAction(DependencyObject root,ButtonBase previous){
   var button=root as ButtonBase;
   if(button!=null&&button.GetType()==previous.GetType()&&button.Focusable&&button.IsEnabled&&Grid.GetColumn(button)==Grid.GetColumn(previous))return button;
   foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()){
    var found=FindAction(child,previous);
    if(found!=null)return found;
   }
   return null;
  }

  protected override void ClearContainerForItemOverride(DependencyObject element,object item){
   var container=(ListBoxItem)element;
   realized.Remove(container);
   container.Tag=null;
   base.ClearContainerForItemOverride(element,item);
  }
 }
}
