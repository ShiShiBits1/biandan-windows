using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Web.Script.Serialization;
namespace Biandan {
 public static class J {
  public static string Encode(object v) { return new JavaScriptSerializer { MaxJsonLength=32*1024*1024, RecursionLimit=100 }.Serialize(v); }
  public static Dictionary<string,object> Parse(string v) { return new JavaScriptSerializer { MaxJsonLength=32*1024*1024, RecursionLimit=100 }.Deserialize<Dictionary<string,object>>(v); }
  public static Dictionary<string,object> Obj(params object[] pairs) { var d=new Dictionary<string,object>(); for(int i=0;i<pairs.Length;i+=2)d[(string)pairs[i]]=pairs[i+1]; return d; }
  public static string S(this IDictionary<string,object> d,string k,string fallback="") { object v; return d!=null&&d.TryGetValue(k,out v)&&v!=null?Convert.ToString(v):fallback; }
  public static bool B(this IDictionary<string,object> d,string k) { object v; return d!=null&&d.TryGetValue(k,out v)&&v!=null&&Convert.ToBoolean(v); }
  public static int I(this IDictionary<string,object> d,string k,int fallback=0) { int n;return int.TryParse(d.S(k),out n)?n:fallback; }
  public static Dictionary<string,object> D(this IDictionary<string,object> d,string k) { object v;return d!=null&&d.TryGetValue(k,out v)?v as Dictionary<string,object>:null; }
  public static List<Dictionary<string,object>> Rows(this IDictionary<string,object> d,string k) { object v;if(d==null||!d.TryGetValue(k,out v)||!(v is IEnumerable))return new List<Dictionary<string,object>>();return ((IEnumerable)v).Cast<object>().OfType<Dictionary<string,object>>().ToList(); }
  public static List<string> Strings(this IDictionary<string,object> d,string k) { object v;if(d==null||!d.TryGetValue(k,out v)||!(v is IEnumerable)||v is string)return new List<string>();return ((IEnumerable)v).Cast<object>().Select(Convert.ToString).ToList(); }
  public static Dictionary<string,object> Clone(Dictionary<string,object> d) {return Parse(Encode(d));}
  public static string Date(DateTime d){return d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
  public static string Today {get{return Date(DateTime.Today);}}
  static long lastId;
  public static string NewId(){
   long previous, next;
   do{
    previous=Interlocked.Read(ref lastId);
    long now=(DateTime.UtcNow.Ticks-621355968000000000L)/10;
    next=Math.Max(now,previous+1);
   }while(Interlocked.CompareExchange(ref lastId,next,previous)!=previous);
   return next.ToString(CultureInfo.InvariantCulture);
  }
 }
 public sealed class TaskItem {
  public Dictionary<string,object> Task,Occurrence;
  public bool Syncing;
  public string Id{get{return Task.S("task_id");}} public string OccurrenceId{get{return Occurrence.S("occurrence_id");}}
  public string Title{get{return Task.S("title");}} public string ListId{get{return Task.S("list_id","1");}}
  public string Start{get{return Occurrence.S("start_date",Task.S("start_date"));}}
  public string End{get{return Occurrence.S("end_date",Task.S("end_date",Start));}}
  public string Time{get{return Occurrence.S("start_time",Task.S("start_time"));}}
  public bool Completed{get{return Occurrence.S("status")=="completed";}} public bool Abandoned{get{return Occurrence.S("status")=="abandoned";}}
  public bool Undated{get{return Task.B("undated");}} public bool Repeating{get{return Task.I("repeat_type")!=0;}}
  public bool OnDate(string date){return !Undated&&string.Compare(Start,date,StringComparison.Ordinal)<=0&&string.Compare(End,date,StringComparison.Ordinal)>=0;}
  public bool Overdue{get{return !Completed&&!Abandoned&&!Undated&&string.Compare(End,J.Today,StringComparison.Ordinal)<0;}}
 }
}
