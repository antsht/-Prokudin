using FluentAssertions;
using Prokudin.Core.Alignment;
using Prokudin.Core.Diagnostics;
using Prokudin.Core.Imaging;

namespace Prokudin.Core.Tests.Diagnostics;

public sealed class ChannelAlignerDiagnosticsTests
{
    [Fact]
    public void AlignChannel_LogsFeaturelessIdentity_WhenVarianceIsZero()
    {
        var capture = new CapturingProcessingDiagnostics
        {
            Options = new ProcessingDiagnosticsOptions(ProcessingLogCategory.PipelineStage, IncludeTimings: false),
        };
        var diagnostics = new FilteringProcessingDiagnostics(capture, capture.Options);
        var reference = ImageBuffer.Filled(32, 32, 0.5f);
        var moving = ImageBuffer.Filled(32, 32, 0.5f);

        var result = ChannelAligner.AlignChannel(reference, moving, diagnostics: diagnostics);

        result.TransformKind.Should().Be("identity");
        capture.Lines.Should().Contain(line => line.Contains("featureless identity", StringComparison.Ordinal));
    }
}
