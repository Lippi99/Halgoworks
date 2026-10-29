namespace Halgoworks.Components.Charts;

public sealed record BarDatum(string Label, double Value, string Display, bool Highlight = false, string? Note = null);

/// <summary>A reference value drawn across the plot, such as a lower bound or the known optimum.</summary>
public sealed record RefLine(string Label, double Value);

public sealed record LineSeries(string Name, IReadOnlyList<double?> Values, string Color, bool Step = false);

public static class Scale
{
    /// <summary>Round tick values (1, 2, 5 × 10^k) covering [min, max].</summary>
    public static List<double> NiceTicks(double min, double max, int target = 5)
    {
        if (max <= min) max = min + 1;
        var raw = (max - min) / target;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * magnitude).First(s => s >= raw);
        var ticks = new List<double>();
        for (var v = Math.Floor(min / step) * step; v <= max + step * 1e-9; v += step)
            ticks.Add(Math.Round(v, 10));
        return ticks;
    }

    /// <summary>"10" with a superscript exponent, for log-scale axes.</summary>
    public static string Power(int exponent)
    {
        const string digits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
        var text = Math.Abs(exponent).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sup = string.Concat(text.Select(c => digits[c - '0']));
        return "10" + (exponent < 0 ? "⁻" : "") + sup;
    }
}
