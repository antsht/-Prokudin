using FluentAssertions;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Tests.Processing;

public sealed class CudaBackendProbeTests
{
    [Fact]
    public void GetBackendKind_DoesNotRequireCuda()
    {
        var backend = CudaBackendProbe.GetBackendKind();

        backend.Should().BeOneOf(AccelerationBackendKind.Cpu, AccelerationBackendKind.CudaAvailable);
    }
}
