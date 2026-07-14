using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;
using Prokudin.Gui.Services.Project;
using Prokudin.Gui.Views;

namespace Prokudin.Gui.ViewModels;

public sealed partial class MainViewModel
{
    public async Task CompleteStartupAsync(StartupChoice choice)
    {
        switch (choice.Type)
        {
            case StartupChoiceType.NewProject:
                ResetSession();
                break;
            case StartupChoiceType.OpenChannels:
                ResetSession();
                await OpenSeparateChannels();
                break;
            case StartupChoiceType.OpenTriptych:
                ResetSession();
                await OpenTriptych();
                break;
            case StartupChoiceType.RecoverAutosave:
                await LoadAutosaveAsync();
                break;
            case StartupChoiceType.OpenRecent:
                if (!string.IsNullOrWhiteSpace(choice.ProjectPath))
                {
                    await LoadProjectFromFolderAsync(choice.ProjectPath);
                }

                break;
            case StartupChoiceType.OpenOther:
                await OpenProjectInternalAsync();
                break;
        }
    }

    public async Task<bool> TryCloseSessionAsync()
    {
        if (!IsProjectDirty)
        {
            return true;
        }

        var result = await PromptUnsavedChangesAsync();
        return result switch
        {
            UnsavedChangesResult.Save => await SaveProjectInternalAsync(requirePath: true),
            UnsavedChangesResult.DontSave => true,
            _ => false,
        };
    }

    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    private async Task SaveProject()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            await SaveProjectAs();
            return;
        }

        await SaveProjectInternalAsync(requirePath: false);
    }

    [RelayCommand]
    private async Task SaveProjectAs()
    {
        var folder = await fileDialogService.PickProjectSaveFolder(ProjectDisplayName);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        ProjectPath = folder;
        await SaveProjectInternalAsync(requirePath: false);
    }

    [RelayCommand]
    private async Task NewProject()
    {
        if (!await ConfirmDiscardIfNeededAsync())
        {
            return;
        }

        ResetSession();
    }

    [RelayCommand]
    private async Task OpenProject()
    {
        if (!await ConfirmDiscardIfNeededAsync())
        {
            return;
        }

        await OpenProjectInternalAsync();
    }

    [RelayCommand]
    private async Task OpenRecentProject(string path)
    {
        if (!await ConfirmDiscardIfNeededAsync())
        {
            return;
        }

        await LoadProjectFromFolderAsync(path);
    }

    [RelayCommand]
    private async Task ShowSettings()
    {
        if (ownerWindow is null)
        {
            return;
        }

        var previousUi = uiSettingsStore.Load();
        var viewModel = new SettingsDialogViewModel(uiSettingsStore, diagnosticsSettingsStore);
        await new SettingsDialog { DataContext = viewModel }.ShowDialog(ownerWindow);

        var ui = uiSettingsStore.Load().Normalize();
        LoadUiSettings(ui);
        LoadDiagnosticsSettings(diagnosticsSettingsStore.Load());
        if (ui.AutosaveEnabled != previousUi.AutosaveEnabled
            || ui.AutosaveIntervalMinutes != previousUi.AutosaveIntervalMinutes)
        {
            ConfigureAutosaveTimer();
        }
    }

    private bool CanSaveProject() => IsProjectDirty;

    private async Task<bool> ConfirmDiscardIfNeededAsync()
    {
        if (!IsProjectDirty)
        {
            return true;
        }

        return await PromptUnsavedChangesAsync() switch
        {
            UnsavedChangesResult.Save => await SaveProjectInternalAsync(requirePath: true),
            UnsavedChangesResult.DontSave => true,
            _ => false,
        };
    }

    private async Task<UnsavedChangesResult> PromptUnsavedChangesAsync()
    {
        if (ownerWindow is null)
        {
            return UnsavedChangesResult.DontSave;
        }

        var dialog = new UnsavedChangesDialog();
        await dialog.ShowDialog(ownerWindow);
        return dialog.Result;
    }

    private async Task OpenProjectInternalAsync()
    {
        var folder = await fileDialogService.OpenProjectFolder();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        await LoadProjectFromFolderAsync(folder);
    }

    private async Task LoadAutosaveAsync()
    {
        if (!autosaveStore.Exists())
        {
            Status = "Autosave not found.";
            return;
        }

        try
        {
            var package = await autosaveStore.LoadAsync();
            ApplyProjectPackage(package, linkedProjectPath: package.Document.LinkedProjectPath);
            ProjectPath = package.Document.LinkedProjectPath;
            MarkProjectDirty();
            Status = "Recovered autosave.";
            AppendLog("Recovered session from autosave.");
        }
        catch (Exception ex)
        {
            Status = "Failed to recover autosave.";
            AppendLog($"Autosave recovery failed: {ex.Message}");
        }
    }

    private async Task LoadProjectFromFolderAsync(string folderPath)
    {
        if (!projectStore.IsValidProjectFolder(folderPath))
        {
            Status = "Selected folder is not a Prokudin project.";
            AppendLog($"Open project failed: missing {ProjectFileNames.Manifest} in {folderPath}.");
            return;
        }

        try
        {
            var package = await projectStore.LoadAsync(folderPath);
            ApplyProjectPackage(package, linkedProjectPath: folderPath);
            ProjectPath = folderPath;
            ClearProjectDirty();
            recentProjectsStore.RecordOpened(folderPath, package.Document.DisplayName ?? ProjectDisplayName);
            RefreshRecentProjectsMenu();
            Status = $"Opened project {ProjectDisplayName}.";
            AppendLog($"Opened project: {folderPath}");
        }
        catch (Exception ex)
        {
            Status = "Failed to open project.";
            AppendLog($"Open project failed: {ex.Message}");
        }
    }

    private async Task<bool> SaveProjectInternalAsync(bool requirePath)
    {
        if (requirePath && string.IsNullOrWhiteSpace(ProjectPath))
        {
            await SaveProjectAs();
            return !string.IsNullOrWhiteSpace(ProjectPath);
        }

        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            return false;
        }

        try
        {
            IsAutosaving = true;
            var package = BuildProjectPackage(includeExportOverride: true);
            await projectStore.SaveAsync(ProjectPath, package);
            recentProjectsStore.RecordOpened(ProjectPath, package.Document.DisplayName);
            RefreshRecentProjectsMenu();
            ClearProjectDirty();
            Status = $"Saved project {ProjectDisplayName}.";
            AppendLog($"Saved project: {ProjectPath}");
            return true;
        }
        catch (Exception ex)
        {
            Status = "Failed to save project.";
            AppendLog($"Save project failed: {ex.Message}");
            return false;
        }
        finally
        {
            IsAutosaving = false;
        }
    }

    private void ResetSession()
    {
        suppressProjectDirtyTracking = true;
        try
        {
            ClearPendingAutoCleanMask();
            ClearEditorHistory();
            RedSlot.Image = null;
            GreenSlot.Image = null;
            BlueSlot.Image = null;
            retouchProvenance.Clear();
            RedSlot.SourcePath = null;
            GreenSlot.SourcePath = null;
            BlueSlot.SourcePath = null;
            ResultSlot.Result = null;
            SetLastAligned(null);
            ProjectPath = null;
            SelectedSlot = RedSlot;
            Status = "New project.";
        }
        finally
        {
            suppressProjectDirtyTracking = false;
        }

        ClearProjectDirty();
        RefreshPreviewImageContext();
    }

    private ProjectPackage BuildProjectPackage(bool includeExportOverride)
    {
        var capture = CaptureProjectState();
        var document = ProjectStateMapper.ToDocument(capture, includeExportOverride);
        document.DisplayName = ProjectDisplayName;
        document.LinkedProjectPath = ProjectPath;

        return new ProjectPackage
        {
            Document = document,
            Red = RedSlot.Image?.Clone(),
            Green = GreenSlot.Image?.Clone(),
            Blue = BlueSlot.Image?.Clone(),
            Result = ResultSlot.Result?.Clone(),
            RedProvenance = RedSlot.Image is { } red ? GetRetouchProvenance(ChannelName.Red, red).Clone() : null,
            GreenProvenance = GreenSlot.Image is { } green ? GetRetouchProvenance(ChannelName.Green, green).Clone() : null,
            BlueProvenance = BlueSlot.Image is { } blue ? GetRetouchProvenance(ChannelName.Blue, blue).Clone() : null,
        };
    }

    private ProjectCapture CaptureProjectState() =>
        new()
        {
            DisplayName = ProjectDisplayName,
            LinkedProjectPath = ProjectPath,
            RedSourcePath = RedSlot.SourcePath,
            GreenSourcePath = GreenSlot.SourcePath,
            BlueSourcePath = BlueSlot.SourcePath,
            TriptychOrder = SelectedTriptychOrder,
            AlignReference = AlignReference,
            AlignDetector = AlignDetector,
            AlignMaxTranslation = AlignMaxTranslation,
            AlignMaxFineIterations = AlignMaxFineIterations,
            AlignCoarseMaxSide = AlignCoarseMaxSide,
            TrimDarkBorders = TrimDarkBorders,
            SelectionRect = SelectionRect,
            LockSquareSelection = LockSquareSelection,
            AutoWhiteBalance = WhiteBalanceSource == global::Prokudin.Core.Color.WhiteBalanceSource.Auto,
            WhiteBalanceSource = WhiteBalanceSource,
            WhitePickRadius = WhitePickRadius,
            WhitePickWarningAcknowledged = WhitePickWarningAcknowledged,
            RedExposureStops = RedExposureStops,
            GreenExposureStops = GreenExposureStops,
            BlueExposureStops = BlueExposureStops,
            LevelsMode = LevelsMode,
            LevelsBlackPoint = LevelsBlackPoint,
            LevelsWhitePoint = LevelsWhitePoint,
            LevelsGamma = LevelsGamma,
            RedLevelsBlackPoint = RedLevelsBlackPoint,
            RedLevelsWhitePoint = RedLevelsWhitePoint,
            RedLevelsGamma = RedLevelsGamma,
            GreenLevelsBlackPoint = GreenLevelsBlackPoint,
            GreenLevelsWhitePoint = GreenLevelsWhitePoint,
            GreenLevelsGamma = GreenLevelsGamma,
            BlueLevelsBlackPoint = BlueLevelsBlackPoint,
            BlueLevelsWhitePoint = BlueLevelsWhitePoint,
            BlueLevelsGamma = BlueLevelsGamma,
            PipetteX = whiteBalancePipetteX,
            PipetteY = whiteBalancePipetteY,
            ColorTemperature = ColorTemperature,
            ColorTint = ColorTint,
            AutoCleanQualityMode = AutoCleanQualityMode,
            AutoCleanSensitivity = AutoCleanSensitivity,
            AutoCleanRadius = AutoCleanRadius,
            HealPatchRadius = HealPatchRadius,
            HealSearchRadius = HealSearchRadius,
            HealSafetyRadius = HealSafetyRadius,
            HealContextRadius = HealContextRadius,
            HealMinTrainingPixels = HealMinTrainingPixels,
            UseCrossChannelHealing = UseCrossChannelHealing,
            UseTeleaHealing = UseTeleaHealing,
            UseLocalLinearPrediction = UseLocalLinearPrediction,
            UseGuidedPatchSearch = UseGuidedPatchSearch,
            UseRobustFit = UseRobustFit,
            AutoMergeNearbyDefects = AutoMergeNearbyDefects,
            AutoMergeDistancePx = AutoMergeDistancePx,
            AutoExpandHealingAreaPx = AutoExpandHealingAreaPx,
            HealMaxComponentArea = HealMaxComponentArea,
            HealPredictionAlphaMin = (float)HealPredictionAlphaMin,
            HealPredictionAlphaMax = (float)HealPredictionAlphaMax,
            HealFeatherSigma = (float)HealFeatherSigma,
            HealMaxAllowedError = (float)HealMaxAllowedError,
            HealLargeComponentConservativeScale = (float)HealLargeComponentConservativeScale,
            DebugHealOutput = DebugHealOutput,
            ShowHealMaskOverlay = ShowHealMaskOverlay,
            BrushSize = BrushSize,
            SelectedWorkflow = SelectedWorkflowTool,
            ToolMode = ToolMode,
            PreviewZoom = PreviewZoomMode,
            OpenOutputFolderAfterExport = OpenOutputFolderAfterExport,
            ExportSettings = CurrentExportSettings(),
        };

    private void ApplyProjectPackage(ProjectPackage package, string? linkedProjectPath)
    {
        suppressProjectDirtyTracking = true;
        try
        {
            ClearPendingAutoCleanMask();
            ClearEditorHistory();

            var state = ProjectStateMapper.ToApplyState(package.Document);
            SelectedTriptychOrder = state.TriptychOrder;
            AlignReference = state.AlignReference;
            AlignDetector = state.AlignDetector;
            AlignMaxTranslation = state.AlignMaxTranslation;
            AlignMaxFineIterations = state.AlignMaxFineIterations;
            AlignCoarseMaxSide = state.AlignCoarseMaxSide;
            TrimDarkBorders = state.TrimDarkBorders;
            SelectionRect = state.SelectionRect;
            LockSquareSelection = state.LockSquareSelection;
            WhiteBalanceSource = state.WhiteBalanceSource;
            WhitePickRadius = state.WhitePickRadius;
            WhitePickWarningAcknowledged = state.WhitePickWarningAcknowledged;
            RedExposureStops = state.RedExposureStops;
            GreenExposureStops = state.GreenExposureStops;
            BlueExposureStops = state.BlueExposureStops;
            LevelsMode = state.LevelsMode;
            LevelsBlackPoint = state.LevelsBlackPoint;
            LevelsWhitePoint = state.LevelsWhitePoint;
            LevelsGamma = state.LevelsGamma;
            RedLevelsBlackPoint = state.RedLevelsBlackPoint;
            RedLevelsWhitePoint = state.RedLevelsWhitePoint;
            RedLevelsGamma = state.RedLevelsGamma;
            GreenLevelsBlackPoint = state.GreenLevelsBlackPoint;
            GreenLevelsWhitePoint = state.GreenLevelsWhitePoint;
            GreenLevelsGamma = state.GreenLevelsGamma;
            BlueLevelsBlackPoint = state.BlueLevelsBlackPoint;
            BlueLevelsWhitePoint = state.BlueLevelsWhitePoint;
            BlueLevelsGamma = state.BlueLevelsGamma;
            whiteBalancePipetteX = state.PipetteX;
            whiteBalancePipetteY = state.PipetteY;
            ColorTemperature = state.ColorTemperature;
            ColorTint = state.ColorTint;
            LoadAutoCleanSettings(state.Clean);
            BrushSize = package.Document.Clean.BrushSize;
            SelectedWorkflowTool = state.SelectedWorkflow;
            ToolMode = state.ToolMode;
            PreviewZoomMode = state.PreviewZoom;
            OpenOutputFolderAfterExport = state.OpenOutputFolderAfterExport;

            if (state.ExportSettings is not null)
            {
                LoadExportSettings(state.ExportSettings);
            }

            RedSlot.Image = package.Red?.Clone();
            GreenSlot.Image = package.Green?.Clone();
            BlueSlot.Image = package.Blue?.Clone();
            retouchProvenance.Clear();
            if (RedSlot.Image is { } red)
            {
                SetRetouchProvenance(ChannelName.Red, red, package.RedProvenance);
            }

            if (GreenSlot.Image is { } green)
            {
                SetRetouchProvenance(ChannelName.Green, green, package.GreenProvenance);
            }

            if (BlueSlot.Image is { } blue)
            {
                SetRetouchProvenance(ChannelName.Blue, blue, package.BlueProvenance);
            }
            RedSlot.SourcePath = state.RedSourcePath;
            GreenSlot.SourcePath = state.GreenSourcePath;
            BlueSlot.SourcePath = state.BlueSourcePath;
            ResultSlot.Result = package.Result?.Clone();
            RestoreLastAlignedIfPrepared();
            SelectedSlot = RedSlot;
            ProjectPath = linkedProjectPath;
        }
        finally
        {
            suppressProjectDirtyTracking = false;
        }

        RefreshChannelStates();
        RefreshPreviewImageContext();
        NotifyAutoCleanCommands();
    }

    private void MarkProjectDirty()
    {
        if (suppressProjectDirtyTracking)
        {
            return;
        }

        IsProjectDirty = true;
    }

    private void ClearProjectDirty() => IsProjectDirty = false;

    private void RefreshRecentProjectsMenu()
    {
        RecentProjectsMenu.Clear();
        foreach (var entry in recentProjectsStore.Load())
        {
            RecentProjectsMenu.Add(entry);
        }
    }

    private void ConfigureAutosaveTimer()
    {
        autosaveTimer?.Stop();
        var ui = uiSettingsStore.Load().Normalize();
        if (!ui.AutosaveEnabled)
        {
            autosaveTimer = null;
            return;
        }

        autosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(ui.AutosaveIntervalMinutes),
        };
        autosaveTimer.Tick += (_, _) => _ = TryAutosaveAsync();
        autosaveTimer.Start();
    }

    private async Task TryAutosaveAsync()
    {
        if (!IsProjectDirty || IsBusy || isAutosavePending)
        {
            return;
        }

        if (!RedSlot.HasImage && !GreenSlot.HasImage && !BlueSlot.HasImage && ResultSlot.Result is null)
        {
            return;
        }

        var ui = uiSettingsStore.Load().Normalize();
        if (!ui.AutosaveEnabled)
        {
            return;
        }

        isAutosavePending = true;
        IsAutosaving = true;
        try
        {
            var package = BuildProjectPackage(includeExportOverride: false);
            package.Document.LinkedProjectPath = ProjectPath;
            await autosaveStore.SaveAsync(package);
            AppendLog("Autosaved session.");
        }
        catch (Exception ex)
        {
            AppendLog($"Autosave failed: {ex.Message}");
        }
        finally
        {
            isAutosavePending = false;
            IsAutosaving = false;
        }
    }

    partial void OnAlignReferenceChanged(ChannelName value)
    {
        MarkProjectDirty();
        OnPropertyChanged(nameof(CanManualNudgeRed));
        OnPropertyChanged(nameof(CanManualNudgeBlue));
        CommitManualNudgeCommand.NotifyCanExecuteChanged();
        NotifyManualNudgeCommands();
    }

    partial void OnAlignDetectorChanged(string value) => MarkProjectDirty();

    partial void OnAlignMaxTranslationChanged(int value) => MarkProjectDirty();

    partial void OnAlignMaxFineIterationsChanged(int value) => MarkProjectDirty();

    partial void OnAlignCoarseMaxSideChanged(int value) => MarkProjectDirty();

    partial void OnTrimDarkBordersChanged(bool value) => MarkProjectDirty();

    partial void OnSelectedTriptychOrderChanged(string value) => MarkProjectDirty();

    partial void OnLockSquareSelectionChanged(bool value) => MarkProjectDirty();

    partial void OnBrushSizeChanged(int value) => MarkProjectDirty();

    partial void OnPreviewZoomModeChanged(PreviewZoomMode value) => MarkProjectDirty();

    partial void OnOpenOutputFolderAfterExportChanged(bool value) => MarkProjectDirty();
}
