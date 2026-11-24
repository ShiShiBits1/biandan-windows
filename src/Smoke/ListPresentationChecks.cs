using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Biandan {
 public sealed partial class App {
  async Task VerifyListPresentationAsync(){
   var colors=Enumerable.Range(0,25).Select(MainView.ListColor).Distinct().Count();
   if(colors!=25||MainView.ListColor(-2)!=MainView.ListColor(0)||MainView.ListColor(25)!=MainView.ListColor(0))
    throw new InvalidOperationException("All 24 list colors must be distinct and unknown indices must use the default.");

   var sorted=Repository.OrderedLists(new[]{
    J.Obj("list_id","11","sort_index",3,"color",5),J.Obj("list_id","2","sort_index",3,"color",22),
    J.Obj("list_id",9,"sort_index",3,"color",5),J.Obj("list_id","25","sort_index",2,"color",22)
   });
   if(!sorted.Select(list=>list.S("list_id")).SequenceEqual(new[]{"25","9","11","2"}))
    throw new InvalidOperationException("Lists must sort by position, color, then numeric business ID.");

   string id=J.NewId();
   try{
    var list=await Repo.CallAsync("create_list",J.Obj("name","清单色验收","operation_id",id,"sort_index",73,"color",22));
    if(list.I("sort_index")!=73||Repo.Lists.Last().S("list_id")!=id)
     throw new InvalidOperationException("A new demo list must honor an explicit sort position.");
    // Simulate an older or future client with a color this client cannot display.
    list["color"]=86;
    Exception failure=null;
    var rename=Dispatcher.BeginInvoke(new Action(delegate{
     var dialog=Desktop.OwnedWindows.Cast<Window>().Single(w=>w.IsVisible&&w.Title=="编辑清单");
     try{
      dialog.UpdateLayout();
      var picker=VisualControls<ComboBox>(dialog).Single(box=>AutomationProperties.GetName(box)=="标记颜色");
      if(picker.Items.Count!=25||picker.SelectedIndex!=0)
       throw new InvalidOperationException("Unknown list colors must have a valid fallback in the editor.");
      VisualControls<TextBox>(dialog).Single(box=>AutomationProperties.GetName(box)=="清单名称").Text="清单改名保色";
      ClickBulkButton(dialog,"保存清单");
     }catch(Exception error){failure=error;}
     finally{if(dialog.IsVisible)dialog.Close();}
    }),DispatcherPriority.ApplicationIdle);
    ListEditor.Show(Desktop,Repo,list);
    await rename.Task;
    if(failure!=null)throw new InvalidOperationException("The list palette editor failed.",failure);
    if(list.I("color")!=86||list.I("sort_index")!=73||list.S("name")!="清单改名保色")
     throw new InvalidOperationException("A rename must preserve both the previous color and sort position.");
   }finally{
    Repo.Lists.RemoveAll(list=>list.S("list_id")==id);
    Repo.Persist();Repo.Publish();
   }
  }
 }
}
