using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prokudin.Gui.Services;
using Prokudin.Gui.Services.Project;
using Prokudin.Gui.Views;

namespace Prokudin.Gui.ViewModels;

public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly IAutosaveStore autosaveStore;
    private readonly IRecentProjectsStore recentProjectsStore;
    private readonly IUiSettingsStore uiSettingsStore;
    private readonly IProcessingDiagnosticsSettingsStore diagnosticsSettingsStore;
    private readonly TaskCompletionSource<StartupChoice?> choiceCompletion = new();

    public WelcomeViewModel(
        IAutosaveStore autosaveStore,
        IRecentProjectsStore recentProjectsStore,
        IUiSettingsStore uiSettingsStore,
        IProcessingDiagnosticsSettingsStore diagnosticsSettingsStore)
    {
        this.autosaveStore = autosaveStore;
        this.recentProjectsStore = recentProjectsStore;
        this.uiSettingsStore = uiSettingsStore;
        this.diagnosticsSettingsStore = diagnosticsSettingsStore;

        AutosaveInfo = autosaveStore.GetInfo();
        RecentProjects = recentProjectsStore.Load();
    }

    public AutosaveInfo AutosaveInfo { get; }

    public IReadOnlyList<RecentProjectEntry> RecentProjects { get; }

    public bool HasAutosave => AutosaveInfo.Exists;

    public string AutosaveSummary =>
        AutosaveInfo.SavedAt is { } savedAt
            ? $"Last saved {savedAt.ToLocalTime():g}"
            : "No autosave available";

    public string? AutosaveLinkedProjectSummary =>
        string.IsNullOrWhiteSpace(AutosaveInfo.LinkedProjectPath)
            ? null
            : $"Linked project: {AutosaveInfo.LinkedProjectPath}";

    public Task<StartupChoice?> WaitForChoiceAsync() => choiceCompletion.Task;

    [RelayCommand]
    private void RecoverAutosave() =>
        Complete(new StartupChoice { Type = StartupChoiceType.RecoverAutosave });

    [RelayCommand]
    private void NewProject() =>
        Complete(new StartupChoice { Type = StartupChoiceType.NewProject });

    [RelayCommand]
    private void OpenOther() =>
        Complete(new StartupChoice { Type = StartupChoiceType.OpenOther });

    [RelayCommand]
    private void OpenRecent(string path) =>
        Complete(new StartupChoice { Type = StartupChoiceType.OpenRecent, ProjectPath = path });

    [RelayCommand]
    private async Task OpenSettings(Window? owner)
    {
        if (owner is null)
        {
            return;
        }

        var viewModel = new SettingsDialogViewModel(uiSettingsStore, diagnosticsSettingsStore);
        await new SettingsDialog { DataContext = viewModel }.ShowDialog(owner);
    }

    public void Cancel() => choiceCompletion.TrySetResult(null);

    private void Complete(StartupChoice choice) => choiceCompletion.TrySetResult(choice);
}
