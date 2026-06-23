namespace Prokudin.Core.Diagnostics;

public static class ProcessingDiagnosticsAmbient
{
    private static readonly AsyncLocal<ScopedProcessingDiagnostics?> ActiveScope = new();

    internal static void Push(ScopedProcessingDiagnostics scope) => ActiveScope.Value = scope;

    internal static void Pop(ScopedProcessingDiagnostics scope)
    {
        if (ReferenceEquals(ActiveScope.Value, scope))
        {
            ActiveScope.Value = null;
        }
    }

    public static void RecordParallel(string method, long iterationCount, bool usedParallel, int maxDegree) =>
        ActiveScope.Value?.RecordParallel(method, iterationCount, usedParallel, maxDegree);
}
