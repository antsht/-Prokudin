namespace Prokudin.Core.Diagnostics;

internal static class DiagnosticsDisposable
{
    public static readonly IDisposable Empty = new EmptyDisposable();

    private sealed class EmptyDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
