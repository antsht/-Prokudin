namespace Prokudin.Gui.Services;

public interface IAutoCleanSettingsStore
{
    AutoCleanSettingsSnapshot Load();

    void Save(AutoCleanSettingsSnapshot settings);
}
