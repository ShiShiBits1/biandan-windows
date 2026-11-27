using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Biandan {
 internal static class TaskRemarkMarkdown {
  static readonly FontFamily CodeFont=new FontFamily("Consolas");
  static readonly Regex Heading=new Regex(@"^ *(#{1,6})(?:[ \t]+|$)(.*)$");
  static readonly Regex Fence=new Regex(@"^ *(`{3,}|~{3,})(.*)$");
  static readonly Regex Item=new Regex(@"^( *)([-+*]|[0-9]{1,9}[.)])[ \t]+(.*)$");
  static readonly Regex Quote=new Regex(@"^ *> ?(.*)$");
  static readonly Regex Link=new Regex(@"\G!?\[(?<label>(?:\\.|[^\[\]\\])*)\]\((?:<(?<url>[^<>\s]+)>|(?<url>(?:[^\s()\\]|\\[()]|\([^()\s]*\))+))(?:[ \t]+""[^""]*"")?\)",
   RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
  static readonly Regex Rule=new Regex(@"^(?:-{3,}|[\*]{3,}|_{3,})[ 	]*$");
  static readonly Regex IndentPrefix=new Regex(@"^<indent level=""([1-4])"">(.*)$");
  static readonly Regex FormatTag=new Regex(@"\G(?:<(?<name>[bius])>|<(?<name>span) data-color=""(?<color>[A-Za-z]+)"">|</(?<close>b|i|u|s|span)>)");
  static readonly Regex BareUrl=new Regex(@"\Ghttps?://[^\s<>\[\]""*]+",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
  static readonly string[] ColorNames={"red","orange","yellow","green","lakeBlue","lightBlue","darkBlue","purple"};

  public static FrameworkElement Render(string source){
   var doc=new FlowDocument {PagePadding=new Thickness(0),FontFamily=new FontFamily("Microsoft YaHei UI"),
    FontSize=13,Foreground=U.C("#513D24"),TextAlignment=TextAlignment.Left};
   var lines=(source??"").Replace("\r\n","\n").Replace('\r','\n').Split('\n');
   Paragraph prose=null;
   var fenceEnds=FindFenceEnds(lines);
   for(int i=0;i<lines.Length;i++){
    string line=lines[i];
    var prefix=IndentPrefix.Match(line);
    int explicitIndent=prefix.Success?int.Parse(prefix.Groups[1].Value)*12:0;
    if(prefix.Success)line=prefix.Groups[2].Value;
    int indent=IndentOf(line);
    int close;
    Match match;
    if(fenceEnds.TryGetValue(i,out close)){
     var code=new Paragraph(new Run(string.Join("\n",lines,i+1,close-i-1))){
      FontFamily=CodeFont,Background=U.C("#F8EFDE"),Foreground=U.C("#513D24"),
      Padding=new Thickness(9,6,9,6),Margin=new Thickness(indent,4,0,4)};
     doc.Blocks.Add(code);
     i=close;
    }else if(Rule.IsMatch(line)){
     doc.Blocks.Add(new Paragraph{
      BorderBrush=U.C("#C8AA7B"),BorderThickness=new Thickness(0,0,0,1),Margin=new Thickness(explicitIndent,6,0,6),FontSize=3
     });
    }else if((match=Heading.Match(line)).Success){
     var level=match.Groups[1].Length;
     var heading=new Paragraph {FontSize=level==1?22:level==2?19:level==3?17:15,FontWeight=FontWeights.SemiBold,
      Margin=new Thickness(indent,doc.Blocks.Count==0?0:5,0,4),Foreground=U.C("#2E2418")};
     AddInlines(heading.Inlines,Regex.Replace(match.Groups[2].Value,@"[ \t]+#+[ \t]*$",""),true);
     doc.Blocks.Add(heading);
    }else if((match=Quote.Match(line)).Success){
     var quote=new Paragraph {Margin=new Thickness(indent,3,0,3),Padding=new Thickness(10,2,0,2),
      BorderThickness=new Thickness(3,0,0,0),BorderBrush=U.C("#C88A20"),Foreground=U.C("#6E5638")};
     AddInlines(quote.Inlines,match.Groups[1].Value,true);
     while(i+1<lines.Length&&IndentOf(lines[i+1])==indent&&(match=Quote.Match(lines[i+1])).Success){
      i++;quote.Inlines.Add(new LineBreak());AddInlines(quote.Inlines,match.Groups[1].Value,true);
     }
     doc.Blocks.Add(quote);
    }else if((match=Item.Match(line)).Success){
     doc.Blocks.Add(ReadList(lines,ref i,match));
    }else{
     if(prose==null||prose.Margin.Left!=explicitIndent){
      prose=new Paragraph {Margin=new Thickness(explicitIndent,0,0,0)};
      doc.Blocks.Add(prose);
     }
     else prose.Inlines.Add(new LineBreak());
     AddInlines(prose.Inlines,line,true);
     continue;
    }
    prose=null;
   }
   return new RichTextBox(doc){IsReadOnly=true,IsDocumentEnabled=true,IsUndoEnabled=false,
    Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),
    VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
  }

  static int IndentOf(string line){
   int spaces=0;
   while(spaces<8&&spaces<line.Length&&line[spaces]==' ')spaces++;
   return spaces/2*12;
  }

  static Dictionary<int,int> FindFenceEnds(string[] lines){
   var ends=new Dictionary<int,int>();
   var nearest=new Dictionary<string,int>(StringComparer.Ordinal);
   for(int i=lines.Length-1;i>=0;i--){
    var fence=Fence.Match(lines[i]);
    if(!fence.Success)continue;
    string marker=fence.Groups[1].Value, suffix=fence.Groups[2].Value;
    int close;
    if((marker[0]!='`'||suffix.IndexOf('`')<0)&&nearest.TryGetValue(marker,out close))ends[i]=close;
    if(string.IsNullOrWhiteSpace(suffix))nearest[marker]=i;
   }
   return ends;
  }

  static List ReadList(string[] lines,ref int index,Match first){
   // A new run of ordered items starts at one, regardless of the source numbering.
   string marker=first.Groups[2].Value;
   bool numbered=char.IsDigit(marker[0]);
   int indent=IndentOf(first.Value);
   var list=new List {MarkerStyle=numbered?TextMarkerStyle.Decimal:TextMarkerStyle.Disc,
    Padding=new Thickness(24,0,0,0),Margin=new Thickness(indent,2,0,2)};
   Match current=first;
   while(true){
    var paragraph=new Paragraph {Margin=new Thickness(0,1,0,1)};
    string text=current.Groups[3].Value;
    bool done=text.StartsWith("[x] ",StringComparison.OrdinalIgnoreCase)||text.Equals("[x]",StringComparison.OrdinalIgnoreCase);
    if(done||text.StartsWith("[ ] ",StringComparison.Ordinal)||text=="[ ]"){
     paragraph.Inlines.Add(new Run(done?"☑ ":"☐ "){Foreground=U.C("#8D560B")});
     text=text.Length==3?"":text.Substring(4);
    }
    AddInlines(paragraph.Inlines,text,true);
    list.ListItems.Add(new ListItem(paragraph));
    if(index+1>=lines.Length)break;
    current=Item.Match(lines[index+1]);
    if(!current.Success||IndentOf(current.Value)!=indent||char.IsDigit(current.Groups[2].Value[0])!=numbered)break;
    index++;
   }
   return list;
  }

  sealed class StyleFrame {
   public string Marker;
   public string Tag;
   public Span Span;
   public InlineCollection Parent;
  }

  static void AddInlines(InlineCollection root,string text,bool allowLinks){
   if(text.Length==0)return;
   var frames=new Stack<StyleFrame>();
   InlineCollection output=root;
   var plain=new StringBuilder();
   var codeEnds=CodePairs(text);
   for(int i=0;i<text.Length;){
    char ch=text[i];
    if(ch=='\\'&&i+1<text.Length&&IsPunctuation(text[i+1])){plain.Append(text[i+1]);i+=2;continue;}
    if(ch=='`'){
     int stop=i+1;while(stop<text.Length&&text[stop]==ch)stop++;
     int closing;
     if(codeEnds.TryGetValue(i,out closing)){
      Flush(output,plain);
      string value=text.Substring(stop,closing-stop);
      if(value.Length>=2&&value[0]==' '&&value[value.Length-1]==' '&&value.Trim(' ').Length>0)
       value=value.Substring(1,value.Length-2);
      output.Add(new Run(value){FontFamily=CodeFont,Background=U.C("#F8EFDE")});
      i=closing+stop-i;
     }else{plain.Append(text,i,stop-i);i=stop;}
     continue;
    }
    if(allowLinks&&(ch=='['||(ch=='!'&&i+1<text.Length&&text[i+1]=='['))){
     Match link;
     try{link=Link.Match(text,i);}
     catch(RegexMatchTimeoutException){plain.Append(text,i,text.Length-i);break;}
     if(link.Success){
      Uri address;
      var value=link.Groups["url"].Value.Replace(@"\(","(").Replace(@"\)",")");
      if(Uri.TryCreate(value,UriKind.Absolute,out address)&&
         (address.Scheme==Uri.UriSchemeHttps||address.Scheme==Uri.UriSchemeHttp)&&address.Host.Length>0){
       Flush(output,plain);
       var element=CreateWebLink(address);
       string label=link.Groups["label"].Value;
       if(ch=='!')element.Inlines.Add(new Run(label.Length>0?"图片 · ":"图片"));
       if(label.Length>0)AddInlines(element.Inlines,label,false);
       else if(ch!='!')element.Inlines.Add(new Run(address.AbsoluteUri));
       output.Add(element);
      }else plain.Append(link.Value);
      i+=link.Length;continue;
     }
    }
    if(ch=='<'&&ReadFormatTag(text,ref i,frames,ref output,plain))continue;
    if(allowLinks&&(ch=='h'||ch=='H')&&(i==0||!char.IsLetterOrDigit(text[i-1]))){
     var url=BareUrl.Match(text,i);
     if(url.Success){
      string value=url.Value.TrimEnd('.',',','!','?',';',':','。','，','！','？','；','：','、','）','》');
      int excess=value.Count(c=>c==')')-value.Count(c=>c=='(');
      int length=value.Length;
      while(excess>0&&length>0&&value[length-1]==')'){length--;excess--;}
      if(length!=value.Length)value=value.Substring(0,length);
      Uri address;
      if(Uri.TryCreate(value,UriKind.Absolute,out address)&&address.Host.Length>0){
       Flush(output,plain);
       var element=CreateWebLink(address);
       element.Inlines.Add(new Run(value));
       output.Add(element);
       i+=value.Length;
       continue;
      }
     }
    }
    if(ch=='*'||ch=='_'||ch=='~'){
     int end=i+1;while(end<text.Length&&text[end]==ch)end++;
     int count=end-i;
     bool betweenLetters=i>0&&end<text.Length&&char.IsLetterOrDigit(text[i-1])&&char.IsLetterOrDigit(text[end]);
     if((ch=='~'&&count!=2)||(ch=='_'&&betweenLetters)){
      plain.Append(text,i,count);i=end;continue;
     }
     bool closes=i>0&&!char.IsWhiteSpace(text[i-1]);
     while(closes&&count>0&&frames.Count>0&&frames.Peek().Marker[0]==ch&&frames.Peek().Marker.Length<=count){
      Flush(output,plain);
      var frame=frames.Pop();count-=frame.Marker.Length;
      output=frame.Parent;output.Add(frame.Span);
     }
     bool opens=end<text.Length&&!char.IsWhiteSpace(text[end]);
     while(opens&&count>0&&frames.Count<16){
      Flush(output,plain);
      int width=Math.Min(2,count);
      Span span=ch=='~'?(Span)new Span {TextDecorations=TextDecorations.Strikethrough}:width==2?(Span)new Bold():new Italic();
      frames.Push(new StyleFrame{Marker=new string(ch,width),Span=span,Parent=output});
      output=span.Inlines;count-=width;
     }
     plain.Append(ch,count);i=end;continue;
    }
    plain.Append(ch);i++;
   }
   Flush(output,plain);
   while(frames.Count>0){
    var frame=frames.Pop();
    frame.Parent.Add(new Run(frame.Marker));
    while(frame.Span.Inlines.FirstInline!=null){
     var child=frame.Span.Inlines.FirstInline;
     frame.Span.Inlines.Remove(child);frame.Parent.Add(child);
    }
   }
  }

  static bool ReadFormatTag(string text,ref int index,Stack<StyleFrame> frames,ref InlineCollection output,StringBuilder plain){
   var match=FormatTag.Match(text,index);
   if(!match.Success)return false;
   string close=match.Groups["close"].Value;
   if(close!=""){
    if(frames.Count==0||frames.Peek().Tag!=close)return false;
    Flush(output,plain);
    var frame=frames.Pop();
    output=frame.Parent;
    output.Add(frame.Span);
   }else{
    if(frames.Count>=16)return false;
    string tag=match.Groups["name"].Value;
    Span span;
    switch(tag){
     case "b":span=new Bold();break;
     case "i":span=new Italic();break;
     case "u":span=new Underline();break;
     case "s":span=new Span{TextDecorations=TextDecorations.Strikethrough};break;
     default:
      int color=Array.IndexOf(ColorNames,match.Groups["color"].Value);
      if(color<0)return false;
      span=new Span{Foreground=U.C(ListPalette.Hex(color+1))};
      break;
    }
    Flush(output,plain);
    frames.Push(new StyleFrame{Marker=match.Value,Tag=tag,Span=span,Parent=output});
    output=span.Inlines;
   }
   index+=match.Length;
   return true;
  }

  static Hyperlink CreateWebLink(Uri address){
   var link=new Hyperlink {NavigateUri=address,ToolTip=address.AbsoluteUri,Foreground=U.C("#8D560B")};
   link.RequestNavigate+=delegate(object sender,System.Windows.Navigation.RequestNavigateEventArgs e){
    e.Handled=true;
    try{using(Process.Start(new ProcessStartInfo(address.AbsoluteUri){UseShellExecute=true})){}}
    catch(Win32Exception error){U.Error(Window.GetWindow(link),error);}
    catch(InvalidOperationException error){U.Error(Window.GetWindow(link),error);}
   };
   return link;
  }

  static Dictionary<int,int> CodePairs(string text){
   var result=new Dictionary<int,int>();
   var lastByWidth=new Dictionary<int,int>();
   for(int i=text.IndexOf('`');i>=0;){
    int end=i+1;while(end<text.Length&&text[end]=='`')end++;
    int width=end-i,previous;
    if(lastByWidth.TryGetValue(width,out previous))result[previous]=i;
    lastByWidth[width]=i;
    i=text.IndexOf('`',end);
   }
   return result;
  }

  static bool IsPunctuation(char c){return c>='!'&&c<='/'||c>=':'&&c<='@'||c>='['&&c<='`'||c>='{'&&c<='~';}
  static void Flush(InlineCollection output,StringBuilder plain){
   if(plain.Length>0){output.Add(new Run(plain.ToString()));plain.Clear();}
  }
 }
}
