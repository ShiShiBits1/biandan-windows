using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Biandan {
 public sealed partial class MainView {
  internal void CloseAccountDetails(){
   ClearAssistant();
   foreach(var window in Application.Current.Windows.OfType<Window>().Where(window=>window.Owner!=null).ToArray()){
    if(window.IsVisible){window.Hide();window.Close();}
   }
  }

  FrameworkElement BackupControls(){
   var panel=U.Stack();
   var buttons=new WrapPanel();
   buttons.Children.Add(U.Button("导出完整备份",()=>SaveBackup(false),"Secondary"));
   if(!repo.Demo){
    var export=U.Button("导出待同步队列",()=>SaveBackup(true),"Secondary");
    export.Margin=new Thickness(8,0,0,0);buttons.Children.Add(export);
    var restore=U.Button("恢复待同步队列",RestoreBackup,"Secondary");
    restore.Margin=new Thickness(8,0,0,0);buttons.Children.Add(restore);
   }
   panel.Children.Add(buttons);
   var help=U.Text(repo.Demo?"完整备份包含当前体验数据。":"完整备份包含云端缓存和本机待同步改动。重新授权后，恢复队列需要与原账号仍存在的云端记录核对。",11,"#7B6548");
   help.TextWrapping=TextWrapping.Wrap;help.Margin=new Thickness(0,8,0,0);panel.Children.Add(help);
   return panel;
  }

  string BackupFilename(bool queueOnly){
   string name=queueOnly?"便单待同步队列":"便单完整备份";
   var dialog=new SaveFileDialog{Title="导出"+name,Filter="JSON 备份|*.json",DefaultExt=".json",AddExtension=true,
    FileName=name+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"};
   return dialog.ShowDialog(this)==true?dialog.FileName:null;
  }

  async void SaveBackup(bool queueOnly){
   string path=BackupFilename(queueOnly);
   if(path==null)return;
   await app.RunAction(async()=>{
    await repo.ExportBackupAsync(path,queueOnly);
    ShowToast(queueOnly?"已导出待同步队列":"已导出完整备份");
   });
  }

  async void RestoreBackup(){
   var dialog=new OpenFileDialog{Title="从便单备份恢复待同步队列",Filter="JSON 备份|*.json",CheckFileExists=true};
   if(dialog.ShowDialog(this)!=true)return;
   if(!U.Confirm(this,"恢复待同步队列","将备份中的待处理改动合并到当前账号，跳过重复记录。改动可能包括删除待办，随后会按同步设置提交。","恢复队列"))return;
   await app.RunAction(async()=>{
    int added=await repo.RestoreQueueAsync(dialog.FileName);
    ShowToast(added==0?"没有可新增的改动，已跳过重复记录":"已恢复 "+added+" 项待同步改动");
   });
  }

  async void LeaveAccount(){
   string path=null;
   if(!repo.Demo){
    if(repo.PendingTaskCount>0){
     string message="本机还有 "+repo.PendingTaskCount+" 项待同步改动。选择保存位置后，便单会先导出队列再退出。";
     message+=repo.HasBackupAccountRecords?"也可以取消后点击「立即同步」。":"当前账号尚无可核对的云端记录。重新授权后只能查阅这份备份，无法自动恢复队列。如需自动恢复，请取消并先同步。";
     if(!U.Confirm(this,"退出前保存改动",message,"导出并退出"))return;
     path=BackupFilename(true);if(path==null)return;
    }else if(!U.Confirm(this,"退出便单账号","退出后将清除此客户端的本机授权与缓存，云端任务会保留。","退出"))return;
   }
   await app.RunAction(async()=>{
    if(!repo.Demo)await repo.LeaveAsync(path);
    foreach(var widget in app.Widgets.Values.ToArray())widget.Close();
    ClearAssistant();app.ShowLogin();
   });
  }
 }
}
