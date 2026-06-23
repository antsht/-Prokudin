using System.Runtime.ExceptionServices;
using Prokudin.Core.Diagnostics;

namespace Prokudin.Core.Processing;

public static class PixelParallel
{
    public const int MinimumParallelIterations = 32_768;

    private static readonly ParallelOptions Options = new()
    {
        MaxDegreeOfParallelism = Environment.ProcessorCount,
    };

    public static void For(int fromInclusive, int toExclusive, Action<int> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (toExclusive < fromInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(toExclusive));
        }

        if (toExclusive - fromInclusive < MinimumParallelIterations || Environment.ProcessorCount <= 1)
        {
            for (var i = fromInclusive; i < toExclusive; i++)
            {
                body(i);
            }

            return;
        }

        Parallel.For(fromInclusive, toExclusive, Options, body);
    }

    public static void ForRows(int height, Action<int> body)
    {
        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        For(0, height, body);
    }

    public static void For<TLocal>(
        int fromInclusive,
        int toExclusive,
        Func<TLocal> localInit,
        Func<int, TLocal, TLocal> body,
        Action<TLocal> localFinally)
    {
        ArgumentNullException.ThrowIfNull(localInit);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(localFinally);

        if (toExclusive < fromInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(toExclusive));
        }

        if (toExclusive - fromInclusive < MinimumParallelIterations || Environment.ProcessorCount <= 1)
        {
            var local = localInit();
            for (var i = fromInclusive; i < toExclusive; i++)
            {
                local = body(i, local);
            }

            localFinally(local);
            return;
        }

        Parallel.For(
            fromInclusive,
            toExclusive,
            Options,
            localInit,
            (i, _, local) => body(i, local),
            localFinally);
    }

    public static void Invoke(params Action[] actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        if (actions.Length == 0)
        {
            return;
        }

        if (actions.Length == 1 || Environment.ProcessorCount <= 1)
        {
            foreach (var action in actions)
            {
                action();
            }

            return;
        }

        try
        {
            Parallel.Invoke(Options, actions);
        }
        catch (AggregateException exception) when (exception.InnerExceptions.Count == 1)
        {
            ExceptionDispatchInfo.Capture(exception.InnerExceptions[0]).Throw();
            throw;
        }
    }
}
