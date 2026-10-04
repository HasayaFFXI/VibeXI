namespace Zerg.Core.Charts;

/// <summary>
/// The cumulative chart as a table: the same grid, thinned to a number of
/// rows a person can read.
/// </summary>
public static class Sampling
{
    /// <summary>
    /// Which grid times the table shows: every n-th, for about
    /// <paramref name="rows"/> rows, and always the last, which is the figure
    /// the chart ends on.
    /// </summary>
    public static List<int> Rows(int count, int rows = 16)
    {
        var picked = new List<int>();
        if (count <= 0) return picked;
        int stride = Math.Max(1, (int)Math.Ceiling(count / (double)rows));
        for (int i = 0; i < count; i += stride) picked.Add(i);
        if (picked[^1] != count - 1) picked.Add(count - 1);
        return picked;
    }
}
