using System;
using System.Globalization;
using System.Linq;

namespace Biandan {
 public sealed partial class App {
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
