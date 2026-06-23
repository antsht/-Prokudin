namespace Prokudin.Gui.Services;

public interface IProcessingDiagnosticsSettingsStore
{
    ProcessingDiagnosticsSettings Load();

    void Save(ProcessingDiagnosticsSettings settings);
}
