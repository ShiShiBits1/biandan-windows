using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace Biandan {
 internal static class WindowWorkArea {
  const int GetMinMaxInfo=0x24, WindowPosChanging=0x46;
  const int NoSizeOrMove=0x0001|0x0002;
  internal static void Attach(Window window){
   window.SourceInitialized+=delegate{
    var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
    if(source!=null)source.AddHook(UpdateBounds);
   };
  }

  [DllImport("user32.dll")]static extern bool IsZoomed(IntPtr window);

  static IntPtr UpdateBounds(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
   if(lParam==IntPtr.Zero)return IntPtr.Zero;
   if(message==GetMinMaxInfo){
    var screen=Screen.FromHandle(hwnd);
    var work=screen.WorkingArea;
    Marshal.WriteInt32(lParam,8,work.Width);
    Marshal.WriteInt32(lParam,12,work.Height);
    Marshal.WriteInt32(lParam,16,work.Left-screen.Bounds.Left);
    Marshal.WriteInt32(lParam,20,work.Top-screen.Bounds.Top);
   }else if(message==WindowPosChanging&&IsZoomed(hwnd)){
    int xOffset=IntPtr.Size*2;
    if((Marshal.ReadInt32(lParam,xOffset+16)&NoSizeOrMove)!=0)return IntPtr.Zero;
    var proposed=new Rectangle(Marshal.ReadInt32(lParam,xOffset),Marshal.ReadInt32(lParam,xOffset+4),
     Marshal.ReadInt32(lParam,xOffset+8),Marshal.ReadInt32(lParam,xOffset+12));
    var work=Screen.FromRectangle(proposed).WorkingArea;
    Marshal.WriteInt32(lParam,xOffset,work.Left);
    Marshal.WriteInt32(lParam,xOffset+4,work.Top);
    Marshal.WriteInt32(lParam,xOffset+8,work.Width);
    Marshal.WriteInt32(lParam,xOffset+12,work.Height);
   }
   return IntPtr.Zero;
  }
 }
}
