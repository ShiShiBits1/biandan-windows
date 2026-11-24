using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Biandan {
 internal sealed class AssistantRepeat {
  public const string Schema = @"{
    ""type"": [""object"", ""null""],
    ""additionalProperties"": false,
    ""required"": [""mode"", ""unit"", ""interval"", ""dates""],
    ""properties"": {
      ""mode"": {""type"": ""string"", ""enum"": [""daily"", ""workdays"", ""days_off"", ""ebbinghaus"", ""custom""]},
      ""unit"": {""type"": [""string"", ""null""], ""enum"": [""day"", ""week"", ""month"", ""year"", null]},
      ""interval"": {""type"": [""integer"", ""null""]},
      ""dates"": {""type"": [""array"", ""null""], ""items"": {""type"": ""string""}}
    }
  }";

  static readonly Dictionary<string,string> Presets = new Dictionary<string,string>{
   {"daily","每天"}, {"workdays","每个工作日"}, {"days_off","每个休息日"}, {"ebbinghaus","艾宾浩斯记忆"}
  };
  static readonly Dictionary<string,string> Units = new Dictionary<string,string>{
   {"day","天"}, {"week","周"}, {"month","个月"}, {"year","年"}
  };
  static readonly string[] Weekdays = {"周日","周一","周二","周三","周四","周五","周六"};
  readonly string mode,unit;
  readonly int interval;
  readonly string[] dates;

  AssistantRepeat(string mode,string unit,int interval,string[] dates){
   this.mode=mode;
   this.unit=unit;
   this.interval=interval;
   this.dates=dates;
  }

  public static AssistantRepeat Parse(Dictionary<string,object> value){
   if(value==null)return null;
   if(value.Keys.Except(new[]{"mode","unit","interval","dates"}).Any())
    throw new McpException("小助手返回了未支持的循环设置。");
   var mode=value.S("mode");
   object unitValue,intervalValue,dateValues;
   value.TryGetValue("unit",out unitValue);
   value.TryGetValue("interval",out intervalValue);
   value.TryGetValue("dates",out dateValues);
   if(Presets.ContainsKey(mode)){
    if(unitValue!=null||intervalValue!=null||dateValues!=null)
     throw new McpException("预设循环不能同时使用自定义间隔或日期。");
    return new AssistantRepeat(mode,null,1,new string[0]);
   }
   if(mode!="custom")throw new McpException("小助手返回了未支持的循环类型。");
   string unit=unitValue as string;
   if(unit==null||!Units.ContainsKey(unit))throw new McpException("自定义循环需要公历日、周、月或年。");
   if(!(intervalValue is int)||(int)intervalValue<1||(int)intervalValue>366)
    throw new McpException("循环间隔应为 1 到 366 的整数。");
   var list=dateValues as IList;
   if(list==null||list.Count>366||((unit=="day")!=(list.Count==0)))
    throw new McpException("按天循环不指定日期；按周、月或年循环需要指定日期。");
   var selected=list.Cast<object>().Select(item=>item as string).ToArray();
   if(selected.Any(date=>!ValidDate(unit,date)))
    throw new McpException("循环日期无效。周日期使用 0–6，月日期使用 1–32，年日期使用 MM-DD。");
   return new AssistantRepeat(mode,unit,(int)intervalValue,selected.Distinct().ToArray());
  }

  static bool ValidDate(string unit,string value){
   if(string.IsNullOrEmpty(value))return false;
   if(unit=="year"){
    DateTime parsed;
    return DateTime.TryParseExact("2000-"+value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed);
   }
   int day;
   if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out day)||
      day.ToString(CultureInfo.InvariantCulture)!=value)return false;
   return unit=="week"?day>=0&&day<=6:day>=1&&day<=32;
  }

  public Dictionary<string,object> ToRule(){
   if(mode!="custom")return J.Obj("mode",mode);
   return J.Obj("mode",mode,"unit",unit,"interval",interval,"dates",dates);
  }

  public string Description{
   get{
    if(mode!="custom")return Presets[mode];
    string cycle="每 "+interval.ToString(CultureInfo.InvariantCulture)+" "+Units[unit];
    if(dates.Length==0)return cycle;
    IEnumerable<string> labels=dates;
    if(unit=="week")labels=dates.Select(v=>Weekdays[int.Parse(v,CultureInfo.InvariantCulture)]);
    if(unit=="month")labels=dates.Select(v=>v=="32"?"月末":v+" 日");
    return cycle+" · "+string.Join("、",labels);
   }
  }
 }
}