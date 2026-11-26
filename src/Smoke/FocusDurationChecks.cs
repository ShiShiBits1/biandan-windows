using System;
using System.Globalization;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Linq;

namespace Biandan {
 public sealed partial class App {
  void VerifyTomatoSummary(){
   var original=Repo.Focus;
   try{
    Repo.Focus=new List<Dictionary<string,object>>{
     J.Obj("date",J.Date(DateTime.Today.AddDays(-1)),"minutes",25,"good_count",7),
     J.Obj("date",J.Today,"minutes",12.5,"good_count",0,"bad_count",2)
    };
    Desktop.Navigate("today");
    CheckFocusMetric("今日番茄数","0");
    Repo.Focus.Add(J.Obj("date",J.Today,"minutes",50,"good_count",2));
    Repo.Focus.Add(J.Obj("date",J.Today,"minutes",25,"good_count",1));
    Repo.Publish();
    CheckFocusMetric("今日番茄数","3");
    Desktop.Navigate("all");
    CheckFocusMetric("今日番茄数","3");
    Desktop.Navigate("focus");
    CheckFocusMetric("完成番茄","3");
    CheckFocusMetric("今日专注",FocusDuration.Format(87.5m)+" 分钟");
   }finally{
    Repo.Focus=original;
    Desktop.Navigate("today");
   }
  }

  void CheckFocusMetric(string label,string expected){
   Desktop.UpdateLayout();
   var caption=VisualControls<TextBlock>(Desktop).Single(text=>text.IsVisible&&text.Text==label);
   var fields=((StackPanel)caption.Parent).Children.OfType<TextBlock>();
   if(fields.Single(text=>text!=caption).Text!=expected)
    throw new InvalidOperationException("Unexpected metric value for "+label+"; expected "+expected);
  }

  static void VerifyFocusDurations(){
   var records=J.Parse("{\"items\":[{\"minutes\":12.5},{\"minutes\":1.5},{\"minutes\":25},{\"minutes\":\"2.125\"},{\"minutes\":null},{}]}").Rows("items");
   if(records.Sum(record=>FocusDuration.Minutes(record))!=41.125m||
      FocusDuration.Format(FocusDuration.Minutes(records[0]))!=12.5m.ToString(CultureInfo.CurrentCulture)||
      FocusDuration.Format(FocusDuration.Minutes(records[2]))!="25")
    throw new InvalidOperationException("Focus records lost fractional minutes during reading or formatting.");

   var shortFractions=new[]{J.Obj("minutes",1.004),J.Obj("minutes",1.004)};
   if(FocusDuration.Format(shortFractions.Sum(record=>FocusDuration.Minutes(record)))!=2.01m.ToString(CultureInfo.CurrentCulture))
    throw new InvalidOperationException("Focus duration totals must be rounded after summation.");
  }
 }
}
