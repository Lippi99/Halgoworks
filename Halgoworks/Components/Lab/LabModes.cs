namespace Halgoworks.Components.Lab;

/// <summary>The ways to use a lab: watch one algorithm, race several, solve it yourself, or run them all on a big instance.</summary>
public static class LabModes
{
    public const string Watch = "watch";
    public const string Race = "race";
    public const string Play = "play";
    public const string Scale = "scale";

    private static readonly SegOption<string> WatchOption = new(Watch, "Watch", Hint: "Step through an algorithm");
    private static readonly SegOption<string> RaceOption = new(Race, "Race", Hint: "Run two or three algorithms side by side");
    private static readonly SegOption<string> PlayOption = new(Play, "Play", Hint: "Solve it yourself and compare with the algorithms");
    private static readonly SegOption<string> ScaleOption = new(Scale, "Scale", Hint: "Every algorithm on a much bigger instance, timed");

    /// <summary>Labs where solving by hand makes sense get a Play mode.</summary>
    public static IReadOnlyList<SegOption<string>> WithPlay { get; } = [WatchOption, RaceOption, PlayOption, ScaleOption];

    public static IReadOnlyList<SegOption<string>> WithoutPlay { get; } = [WatchOption, RaceOption, ScaleOption];

    public static string Parse(string? value, bool play = true) => value switch
    {
        Race => Race,
        Scale => Scale,
        Play when play => Play,
        _ => Watch,
    };
}
