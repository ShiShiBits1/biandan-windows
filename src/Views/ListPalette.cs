using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Biandan {
 internal static class ListPalette {
  // Row number is the color index shared with the other Biandan clients.
  static readonly string[,] Colors={
   {"默认","#927B54"},
   {"红色","#D45251"},{"橙色","#DA7F36"},{"黄色","#B79929"},{"绿色","#519353"},
   {"湖蓝","#2E9992"},{"浅蓝","#559AC6"},{"深蓝","#476CB3"},{"紫色","#9562B7"},
   {"柔红","#EBA6A4"},{"柔橙","#E7B48A"},{"柔黄","#DCC987"},{"柔绿","#A3C69A"},
   {"柔湖蓝","#91C8BD"},{"柔浅蓝","#9FC9DF"},{"柔深蓝","#A2B3D6"},{"柔紫","#C5A7D8"},
   {"莫兰迪红","#B67F7C"},{"莫兰迪橙","#BB9779"},{"莫兰迪黄","#B1A371"},{"莫兰迪绿","#83997C"},
   {"莫兰迪湖蓝","#7B9F96"},{"莫兰迪浅蓝","#89A2B1"},{"莫兰迪深蓝","#75869E"},{"莫兰迪紫","#A18BA9"}
  };

  internal static int IndexOrDefault(int index){return index>=0&&index<Colors.GetLength(0)?index:0;}
  internal static string Hex(int index){return Colors[IndexOrDefault(index),1];}

  internal static ComboBox Picker(int initialColor){
   var picker=new ComboBox();
   AutomationProperties.SetName(picker,"标记颜色");
   for(int index=0;index<Colors.GetLength(0);index++){
    string label=Colors[index,0];
    var row=U.Stack(true);
    row.Children.Add(new Ellipse{Width=11,Height=11,Fill=U.C(Hex(index)),Margin=new Thickness(0,0,8,0)});
    row.Children.Add(new TextBlock{Text=label});
    var choice=new ComboBoxItem{Content=row};
    TextSearch.SetText(choice,label);
    AutomationProperties.SetName(choice,label);
    picker.Items.Add(choice);
   }
   picker.SelectedIndex=IndexOrDefault(initialColor);
   return picker;
  }
 }
}
