using System.Globalization;

namespace Halgoworks.Core;

/// <summary>
/// One frame of an algorithm trace: a snapshot of the state plus an explanation of
/// what the algorithm just did and why. Problem-specific steps add their own state.
/// </summary>
public abstract record Step
{
    /// <summary>Short headline for the frame.</summary>
    public string Title { get; init; } = "";

    /// <summary>Narration. Supports <c>**bold**</c>, <c>==focus==</c> and <c>`code`</c> markup.</summary>
    public string Text { get; init; } = "";

    /// <summary>Zero-based pseudo-code line being executed, or -1 for none.</summary>
    public int Line { get; init; } = -1;

    /// <summary>Coarse stage of the algorithm, used to label the timeline.</summary>
    public string Phase { get; init; } = "";

    /// <summary>Variables worth watching at this point, like a debugger's watch window.</summary>
    public IReadOnlyList<Watch> Watches { get; init; } = [];
}

public readonly record struct Watch(string Name, string Value)
{
    public static Watch Of(string name, int value) => new(name, value.ToString(CultureInfo.InvariantCulture));
    public static Watch Of(string name, double value) => new(name, Num.F1(value));
    public static Watch Of(string name, string value) => new(name, value);
}

public enum AlgorithmKind
{
    Exact,
    Heuristic,
    LocalSearch,
}

/// <summary>Static description of an algorithm: what it is, its cost, and the pseudo-code that steps point into.</summary>
public sealed record AlgorithmInfo(
    string Id,
    string Name,
    AlgorithmKind Kind,
    string Summary,
    string Time,
    string Space,
    IReadOnlyList<string> Code);

/// <summary>Culture-independent number formatting for narration.</summary>
public static class Num
{
    public static string F1(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    public static string F2(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Pct(double ratio) => (ratio * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>Joins names as "a, b and c".</summary>
    public static string List(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => "nothing",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }
}
