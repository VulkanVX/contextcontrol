# Google research for local models

Turn on **Google auto** in the Local chat composer or **Settings → Prompt Window → Google research for local models**. It is enabled by default and also available in Chat Monitor's quick reply. It works in Raw chat and CC flow with installed Ollama or connected compatible chat models, including models without native function calling. It does not apply to image generation or Codex CLI.

Ask normally: “Search for Avalonia's official documentation” or “What is the current state of …?” ContextControl asks the selected local model whether research is needed. The model writes a Google query, receives up to six result titles, URLs and snippets, then chooses up to three numbered links to read. ContextControl retrieves visible page text and returns excerpts to that same model for its final answer. Source buttons below the answer open the original pages and remain in chat history.

The request's progress row and Chat Monitor show planning, searching, choosing pages, reading and answering. Blocked or failed pages are marked unavailable, and the model gets one opportunity to choose alternatives from untried results. Research makes at most five distinct page attempts to collect up to three readable pages. The normal Stop button cancels the complete request, including time spent waiting for another chat's browser operation. Browser operations are serialized, and each result is checked against its own query or requested URL.

Current-information requests override a mistaken no-search plan. If an unresearched answer admits missing public knowledge or an outdated training cutoff, Google auto makes one recovery attempt: research the subject, read sources, then replace the draft with a sourced answer. It skips text editing, recognized private-data questions, failed generation and answers already researched. If lookup fails, the draft remains with an explanation. This adds evidence to the reply and preserves its sources in chat history; it does not train or update the model's weights. Raw chat retains its existing per-prompt behavior.

## Photos beside entries

Explicit requests such as “Show me a photo of Nvidia 5090” also trigger research. The subject can receive up to three related source images even when the answer has no list or table. A combined explanation/photo request retains the explanation. In-message illustrations use publisher captions and section headings: an overview image follows the introduction, and other images sit beside matching sections on wide windows or below them on narrow windows. Images without a section match stay with the introduction. The host attempts photos after the answer and states when no matching photo could be retrieved. Image-search result pages are excluded as photo sources. Product matching can recognize a manufacturer in the source hostname while still requiring the correct model number. WoW and World of Warcraft are recognized as the same subject.

The answer appears first. For lists of named places or products, ContextControl then looks for an individual source whose title matches each entry. A matching photo appears inside that entry's card, beside its details on wide windows and below its name on narrow windows. Numbered entries, repeated bold-name sections and named table rows are supported. General roundup images are not displayed in a gallery below the answer. Sources remain available as compact links.

Click a photo to enlarge it and its source link to open the page. Entry names are matched conservatively, including accented spellings. A roundup can provide several venue photos only when each image's own caption or section identifies that venue. Generic logos, ads, newsletter promotions and repeated images are skipped; explicit subject requests can use a logo that names the actual subject. Article illustrations inside semantic aside elements are retained. Matching uses source metadata; it does not establish the contents of the image through visual analysis.

Photos are optional. Already-read page images are reused first. Each of at most six named venues has its own 12-second budget, so a slow site cannot consume the whole list's time. The overall limit is at most 80 seconds for a venue list and 45 seconds for an explicit subject. Fallback uses at most two focused queries, each reading up to three matching pages while time remains. Each image-loading batch has a six-second limit. Blocked, missing or oversized images leave the answer intact, and Stop cancels optional photo work without removing completed text. Downloads and resizing run outside the UI thread. Photos are downscaled to at most 1280 pixels on the longest edge. The cache at `%LOCALAPPDATA%\ContextControl\WebPhotos` keeps up to 256 images, pruning older entries around 120 MB. Entry identities, captions, section associations and preview paths are saved in chat history. Existing answers retain their source links; new research answers can include matched photos.

These are source illustrations for the user. Image bytes are not added to the model's text context, and this feature does not give a text-only model visual understanding. Ordinary public image requests use no browser cookies or sign-in credentials. A failed image request is not retried through an access-block workaround.

## Browser and privacy

Google research uses the Windows WebView2 browser; no search API key or cloud model is required. If WebView2 is missing, use the runtime option in the ContextControl installer. The ordinary Google window opens when a search starts. Complete Google's consent or verification there if requested; ContextControl waits for the actual results page. Closing the window stops the current browser operation. **Stop research** cancels the active request. The window may be minimized while research runs.

Only a search query is submitted to Google: normally the model generates it; bounded lookup requests can supply a fallback query when planning fails. Project capsules and attachments are not uploaded to the search page. The chosen sites receive normal browser visits. Search content and page text are treated as untrusted reference data. The model can select only numbered results; it cannot execute browser scripts, submit forms, download files, or open local files through this feature. Ordinary page scripts still run as they would in a browser.

The separate browser profile is stored in `%LOCALAPPDATA%\ContextControl\GoogleResearch`. The Auto/Off preference is saved with other local settings. Switch Google off to prevent Google research requests from local chats.

## Limits

Recognized Google interface text such as “Translate this page” and “Išversti šį puslapį” is removed from results and snippet lines. If a model still emits that label as a venue, one correction uses the original source evidence; an unsuccessful correction omits the invalid entry. This targets interface-label confusion and is not a general fact checker.

- Small models may produce imperfect queries, selections or citations. Invalid planner JSON falls back to the user's explicit/current-information query; invalid selections fall back to the first two valid results.
- Google may change its page layout, require verification, or return no readable results. In that case ContextControl reports the failure instead of silently answering as if research succeeded.
- Page reading is limited to visible HTML text. PDFs, sign-in pages, blocked pages and redirects that do not match the selected source may be unavailable. HTTP failures and recognizable block screens are rejected as evidence; access restrictions are not bypassed. The final prompt clearly distinguishes page excerpts from search snippets and discloses when a requested source was unavailable.
- Excerpts are bounded by the selected context window. Large project capsules may require a larger local context setting or a shorter prompt. Search and page selection add local model generation time before the final answer.
- Web evidence reserves space for the answer using a conservative character estimate; exact token usage depends on the model. Thinking off is sent explicitly. A thinking-only result retries once with thinking off when it was previously enabled or unspecified. If it still cannot answer, the chat reports the failure alongside the source previews. Responses cut short by a context/output limit are labelled incomplete.
- This supplies evidence to a model; it does not guarantee that every statement or citation in its answer is correct.

The browser integration uses Microsoft's [WebView2 script evaluation API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.executescriptasync). It does not depend on Google's Custom Search JSON API, which is [closed to new customers](https://developers.google.com/custom-search/v1/overview).

## Validation

The opt-in commands `--google-media-smoke wow` and `--google-media-smoke tallinn` use an installed `granite3.3:2b` with live Google and ordinary page reads. The 0.5.1 checks retrieved three images from Blizzard's WoW Forever article and photos for two distinct Tallinn venues (Põhjala Tap Room and Whisper Sister); the third venue did not yield a photo. The checks verify retrieval and source association, not every statement the model generates. Source metadata, cached paths and the generated answer are saved to a local `.tmp/media-*.json` proof file. These web-dependent checks are separate from unattended release tests.

On Windows, `dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --google-knowledge-smoke` reproduces the recovery path with a seeded unknown draft, live Google/page reads, a local Granite 3.3 2B answer and a matched source photo. It requires that model already installed in Ollama and makes real web requests. The release check produced an RTX 5090 caption and the matching photo from NVIDIA's own product page.

Offline checks, with no Google or model requests:

```powershell
dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --google-research-regression
dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --local-chat-regression
dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --ui-experience-regression .tmp/ui-review
```

Opt-in Windows WebView2 DOM fixture check:

```powershell
dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --google-browser-smoke
```

To run a real Google search, read the model's selected pages, and generate an answer with an installed local model, append its exact Ollama model name:

```powershell
dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --google-browser-smoke granite3.3:2b
```

This last check opens the real Google browser window and may require user interaction for consent or verification. It prints the plans, source URLs and final answer. It is not part of unattended release checks.

The original pizza-research failure has its own opt-in check at 4,096 context tokens:

```powershell
dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --google-pizza-smoke qwen3.5:4b-q4_K_M
```

For repeatable inference using synthetic source evidence without contacting Google, use `--local-chat-live qwen3.5:4b-q4_K_M` instead.
