using Prokudin.Core.Imaging;

namespace Prokudin.Gui.Services;

public interface IExportSettingsStore
{
    RgbExportSettings Load();

    void Save(RgbExportSettings settings);
}
