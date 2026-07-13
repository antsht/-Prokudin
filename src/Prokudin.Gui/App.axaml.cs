using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Prokudin.Gui.Services;
using Prokudin.Gui.Services.Project;
using Prokudin.Gui.ViewModels;
using Prokudin.Gui.Views;

namespace Prokudin.Gui;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = StartDesktopAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartDesktopAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            await ShowWelcomeAndMainAsync(desktop);
        }
        catch (Exception exception)
        {
            StartupExceptionReporter.Report(exception);
            ShowStartupError(desktop);
        }
    }

    private static async Task ShowWelcomeAndMainAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var autosaveStore = new JsonAutosaveStore();
        var recentProjectsStore = new JsonRecentProjectsStore();
        var uiSettingsStore = new JsonUiSettingsStore();
        var diagnosticsSettingsStore = new JsonProcessingDiagnosticsSettingsStore();

        var welcomeViewModel = new WelcomeViewModel(
            autosaveStore,
            recentProjectsStore,
            uiSettingsStore,
            diagnosticsSettingsStore);
        var welcomeWindow = new WelcomeWindow { DataContext = welcomeViewModel };
        desktop.MainWindow = welcomeWindow;
        welcomeWindow.Show();
        welcomeWindow.Activate();

        var choice = await welcomeViewModel.WaitForChoiceAsync();
        if (choice is null)
        {
            desktop.Shutdown();
            return;
        }

        var mainWindow = new MainWindow();
        var mainViewModel = new MainViewModel(
            new StorageFileDialogService(mainWindow),
            new JsonExportSettingsStore(),
            diagnosticsSettingsStore,
            new JsonAutoCleanSettingsStore(),
            uiSettingsStore,
            new GitHubReleaseUpdateChecker(),
            new JsonProjectStore(),
            autosaveStore,
            recentProjectsStore);
        mainWindow.DataContext = mainViewModel;
        mainViewModel.AttachOwnerWindow(mainWindow);
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        welcomeWindow.Close();

        await mainViewModel.CompleteStartupAsync(choice);
    }

    private static void ShowStartupError(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var errorWindow = new Window
        {
            Title = "Prokudin startup error",
            Width = 580,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBlock
            {
                Margin = new Thickness(24),
                Text = $"Prokudin could not start. Details were written to:{Environment.NewLine}{StartupExceptionReporter.DefaultLogPath}",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            },
        };
        errorWindow.Closed += (_, _) => desktop.Shutdown();
        desktop.MainWindow = errorWindow;
        errorWindow.Show();
        errorWindow.Activate();
    }
}
