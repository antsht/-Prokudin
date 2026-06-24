namespace Prokudin.Gui.Services;

public interface IUiSettingsStore
{
    UiSettings Load();

    void Save(UiSettings settings);
}
