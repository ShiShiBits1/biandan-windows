using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Biandan {
 public static class DemoCalendar {
  public static DateTime Date(string value){
   DateTime date;
   if(!DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))
    throw new McpException("请选择有效日期。");
   return date;
  }

  public static IEnumerable<Tuple<DateTime,DateTime>> Windows(DateTime first,DateTime last,Dictionary<string,object> rule,int weekStart=0){
   if(last<first)throw new McpException("结束日期不能早于开始日期。");
   if(rule==null){yield return Tuple.Create(first,last);yield break;}

   var minimum=new DateTime(1900,1,31);
   var maximum=new DateTime(2100,12,31);
   if(first<minimum||last>maximum)throw new McpException("循环日期应在 1900-01-31 至 2100-12-31 之间。");
   string mode=rule.S("mode");
   string unit=rule.S("unit","day");
   int interval=rule.I("interval",1);
   if(interval<1||interval>366)throw new McpException("循环间隔应为 1 到 366 的整数。");
   if(weekStart!=0&&weekStart!=1)throw new McpException("每周的第一天应为周日或周一。");
   var firstWindow=first;
   if(mode=="weekly_period")firstWindow=first.AddDays(-((int)first.DayOfWeek-weekStart+7)%7);
   if(mode=="monthly_period")firstWindow=new DateTime(first.Year,first.Month,1);
   if(mode=="yearly_period")firstWindow=new DateTime(first.Year,1,1);

   var selected=new HashSet<string>(rule.Strings("dates"));
   var windows=new SortedSet<DateTime>();
   if(mode=="custom"&&unit=="month"){
    var month=new DateTime(first.Year,first.Month,1);
    while(month<=last){
     foreach(var value in selected){
      int number;
      if(!int.TryParse(value,out number)||number<1||number>32)throw new McpException("每月日期请输入 1–32。");
      var date=month.AddDays((number==32?DateTime.DaysInMonth(month.Year,month.Month):number)-1);
      if(date>=first&&date<=last)windows.Add(date);
     }
     if(windows.Count>1000)throw new McpException("循环最多生成 1000 次，请缩短日期范围。");
     month=month.AddMonths(interval);
    }
   }else{
    for(var day=firstWindow;day<=last;day=day.AddDays(1)){
     if(Includes(day,first,mode,unit,interval,selected,weekStart))windows.Add(day);
     if(windows.Count>1000)throw new McpException("循环最多生成 1000 次，请缩短日期范围。");
    }
   }
   if(windows.Count==0)throw new McpException("此日期范围内没有符合规则的日期。");
   foreach(var day in windows){
    var end=day;
    if(mode=="weekly_period")end=day.AddDays(6);
    if(mode=="monthly_period")end=day.AddMonths(1).AddDays(-1);
    if(mode=="yearly_period")end=new DateTime(day.Year,12,31);
    if(end>maximum)throw new McpException("某次执行的结束日期超过允许的范围。");
    yield return Tuple.Create(day,end);
   }
  }

  static bool Includes(DateTime day,DateTime first,string mode,string unit,int interval,HashSet<string> selected,int weekStart){
   bool weekend=day.DayOfWeek==DayOfWeek.Saturday||day.DayOfWeek==DayOfWeek.Sunday;
   switch(mode){
    case "workdays": return !weekend;
    case "days_off": return weekend;
    case "weekly_period": return (int)day.DayOfWeek==weekStart;
    case "monthly_period": return day.Day==1;
    case "yearly_period": return day.DayOfYear==1;
    case "ebbinghaus":
     int offset=(day-first).Days;
     return new[]{0,1,2,4,6,14}.Contains(offset)||(offset>=29&&(offset-29)%15==0);
    case "custom":
     if(unit=="day")return (day-first).Days%interval==0;
     if(unit=="week"){
      // The MCP uses a signed weekday difference to anchor custom week intervals.
      var anchor=first.AddDays(weekStart-(int)first.DayOfWeek);
      int offsetDays=(day-anchor).Days;
      return offsetDays>=0&&(offsetDays/7)%interval==0&&selected.Contains(((int)day.DayOfWeek).ToString(CultureInfo.InvariantCulture));
     }
     return (day.Year-first.Year)%interval==0&&selected.Contains(day.ToString("MM-dd",CultureInfo.InvariantCulture));
    default: return true;
   }
  }
 }
}