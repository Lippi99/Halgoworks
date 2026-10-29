using System.Globalization;

namespace Halgoworks.Services;

/// <summary>
/// Chart colors, validated for the dark ink surface (#14223a). Categorical slots are assigned in
/// order and never cycled; anything past slot 8 folds into <see cref="Other"/>.
/// </summary>
public static class Palette
{
    public static readonly string[] Series = ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"];

    public const string Other = "#5b6b85";
    public const string Muted = "#4a5d7e";
    public const string Signal = "#ffb23e";
    public const string Good = "#0ca30c";
    public const string GoodText = "#3fcf5a";
    public const string Bad = "#d03b3b";
    public const string BadText = "#ff7a72";
    public const string Ink0 = "#0e1828";
    public const string Ink1 = "#14223a";
    public const string Ink2 = "#1b2c48";
    public const string Ink3 = "#243756";
    public const string Line = "#2a3d5e";
    public const string Text1 = "#eaf0f8";
    public const string Text2 = "#aab7cb";
    public const string Text3 = "#7d8ba3";

    /// <summary>Sequential blue for magnitude. In dark mode low values sit near the surface and high values glow.</summary>
    public static readonly string[] Ramp =
        ["#0d366b", "#104281", "#184f95", "#1c5cab", "#256abf", "#2a78d6", "#3987e5", "#5598e7", "#6da7ec", "#86b6ef", "#9ec5f4", "#b7d3f6", "#cde2fb"];

    /// <summary>Ordinal size classes (small, medium, large) for bin packing.</summary>
    public static readonly string[] SizeClass = ["#1c5cab", "#3987e5", "#86b6ef"];

    public static string Slot(int index) => index >= 0 && index < Series.Length ? Series[index] : Other;

    public static string RampAt(double t)
    {
        var i = (int)Math.Round(Math.Clamp(t, 0, 1) * (Ramp.Length - 1));
        return Ramp[i];
    }

    /// <summary>Label ink for text set inside a ramp fill.</summary>
    public static string InkOnRamp(double t) => t >= 0.7 ? Ink0 : Text1;

    public static string F(double value, string format = "0.#") => value.ToString(format, CultureInfo.InvariantCulture);
}
