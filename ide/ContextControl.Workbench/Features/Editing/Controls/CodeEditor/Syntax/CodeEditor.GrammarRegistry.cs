using System.Globalization;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace ContextControl.Workbench.Controls;

public sealed partial class CodeEditor
{
    private static IGrammar? GetGrammarForExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        lock (GrammarLock)
        {
            if (SharedGrammarByExtension.TryGetValue(extension, out var cached))
            {
                return cached;
            }

            IGrammar? grammar = null;
            try
            {
                var scopeName = SharedRegistryOptions.GetScopeByExtension(extension);
                if (!string.IsNullOrWhiteSpace(scopeName))
                {
                    grammar = SharedRegistry.LoadGrammar(scopeName);
                }
            }
            catch
            {
                grammar = null;
            }

            SharedGrammarByExtension[extension] = grammar;
            return grammar;
        }
    }
}
