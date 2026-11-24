using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Biandan {
 public sealed partial class Repository {
  internal bool CanRescheduleItem(TaskItem item){
   if(!CanWrite||item==null||item.Syncing||item.Occurrence.S("status")!="pending"||item.Occurrence.I("completed_count")<0)return false;
   if(item.Repeating)return !item.Undated&&(Demo||Connected);
   return Items().Count(candidate=>candidate.Id==item.Id)==1;
  }

  static bool ReadScheduleClock(string value,TimeSpan fallback,out TimeSpan result){
   if(string.IsNullOrEmpty(value)){result=fallback;return true;}
   return TimeSpan.TryParseExact(value,new[]{@"hh\:mm",@"hh\:mm\:ss"},CultureInfo.InvariantCulture,out result);
  }

  internal static bool TryScheduleWindow(TaskItem item,out DateTime first,out DateTime last){
   first=last=DateTime.MinValue;
   if(item.Undated||!DateTime.TryParseExact(item.Start,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out first)||
      !DateTime.TryParseExact(item.End,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out last))return false;
   TimeSpan start,end;
   if(!ReadScheduleClock(item.Time,TimeSpan.Zero,out start))return false;
   // Older local tasks can carry just a start clock. Keep the half-hour block shown in the week view.
   int fallback=first==last&&start!=TimeSpan.Zero?Math.Min(1439,(int)start.TotalMinutes+30):1439;
   if(!ReadScheduleClock(item.Occurrence.S("end_time",item.Task.S("end_time")),TimeSpan.FromMinutes(fallback),out end))return false;
   first=first.AddMinutes((int)start.TotalMinutes);
   last=last.AddMinutes((int)end.TotalMinutes);
   return last>=first;
  }

  internal static Dictionary<string,object> ScheduleArguments(TaskItem item,DateTime date,int? minute=null){
   if(minute.HasValue&&(minute.Value<0||minute.Value>=1440))throw new McpException("请选择有效的开始时刻。");
   DateTime start=date.Date;
   TimeSpan duration=TimeSpan.FromMinutes(minute.HasValue?Math.Min(30,1439-minute.Value):1439);
   if(!item.Undated){
    DateTime first,last;
    if(!TryScheduleWindow(item,out first,out last))throw new McpException("当前日程的起止日期或时间无效，请在详情中调整。");
    duration=last-first;
    if(!minute.HasValue)start=start.Add(first.TimeOfDay);
   }
   if(minute.HasValue)start=start.AddMinutes(minute.Value);
   if(DateTime.MaxValue-start<duration)throw new McpException("结束日期超出有效范围。");
   DateTime end=start.Add(duration);
   return J.Obj("task_id",item.Id,"start_date",J.Date(start),"end_date",J.Date(end),
    "start_time",ScheduleClock((int)start.TimeOfDay.TotalMinutes),"end_time",ScheduleClock((int)end.TimeOfDay.TotalMinutes));
  }

  internal Task RescheduleItemAsync(string occurrenceId,DateTime date,int? minute=null){
   var item=Items().FirstOrDefault(candidate=>candidate.OccurrenceId==occurrenceId);
   if(!CanRescheduleItem(item))throw new McpException("这次待办当前无法改期。请检查任务状态，循环待办还需连接云端。");
   var args=ScheduleArguments(item,date,minute);
   DateTime first,last;
   if(TryScheduleWindow(item,out first,out last)&&first.Date==date.Date&&
      (!minute.HasValue||first.TimeOfDay==TimeSpan.FromMinutes(minute.Value)))return Task.FromResult(0);
   if(item.Repeating){
    args.Remove("task_id");
    args["occurrence_id"]=occurrenceId;
    return CallAsync("reschedule_occurrence",args);
   }
   if(Demo)return CallAsync("reschedule_task",args);
   QueueTaskChange("reschedule_task",args);
   return Task.FromResult(0);
  }
 }
}
