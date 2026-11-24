using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Biandan {
 internal static class AssistantSkill {
  internal const string ResourceName="Biandan.AssistantSkill.zip";
  static readonly Lazy<string> instructions=new Lazy<string>(ReadInstructions);
  internal static string Instructions { get { return instructions.Value; } }

  static string ReadInstructions(){
   using(var resource=typeof(AssistantSkill).Assembly.GetManifestResourceStream(ResourceName)){
    if(resource==null)throw new InvalidDataException("便单小助手缺少内置技能包。");
    using(var archive=new ZipArchive(resource,ZipArchiveMode.Read)){
     var text=new StringBuilder(ReadEntry(archive,"biandan-assistant/SKILL.md"));
     foreach(var tool in new[]{"CreateNewEvent","CreateRemindEvent","CreateNoTimeEvent","QueryMultiDatesEvents","StartPomodoro"}){
      string path="biandan-assistant/references/tools/cn.biandangroup.biandan__"+tool+".json";
      var reference=J.Parse(ReadEntry(archive,path));
      if(reference.S("toolName")!=tool||reference.D("arguments")==null)
       throw new InvalidDataException("小助手技能工具说明无效："+tool);
      text.AppendLine().AppendLine(path).AppendLine(reference.S("description"));
      text.AppendLine(J.Encode(reference.D("arguments")));
     }
     return text.ToString();
    }
   }
  }

  static string ReadEntry(ZipArchive archive,string path){
   var entry=archive.GetEntry(path);
   if(entry==null)throw new InvalidDataException("小助手技能包缺少文件："+path);
   using(var reader=new StreamReader(entry.Open(),new UTF8Encoding(false,true),true))
    return reader.ReadToEnd();
  }
 }
}
