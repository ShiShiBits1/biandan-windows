using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Biandan {
 internal sealed class BackendJob:IDisposable {
  const uint KillOnClose=0x00002000;
  const int ExtendedLimitInformation=9;
  readonly SafeFileHandle handle;

  public BackendJob(){
   handle=CreateJobObject(IntPtr.Zero,null);
   if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
   var settings=new JobExtendedLimits();
   settings.Basic.LimitFlags=KillOnClose;
   if(!SetInformationJobObject(handle,ExtendedLimitInformation,ref settings,(uint)Marshal.SizeOf(settings))){
    int error=Marshal.GetLastWin32Error();
    handle.Dispose();
    throw new Win32Exception(error);
   }
  }

  public void Add(Process process){
   if(!AssignProcessToJobObject(handle,process.Handle))
    throw new Win32Exception(Marshal.GetLastWin32Error());
  }

  public void Dispose(){handle.Dispose();}

  [StructLayout(LayoutKind.Sequential)]
  struct JobBasicLimits {
   public long PerProcessUserTimeLimit,PerJobUserTimeLimit;
   public uint LimitFlags;
   public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;
   public uint ActiveProcessLimit;
   public UIntPtr Affinity;
   public uint PriorityClass,SchedulingClass;
  }

  [StructLayout(LayoutKind.Sequential)]
  struct IoCounters {
   public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount;
   public ulong ReadTransferCount,WriteTransferCount,OtherTransferCount;
  }

  [StructLayout(LayoutKind.Sequential)]
  struct JobExtendedLimits {
   public JobBasicLimits Basic;
   public IoCounters Io;
   public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;
  }

  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  static extern SafeFileHandle CreateJobObject(IntPtr attributes,string name);

  [DllImport("kernel32.dll",SetLastError=true)]
  static extern bool SetInformationJobObject(SafeFileHandle job,int infoClass,ref JobExtendedLimits info,uint size);

  [DllImport("kernel32.dll",SetLastError=true)]
  static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
 }
}