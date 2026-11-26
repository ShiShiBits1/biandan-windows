using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Biandan {
 internal static class FocusDuration {
  internal static readonly int[] Presets={15,25,45,60,90};

  internal static IEnumerable<Dictionary<string,object>> Today(IEnumerable<Dictionary<string,object>> records){
   string date=J.Today;
   return records.Where(record=>record.S("date")==date);
  }

  internal static long Tomatoes(IEnumerable<Dictionary<string,object>> records){
   return records.Sum(record=>(long)record.I("good_count"));
  }

  internal static decimal Minutes(Dictionary<string,object> record){
   object value;
   if(!record.TryGetValue("minutes",out value)||value==null)return 0m;
   decimal minutes;
   string stored=Convert.ToString(value,CultureInfo.InvariantCulture);
   return decimal.TryParse(stored,NumberStyles.Float,CultureInfo.InvariantCulture,out minutes)&&minutes>=0m?minutes:0m;
  }

  internal static string Format(decimal minutes){
   return minutes.ToString("0.##",CultureInfo.CurrentCulture);
  }
 }
}
