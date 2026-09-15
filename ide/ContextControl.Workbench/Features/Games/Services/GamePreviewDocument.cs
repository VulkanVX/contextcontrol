using System.Net;

namespace ContextControl.Workbench.Services;

public static class GamePreviewDocument
{
    // No same-origin, popups, forms, downloads, native bindings, or network access.
    // Error messages cross the frame boundary as text only; they never invoke host commands.
    public static string Build(string html, bool checkInputs = false)
    {
        if (html.Length > GameArtifact.MaxCharacters) throw new InvalidOperationException("This preview is too large. Keep the HTML below 400,000 characters.");
        const string policy = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: blob:; media-src data: blob:; font-src data:; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";
        var instrumentation = """
            <script>
            (() => {
              const report = text => parent.postMessage({kind:'game-error',text:String(text).slice(0,2000)},'*');
              addEventListener('error',e=>report(e.error?.stack || e.message || 'A game resource could not load.'),true);
              addEventListener('unhandledrejection',e=>report(e.reason?.message || e.reason));
              addEventListener('securitypolicyviolation',e=>report('Unavailable in offline preview: '+e.violatedDirective+' '+e.blockedURI));
              addEventListener('load',()=>parent.postMessage({kind:'game-ready'},'*'));
              let frames=0,start=performance.now();
              function sample(now){frames++;if(now-start>=1000){parent.postMessage({kind:'game-stats',text:String(Math.round(frames*1000/(now-start)))},'*');frames=0;start=now}requestAnimationFrame(sample)}
              requestAnimationFrame(sample);
            })();
            </script>
            """;
        var checks = checkInputs ? """
            <script>
            addEventListener('load',()=>{
              const click = names => [...document.querySelectorAll('button')].find(b=>names.includes(b.textContent.trim().toLowerCase()))?.click();
              setTimeout(()=>click(['start','play','start game']),100);
              ['Enter','ArrowRight','ArrowDown','ArrowLeft','ArrowUp',' '].forEach((key,i)=>setTimeout(()=>{
                const target=document.querySelector('canvas')||document.body;
                target.dispatchEvent(new KeyboardEvent('keydown',{key,code:key===' '?'Space':key,bubbles:true}));
                target.dispatchEvent(new KeyboardEvent('keyup',{key,code:key===' '?'Space':key,bubbles:true}));
              },250+i*150));
              setTimeout(()=>click(['restart','reset','play again','restart game']),1350);
              setTimeout(()=>parent.postMessage({kind:'game-check-done'},'*'),2200);
            });
            </script>
            """ : "";
        var child = "<!doctype html><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"" + policy + "\">" + instrumentation + checks + html;
        return """
            <!doctype html><html><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; frame-src about:; img-src data: blob:; media-src data: blob:; font-src data:; connect-src 'none'; base-uri 'none'; form-action 'none'">
            <style>html,body{margin:0;width:100%;height:100%;overflow:hidden;background:#0b1020}iframe{width:100%;height:100%;border:0;display:block}</style></head><body>
            <script>
            window.gameEvents=[];
            addEventListener('message',e=>{
              if(e.source!==document.getElementById('game')?.contentWindow || !e.data || !['game-error','game-ready','game-stats','game-check-done'].includes(e.data.kind))return;
              if(window.gameEvents.length<100) window.gameEvents.push({kind:e.data.kind,text:String(e.data.text||'').slice(0,2000)});
            });
            </script>
            """ + "<iframe id='game' title='Game preview' sandbox='allow-scripts' allow='autoplay' srcdoc=\"" + WebUtility.HtmlEncode(child) + "\"></iframe></body></html>";
    }
    public const string PollScript = "JSON.stringify(window.gameEvents ? window.gameEvents.splice(0) : [])";
    public const string Stopped = "<!doctype html><html><body style='margin:0;background:#0b1020;color:#a7b5d1;font:16px system-ui;display:grid;place-items:center;height:100vh'><div>Preview stopped · press Run to play again</div></body></html>";
}
