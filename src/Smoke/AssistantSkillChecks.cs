using System;
namespace Biandan {
 public sealed partial class App {
  void VerifyBundledAssistantSkill(){
   var task=System.Linq.Enumerable.First(Repo.Items());
   var older=CodexAssistant.BuildRequest("查询",new[]{task},false);
   var shared=CodexAssistant.BuildRequest("查询",new[]{task},true);
   if(!older.Contains("\"tasks\":[]")||!shared.Contains("\"title\":"+J.Encode(task.Title)))
    throw new InvalidOperationException("Assistant task context must be opt-in.");

   var intent=J.Obj("answer","单击开始","tasks",new object[0],"query",null,
    "focus",J.Obj("title","阅读","minutes",25));
   Func<AssistantReply> parse=()=>CodexAssistant.Parse(J.Encode(J.Obj("type","item.completed",
    "item",J.Obj("type","agent_message","text",J.Encode(intent)))));
   if(parse().Focus.Minutes!=25)throw new InvalidOperationException("Focus intent lost its duration.");
   intent["focus"]=J.Obj("title","阅读","minutes",0);
   bool rejected=false;
   try{parse();}catch(InvalidOperationException){rejected=true;}
   if(!rejected)throw new InvalidOperationException("An invalid focus intent should not produce a start action.");
   var request=CodexAssistant.BuildRequest("检查内置技能",null,false);
   foreach(var marker in new[]{"name: biandan-assistant","CreateRemindEvent","CreateNoTimeEvent",
      "QueryMultiDatesEvents","StartPomodoro","当前 Windows 宿主","检查内置技能"}){
    if(!request.Contains(marker))throw new InvalidOperationException("Missing assistant instruction: "+marker);
   }
  }
 }
}
