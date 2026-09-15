using ContextControl.Workbench.Services;

internal static class SettingsPersistenceTests
{
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ContextControlSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = WorkbenchSettings.Load(root);
            settings.UiFontSize = 13.5;
            settings.Save();
            using var stop = new CancellationTokenSource();
            var reads = 0;
            var reader = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    var loaded = WorkbenchSettings.Load(root);
                    if (loaded.UiFontSize != 13.5) throw new InvalidOperationException("Reader saw incomplete/default settings during a save.");
                    Interlocked.Increment(ref reads);
                }
            });
            try
            {
                for (var i = 0; i < 80; i++)
                {
                    settings.UseMeasuredLocalPerformance = i % 2 == 0;
                    settings.Save();
                }
                settings.UseMeasuredLocalPerformance = false;
                settings.Save();
                if (WorkbenchSettings.Load(root).UseMeasuredLocalPerformance) throw new InvalidOperationException("The final preference was not saved.");
            }
            finally { stop.Cancel(); await reader; }
            if (reads == 0) throw new InvalidOperationException("The concurrent reader did not run.");
            if (Directory.GetFiles(root, "*.tmp").Length != 0) throw new InvalidOperationException("Temporary settings files leaked.");
            Console.WriteLine($"SETTINGS_PERSISTENCE_PASS: 81 writes, {reads} complete concurrent reads, final preference preserved");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
