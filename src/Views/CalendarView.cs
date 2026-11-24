using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Biandan {
 public sealed class MonthCalendar:StackPanel {
  public DateTime Month,Selected;readonly Repository repo;readonly Action<DateTime> select;readonly bool compact;public Action<DateTime> MonthChanged;
  public MonthCalendar(Repository repository,DateTime month,DateTime selected,Action<DateTime> onSelect,bool small=true){repo=repository;Month=new DateTime(month.Year,month.Month,1);Selected=selected;select=onSelect;compact=small;Render();}
  DateTime lastMonth,lastSelected,lastToday;
  int[] compactCounts;
  List<TaskItem> lastItems;
  int[] CountsByDate(List<TaskItem> items,string[] dates){
   var counts=new int[dates.Length];
   foreach(var item in items){
    if(item.Abandoned||item.Undated||string.CompareOrdinal(item.End,dates[0])<0||string.CompareOrdinal(item.Start,dates[dates.Length-1])>0)continue;
    int first=Array.BinarySearch(dates,item.Start,StringComparer.Ordinal);
    int after=Array.BinarySearch(dates,item.End,StringComparer.Ordinal);
    first=first<0?~first:first;after=after<0?~after:after+1;
    if(first>=after)continue;
    counts[first]++;if(after<dates.Length)counts[after]--;
   }
   for(int i=1;i<counts.Length;i++)counts[i]+=counts[i-1];
   return counts;
  }
  public void Render(){
   int shift=((int)Month.DayOfWeek+6)%7;var start=Month.AddDays(-shift);
   var items=repo.Items();
   if(compact){
    bool sameMonth=lastMonth==Month&&lastSelected==Selected&&lastToday==DateTime.Today&&compactCounts!=null;
    if(sameMonth&&ReferenceEquals(lastItems,items))return;
    lastItems=items;
    var dates=Enumerable.Range(0,42).Select(i=>J.Date(start.AddDays(i))).ToArray();
    var counts=CountsByDate(items,dates);
    if(sameMonth&&compactCounts.SequenceEqual(counts))return;
    lastMonth=Month;lastSelected=Selected;lastToday=DateTime.Today;compactCounts=counts;
   }
   Children.Clear();var head=U.Grid("*,Auto,Auto");var title=U.Text(Month.ToString("yyyy 年 M 月"),compact?14:18,"#2E2418",true);U.Place(head,title);
   U.Place(head,U.IconButton("\uE76B","上个月",()=>Move(-1)),1);U.Place(head,U.IconButton("\uE76C","下个月",()=>Move(1)),2);Children.Add(head);U.Gap(this,compact?12:20);
   var week=U.Grid("*,*,*,*,*,*,*");for(int i=0;i<7;i++){var text=U.Text(new[]{"一","二","三","四","五","六","日"}[i],11,i>4?"#897859":"#79684F");text.HorizontalAlignment=HorizontalAlignment.Center;U.Place(week,text,i);}Children.Add(week);U.Gap(this,8);
   var days=U.Grid("*,*,*,*,*,*,*","*,*,*,*,*,*");
   for(int i=0;i<42;i++){
    DateTime d=start.AddDays(i);bool chosen=d.Date==Selected.Date,today=d.Date==DateTime.Today;var date=J.Date(d);var ds=compact?null:items.Where(t=>t.OnDate(date)&&!t.Abandoned).ToList();int count=compact?compactCounts[i]:ds.Count;
    var panel=U.Stack();var num=U.Text(d.Day.ToString(),compact?12:14,chosen?"#412B0C":d.Month!=Month.Month?"#AD9B7C":today?"#8D560B":"#513D24",chosen||today);num.HorizontalAlignment=HorizontalAlignment.Center;panel.Children.Add(num);
    if(compact){var dot=new Ellipse{Width=3.5,Height=3.5,Fill=U.C(chosen?"#67400A":"#B77516"),Margin=new Thickness(0,4,0,0),Visibility=count>0?Visibility.Visible:Visibility.Hidden};panel.Children.Add(dot);}
    else{U.Gap(panel,5);foreach(var t in ds.Take(2)){var titleText=U.Text("· "+t.Title,10,chosen?"#63451E":t.Completed?"#7B6548":"#6E5638");panel.Children.Add(titleText);}if(ds.Count>2)panel.Children.Add(U.Text("+"+(ds.Count-2),10,chosen?"#412B0C":"#7B6548"));}
    var button=U.Button("",()=>{Selected=d;if(select!=null)select(d);Render();});button.Content=panel;button.MinHeight=compact?32:88;button.Padding=new Thickness(compact?2:6);button.Margin=new Thickness(compact?2:3);button.Background=chosen?U.C("#FFCE6D"):today?U.C("#FFF1D5"):compact?Brushes.Transparent:U.C("#FFFAF0");button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.ToolTip=d.ToString("yyyy 年 M 月 d 日")+" · "+count+" 项待办";
    U.Place(days,button,i%7,i/7);
   }Children.Add(days);
  }
  void Move(int n){Month=Month.AddMonths(n);if(MonthChanged!=null)MonthChanged(Month);Render();}
 }
}
