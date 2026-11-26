namespace Perron.Bench.Reporting;

/// <summary>A mean with a 95 % bootstrap confidence interval.</summary>
public sealed record Estimate(double Mean, double Low, double High, int N)
{
    /// <summary>True if the interval does not contain 0 (used for paired differences).</summary>
    public bool ExcludesZero => N > 1 && (Low > 0 || High < 0);
}

/// <summary>Statistics of the report (plan.md, section 6): means and percentile bootstrap over tasks.</summary>
public static class Statistics
{
    public const int Resamples = 2000;
    public const int Seed = 20261126;

    /// <summary>
    /// Mean of <paramref name="values"/> (one value per task) with a 95 % percentile bootstrap interval, resampling the
    /// tasks with replacement; deterministic for a fixed seed.
    /// </summary>
    public static Estimate? Bootstrap(IReadOnlyList<double> values, int resamples = Resamples, int seed = Seed)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var mean = values.Average();
        if (values.Count == 1)
        {
            return new Estimate(mean, mean, mean, 1);
        }

        var random = new Random(seed);
        var means = new double[resamples];
        for (var r = 0; r < resamples; r++)
        {
            double sum = 0;
            for (var i = 0; i < values.Count; i++)
            {
                sum += values[random.Next(values.Count)];
            }

            means[r] = sum / values.Count;
        }

        Array.Sort(means);
        return new Estimate(mean, Percentile(means, 0.025), Percentile(means, 0.975), values.Count);
    }

    /// <summary>Linear interpolation between closest ranks of a sorted array.</summary>
    public static double Percentile(double[] sorted, double p)
    {
        var position = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }
}
