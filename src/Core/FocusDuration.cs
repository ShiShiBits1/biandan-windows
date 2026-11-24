using System;
using System.Collections.Generic;
using System.Globalization;

namespace Biandan {
 internal static class FocusDuration {
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
