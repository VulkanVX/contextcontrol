# Architecture graph

Open **Graph** to explore the current project using its existing file rules. The architecture layout and optional tree panel show the same source hierarchy.

## Read the graph

- A lightly tinted region groups a folder's descendants. Its node card carries a narrow generation-color marker. **Levels** edits those accent colors; saved custom colors remain available. The old untouched default palette upgrades to the new palette.
- Folder cards retain **F** (file count) and **LOC** (lines of code). File cards retain their version and LOC. Skipped entries keep their `skip` marker; empty folders remain visible.
- Selection uses both a stronger outline and a raised color level. Hover any card for its complete path and metrics when the label is clipped. Zoom in to read small labels.
- Large graphs retain the existing limits: at most 4,200 displayed nodes and 120 children per parent, followed by an aggregate `+… more` node. This refresh does not change which project files are scanned.

## Navigate and export

Drag blank canvas to pan. Scroll to move vertically, **Shift + scroll** to move horizontally, and **Ctrl + scroll** to zoom around the pointer. **Find** or **Ctrl+F** searches files and folders; Enter accepts a result and Escape closes search.

The packing button switches the existing generation-one tree/cube arrangement. **Fit layout** resets the arrangement and fits the graph. **Tree** opens the full project tree, with copy and scrolling controls. The toolbar wraps at compact widths and the navigation footer leaves graph nodes unobscured.

**Export** retains raster, SVG and structured graph formats, plus the optional project-details sections. Raster output and SVG node cards use the new color levels. Structured output retains node identities, geometry and metadata.

## Rendering checks

Run `dotnet run --project ide/ContextControl.Workbench.Tests -c Release -- --graph-experience .tmp/graph-review` for rendered fixtures and regression checks. It writes dark/light, both packing modes, selection/custom-color images, responsive panel captures and managed rendering timings. The benchmark measures headless Skia drawing, excluding native-window presentation and GPU latency; it is not an FPS prediction.

The renderer retains geometry while caching palettes, tonal states, fonts, metric strings and formatted labels. Selection no longer flushes every label. Cache sizes remain bounded, and changing a file's version or LOC refreshes its label.
