# Короткое ТЗ для coding agent: редизайн UI Prokudin GUI

> **Status (2026-06-27):** Implemented. Current GUI behavior is documented in `AGENTS.md` and `docs/architecture.md`. This file is kept as historical context for the redesign; do not treat it as active implementation scope.

## 1. Цель

Переделать интерфейс Avalonia-приложения **Prokudin** из архаичного набора разрозненных кнопок в профессиональную рабочую среду для эксперта-реставратора изображений.

Интерфейс должен поддерживать свободный экспертный workflow:

```text
Import → Align → Crop → Clean / Retouch → Color → Export
```

Это **не wizard**: пользователь должен свободно переключаться между этапами, возвращаться назад, повторять операции и вручную настраивать параметры.

## 2. Scope

Изменять только GUI-слой:

```text
src/Prokudin.Gui
```

Не менять:

```text
src/Prokudin.Core
src/Prokudin.Cli
tests/Prokudin.Core.Tests
```

Core-алгоритмы alignment, crop, color, retouch, healing, auto-clean и export должны остаться без изменений.

## 3. Новый layout MainWindow

Переписать `MainWindow.axaml` на структуру:

```text
MainWindow
├── Top menu
├── Context command bar
├── Main workspace
│   ├── Left channel panel
│   ├── Vertical workflow toolbar
│   ├── Image preview area
│   └── Right inspector panel
├── Processing log panel
└── Status bar
```

### Grid-схема

```text
Rows:
0: Menu
1: Context command bar
2: Main workspace
3: Horizontal GridSplitter
4: Processing log
5: Status bar

Columns inside Main workspace:
0: Left channel panel
1: Vertical GridSplitter
2: Workflow toolbar
3: Image preview
4: Vertical GridSplitter
5: Right inspector panel
```

### Размеры панелей

| Панель | Default | Min | Max |
|---|---:|---:|---:|
| Left channel panel | 260 px | 220 px | 420 px |
| Workflow toolbar | 88 px | 72 px | 120 px |
| Right inspector | 360 px | 300 px | 520 px |
| Processing log | 150 px | 44 px | 360 px |

Требования:

- левая панель должна растягиваться через `GridSplitter`;
- правая inspector panel должна растягиваться через `GridSplitter`;
- нижний processing log должен растягиваться через `GridSplitter`;
- processing log должен уметь сворачиваться и разворачиваться.

## 4. Левая панель каналов

Оставить карточки каналов:

```text
Channels
├── Red
├── Green
├── Blue
└── Result
```

Для каждой карточки показывать:

- thumbnail;
- channel name;
- image size;
- state badge:
  - `Empty`;
  - `Loaded`;
  - `Aligned`;
  - `Retouched`;
  - `Result`;
- маленькие action-кнопки:
  - load / replace для `Red`, `Green`, `Blue`;
  - export/settings для `Result`.

Требования:

- сохранить drag/drop swap между `Red`, `Green`, `Blue`;
- `Result` не должен участвовать в swap;
- selected card должна визуально выделяться;
- при выборе карточки должен обновляться preview и right inspector.

## 5. Vertical workflow toolbar

Добавить вертикальный toolbar рядом с preview:

```text
Import
Align
Crop
Clean
Color
Export
```

Добавить enum:

```csharp
public enum WorkflowTool
{
    Import,
    Align,
    Crop,
    Clean,
    Color,
    Export
}
```

В `MainViewModel` добавить свойство:

```csharp
public WorkflowTool SelectedWorkflowTool { get; set; }
```

Требования к кнопкам:

- icon;
- label;
- tooltip;
- selected visual state;
- режимы всегда доступны;
- конкретные команды внутри режима могут быть disabled.

## 6. Context command bar

Заменить текущий длинный общий toolbar на верхнюю context command bar.

Context bar показывает только быстрые действия текущего workflow. Подробные параметры должны быть в right inspector.

### Import

```text
Open Red | Open Green | Open Blue | Open Triptych | Order: BGR/RGB
```

### Align

```text
Auto-align | Reference: Green | Detector: SIFT/ORB | Max shift | Rebuild result
```

### Crop

```text
Selection | Crop to selection | Crop overlap | Square crop | Reset crop
```

### Clean

```text
Tool: Heal / Clone / Auto-mask | Brush | Radius | Detect mask | Apply | Cancel
```

### Color

```text
Auto WB | WB picker | Reset exposure
```

### Export

```text
Export result | Export channels | Format | Settings
```

## 7. Right inspector panel

Right inspector — основная панель подробных экспертных параметров.

Сверху всегда показывать:

```text
Selected
Channel: Red / Green / Blue / Result
Size: 3692 × 3071
State: Aligned / Retouched / Result
Zoom: Fit / 1:1
```

## 8. Inspector: Import

```text
Input
- Triptych order: BGR / RGB
- Input mode: Separate channels / Triptych
- Trim dark borders: on/off
- Source bit depth: UInt8 / UInt16 / Float32 readonly
```

## 9. Inspector: Align

```text
Alignment
- Reference channel: Red / Green / Blue
- Detector: SIFT / ORB
- Max translation: NumericUpDown
- Max align iterations: NumericUpDown
- Coarse alignment max side: NumericUpDown

Result metadata readonly:
- Red: transform kind, inliers, shifts
- Green: reference / transform kind
- Blue: transform kind, inliers, shifts
```

## 10. Inspector: Crop

```text
Crop
- Selection X
- Selection Y
- Selection Width
- Selection Height
- Lock square selection: on/off
- Crop selected channel only
- Crop all aligned channels
- Crop result and prepared R/G/B together
```

Важно сохранить текущее поведение: если crop применяется к `Result`, prepared `R/G/B` должны обрезаться тем же прямоугольником, чтобы retouch и rebuild оставались aligned.

## 11. Inspector: Clean / Retouch

Right inspector для `Clean` должен быть самым подробным.

```text
Retouch Tool
- Tool: Heal / Clone Stamp / Auto-clean Mask
- Brush size
- Radius
- Quality: Quality / Balanced / Fast
- Sensitivity
```

Переименовать текущий параметр `Agg` в `Sensitivity`.

### Healing Model

```text
Healing Model
- Cross-channel guided healing: on/off
- Fallback sub-mode: Patch / Telea
- Use local linear prediction: on/off
- Use guided patch search: on/off
- Use robust fit: on/off
```

### Mask Preparation

```text
Mask Preparation
- Merge nearby defects: on/off
- Merge gap px
- Expand healing area px
- Max expanded component area
```

Порядок подготовки маски должен остаться:

```text
raw auto mask → merge nearby defects → expand healing area → final healing mask
```

### Patch Search

```text
Patch Search
- Patch radius
- Search radius
- Safety radius
- Context radius
- Min training pixels
```

### Prediction Blend

```text
Prediction Blend
- Prediction alpha min
- Prediction alpha max
- Feather sigma
- Max allowed error
- Large component conservative scale
```

### Debug

```text
Debug
- Debug heal output: on/off
- Show mask overlay: on/off
- Review mask on result: on/off
- Show confidence preview if available
```

Advanced-настройки не скрывать полностью: интерфейс рассчитан на эксперта-реставратора.

## 12. Inspector: Color

```text
Color
- Auto white balance: on/off
- White balance picker mode
- R exposure
- G exposure
- B exposure
- Reset exposure
- Levels black point
- Levels white point
- Gamma
```

## 13. Inspector: Export

```text
Export
- Format: PNG / JPEG / TIFF
- Max side
- PNG compression
- JPEG quality
- TIFF compression
- Export result
- Export prepared channels
- Open output folder after export: on/off
```

Сохранить существующее поведение persistence export settings.

## 14. Processing log

Оформить нижний лог как diagnostic panel.

Header:

```text
Processing log     Backends  Pipeline  CPU parallel  Timings     Clear     Collapse
```

Требования:

- resize через horizontal `GridSplitter`;
- collapse/expand;
- сохранить auto-scroll вниз;
- при ручной прокрутке вверх auto-scroll должен временно отключаться;
- кнопка `Clear`.

## 15. Status bar

Сделать одну нижнюю status bar:

```text
Left: current status message
Center: selected slot + dimensions
Right: busy/progress indicator
```

Пример:

```text
Auto-align complete. Red: homography, 200 inliers | Selected: Result 3692×3071 | Ready
```

## 16. Menu

Добавить структуру меню:

```text
File
  Open Red...
  Open Green...
  Open Blue...
  Open Triptych...
  Export Result...
  Export Channels...
  Exit

Edit
  Undo
  Redo

View
  Theme: Light / Dark / System
  Fit to Window
  1:1
  Show Left Channel Panel
  Show Right Inspector
  Show Processing Log

Process
  Auto-align
  Rebuild Result
  Detect Auto-clean Mask
  Apply Auto-clean Mask
  Cancel Auto-clean Mask

Tools
  Selection
  Heal Brush
  Clone Stamp
  White Balance Picker

Help
  User Guide
  Keyboard Shortcuts
  About Prokudin
```

## 17. About dialog

Добавить modal dialog:

```text
About Prokudin
```

Минимальное содержимое:

```text
Prokudin
RGB reconstruction tool for Prokudin-Gorskii channel images.
Version: <assembly version>
.NET / Avalonia desktop application.
```

## 18. Theme system

Добавить поддержку тем:

```csharp
public enum AppThemeMode
{
    Light,
    Dark,
    System
}
```

В настройках GUI:

```csharp
public AppThemeMode ThemeMode { get; set; }
```

Требования:

- переключатель темы в `View → Theme`;
- тёмная тема должна переключаться без перезапуска приложения;
- дополнительно можно добавить компактный theme toggle в правом верхнем углу;
- выбор темы должен сохраняться.

## 19. UI settings persistence

Создать сервис:

```text
Services/JsonUiSettingsStore.cs
```

Сохранять:

```csharp
public sealed class UiSettings
{
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;
    public double LeftPanelWidth { get; set; } = 260;
    public double RightInspectorWidth { get; set; } = 360;
    public double ProcessingLogHeight { get; set; } = 150;
    public bool IsProcessingLogVisible { get; set; } = true;
    public bool IsRightInspectorVisible { get; set; } = true;
    public WorkflowTool SelectedWorkflowTool { get; set; } = WorkflowTool.Import;
}
```

Файл настроек:

```text
%LocalAppData%/Prokudin/ui-settings.json
```

## 20. UX rules

- Интерфейс рассчитан на эксперта, поэтому advanced-настройки не удалять и не прятать глубоко.
- Не показывать все параметры в верхней панели.
- Верхняя панель — только быстрые действия.
- Right inspector — подробная настройка.
- Workflow toolbar — навигация по режимам, не wizard.
- Не блокировать переход между режимами.
- Недоступные команды должны быть disabled.
- Основной preview должен занимать максимум полезной площади.
- `Agg` переименовать в `Sensitivity`.
- `Telea`, `Cross-channel`, `Debug heal`, `Backends`, `CPU parallel`, `Timings` должны оставаться доступными.
- Core-логику не менять.

## 21. Implementation stages

### Stage 1 — Layout refactor

- Переписать `MainWindow.axaml`.
- Добавить left/right/log splitters.
- Перенести текущий toolbar в новую структуру.
- Сохранить существующий preview control.

### Stage 2 — Workflow state

- Добавить `WorkflowTool`.
- Добавить `SelectedWorkflowTool`.
- Сделать vertical workflow toolbar.
- Сделать context command bar с разным содержимым по workflow.

### Stage 3 — Right inspector

- Добавить dynamic inspector panel.
- Разнести параметры по секциям:
  - Import;
  - Align;
  - Crop;
  - Clean;
  - Color;
  - Export.
- Перенести туда expert-настройки healing, mask, diagnostics, export.

### Stage 4 — Menu, About, Help

- Добавить `View`.
- Добавить `Help`.
- Добавить `About Prokudin` modal dialog.
- Добавить `Keyboard Shortcuts` dialog.

### Stage 5 — Theme support

- Добавить `AppThemeMode`.
- Добавить `JsonUiSettingsStore`.
- Реализовать `Light / Dark / System`.
- Сохранять выбранную тему.

### Stage 6 — Polish

- Проверить disabled states.
- Проверить работу на размере окна `1280×800`.
- Проверить работу на размере окна `1920×1080`.
- Проверить сохранение размеров панелей.
- Проверить, что все старые команды доступны.

## 22. Acceptance criteria

1. Левая панель каналов растягивается.
2. Правая inspector panel растягивается.
3. Нижний processing log растягивается и сворачивается.
4. Верхний длинный toolbar заменён на context command bar.
5. Есть vertical workflow toolbar: `Import`, `Align`, `Crop`, `Clean`, `Color`, `Export`.
6. Переключение workflow не блокирует пользователя.
7. Right inspector показывает подробные параметры выбранного workflow.
8. Advanced-настройки доступны в inspector.
9. `Agg` переименован в `Sensitivity`.
10. В меню есть `View → Theme → Light / Dark / System`.
11. Тёмная тема работает без перезапуска приложения.
12. Выбранная тема и размеры панелей сохраняются между запусками.
13. В меню есть `Help → About Prokudin`.
14. Auto-align работает как раньше.
15. Retouch, heal brush, clone stamp и auto-clean работают как раньше.
16. Crop работает как раньше.
17. Auto WB и exposure работают как раньше.
18. Export result и export channels работают как раньше.
19. Undo/Redo продолжают работать.
20. Core-проекты не изменены.
