using System.Diagnostics;
using System.Text.Json;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

internal static class GameLabTests
{
    private static int _checks;
    internal const string Fixture = "<!doctype html><html><head><title>Snake lab</title><style>body{background:#111827;color:#74e1c0;font:22px system-ui;text-align:center}canvas{border:2px solid #74e1c0}</style></head><body><h1>Snake lab</h1><p id='score'>Score: 0</p><canvas id='board' width='320' height='320'></canvas><button onclick='reset()'>Restart</button><script>let x=4,y=4,ticks=0;const c=board.getContext('2d');function draw(){c.fillStyle='#111827';c.fillRect(0,0,320,320);c.fillStyle='#74e1c0';c.fillRect(x*20,y*20,18,18)}function reset(){x=4;y=4;draw()}addEventListener('keydown',e=>{if(e.key==='ArrowRight'){x=(x+1)%16;draw()}});setInterval(()=>ticks++,50);reset();</script></body></html>";
    private static LocalLlmChatMessageViewModel Message(string text) => new("assistant", text);
    private static void Check(bool ok, string why) { _checks++; if (!ok) throw new InvalidOperationException(why); }
    internal static void Run()
    {
        var message = Message("Try it:\n```html\n" + Fixture + "\n```\nArrow keys to move.");
        Check(GameArtifact.FromMessage(message)?.Title == "Snake lab", "Extract a complete fenced HTML artifact.");
        Check(GameArtifact.FromMessage(Message(Fixture)) is not null, "Accept a complete unfenced document.");
        message.IsAwaitingAnswer = true;
        Check(GameArtifact.FromMessage(message) is null, "Never run a streaming message.");
        Check(GameArtifact.FromMessage(Message("```html\n" + Fixture)) is null, "Require the closing code fence.");
        Check(GameArtifact.FromMessage(Message("```html\n<html><script>unfinished\n```")) is null, "Incomplete HTML has no Run action.");
        Check(GameArtifact.FromMessage(Message("<think>```html\n" + Fixture + "\n```</think>")) is null, "Reasoning snippets are not artifacts.");
        Check(GameArtifact.FromMessage(Message("```html\n" + Fixture + "\n```\n**Response incomplete.** stopped")) is null, "A cancelled response cannot silently become runnable.");
        Check(GameArtifact.FromMessage(new("user", Fixture)) is null, "Do not treat the user's quoted HTML as a generated game.");
        var separate = GameArtifact.FromMessage(Message("```html\n<html><head><link rel='stylesheet' href='style.css'></head><body><script src='game.js'></script></body></html>\n```\n```css\nbody{color:red}\n```\n```js\nwindow.ready=true;\n```"));
        Check(separate?.Html.Contains("<style>body{color:red}</style>") == true && separate.Html.Contains("<script>window.ready=true;</script>"), "Combine a three-block browser project.");
        Check(!separate!.Html.Contains("src='game.js'") && !separate.Html.Contains("href='style.css'"), "Remove the replaced local dependency references.");
        Check(GameArtifact.IsCreationRequest("create a snake game") && GameArtifact.IsCreationRequest("build pong"), "Recognize browser-friendly game creation.");
        Check(!GameArtifact.IsCreationRequest("create a snake game in Python") && !GameArtifact.IsCreationRequest("what is the snake game?"), "Respect explicit runtimes and informational questions.");
        Check(!GameArtifact.IsCreationRequest("create a game in C++") && !GameArtifact.IsCreationRequest("make a game in C#"), "Native language names with punctuation must also be respected.");
        Check(GameArtifact.Prompt("add obstacles", new("Snake", Fixture)).Contains(Fixture), "Revision requests include the previous source.");
        var preview = GamePreviewDocument.Build(Fixture);
        Check(preview.Contains("sandbox='allow-scripts'") && !preview.Contains("allow-same-origin"), "Use an opaque-origin game frame.");
        Check(preview.Contains("connect-src 'none'") && preview.Contains("form-action 'none'"), "Keep the browser preview offline.");
        Check(!preview.Contains(Fixture) && preview.Contains("&lt;canvas"), "Encode generated markup into srcdoc.");
        Check(preview.Contains("e.source!==document.getElementById('game')?.contentWindow"), "Only accept messages from the preview frame.");
        try { GamePreviewDocument.Build(new string('x', GameArtifact.MaxCharacters + 1)); throw new Exception("Expected size rejection"); }
        catch (InvalidOperationException) { _checks++; }
        Console.WriteLine($"GAME_LAB_REGRESSION_PASS {_checks} checks");
    }
    internal static async Task Live(string model, string output)
    {
        Directory.CreateDirectory(output);
        var clock = Stopwatch.StartNew(); var chunks = 0; var lastLog = TimeSpan.Zero;
        var request = new LocalLlmRequest(model, GameArtifact.Prompt("Create a small but complete Snake game. A dark canvas, arrow-key movement, food, score, wall/self collisions and a restart button. Keep the code concise, around 80 lines."), "game", [], 8192, Think: true);
        var result = await new LocalLlmService().SendChatAsync(request, new Progress<LocalLlmGenerationProgress>(p =>
        {
            if (p.Delta is { Length: > 0 } || p.ThinkingDelta is { Length: > 0 }) chunks++;
            if (clock.Elapsed - lastLog > TimeSpan.FromSeconds(20)) { lastLog = clock.Elapsed; Console.WriteLine($"{clock.Elapsed:hh\\:mm\\:ss} · {chunks} chunks · {p.Status}"); }
        }), new Progress<string>(Console.WriteLine));
        await File.WriteAllTextAsync(Path.Combine(output, "response.txt"), result.Message ?? result.Status);
        var artifact = GameArtifact.FromMessage(Message(result.Message ?? ""));
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { model, result.Succeeded, result.Status, seconds = clock.Elapsed.TotalSeconds, chunks, result.Stats, artifact = artifact?.Title }, new JsonSerializerOptions { WriteIndented = true }));
        Check(result.Succeeded && artifact is not null, "The live model must return a complete runnable HTML game.");
        await File.WriteAllTextAsync(Path.Combine(output, "index.html"), artifact!.Html);
        Console.WriteLine($"GAME_LIVE_PASS: {model}, {clock.Elapsed.TotalSeconds:0}s, {chunks} chunks, {artifact.Html.Length} HTML characters");
    }
}
