using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace ContextControl.Workbench.Views;

internal static class GameEditorSyntax
{
    public static IHighlightingDefinition Create()
    {
        using var reader = XmlReader.Create(new StringReader("""
            <SyntaxDefinition name="Game HTML" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Comment" foreground="#8594AD"/>
              <Color name="String" foreground="#B3DB92"/>
              <Color name="Tag" foreground="#7DCFFF"/>
              <Color name="Keyword" foreground="#B9A3EF"/>
              <Color name="Number" foreground="#F2BB86"/>
              <RuleSet>
                <Span color="Comment" begin="&lt;!--" end="--&gt;" multiline="true"/>
                <Span color="Comment" begin="/\*" end="\*/" multiline="true"/>
                <Span color="String" begin="&quot;" end="&quot;"/>
                <Span color="String" begin="'" end="'"/>
                <Rule color="Tag">&lt;/?[A-Za-z][A-Za-z0-9-]*|/?&gt;</Rule>
                <Rule color="Number">\b[0-9]+(\.[0-9]+)?\b</Rule>
                <Keywords color="Keyword"><Word>function</Word><Word>const</Word><Word>let</Word><Word>var</Word><Word>if</Word><Word>else</Word><Word>return</Word><Word>for</Word><Word>while</Word><Word>new</Word><Word>true</Word><Word>false</Word><Word>null</Word><Word>class</Word></Keywords>
              </RuleSet>
            </SyntaxDefinition>
            """));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
