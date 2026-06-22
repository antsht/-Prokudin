using System.Collections.Concurrent;
using FluentAssertions;
using Prokudin.Core.Processing;

namespace Prokudin.Core.Tests.Processing;

public sealed class PixelParallelTests
{
    [Fact]
    public void For_WritesEveryIndex()
    {
        var values = new int[PixelParallel.MinimumParallelIterations + 1024];

        PixelParallel.For(0, values.Length, i => values[i] = i + 1);

        values.Should().OnlyContain(value => value > 0);
        values[0].Should().Be(1);
        values[^1].Should().Be(values.Length);
    }

    [Fact]
    public void For_UsesSequentialBranchForSmallWorkloads()
    {
        var threadIds = new HashSet<int>();

        PixelParallel.For(0, 128, _ => threadIds.Add(Environment.CurrentManagedThreadId));

        threadIds.Should().ContainSingle();
    }

    [Fact]
    public void ForRows_ProcessesRows()
    {
        var rows = new ConcurrentBag<int>();

        PixelParallel.ForRows(128, rows.Add);

        rows.Should().BeEquivalentTo(Enumerable.Range(0, 128));
    }

    [Fact]
    public void For_WithThreadLocalState_ReducesValues()
    {
        var gate = new object();
        var sum = 0L;

        PixelParallel.For(
            0,
            PixelParallel.MinimumParallelIterations + 1024,
            localInit: () => 0L,
            body: static (i, local) => local + i,
            localFinally: local =>
            {
                lock (gate)
                {
                    sum += local;
                }
            });

        var count = PixelParallel.MinimumParallelIterations + 1024L;
        sum.Should().Be((count - 1) * count / 2);
    }

    [Fact]
    public void Invoke_RunsAllActions()
    {
        var values = new int[3];

        PixelParallel.Invoke(
            () => values[0] = 1,
            () => values[1] = 2,
            () => values[2] = 3);

        values.Should().Equal(1, 2, 3);
    }
}
