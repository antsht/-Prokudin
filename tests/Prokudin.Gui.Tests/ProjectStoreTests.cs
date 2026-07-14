using System.Collections.Concurrent;
using System.Threading;
using FluentAssertions;
using Prokudin.Core.Alignment;
using Prokudin.Core.Color;
using Prokudin.Core.Imaging;
using Prokudin.Core.Retouch;
using Prokudin.Gui.Services.Project;
using Prokudin.Gui.ViewModels;

namespace Prokudin.Gui.Tests;

public sealed class JsonProjectStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsRetouchProvenanceSidecars()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prokudin-provenance-{Guid.NewGuid():N}");
        try
        {
            var provenance = new RetouchProvenanceMap(2, 2);
            provenance[1] = RetouchProvenance.HighConfidenceHealing;
            provenance[3] = RetouchProvenance.CloneStamp;
            var package = new ProjectPackage
            {
                Document = ProjectStateMapper.ToDocument(new ProjectCapture
                {
                    TriptychOrder = "BGR",
                    AlignDetector = "sift",
                }, includeExportOverride: false),
                Red = ImageBuffer.Filled(2, 2, 0.4f),
                RedProvenance = provenance,
            };

            var store = new JsonProjectStore();
            await store.SaveAsync(folder, package);
            var loaded = await store.LoadAsync(folder);

            loaded.RedProvenance![1].Should().Be(RetouchProvenance.HighConfidenceHealing);
            loaded.RedProvenance[3].Should().Be(RetouchProvenance.CloneStamp);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_WithoutProvenanceSidecar_MarksLegacyPixelsUnknown()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prokudin-legacy-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonProjectStore();
            var package = new ProjectPackage
            {
                Document = ProjectStateMapper.ToDocument(new ProjectCapture
                {
                    TriptychOrder = "BGR",
                    AlignDetector = "sift",
                }, includeExportOverride: false),
                Red = ImageBuffer.Filled(2, 2, 0.4f),
            };
            await store.SaveAsync(folder, package);

            var loaded = await store.LoadAsync(folder);

            loaded.RedProvenance![0].Should().Be(RetouchProvenance.Unknown);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

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
                ColorTemperature = 15,
                ColorTint = -8,
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
            loaded.Document.Color.Temperature.Should().Be(15);
            loaded.Document.Color.Tint.Should().Be(-8);
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

public sealed class JsonAutosaveStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsRetouchProvenance()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prokudin-autosave-provenance-{Guid.NewGuid():N}");
        var store = new JsonAutosaveStore(folder);
        var provenance = new RetouchProvenanceMap(2, 2);
        provenance[2] = RetouchProvenance.LowConfidenceHealing;
        var package = new ProjectPackage
        {
            Document = new ProjectDocument { DisplayName = "Provenance autosave" },
            Red = ImageBuffer.Filled(2, 2, 0.4f),
            RedProvenance = provenance,
        };

        try
        {
            await store.SaveAsync(package);
            var loaded = await store.LoadAsync();

            loaded.RedProvenance![2].Should().Be(RetouchProvenance.LowConfidenceHealing);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetInfo_ReadsManifestMetadataWithoutLoadingChannels()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prokudin-autosave-{Guid.NewGuid():N}");
        var store = new JsonAutosaveStore(folder);
        var package = new ProjectPackage
        {
            Document = new ProjectDocument
            {
                DisplayName = "Autosave session",
                LinkedProjectPath = @"C:\Projects\demo",
            },
            Red = new ImageBuffer(2, 2, [0.1f, 0.2f, 0.3f, 0.4f]),
        };

        try
        {
            await store.SaveAsync(package);

            var info = store.GetInfo();

            info.Exists.Should().BeTrue();
            info.SavedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
            info.DisplayName.Should().Be("Autosave session");
            info.LinkedProjectPath.Should().Be(@"C:\Projects\demo");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetInfo_CompletesUnderCapturedSynchronizationContext()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prokudin-autosave-{Guid.NewGuid():N}");
        var store = new JsonAutosaveStore(folder);
        var package = new ProjectPackage
        {
            Document = new ProjectDocument { DisplayName = "Deadlock guard" },
            Red = new ImageBuffer(2, 2, [0.1f, 0.2f, 0.3f, 0.4f]),
        };

        try
        {
            await store.SaveAsync(package);

            using var context = new QueuedSynchronizationContext();
            var info = await context.RunAsync(store.GetInfo).WaitAsync(TimeSpan.FromSeconds(2));

            info.Exists.Should().BeTrue();
            info.DisplayName.Should().Be("Deadlock guard");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();

        public QueuedSynchronizationContext()
        {
            var thread = new Thread(ProcessQueue)
            {
                IsBackground = true,
                Name = "QueuedSynchronizationContext",
            };
            if (OperatingSystem.IsWindows())
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));

        public Task<T> RunAsync<T>(Func<T> func)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ =>
            {
                try
                {
                    completion.SetResult(func());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }, null);

            return completion.Task;
        }

        private void ProcessQueue()
        {
            SetSynchronizationContext(this);
            foreach (var item in queue.GetConsumingEnumerable())
            {
                item.Callback(item.State);
            }
        }

        public void Dispose() => queue.CompleteAdding();
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
            WhiteBalanceSource = WhiteBalanceSource.WhitePick,
            WhitePickRadius = 7,
            WhitePickWarningAcknowledged = true,
            RedExposureStops = 0.5,
            LevelsMode = LevelsMode.Manual,
            LevelsBlackPoint = 0.1,
            LevelsWhitePoint = 0.9,
            LevelsGamma = 1.2,
            RedLevelsBlackPoint = 0.2,
            RedLevelsWhitePoint = 0.8,
            RedLevelsGamma = 1.1,
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
        state.WhiteBalanceSource.Should().Be(WhiteBalanceSource.WhitePick);
        state.WhitePickRadius.Should().Be(7);
        state.WhitePickWarningAcknowledged.Should().BeTrue();
        state.RedLevelsBlackPoint.Should().Be(0.2);
        state.RedLevelsWhitePoint.Should().Be(0.8);
        state.RedLevelsGamma.Should().Be(1.1);
        state.Clean.Sensitivity.Should().Be(42);
        state.ToolMode.Should().Be(EditorToolMode.Heal);
        document.Export.Should().NotBeNull();
    }

    [Fact]
    public void ToApplyState_LegacyColourSettings_MapExistingPipetteToWhitePick()
    {
        var document = new ProjectDocument
        {
            Color = new ProjectColorSettings
            {
                AutoWhiteBalance = false,
                PipetteX = 10,
                PipetteY = 20,
            },
        };

        var state = ProjectStateMapper.ToApplyState(document);

        state.WhiteBalanceSource.Should().Be(WhiteBalanceSource.WhitePick);
        state.PipetteX.Should().Be(10);
        state.PipetteY.Should().Be(20);
        state.WhitePickRadius.Should().Be(3);
    }
}
