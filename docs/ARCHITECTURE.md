# Architecture

## Decision record

The workspace contained the historical **OpenPDN** (Paint.NET 3.x) source — .NET Framework 2.0-era WinForms, tens of thousands of lines coupled to GDI+ and `System.Windows.Forms`. Modernizing it was rejected: it cannot be dark-themed without rewriting every control, it does not build on modern SDKs, and its extensibility model is tangled through `AppWorkspace`/`DocumentWorkspace` god classes.

**Chosen path: clean rewrite in C# / WPF on .NET 10**, treating OpenPDN and Paint.NET 5.x as the UX specification (layout, tool behavior, mouse conventions, panel design) rather than as code to port. WPF gives hardware-accelerated composition, full control templating (required for a complete dark theme, including menus and dialogs), and vector icons.

## Solution layout

```
Artista.slnx
├── src/Artista.Core      class library, no UI logic (WPF referenced only for WIC codecs)
│   ├── Imaging/          Surface (BGRA32 straight-alpha uint[] buffer), ColorBgra, RectInt
│   ├── Layers/           Layer, BlendMode + compositing math
│   ├── Documents/        Document (layers + selection + size), DocumentTransforms
│   ├── Rendering/        Compositor (visible layers → composite, dirty-rect, preview substitution)
│   ├── Selections/       Selection (byte coverage mask), SelectionRasterizer (AA scanline), FloodFill
│   ├── History/          HistoryMemento hierarchy + HistoryStack (delta-based, memory-budgeted)
│   ├── ColorEngine/      OkLab conversion, ColorMatcher (shared Remove Color engine)
│   ├── Drawing/          StrokeBuffer (brush engine), ShapeRenderer, GradientRenderer, BucketFill
│   ├── Effects/          EffectBase/PerPixelEffect, EffectParameter descriptors, EffectRegistry,
│   │                     all adjustments & effects incl. RemoveColorEffect
│   └── IO/               ImageCodec (WIC), ArtzFormat (zip project), SafeSave
├── src/Artista.App       WPF application
│   ├── Themes/           Dark.xaml / Light.xaml palettes, Controls.xaml (all control templates),
│   │                     Icons.xaml (vector tool/command icons)
│   ├── Models/           DocumentWorkspace (doc + history + composite bitmap + view state),
│   │                     ToolEnvironment (shared tool settings, ~ Paint.NET's AppEnvironment)
│   ├── Controls/         CanvasView (render: checkerboard/bitmap/grid/ants/overlay),
│   │                     DocumentView (zoom/pan/scrollbars/rulers/input routing)
│   ├── Tools/            ToolBase + IToolContext, ToolRegistry, all 25 tools
│   ├── Panels/           ColorsPanel (+ ColorWheel), LayersPanel, HistoryPanel, IShellHost
│   ├── Dialogs/          DialogBase, document dialogs, EffectDialog (auto-generated, live preview),
│   │                     CurveEditor, ProgressDialog
│   ├── MainWindow*.cs    shell layout, commands/menus, self-test harness
│   ├── ThemeManager.cs   palette swap + DWM dark title bars
│   └── AppSettings.cs    JSON settings in %AppData%\Artista
└── tests/Artista.Tests   xUnit suite over Artista.Core (82 tests)
```

## Key design points

**Pixel model** — `Surface` is a flat `uint[]` of packed BGRA (blue low byte), *straight* (non-premultiplied) alpha, matching WPF's `Bgra32`. All editing operates on these buffers; the UI uploads dirty rectangles into a `WriteableBitmap` per document.

**Compositing** — `Compositor.Composite` walks visible layers per row (parallelized) with a fast path for Normal/opaque. It accepts a *substitute surface* for one layer id — this is how effect live previews and preview-based tools render without mutating the real layer.

**History** — mementos store the *state to restore* and, when applied, return their inverse (Paint.NET's model). Pixel changes are stored as tight per-region deltas (`SurfaceRegionMemento`); structural changes capture layer-list/surface references (`DocumentStructureMemento` — which is why transforms must replace, never mutate, layer surfaces). `HistoryStack` trims oldest entries beyond a configurable byte budget.

**Strokes** — `StrokeBuffer` accumulates dab coverage with `max()` so overlapping dabs inside one stroke don't stack opacity; appliers blend from a stroke-start snapshot into the live surface, giving correct uniform-opacity strokes, live feedback, selection masking, and exactly one history delta per stroke.

**Color engine** — `ColorMatcher` maps (target, tolerance, softness) → per-pixel match factor using OKLab distance (quadratic tolerance response; smoothstep softness band). The Remove Color effect and the Color Remover brush both consume this single engine, and the unit tests pin its semantics (tolerance 0 = exact RGB only, proportional alpha preservation, no color halos).

**Effects** — an effect declares parameters (`IntParameter`, `ColorParameter`, `CurvesParameter`, …); `EffectDialog` builds the UI generically and renders previews on a background thread with cancellation. `EffectRunner.RunMasked` clips results to the selection with antialiased coverage blending. Application commits happen only after a full successful render, so cancellation can never leave a half-modified document.

**Theming** — two palette dictionaries define identical brush keys; `Controls.xaml` templates every control against them with `DynamicResource`. `ThemeManager` swaps the palette dictionary in place at runtime and sets the DWM immersive-dark attribute on every window.

**Threading** — pointer-driven edits run on the UI thread against small dirty rects (row-parallel internally); effects and multi-layer applies run on worker threads with `CancellationToken`s; `WriteableBitmap` uploads always happen on the UI thread.
