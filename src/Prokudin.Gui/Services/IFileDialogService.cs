namespace Prokudin.Gui.Services;

public interface IFileDialogService
{
    Task<string?> OpenImage();

    Task<string?> OpenFolder();

    Task<string?> SavePng();
}
