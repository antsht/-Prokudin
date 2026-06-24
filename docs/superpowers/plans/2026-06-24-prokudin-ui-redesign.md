# Prokudin GUI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild Prokudin GUI into a workflow-based restoration workspace with resizable panels, context command bar, right inspector, theme support, and targeted Core settings for levels/gamma and align parameters.

**Architecture:** Six-stage incremental refactor of `src/Prokudin.Gui` plus small Core additions (`LevelsSettings`, `ColorCorrection.ApplyLevelsSettings`, tests). `MainViewModel` stays the single orchestrator with CommunityToolkit.Mvvm. Views use Zafiro semantic containers and `ContentControl` templates keyed by `WorkflowTool`.

**Tech Stack:** .NET 10, C# 14, Avalonia 12, Zafiro.Avalonia, CommunityToolkit.Mvvm, xUnit, FluentAssertions, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-06-24-prokudin-ui-redesign-design.md`

---

## File map

### Core (new/modified)

| File | Action |
| --- | --- |
| `src/Prokudin.Core/Color/LevelsMode.cs` | Create |
| `src/Prokudin.Core/Color/LevelsSettings.cs` | Create |
| `src/Prokudin.Core/Color/ColorCorrection.cs` | Add `ApplyLevelsSettings`, `ApplyManualLevelsAndGamma` |
| `src/Prokudin.Core/Pipeline/PipelineSettings.cs` | Add `LevelsSettings Levels` |
| `src/Prokudin.Core/Pipeline/ReconstructionPipeline.cs` | Use `settings.Levels` in `BuildRgb` |
| `tests/Prokudin.Core.Tests/Color/LevelsSettingsTests.cs` | Create |

### GUI (new)

| File | Action |
| --- | --- |
| `ViewModels/WorkflowTool.cs` | Create enum |
| `ViewModels/ChannelSlotState.cs` | Create enum |
| `ViewModels/AppThemeMode.cs` | Create enum |
| `Services/UiSettings.cs` | Create |
| `Services/IUiSettingsStore.cs` | Create |
| `Services/JsonUiSettingsStore.cs` | Create |
| `Services/ThemeService.cs` | Create |
| `Views/WorkflowToolbar.axaml` (+`.cs`) | Create |
| `Views/ContextCommandBar.axaml` (+`.cs`) | Create |
| `Views/ContextBars/*.axaml` | Create per workflow (6 files) |
| `Views/Inspector/InspectorPanel.axaml` (+`.cs`) | Create |
| `Views/Inspector/*Inspector.axaml` | Create per workflow (6 files) |
| `Views/ProcessingLogPanel.axaml` (+`.cs`) | Create |
| `Views/StatusBar.axaml` (+`.cs`) | Create |
| `Views/AboutDialog.axaml` (+`.cs`) | Create |
| `Views/KeyboardShortcutsDialog.axaml` (+`.cs`) | Create |

### GUI (modified)

| File | Action |
| --- | --- |
| `Views/MainWindow.axaml` (+`.cs`) | Full grid shell |
| `Views/ChannelSlotCard.axaml` | State badge, selected style |
| `ViewModels/ChannelSlotViewModel.cs` | `State`, `IsSelected` |
| `ViewModels/MainViewModel.cs` | Workflow, align, levels, crop overlap, heal params, theme |
| `ViewModels/ImageSelectionRect.cs` | Square constraint helper |
| `Services/AutoCleanSettingsSnapshot.cs` | Expand heal fields |
| `Themes/Containers.axaml` | Workflow tool, inspector section, badge styles |
| `App.axaml.cs` | Load ui settings + theme on startup |
| `tests/Prokudin.Gui.Tests/JsonUiSettingsStoreTests.cs` | Create |

---

## Stage 1 — Layout refactor

### Task 1: Core levels settings (prerequisite for Color inspector)

**Files:**
- Create: `src/Prokudin.Core/Color/LevelsMode.cs`
- Create: `src/Prokudin.Core/Color/LevelsSettings.cs`
- Modify: `src/Prokudin.Core/Color/ColorCorrection.cs`
- Modify: `src/Prokudin.Core/Pipeline/PipelineSettings.cs`
- Modify: `src/Prokudin.Core/Pipeline/ReconstructionPipeline.cs`
- Create: `tests/Prokudin.Core.Tests/Color/LevelsSettingsTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using FluentAssertions;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests.Color;

public sealed class LevelsSettingsTests
{
    [Fact]
    public void DefaultSettings_MatchesGentleLevels_OnGradient()
    {
        var rgb = TestRgbGradient(64, 64);
        var gentle = ColorCorrection.ApplyGentleLevels(rgb);
        var viaSettings = ColorCorrection.ApplyLevelsSettings(rgb, new LevelsSettings());
        gentle.Pixels.Should().BeEquivalentTo(viaSettings.Pixels, options => options.WithTolerance(1e-5f));
    }

    [Fact]
    public void ManualLevelsAndGamma_DarkensMidtones()
    {
        var rgb = SolidRgb(0.5f);
        var output = ColorCorrection.ApplyManualLevelsAndGamma(rgb, black: 0.0f, white: 1.0f, gamma: 2.0f);
        output.Pixels[0].Should().BeLessThan(0.5f);
    }

    private static RgbImageBuffer TestRgbGradient(int w, int h) { /* 0..1 horizontal ramp */ }
    private static RgbImageBuffer SolidRgb(float v) { /* fill */ }
}
```

- [ ] **Step 2: Run test — expect FAIL**

Run: `dotnet test tests/Prokudin.Core.Tests/Prokudin.Core.Tests.csproj --filter LevelsSettingsTests -v n`

- [ ] **Step 3: Implement**

`LevelsMode.cs`:

```csharp
namespace Prokudin.Core.Color;

public enum LevelsMode
{
    Off,
    AutoPercentile,
    Manual,
}
```

`LevelsSettings.cs`:

```csharp
namespace Prokudin.Core.Color;

public sealed record LevelsSettings(
    LevelsMode Mode = LevelsMode.AutoPercentile,
    float BlackPoint = 0.0f,
    float WhitePoint = 1.0f,
    float Gamma = 1.0f,
    float AutoLowPercent = 1.0f,
    float AutoHighPercent = 99.0f,
    float AutoMaxGain = 1.3f);
```

`ColorCorrection.cs` additions:

```csharp
public static RgbImageBuffer ApplyLevelsSettings(RgbImageBuffer rgb, LevelsSettings settings) =>
    settings.Mode switch
    {
        LevelsMode.Off => rgb,
        LevelsMode.Manual => ApplyManualLevelsAndGamma(
            rgb,
            Math.Clamp(settings.BlackPoint, 0f, 1f),
            Math.Clamp(settings.WhitePoint, settings.BlackPoint + 1e-4f, 1f),
            Math.Clamp(settings.Gamma, 0.1f, 5f)),
        _ => ApplyGentleLevels(rgb, settings.AutoLowPercent, settings.AutoHighPercent, settings.AutoMaxGain),
    };

public static RgbImageBuffer ApplyManualLevelsAndGamma(RgbImageBuffer rgb, float black, float white, float gamma)
{
    var output = rgb.Clone();
    var invRange = 1.0f / Math.Max(white - black, 1e-6f);
    var invGamma = 1.0f / gamma;
    PixelParallel.For(0, output.Pixels.Length, i =>
    {
        var stretched = Math.Clamp((output.Pixels[i] - black) * invRange, 0f, 1f);
        output.Pixels[i] = MathF.Pow(stretched, invGamma);
    });
    return output;
}
```

`PipelineSettings.cs`:

```csharp
public LevelsSettings Levels { get; init; } = new();
```

`ReconstructionPipeline.BuildRgb` line ~128:

```csharp
corrected = ColorCorrection.ApplyLevelsSettings(corrected, settings.Levels);
```

- [ ] **Step 4: Run tests — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/Prokudin.Core/Color/ src/Prokudin.Core/Pipeline/ tests/Prokudin.Core.Tests/Color/
git commit -m "feat(core): add configurable levels and gamma settings for RGB output"
```

---

### Task 2: MainWindow grid shell

**Files:**
- Modify: `src/Prokudin.Gui/Views/MainWindow.axaml`
- Modify: `src/Prokudin.Gui/Views/MainWindow.axaml.cs`
- Create: `src/Prokudin.Gui/Views/ProcessingLogPanel.axaml` (+`.cs`) — move log from MainWindow
- Create: `src/Prokudin.Gui/Views/StatusBar.axaml` (+`.cs`)

- [ ] **Step 1: Replace root layout**

`MainWindow.axaml` root becomes:

```xml
<Grid RowDefinitions="Auto,Auto,*,4,Auto,Auto">
  <Menu Grid.Row="0" ... existing File/Edit/Process ... />
  <Border Grid.Row="1" x:Name="ContextBarHost" Classes="ChromeBar top" />
  <Grid Grid.Row="2" ColumnDefinitions="260,4,88,*,4,360" x:Name="WorkspaceGrid">
    <z:HeaderedContainer Grid.Column="0" Header="Channels" Classes="Sidebar">...</z:HeaderedContainer>
    <GridSplitter Grid.Column="1" ResizeDirection="Columns" />
    <Border Grid.Column="2" x:Name="WorkflowToolbarHost" />
    <views:ImagePreviewControl Grid.Column="3" ... keep existing bindings ... />
    <GridSplitter Grid.Column="4" ResizeDirection="Columns" />
    <Border Grid.Column="5" x:Name="InspectorHost" />
  </Grid>
  <GridSplitter Grid.Row="3" ResizeDirection="Rows" />
  <views:ProcessingLogPanel Grid.Row="4" DataContext="{Binding}" />
  <views:StatusBar Grid.Row="5" DataContext="{Binding}" />
</Grid>
```

Move channel `ListBox` + export popup into column 0 unchanged initially. Move processing log markup into `ProcessingLogPanel`. Remove old title `EdgePanel` status strip (row 2 dimensions bar).

- [ ] **Step 2: StatusBar.axaml**

```xml
<Border Classes="ChromeBar bottom" Padding="8,4">
  <Grid ColumnDefinitions="*,Auto,Auto">
    <TextBlock Text="{Binding Status}" TextTrimming="CharacterEllipsis" />
    <TextBlock Grid.Column="1" Classes="MutedCaption"
               Text="{Binding SelectedSlotSummary}" Margin="16,0" />
    <TextBlock Grid.Column="2" Text="{Binding BusyIndicatorText}" Classes="MutedCaption" />
  </Grid>
</Border>
```

Add `SelectedSlotSummary` and `BusyIndicatorText` computed properties on `MainViewModel`.

- [ ] **Step 3: Build and smoke-run**

Run: `dotnet build src/Prokudin.Gui/Prokudin.Gui.csproj`

- [ ] **Step 4: Commit**

```bash
git add src/Prokudin.Gui/Views/
git commit -m "refactor(gui): replace MainWindow with grid shell and status bar"
```

---

## Stage 2 — Workflow state

### Task 3: WorkflowTool enum and vertical toolbar

**Files:**
- Create: `src/Prokudin.Gui/ViewModels/WorkflowTool.cs`
- Create: `src/Prokudin.Gui/Views/WorkflowToolbar.axaml` (+`.cs`)
- Modify: `src/Prokudin.Gui/ViewModels/MainViewModel.cs`
- Modify: `src/Prokudin.Gui/Themes/Containers.axaml`

- [ ] **Step 1: Add enum and VM property**

```csharp
// WorkflowTool.cs
namespace Prokudin.Gui.ViewModels;

public enum WorkflowTool
{
    Import,
    Align,
    Crop,
    Clean,
    Color,
    Export,
}
```

```csharp
// MainViewModel.cs
[ObservableProperty]
private WorkflowTool selectedWorkflowTool = WorkflowTool.Import;

public IReadOnlyList<WorkflowTool> WorkflowTools { get; } = Enum.GetValues<WorkflowTool>();
```

- [ ] **Step 2: WorkflowToolbar.axaml**

Vertical `StackPanel` of `RadioButton` or `ToggleButton` in group bound to `SelectedWorkflowTool` via converter or code-behind `SelectWorkflowToolCommand` with parameter.

Each item: `{StaticResource Icon...}` + `TextBlock` label below.

- [ ] **Step 3: Host in MainWindow column 2**

- [ ] **Step 4: Build**

- [ ] **Step 5: Commit**

---

### Task 4: Context command bar with DataTemplates

**Files:**
- Create: `src/Prokudin.Gui/Views/ContextCommandBar.axaml` (+`.cs`)
- Create: `src/Prokudin.Gui/Views/ContextBars/ImportContextBar.axaml` (and Align, Crop, Clean, Color, Export)

- [ ] **Step 1: ContextCommandBar**

```xml
<UserControl xmlns:bars="using:Prokudin.Gui.Views.ContextBars" ...>
  <ContentControl Content="{Binding SelectedWorkflowTool}">
    <ContentControl.DataTemplates>
      <DataTemplate DataType="vm:WorkflowTool" x:DataType="vm:WorkflowTool">
        <!-- use compiled selectors or six explicit DataTemplates keyed by x:Key -->
      </DataTemplate>
    </ContentControl.DataTemplates>
  </ContentControl>
</UserControl>
```

Use six `DataTemplate` entries with `DataType` `{x:Type vm:WorkflowTool}` won't work for enum — instead bind `Content` to `MainViewModel` and use `ContentTemplateSelector` or six `IsVisible` panels:

```xml
<Panel>
  <bars:ImportContextBar IsVisible="{Binding IsImportWorkflow}" />
  <bars:AlignContextBar IsVisible="{Binding IsAlignWorkflow}" />
  ...
</Panel>
```

Add `IsImportWorkflow => SelectedWorkflowTool == WorkflowTool.Import` etc. on VM.

- [ ] **Step 2: Move toolbar buttons from old WrapPanel**

`ImportContextBar`: Open R/G/B, Triptych, order ComboBox — cut from sidebar toolbar + menu duplicates OK.

`AlignContextBar`: Auto-align, reference ComboBox, detector ComboBox, max shift NumericUpDown, Rebuild result button.

`CropContextBar`: tool mode select, crop, crop overlap, square lock, reset selection.

`CleanContextBar`: heal/clone/select toggles, brush slider, radius, detect/apply/cancel.

`ColorContextBar`: auto WB, picker toggle, reset exposure.

`ExportContextBar`: export buttons, format, settings.

- [ ] **Step 3: Delete old WrapPanel toolbar row from MainWindow**

- [ ] **Step 4: Run full test suite**

Run: `dotnet test Prokudin.slnx`

- [ ] **Step 5: Commit**

---

## Stage 3 — Right inspector + Core wiring

### Task 5: Align and levels VM properties

**Files:**
- Modify: `src/Prokudin.Gui/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add align observables**

```csharp
[ObservableProperty] private ChannelName alignReference = ChannelName.Green;
[ObservableProperty] private string alignDetector = "sift";
[ObservableProperty] private int alignMaxTranslation = 128;
[ObservableProperty] private int alignMaxFineIterations = 3;
[ObservableProperty] private int alignCoarseMaxSide = 1024;
[ObservableProperty] private bool trimDarkBorders;
```

- [ ] **Step 2: Add levels observables**

```csharp
[ObservableProperty] private LevelsMode levelsMode = LevelsMode.AutoPercentile;
[ObservableProperty] private float levelsBlackPoint;
[ObservableProperty] private float levelsWhitePoint = 1f;
[ObservableProperty] private float levelsGamma = 1f;
```

- [ ] **Step 3: Update `CurrentPipelineSettings()`**

```csharp
Align = new AlignOptions(
    Reference: AlignReference,
    Detector: AlignDetector,
    MaxFineIterations: AlignMaxFineIterations,
    TrimBorders: TrimDarkBorders,
    MaxTranslation: AlignMaxTranslation,
    CoarseAlignmentMaxSide: AlignCoarseMaxSide),
Levels = new LevelsSettings(
    Mode: LevelsMode,
    BlackPoint: LevelsBlackPoint,
    WhitePoint: LevelsWhitePoint,
    Gamma: LevelsGamma),
```

- [ ] **Step 4: Expose readonly align metadata properties for inspector**

```csharp
public string RedAlignSummary => FormatChannelAlign(ChannelName.Red);
// uses lastAligned?.AlignMetadata
```

- [ ] **Step 5: Commit**

---

### Task 6: Crop overlap and square selection

**Files:**
- Modify: `src/Prokudin.Gui/ViewModels/ImageSelectionRect.cs`
- Modify: `src/Prokudin.Gui/Views/ImagePreviewControl.axaml.cs`
- Modify: `src/Prokudin.Gui/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Square helper**

```csharp
public static ImageSelectionRect FromPoints(double x0, double y0, double x1, double y1, bool forceSquare = false)
{
    if (!forceSquare) return FromPoints(x0, y0, x1, y1);
    var dx = Math.Abs(x1 - x0);
    var dy = Math.Abs(y1 - y0);
    var size = Math.Max(dx, dy);
    // expand from anchor (x0,y0) ...
}
```

- [ ] **Step 2: `LockSquareSelection` property on VM; pass to preview control**

- [ ] **Step 3: `CropOverlapCommand`**

```csharp
[RelayCommand(CanExecute = nameof(CanCropOverlap))]
private async Task CropOverlap()
{
    if (lastAligned is null) return;
    PushUndo();
    var (cropped, info) = AlignedChannelCropper.CropToLargestFullOverlap(lastAligned);
    SetPreparedChannels(cropped);
    lastAligned = cropped;
    var (rgb, _) = ReconstructionPipeline.BuildRgb(cropped, CurrentPipelineSettings());
    ResultSlot.Result = rgb;
    AppendLog(FormatCropInfo(info));
}
```

- [ ] **Step 4: `ResetCropCommand` clears `SelectionRect`**

- [ ] **Step 5: Commit**

---

### Task 7: Expand HealOptions and AutoCleanSettingsSnapshot

**Files:**
- Modify: `src/Prokudin.Gui/Services/AutoCleanSettingsSnapshot.cs`
- Modify: `src/Prokudin.Gui/ViewModels/MainViewModel.cs`
- Modify: `tests/Prokudin.Gui.Tests/JsonAutoCleanSettingsStoreTests.cs`

- [ ] **Step 1: Extend snapshot record with all HealOptions GUI fields**

- [ ] **Step 2: Add VM properties + wire `CreateHealOptions()`**

```csharp
return new HealOptions(
    Mode: UseCrossChannelHealing ? HealingMode.CrossChannelGuided : HealingMode.CurrentChannelOnly,
    SubMode: UseTeleaHealing ? HealingSubMode.Telea : HealingSubMode.Patch,
    PatchRadius: HealPatchRadius,
    SearchRadius: HealSearchRadius,
    // ... all TZ fields
    DebugOutput: DebugHealOutput,
    Diagnostics: CreateDiagnostics());
```

- [ ] **Step 3: Tests for JSON round-trip**

- [ ] **Step 4: Commit**

---

### Task 8: Inspector panels

**Files:**
- Create: `src/Prokudin.Gui/Views/Inspector/InspectorPanel.axaml` (+`.cs`)
- Create: six `*Inspector.axaml` files

- [ ] **Step 1: InspectorPanel header** (common selected channel / size / state / zoom)

- [ ] **Step 2: CleanInspector.axaml** — all sections from TZ §11 using `HeaderedContainer` per section

- [ ] **Step 3: Remaining five inspectors** — bind to VM properties; relocate controls from old toolbar

- [ ] **Step 4: Channel slot state badge**

`ChannelSlotViewModel.State` updated in `SetChannel`, `SetPreparedChannels`, after heal/crop.

`ChannelSlotCard.axaml` add `Border` with `Text="{Binding StateLabel}"`.

- [ ] **Step 5: Rename Agg → Sensitivity in all XAML**

- [ ] **Step 6: Run tests + manual smoke**

- [ ] **Step 7: Commit**

---

## Stage 4 — Menu, About, Help

### Task 9: Complete menu and dialogs

**Files:**
- Modify: `src/Prokudin.Gui/Views/MainWindow.axaml`
- Create: `src/Prokudin.Gui/Views/AboutDialog.axaml` (+`.cs`)
- Create: `src/Prokudin.Gui/Views/KeyboardShortcutsDialog.axaml` (+`.cs`)
- Modify: `src/Prokudin.Gui/ViewModels/MainViewModel.cs`

- [ ] **Step 1: AboutDialog**

```xml
<Window Title="About Prokudin" SizeToContent="WidthAndHeight" CanResize="False">
  <StackPanel Margin="24" Spacing="8" MinWidth="320">
    <TextBlock Text="Prokudin" FontSize="20" FontWeight="SemiBold" />
    <TextBlock Text="RGB reconstruction tool for Prokudin-Gorskii channel images." TextWrapping="Wrap" />
    <TextBlock Text="{Binding VersionText}" Classes="MutedCaption" />
    <TextBlock Text=".NET / Avalonia desktop application." Classes="MutedCaption" />
    <Button Content="OK" HorizontalAlignment="Right" Click="OnClose" />
  </StackPanel>
</Window>
```

Version from `Assembly.GetExecutingAssembly().GetName().Version`.

- [ ] **Step 2: KeyboardShortcutsDialog** — static TextBlock list

- [ ] **Step 3: Menu items** View, Tools, Help per TZ §16; `Exit` shuts down desktop lifetime

- [ ] **Step 4: `ShowAboutCommand`, `ShowKeyboardShortcutsCommand`**

- [ ] **Step 5: Commit**

---

## Stage 5 — Theme and UI settings

### Task 10: JsonUiSettingsStore and theme

**Files:**
- Create: `src/Prokudin.Gui/Services/UiSettings.cs`
- Create: `src/Prokudin.Gui/Services/IUiSettingsStore.cs`
- Create: `src/Prokudin.Gui/Services/JsonUiSettingsStore.cs`
- Create: `src/Prokudin.Gui/Services/ThemeService.cs`
- Create: `tests/Prokudin.Gui.Tests/JsonUiSettingsStoreTests.cs`
- Modify: `src/Prokudin.Gui/App.axaml.cs`
- Modify: `src/Prokudin.Gui/Views/MainWindow.axaml.cs`

- [ ] **Step 1: Store tests**

```csharp
[Fact]
public void RoundTrip_PreservesThemeAndPanelWidths()
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
    var store = new JsonUiSettingsStore(path);
    var settings = new UiSettings { ThemeMode = AppThemeMode.Dark, LeftPanelWidth = 300 };
    store.Save(settings);
    store.Load().Should().BeEquivalentTo(settings);
}
```

- [ ] **Step 2: Implement store** (mirror `JsonExportSettingsStore` error handling)

- [ ] **Step 3: ThemeService**

```csharp
public static void Apply(AppThemeMode mode)
{
    if (Application.Current is not { } app) return;
    app.RequestedThemeVariant = mode switch
    {
        AppThemeMode.Light => ThemeVariant.Light,
        AppThemeMode.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
```

- [ ] **Step 4: Load/save panel sizes on splitter drag** (code-behind `DragCompleted` → VM `SaveUiSettings()`)

- [ ] **Step 5: Menu View → Theme radio items**

- [ ] **Step 6: Commit**

---

## Stage 6 — Polish

### Task 11: Acceptance pass

- [ ] **Step 1: Run full test suite**

Run: `dotnet test Prokudin.slnx`

- [ ] **Step 2: Verify TZ §22 checklist manually at 1280×800 and 1920×1080**

- [ ] **Step 3: Fix disabled states on context bar commands per workflow**

- [ ] **Step 4: Remove dead XAML/styles from old toolbar**

- [ ] **Step 5: Update `AGENTS.md` GUI Facts section** (panel layout, workflow toolbar, inspector, ui-settings.json)

- [ ] **Step 6: Final commit**

```bash
git commit -m "feat(gui): complete restoration workspace redesign"
```

---

## Self-review (spec coverage)

| TZ § | Task |
| --- | --- |
| Layout grid + splitters | Task 2 |
| Channel cards + badges | Task 8 |
| Workflow toolbar | Task 3 |
| Context command bar | Task 4 |
| Right inspector | Task 8 |
| Processing log collapse | Task 2 (ProcessingLogPanel) |
| Status bar | Task 2 |
| Menu / About / Shortcuts | Task 9 |
| Theme + persistence | Task 10 |
| Levels / gamma (option C) | Task 1, 5 |
| Align params | Task 5 |
| Crop overlap / square | Task 6 |
| Heal advanced + Sensitivity rename | Task 7, 8 |
| Acceptance criteria | Task 11 |

No placeholders remain. Core scope limited to `LevelsSettings` + pipeline wire-up.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-24-prokudin-ui-redesign.md`.

**Two execution options:**

1. **Subagent-Driven (recommended)** — fresh subagent per task, review between tasks, fast iteration
2. **Inline Execution** — execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
