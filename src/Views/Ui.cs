using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
namespace Biandan {
 public static class U {
  public static Brush C(string hex){return Theme.Brush(hex);}
  public static TextBlock Text(string text,double size=13,string color="#2E2418",bool bold=false){return new TextBlock{Text=text,FontSize=size,Foreground=C(color),FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextTrimming=TextTrimming.CharacterEllipsis};}
  public static TextBlock Icon(string code,double size=16,string color="#79684F"){var t=Text(code,size,color);t.FontFamily=new FontFamily("Segoe MDL2 Assets");return t;}
  public static StackPanel Stack(bool horizontal=false){return new StackPanel{Orientation=horizontal?Orientation.Horizontal:Orientation.Vertical};}
  public static Grid Grid(string columns,string rows=null){
   var g=new Grid();foreach(var s in columns.Split(','))g.ColumnDefinitions.Add(new ColumnDefinition{Width=Length(s)});
   if(rows!=null)foreach(var s in rows.Split(','))g.RowDefinitions.Add(new RowDefinition{Height=Length(s)});return g;
  }
  static GridLength Length(string s){return s=="Auto"?GridLength.Auto:s.EndsWith("*")?new GridLength(s=="*"?1:double.Parse(s.TrimEnd('*')),GridUnitType.Star):new GridLength(double.Parse(s));}
  public static T Place<T>(Grid g,T item,int col=0,int row=0,int colSpan=1,int rowSpan=1)where T:UIElement{System.Windows.Controls.Grid.SetColumn(item,col);System.Windows.Controls.Grid.SetRow(item,row);System.Windows.Controls.Grid.SetColumnSpan(item,colSpan);System.Windows.Controls.Grid.SetRowSpan(item,rowSpan);g.Children.Add(item);return item;}
  public static Button Button(string text,Action action,string style=null){var b=new Button{Content=text};if(style!=null)b.Style=(Style)Application.Current.FindResource(style);if(action!=null)b.Click+=delegate{action();};Motion.EnablePress(b);return b;}
  public static Button IconButton(string code,string tooltip,Action action){var b=Button("",action,"IconButton");b.Content=Icon(code);b.ToolTip=tooltip;return b;}
  public static Border Card(UIElement child,string background="#FFFFFF",double radius=16,Thickness? padding=null){
   return ConfigureCard(new Border(),child,background,radius,padding);
  }
  internal static Border TaskCard(UIElement child,string background,double radius,Thickness padding){
   return ConfigureCard(new TaskCardSurface(),child,background,radius,padding);
  }
  static Border ConfigureCard(Border card,UIElement child,string background,double radius,Thickness? padding){
   card.Child=child;card.Background=C(background);card.CornerRadius=new CornerRadius(radius);
   card.Padding=padding??new Thickness(20);card.BorderBrush=C("#EFE1C8");card.BorderThickness=new Thickness(1);
   return card;
  }
  public static Border Pill(string text,string color="#8D560B",string bg="#FFF0D2"){return Card(Text(text,11,color),bg,6,new Thickness(8,4,8,4));}
  public static void Gap(Panel panel,double height=12){panel.Children.Add(new Border{Height=height});}
  public static FrameworkElement Field(string label,UIElement control){var s=Stack();s.Children.Add(Text(label,12,"#79684F"));Gap(s,7);s.Children.Add(control);s.Margin=new Thickness(0,0,0,15);return s;}
  public static TextBox Input(string value="",bool multiline=false){return new TextBox{Text=value,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,MinHeight=multiline?78:38,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden};}
  static readonly BitmapImage logo=LoadLogo();
  static BitmapImage LoadLogo(){
   using(var stream=BundledBackend.OpenResource("Biandan.App.png")){
    var image=new BitmapImage();
    image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();
    return image;
   }
  }
  public static Image Logo(double size=38){return new Image{Source=logo,Width=size,Height=size};}
  public static void Configure(Window w,double width,double height,double cornerRadius=18){
   w.Width=width;w.Height=height;w.WindowStartupLocation=WindowStartupLocation.CenterScreen;w.WindowStyle=WindowStyle.None;w.AllowsTransparency=true;w.Background=Brushes.Transparent;
   w.Icon=logo;
   WindowChrome.SetWindowChrome(w,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(cornerRadius)});
  }
  public static FrameworkElement TitleBar(Window w,string title,bool minimize=true){
   var g=Grid("*,Auto");g.Height=42;g.Background=Brushes.Transparent;g.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Left&&e.OriginalSource is TextBlock){try{w.DragMove();}catch(InvalidOperationException){}}};
   var label=Text(title,12,"#79684F");label.Margin=new Thickness(18,0,0,0);Place(g,label);
   var buttons=Stack(true);if(minimize){buttons.Children.Add(IconButton("\uE921","最小化",()=>w.WindowState=WindowState.Minimized));buttons.Children.Add(IconButton("\uE922","最大化 / 还原",()=>w.WindowState=w.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized));}
   buttons.Children.Add(IconButton("\uE8BB","关闭",()=>w.Close()));buttons.Margin=new Thickness(0,0,9,0);Place(g,buttons,1);return g;
  }
  public static Window Dialog(Window owner,string title,double width=500,double height=620){
   var w=new Window{Title=title,Owner=owner,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};Configure(w,width,height);w.WindowStartupLocation=WindowStartupLocation.CenterOwner;return w;
  }
  public static void DialogContent(Window w,string title,UIElement content,UIElement footer=null,bool scrollContent=true){
   var g=Grid("*","42,*,Auto");
   Place(g,TitleBar(w,title,false));
   FrameworkElement pane=scrollContent?(FrameworkElement)new ScrollViewer{Content=content}:new Border{Child=content};
   pane.Margin=new Thickness(22,12,22,14);
   Place(g,pane,0,1);
   if(footer!=null){
    var actions=new Border{Child=footer,BorderBrush=C("#EFE1C8"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(20,12,20,15)};
    Place(g,actions,0,2);
   }
   bool opening=w.Content==null;
   var layers=new Grid();
   layers.Children.Add(new Control{Style=(Style)w.FindResource("FrostedBackdrop")});
   var wash=new Border{Child=g,CornerRadius=new CornerRadius(18)};
   Motion.LightSurface(wash,"#FFF0D2","#FFFDF8",new Point(.72,.1));
   wash.Background.Opacity=.22;
   layers.Children.Add(wash);
   var dialog=new Border{Child=layers,CornerRadius=new CornerRadius(18),Margin=new Thickness(4,4,7,8)};
   w.Content=dialog;
   if(opening){
    Motion.EnterWhenLoaded(dialog);
    w.KeyDown+=delegate(object sender,KeyEventArgs e){
     if(e.Key!=Key.Escape||Keyboard.Modifiers!=ModifierKeys.None)return;
     e.Handled=true;
     if(!CloseOpenPicker(w))w.Close();
    };
   }
  }
  static bool CloseOpenPicker(DependencyObject parent){
   var options=parent as ComboBox;
   if(options!=null&&options.IsDropDownOpen){options.IsDropDownOpen=false;return true;}
   var date=parent as DatePicker;
   if(date!=null&&date.IsDropDownOpen){date.IsDropDownOpen=false;return true;}
   foreach(var child in LogicalTreeHelper.GetChildren(parent)){
    var element=child as DependencyObject;
    if(element!=null&&CloseOpenPicker(element))return true;
   }
   return false;
  }
  public static bool Confirm(Window owner,string title,string message,string accept="确认"){
   var w=Dialog(owner,title,440,280);var body=Stack();body.Children.Add(Text(title,21,"#2E2418",true));Gap(body,14);var text=Text(message,13,"#6E5638");text.TextWrapping=TextWrapping.Wrap;body.Children.Add(text);
   var footer=Stack(true);footer.HorizontalAlignment=HorizontalAlignment.Right;footer.Children.Add(Button("取消",()=>w.DialogResult=false));var yes=Button(accept,()=>w.DialogResult=true,"Primary");yes.Margin=new Thickness(10,0,0,0);footer.Children.Add(yes);DialogContent(w,title,body,footer);return w.ShowDialog()==true;
  }
  public static void Error(Window owner,Exception error){string text=Friendly(error.Message);MessageBox.Show(owner,text,"便单",MessageBoxButton.OK,MessageBoxImage.Information);}
  public static string Friendly(string message){
   if(message.IndexOf("Complete Huawei",StringComparison.OrdinalIgnoreCase)>=0)return "请在浏览器完成华为账号授权，再点击检查登录。";
   if(message.IndexOf("Timed out waiting",StringComparison.OrdinalIgnoreCase)>=0)return "本次授权等待超时，请点击重新登录。";
   if(message.IndexOf("renewal service",StringComparison.OrdinalIgnoreCase)>=0)return "暂时无法连接账号续期服务，请检查网络或代理后重试。";
   if(message.IndexOf("OAuth callback",StringComparison.OrdinalIgnoreCase)>=0)return "无法接收登录回调，请关闭其他便单授权窗口后重试。";
   if(message.IndexOf("reach the service",StringComparison.OrdinalIgnoreCase)>=0||message.IndexOf("HTTP 5",StringComparison.OrdinalIgnoreCase)>=0)return "暂时无法连接便单云端，请检查网络或代理后重试。";
   return message;
  }
  public static void Capture(FrameworkElement element,string path){element.UpdateLayout();var bmp=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(element);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(path))png.Save(f);}
 }
}
