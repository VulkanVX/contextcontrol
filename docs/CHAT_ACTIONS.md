# Chat actions

Type `/` at the beginning of the composer to open the quick menu. Filter by typing, use Up/Down, and press Tab or Enter to insert the selected action. Add your request and press Send. Escape closes the menu. The **/ Actions** button and **Settings → LLMs → Browse actions and examples** open the full library.

| Action | Invocation | What happens |
| --- | --- | --- |
| Google research | `/search latest World of Warcraft news` | Explicitly searches Google, reads selected public sources and builds an answer with references and relevant photos. The planner forms the query but cannot skip an explicit search. Uses the embedded browser; queries go to Google. |
| Create game | `/game Snake with keyboard controls and restart` | Selects browser-game creation. Produces complete HTML/CSS/JS, checks it in Game Lab and attempts repairs when enabled. Needs an installed local chat model. |
| Local chat | `/chat explain recursion` | Uses the selected local model; turns game mode and automatic research off. |
| Generate image | `/image pixel-art forest at sunrise` | Routes to the separate selected ImageGen model. Requires its runtime and weights; a text model is insufficient. |
| Project coding | `/context fix the camera movement` | Selects the existing DIR → CC → GO workflow with current project sources and Skillbook instructions. Patch application remains a separate reviewed action. |
| Open latest game | `/run` | Opens the latest complete game in this chat; no model request. |
| Library | `/actions` | Opens descriptions, requirements and examples; no model request. |

Actions are dispatched by ContextControl before model routing. The command prefix is removed from the model prompt; the selected action supplies its contract. No model-specific function-calling support is required. Natural-language game detection and automatic Google research remain available when no command is specified. Unknown commands show help and do not send a request. A prompt action without an argument shows its description.

## Game creation and checking

With **Settings → LLMs → Run and repair generated browser games** enabled (the default), a completed game enters a checking stage. The app runs the final HTML, records JavaScript errors, exercises common Start/Play/Restart buttons and Enter/Space/arrow-key handlers, and waits for delayed errors. Errors are sent with the final source to the same selected model. Each correction is checked again, with at most two automatic repairs. Stop cancels inference and checking; there is no model-generation deadline.

The browser observation window is bounded to 30 seconds so an unavailable/unresponsive preview cannot produce an indefinite check or a false pass. That bound applies only to local preview observation. Browser unavailability does not trigger model repair. The game executes in the existing offline frame sandbox; no native executable, network access, package installation or host bindings are introduced.

Each run preserves original and repaired output under `.ccWorkbench.generated-projects/game-...`, including `attempt-N.txt`, complete `attempt-N.html` documents, and `validation.json`. An interrupted repair preserves the last completed version. The final answer shows **Game check passed**, **Game needs review**, or a clear unavailable/stopped status, then opens the final complete artifact in Game Lab.

A pass means the observed startup and synthetic input checks produced no runtime errors. It does not prove collision logic, scoring, accessibility, every control, or all gameplay states. Playtest those in Game Lab. Its **Improve / fix with model** button still prepares a draft in the original chat. Native engine projects use the project workflow instead of automatic execution.
