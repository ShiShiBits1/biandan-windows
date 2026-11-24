using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Data;

namespace Biandan {
 internal static class Theme {
  public const string DefaultHex="#FFB639";
  public static readonly Tuple<string,string>[] Presets={
   Tuple.Create("便单黄",DefaultHex),Tuple.Create("雾蓝","#76B0E8"),
   Tuple.Create("薄荷绿","#7DC9A5"),Tuple.Create("丁香紫","#B6A1EB"),Tuple.Create("珊瑚橙","#F49B84")
  };
  static readonly string[] ShadeKeys={
   "#FFB639","#2E2418","#79684F","#8D560B","#C88A20","#432900","#362408","#513D24","#6E5638","#7B6548","#897859","#A89570","#FBE6B5","#F8EFDE","#F8EED9","#FFF9EE","#FFF3DB","#FFF0CD","#FFCC6D","#FFCE6D","#FFE7AF","#FFF1CF","#FFFBF3","#FFFDF7","#FFFDF8","#FFFCF5","#FFFCF6","#FFFFF7","#FFECC0","#FFE5AC","#FFF1D5","#FFF9EC","#FFF0CC","#FFF0CB","#FFF0CA","#FFF1D2","#FFF0D2","#FFF4D9","#FFF1E3","#FEEDD0","#F9DC9D","#FBF1DD","#FFF4E6","#FFF1E6","#FFFAF0","#FFE2A2","#FFD477","#FFF1CC","#FFF1D0","#FFFAED","#EFE1C8","#EDDFC5","#DCC699","#DCCBA9","#CE8B24","#DBB779","#E5D0AA","#EBC87C","#E8D3A6","#B87819","#B77516","#D28C1C","#F5E5C5","#7AFFD888","#00FFE8B8"
  };
  static readonly Color DefaultColor=Color.FromRgb(255,182,57);
  static readonly Dictionary<string,SolidColorBrush> Shades=new Dictionary<string,SolidColorBrush>(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string,ThemeTone> Values=new Dictionary<string,ThemeTone>(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string,Brush> FixedBrushes=new Dictionary<string,Brush>(StringComparer.OrdinalIgnoreCase);
  public static string Selected{get;private set;}

  public static bool TryHex(string text,out string hex){
   text=(text??"").Trim().TrimStart('#');
   uint value;
   if(text.Length!=6||!uint.TryParse(text,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out value)){
    hex=null;return false;
   }
   hex="#"+text.ToUpperInvariant();return true;
  }

  public static void Apply(ResourceDictionary resources,string requested){
   string hex;
   if(!TryHex(requested,out hex))hex=DefaultHex;
   if(hex==Selected&&Shades.Count!=0)return;
   Selected=hex;
   var accent=(Color)ColorConverter.ConvertFromString(hex);
   foreach(string key in ShadeKeys){
    var original=(Color)ColorConverter.ConvertFromString(key);
    var color=key==DefaultHex?accent:Tint(original,accent);
    SolidColorBrush brush;
    if(!Shades.TryGetValue(key,out brush)){
     var tone=new ThemeTone{Color=color};
     Values[key]=tone;
     brush=new SolidColorBrush();
     BindingOperations.SetBinding(brush,SolidColorBrush.ColorProperty,new Binding("Color"){Source=tone});
     Shades[key]=brush;
     resources["ThemeBrush"+key.Substring(1)]=brush;
    }else Values[key].Color=color;
    resources["ThemeColor"+key.Substring(1)]=color;
   }
   resources["AccentInk"]=new SolidColorBrush(Luminance(accent)<.177?Colors.White:Color.FromRgb(26,20,15));
  }

  internal static GradientStop ColorStop(string shade,double offset){
   var stop=new GradientStop{Offset=offset};
   BindingOperations.SetBinding(stop,GradientStop.ColorProperty,new Binding("Color"){Source=Brush(shade),Mode=BindingMode.OneWay});
   return stop;
  }

  public static Brush Brush(string value){
   SolidColorBrush shade;
   if(Shades.TryGetValue(value,out shade))return shade;
   Brush fixedBrush;
   if(FixedBrushes.TryGetValue(value,out fixedBrush))return fixedBrush;
   fixedBrush=(Brush)new BrushConverter().ConvertFromString(value);
   if(fixedBrush.CanFreeze)fixedBrush.Freeze();
   FixedBrushes[value]=fixedBrush;
   return fixedBrush;
  }

  sealed class ThemeTone:DependencyObject{
   public static readonly DependencyProperty ColorProperty=DependencyProperty.Register(
    "Color",typeof(Color),typeof(ThemeTone),new PropertyMetadata(Colors.Transparent));
   public Color Color{get{return (Color)GetValue(ColorProperty);}set{SetValue(ColorProperty,value);}}
  }

  static double Luminance(Color color){
   return .2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
  }
  static double Linear(byte channel){
   double v=channel/255.0;
   return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);
  }

  static Color Tint(Color source,Color accent){
   if(accent==DefaultColor)return source;
   double hue,saturation,light;
   ToHsl(source,out hue,out saturation,out light);
   double targetHue,targetSat,targetLight;
   ToHsl(accent,out targetHue,out targetSat,out targetLight);
   double sat=saturation*Math.Min(1,targetSat*1.3);
   double c=(1-Math.Abs(2*light-1))*sat;
   double h=targetHue/60,x=c*(1-Math.Abs(h%2-1)),m=light-c/2;
   double r=0,g=0,b=0;
   if(h<1){r=c;g=x;}
   else if(h<2){r=x;g=c;}
   else if(h<3){g=c;b=x;}
   else if(h<4){g=x;b=c;}
   else if(h<5){r=x;b=c;}
   else{r=c;b=x;}
   return Color.FromArgb(source.A,Channel(r+m),Channel(g+m),Channel(b+m));
  }

  static byte Channel(double value){return (byte)Math.Round(255*Math.Max(0,Math.Min(1,value)));}

  static void ToHsl(Color value,out double hue,out double saturation,out double light){
   double r=value.R/255.0,g=value.G/255.0,b=value.B/255.0;
   double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
   light=(max+min)/2;
   saturation=delta==0?0:delta/(1-Math.Abs(2*light-1));
   if(delta==0)hue=0;
   else if(max==r)hue=(g-b)/delta;
   else if(max==g)hue=2+(b-r)/delta;
   else hue=4+(r-g)/delta;
   hue=(hue*60+360)%360;
  }
 }
}