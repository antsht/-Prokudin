namespace Prokudin.Core.Retouch;

internal readonly record struct LinearModel(double A, double B, double C, int Count);

internal static class LinearModelFitter
{
    public static LinearModel Fit(
        IReadOnlyList<float> target,
        IReadOnlyList<float> guide1,
        IReadOnlyList<float> guide2,
        bool robustFit)
    {
        var model = FitSubset(target, guide1, guide2, _ => true);
        if (!robustFit || model.Count < 8)
        {
            return model;
        }

        var residuals = new float[target.Count];
        for (var i = 0; i < target.Count; i++)
        {
            residuals[i] = Math.Abs(target[i] - Predict(model, guide1[i], guide2[i]));
        }

        Array.Sort(residuals);
        var threshold = residuals[(int)Math.Floor((residuals.Length - 1) * 0.90)];
        return FitSubset(
            target,
            guide1,
            guide2,
            i => Math.Abs(target[i] - Predict(model, guide1[i], guide2[i])) <= threshold);
    }

    public static float Predict(LinearModel model, float guide1, float guide2) =>
        (float)((model.A * guide1) + (model.B * guide2) + model.C);

    private static LinearModel FitSubset(
        IReadOnlyList<float> target,
        IReadOnlyList<float> guide1,
        IReadOnlyList<float> guide2,
        Func<int, bool> include)
    {
        var count = 0;
        var sumX1 = 0.0;
        var sumX2 = 0.0;
        var sumY = 0.0;
        var sumX1X1 = 0.0;
        var sumX1X2 = 0.0;
        var sumX2X2 = 0.0;
        var sumX1Y = 0.0;
        var sumX2Y = 0.0;

        for (var i = 0; i < target.Count; i++)
        {
            if (!include(i))
            {
                continue;
            }

            var x1 = guide1[i];
            var x2 = guide2[i];
            var y = target[i];
            count++;
            sumX1 += x1;
            sumX2 += x2;
            sumY += y;
            sumX1X1 += x1 * x1;
            sumX1X2 += x1 * x2;
            sumX2X2 += x2 * x2;
            sumX1Y += x1 * y;
            sumX2Y += x2 * y;
        }

        if (count == 0)
        {
            return new LinearModel(0.0, 0.0, 0.0, 0);
        }

        var matrix = new[,]
        {
            { sumX1X1 + 1e-6, sumX1X2, sumX1 },
            { sumX1X2, sumX2X2 + 1e-6, sumX2 },
            { sumX1, sumX2, count + 1e-6 },
        };
        var vector = new[] { sumX1Y, sumX2Y, sumY };
        if (!Solve3x3(matrix, vector, out var solution))
        {
            return new LinearModel(0.0, 0.0, sumY / count, count);
        }

        return new LinearModel(solution[0], solution[1], solution[2], count);
    }

    private static bool Solve3x3(double[,] matrix, double[] vector, out double[] solution)
    {
        var augmented = new double[3, 4];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                augmented[row, column] = matrix[row, column];
            }

            augmented[row, 3] = vector[row];
        }

        for (var pivot = 0; pivot < 3; pivot++)
        {
            var bestRow = pivot;
            var bestValue = Math.Abs(augmented[pivot, pivot]);
            for (var row = pivot + 1; row < 3; row++)
            {
                var value = Math.Abs(augmented[row, pivot]);
                if (value > bestValue)
                {
                    bestValue = value;
                    bestRow = row;
                }
            }

            if (bestValue < 1e-12)
            {
                solution = [];
                return false;
            }

            if (bestRow != pivot)
            {
                for (var column = pivot; column < 4; column++)
                {
                    (augmented[pivot, column], augmented[bestRow, column]) =
                        (augmented[bestRow, column], augmented[pivot, column]);
                }
            }

            var pivotValue = augmented[pivot, pivot];
            for (var column = pivot; column < 4; column++)
            {
                augmented[pivot, column] /= pivotValue;
            }

            for (var row = 0; row < 3; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                var factor = augmented[row, pivot];
                for (var column = pivot; column < 4; column++)
                {
                    augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
        }

        solution = [augmented[0, 3], augmented[1, 3], augmented[2, 3]];
        return true;
    }
}
