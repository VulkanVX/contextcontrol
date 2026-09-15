using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextControl.Workbench;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;
using ContextControl.Workbench.Views.MainWindowParts;

internal static class ProjectGraphExperienceTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(string output, bool rendererOnly = false, bool baseline = false)
    {
        Directory.CreateDirectory(output);
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont().SetupWithoutStarting();
        var results = new List<object>();
        if (!rendererOnly) Panels(output);
        foreach (var theme in new[] { "studio", "empty" })
        foreach (var mode in new[] { "graph", "cube" })
        {
            var graph = new ProjectGraphRenderControl { Items = [Fixture()], ThemeKey = theme, LayoutMode = mode };
            var window = new Window { Width = 1360, Height = 820, Content = graph };
            WorkbenchThemeResources.Apply(window, theme);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            using var bitmap = new RenderTargetBitmap(new PixelSize(1360, 820), new Vector(96, 96));
            bitmap.Render(graph); bitmap.Save(Path.Combine(output, $"{theme}-{mode}.png"));
            File.WriteAllText(Path.Combine(output, $"{theme}-{mode}.json"), graph.ExportGraphText("json"));
            File.WriteAllText(Path.Combine(output, $"{theme}-{mode}.svg"), graph.ExportGraphText("svg"));
            var original = graph.ExportGraphText("json");
            graph.SelectedNode = graph.Items[0].Children.First(n => n.IsFolder && n.Children.Count > 0);
            typeof(ProjectGraphRenderControl).GetField("_zoom", Private)!.SetValue(graph, 1.7d);
            graph.CenterSelectedNode(); bitmap.Render(graph); bitmap.Save(Path.Combine(output, $"{theme}-{mode}-selected.png"));
            if (graph.ExportGraphText("json") != original) throw new InvalidOperationException("Selection and zoom must preserve graph structure and metadata.");
            graph.GenerationPalette = "#000000,#FFFFFF,#FF0000,#FFFF00,#0000FF";
            bitmap.Render(graph); bitmap.Save(Path.Combine(output, $"{theme}-{mode}-custom.png"));
            if (!baseline) VerifyContrast(graph);
            var liveNode = graph.Items[0].Children.First(n => n.IsFolder && n.Children.Any(c => c.IsFile)).Children.First(n => n.IsFile);
            liveNode.UpdateVersionAndLoc("v99", 12345);
            bitmap.Render(graph);
            var graphNodes = ((System.Collections.IEnumerable)typeof(ProjectGraphRenderControl).GetField("_nodes", Private)!.GetValue(graph)!).Cast<object>();
            var cachedNode = graphNodes.Single(n => ReferenceEquals(n.GetType().GetProperty("Node")!.GetValue(n), liveNode));
            if (!baseline && !cachedNode.GetType().GetProperty("Meta")!.GetValue(cachedNode)!.ToString()!.Contains("v99")) throw new InvalidOperationException("Cached metrics must follow live version/LOC updates.");
            graph.GenerationPalette = "invalid,,";
            bitmap.Render(graph); // Invalid saved colors must fall back safely.
            window.Close();
        }
        foreach (var count in new[] { 120, 3000 })
        {
            var graph = new ProjectGraphRenderControl { Items = [Fixture(count)], ThemeKey = "studio" };
            var window = new Window { Width = 1360, Height = 820, Content = graph };
            WorkbenchThemeResources.Apply(window, "studio"); window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            using var bitmap = new RenderTargetBitmap(new PixelSize(1360, 820), new Vector(96, 96));
            bitmap.Render(graph);
            // Matched zoom exposes many labels on the stress fixture. GPU/presentation latency is excluded.
            typeof(ProjectGraphRenderControl).GetField("_zoom", Private)!.SetValue(graph, .65d);
            typeof(ProjectGraphRenderControl).GetField("_pan", Private)!.SetValue(graph, new Vector(22, 22));
            var choices = graph.Items[0].Children.First(n => n.IsFolder && n.Children.Count > 1).Children.Take(2).ToArray();
            foreach (var interaction in new[] { "redraw", "selection" })
            {
                for (var i = 0; i < 12; i++) Frame(i);
                var samples = new List<double>();
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 80; i++)
                {
                    var clock = Stopwatch.StartNew(); Frame(i); samples.Add(clock.Elapsed.TotalMilliseconds);
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                samples.Sort();
                var nodes = JsonNode.Parse(graph.ExportGraphText("json"))!["nodes"]!.AsArray().Count;
                results.Add(new { interaction, files = count, nodes, frames = samples.Count, medianMs = samples[40], p95Ms = samples[76], allocatedBytesPerFrame = allocated / samples.Count });
                void Frame(int index) { if (interaction == "selection") graph.SelectedNode = choices[index % 2]; Draw(); }
            }
            window.Close();
            void Draw() { using var dc = bitmap.CreateDrawingContext(); graph.Render(dc); }
        }
        File.WriteAllText(Path.Combine(output, "performance.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PROJECT_GRAPH_PASS: two layouts, two themes, selection, custom/invalid palette, unchanged structure; {JsonSerializer.Serialize(results)}");
    }

    private static void VerifyContrast(ProjectGraphRenderControl graph)
    {
        var resources = typeof(ProjectGraphRenderControl).GetMethod("ResolveRenderResources", Private)!.Invoke(graph, null);
        var nodes = ((System.Collections.IEnumerable)typeof(ProjectGraphRenderControl).GetField("_nodes", Private)!.GetValue(graph)!).Cast<object>();
        foreach (var node in nodes)
        foreach (var (selected, hovered) in new[] { (false, false), (true, false), (false, true) })
        {
            var appearance = typeof(ProjectGraphRenderControl).GetMethod("Appearance", Private)!.Invoke(graph, [node, selected, hovered, resources])!;
            Color Read(string name) => ((ISolidColorBrush)appearance.GetType().GetProperty(name)!.GetValue(appearance)!).Color;
            foreach (var text in new[] { "Title", "Meta" })
            {
                // Independent sRGB contrast oracle, including custom white/black/red/yellow/blue accents.
                static double Light(Color c) { double L(byte b) { var v = b / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); } return .2126 * L(c.R) + .7152 * L(c.G) + .0722 * L(c.B); }
                var a = Light(Read(text)); var b = Light(Read("Fill"));
                if ((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) < 4.5) throw new InvalidOperationException("Graph text contrast falls below 4.5:1.");
            }
        }
    }

    private static void Panels(string output)
    {
        var root = Path.Combine(Path.GetFullPath(output), "isolated-settings"); Directory.CreateDirectory(root);
        var fixture = Fixture();
        var project = new ProjectTabViewModel("atlas", "A", "Atlas Engine", root, "50", "9", "v5", root);
        using var workbench = (WorkbenchViewModel)typeof(WorkbenchViewModel).GetConstructors(Private).Single().Invoke([
            new ObservableCollection<ProjectTabViewModel> { project }, new ObservableCollection<ProjectNodeViewModel> { fixture },
            new Dictionary<string, FileHistoryViewModel>(), null, true, WorkbenchSettings.Load(root), false]);
        typeof(WorkbenchViewModel).GetField("_currentProject", Private)!.SetValue(workbench, project);
        workbench.SwitchWorkspaceModeCommand.Execute(workbench.WorkspaceModes.Single(m => m.Key == "graph"));
        typeof(WorkbenchViewModel).GetMethod("RefreshProjectGraph", Private)!.Invoke(workbench, null);
        if (workbench.ProjectGraphLayoutMode != "cube") workbench.ToggleProjectGraphLayoutModeCommand.Execute(null);
        workbench.IsProjectGraphTreePaneOpen = true;
        var header = new WorkspaceHeaderBar(); var page = new ProjectGraphPage();
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { header, page } }; Grid.SetRow(page, 1);
        var window = new Window { Content = content, DataContext = workbench };
        foreach (var (theme, width, size) in new[] { ("studio", 1440, 11d), ("empty", 1440, 11d), ("studio", 960, 11d), ("studio", 1440, 16.5d) })
        {
            window.Width = width; window.Height = 920;
            workbench.SelectedTheme = workbench.Themes.Single(t => t.Key == theme);
            WorkbenchThemeResources.Apply(window, theme, uiFontSize: size);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var graph = page.FindControl<ProjectGraphRenderControl>("ProjectGraphView")!;
            graph.FitToView();
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, 920), new Vector(96, 96));
            bitmap.Render((Control)window.Content!); bitmap.Save(Path.Combine(output, $"workspace-{theme}-{width}-{size}.png"));
            if (graph.Bounds.Width < 260 || graph.Bounds.Height < 250) throw new InvalidOperationException("The graph viewport must stay usable beside the tree panel.");
            var toolbar = header.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("graph-toolbar-button")).ToArray();
            if (toolbar.Length != 5 || toolbar.Any(b => b.Bounds.Height < 30)) throw new InvalidOperationException("All graph actions need usable hit targets.");
            workbench.OpenProjectGraphSearch(); workbench.ProjectGraphSearchText = "System";
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            if (!page.FindControl<TextBox>("ProjectGraphSearchBox")!.IsFocused) throw new InvalidOperationException("Opening graph search must focus its input.");
            bitmap.Render((Control)window.Content!); bitmap.Save(Path.Combine(output, $"search-{theme}-{width}-{size}.png"));
            if (page.FindControl<Border>("ProjectGraphSearchPanel")!.Bounds.Width > graph.Bounds.Width) throw new InvalidOperationException("Search must fit the graph viewport.");
            workbench.CloseProjectGraphSearch();
        }
        window.Close();
    }

    internal static ProjectNodeViewModel Fixture(int count = 46)
    {
        var names = new[] { "Rendering", "World", "Gameplay", "Platform", "Assets", "Tools" };
        var folders = names.Select((name, group) => new ProjectNodeViewModel(name, name, true, "v2",
            Enumerable.Range(0, (count + 5) / 6).Select(i => new ProjectNodeViewModel($"{name}System{i:00}.cs", $"{name}/{name}System{i:00}.cs", false, $"v{2 + i % 4}", loc: 80 + i * 11)),
            loc: count * 150 / 6, fileCount: (count + 5) / 6, diskFileCount: (count + 5) / 6)).ToList();
        folders[0].Children.Add(new ProjectNodeViewModel("Shaders", "Rendering/Shaders", true, "v3", [new("Terrain.hlsl", "Rendering/Shaders/Terrain.hlsl", false, "v7", loc: 312)], loc: 312, fileCount: 1));
        folders.Add(new ProjectNodeViewModel("Tests", "Tests", true, "v1"));
        folders.Add(new ProjectNodeViewModel("vendor", "vendor", true, "", isExternal: true));
        folders.Add(new ProjectNodeViewModel("README.md", "README.md", false, "v4", loc: 52));
        return new("Atlas Engine", "", true, "v5", folders, loc: count * 150 + 364, fileCount: count + 2, diskFileCount: count + 2);
    }
}
