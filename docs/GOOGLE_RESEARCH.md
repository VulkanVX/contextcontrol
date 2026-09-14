# Google research for local models

Turn on **Google auto** in the Local chat composer or **Settings → Prompt Window → Google research for local models**. It is enabled by default and also available in Chat Monitor's quick reply. It works in Raw chat and CC flow with installed Ollama chat models, including models without native function calling. It does not apply to image generation or Codex CLI.

Ask normally: “Search for Avalonia's official documentation” or “What is the current state of …?” ContextControl asks the selected local model whether research is needed. The model writes a Google query, receives up to six result titles, URLs and snippets, then chooses up to three numbered links to read. ContextControl retrieves visible page text and returns excerpts to that same model for its final answer. Source buttons below the answer open the original pages and remain in chat history.

The request's progress row and Chat Monitor show planning, searching, choosing pages, reading and answering. Blocked or failed pages are marked unavailable, and the model gets one opportunity to choose alternatives from untried results. Research makes at most five distinct page attempts to collect up to three readable pages. The normal Stop button cancels the complete request, including time spent waiting for another chat's browser operation. Browser operations are serialized, and each result is checked against its own query or requested URL.

## Browser and privacy

Google research uses the Windows WebView2 browser; no search API key or cloud model is required. If WebView2 is missing, use the runtime option in the ContextControl installer. The ordinary Google window opens when a search starts. Complete Google's consent or verification there if requested; ContextControl waits for the actual results page. Closing the window stops the current browser operation. **Stop research** cancels the active request. The window may be minimized while research runs.

Only the generated query is submitted to Google; ContextControl does not upload the entire prompt, project capsule, or attachments to the search page. The chosen sites receive normal browser visits. Search content and page text are treated as untrusted reference data. The model can select only numbered results; it cannot execute browser scripts, submit forms, download files, or open local files through this feature. Ordinary page scripts still run as they would in a browser.

The separate browser profile is stored in `%LOCALAPPDATA%\ContextControl\GoogleResearch`. The Auto/Off preference is saved with other local settings. Switch Google off to prevent Google research requests from local chats.

## Limits

- Small models may produce imperfect queries, selections or citations. Invalid planner JSON falls back to the user's explicit/current-information query; invalid selections fall back to the first two valid results.
- Google may change its page layout, require verification, or return no readable results. In that case ContextControl reports the failure instead of silently answering as if research succeeded.
- Page reading is limited to visible HTML text. PDFs, sign-in pages, blocked pages and redirects that do not match the selected source may be unavailable. HTTP failures and recognizable block screens are rejected as evidence; access restrictions are not bypassed. The final prompt clearly distinguishes page excerpts from search snippets and discloses when a requested source was unavailable.
- Excerpts are bounded by the selected context window. Large project capsules may require a larger local context setting or a shorter prompt. Search and page selection add local model generation time before the final answer.
- This supplies evidence to a model; it does not guarantee that every statement or citation in its answer is correct.

The browser integration uses Microsoft's [WebView2 script evaluation API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.executescriptasync). It does not depend on Google's Custom Search JSON API, which is [closed to new customers](https://developers.google.com/custom-search/v1/overview).

## Validation

Offline checks, with no Google or model requests:

```powershell
dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --google-research-regression
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
