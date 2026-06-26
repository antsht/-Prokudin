using FluentAssertions;
using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;
using Prokudin.Gui.Services.Project;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Tests;

public sealed class JsonProjectStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsProjectState()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prokudin-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var store = new JsonProjectStore();
            var capture = new ProjectCapture
            {
                TriptychOrder = "BGR",
                AlignDetector = "sift",
                AlignReference = ChannelName.Green,
                AlignMaxTranslation = 96,
                SelectedWorkflow = WorkflowTool.Align,
                ToolMode = EditorToolMode.Select,
                PreviewZoom = PreviewZoomMode.OneToOne,
            };
            var package = new ProjectPackage
            {
                Document = ProjectStateMapper.ToDocument(capture, includeExportOverride: false),
                Red = new ImageBuffer(2, 2, [0.1f, 0.2f, 0.3f, 0.4f]),
                Green = new ImageBuffer(2, 2, [0.2f, 0.3f, 0.4f, 0.5f]),
                Blue = new ImageBuffer(2, 2, [0.3f, 0.4f, 0.5f, 0.6f]),
                Result = new RgbImageBuffer(2, 2, Enumerable.Repeat(0.5f, 12).ToArray()),
            };

            await store.SaveAsync(folder, package);
            store.IsValidProjectFolder(folder).Should().BeTrue();

            var loaded = await store.LoadAsync(folder);
            loaded.Document.Import.TriptychOrder.Should().Be("BGR");
            loaded.Document.Align.MaxTranslation.Should().Be(96);
            loaded.Red.Should().NotBeNull();
            loaded.Red!.Width.Should().Be(2);
            loaded.Result.Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}

public sealed class JsonRecentProjectsStoreTests
{
    [Fact]
    public void RecordOpened_KeepsMostRecentThree()
    {
        var path = Path.Combine(Path.GetTempPath(), $"recent-{Guid.NewGuid():N}.json");
        var store = new JsonRecentProjectsStore(path);
        var folders = Enumerable.Range(1, 4)
            .Select(i =>
            {
                var folder = Path.Combine(Path.GetTempPath(), $"prokudin-recent-{Guid.NewGuid():N}");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, ProjectFileNames.Manifest), "{}");
                return folder;
            })
            .ToList();

        try
        {
            foreach (var folder in folders)
            {
                store.RecordOpened(folder, Path.GetFileName(folder));
            }

            store.Load().Select(entry => entry.Path).Should().Equal(folders[3], folders[2], folders[1]);
        }
        finally
        {
            File.Delete(path);
            foreach (var folder in folders)
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }
    }
}

public sealed class ProjectStateMapperTests
{
    [Fact]
    public void ToDocument_AndToApplyState_PreserveAlignAndColorSettings()
    {
        var capture = new ProjectCapture
        {
            TriptychOrder = "RGB",
            AlignDetector = "orb",
            AlignReference = ChannelName.Blue,
            AlignMaxTranslation = 64,
            AlignMaxFineIterations = 2,
            AlignCoarseMaxSide = 512,
            TrimDarkBorders = true,
            SelectionRect = new ImageSelectionRect(1, 2, 3, 4),
            LockSquareSelection = true,
            AutoWhiteBalance = false,
            RedExposureStops = 0.5,
            LevelsMode = LevelsMode.Manual,
            LevelsBlackPoint = 0.1,
            LevelsWhitePoint = 0.9,
            LevelsGamma = 1.2,
            PipetteX = 10,
            PipetteY = 20,
            AutoCleanSensitivity = 42,
            BrushSize = 16,
            SelectedWorkflow = WorkflowTool.Clean,
            ToolMode = EditorToolMode.Heal,
            PreviewZoom = PreviewZoomMode.FitToWindow,
        };

        var document = ProjectStateMapper.ToDocument(capture, includeExportOverride: true);
        var state = ProjectStateMapper.ToApplyState(document);

        state.TriptychOrder.Should().Be("RGB");
        state.AlignDetector.Should().Be("orb");
        state.AlignReference.Should().Be(ChannelName.Blue);
        state.SelectionRect.Should().Be(new ImageSelectionRect(1, 2, 3, 4));
        state.LevelsGamma.Should().Be(1.2);
        state.Clean.Sensitivity.Should().Be(42);
        state.ToolMode.Should().Be(EditorToolMode.Heal);
        document.Export.Should().NotBeNull();
    }
}
