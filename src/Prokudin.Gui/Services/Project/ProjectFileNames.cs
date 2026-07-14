namespace Prokudin.Gui.Services.Project;

public static class ProjectFileNames
{
    public const string Manifest = "project.json";

    public const string RedChannel = "red.tif";

    public const string GreenChannel = "green.tif";

    public const string BlueChannel = "blue.tif";

    public const string Result = "result.tif";

    public const string RedProvenance = "red.provenance.bin";

    public const string GreenProvenance = "green.provenance.bin";

    public const string BlueProvenance = "blue.provenance.bin";

    public const string SaveTempFolder = "_save-tmp";

    public static bool IsProjectFile(string fileName) =>
        fileName.Equals(Manifest, StringComparison.OrdinalIgnoreCase)
        || fileName.Equals(RedChannel, StringComparison.OrdinalIgnoreCase)
        || fileName.Equals(GreenChannel, StringComparison.OrdinalIgnoreCase)
        || fileName.Equals(BlueChannel, StringComparison.OrdinalIgnoreCase)
        || fileName.Equals(Result, StringComparison.OrdinalIgnoreCase)
        || fileName.Equals(RedProvenance, StringComparison.OrdinalIgnoreCase)
        || fileName.Equals(GreenProvenance, StringComparison.OrdinalIgnoreCase)
        || fileName.Equals(BlueProvenance, StringComparison.OrdinalIgnoreCase);
}
