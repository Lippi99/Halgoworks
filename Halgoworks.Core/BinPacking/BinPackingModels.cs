namespace Halgoworks.Core.BinPacking;

public sealed record BinItem(int Id, int Size);

public sealed record BinPackingInstance(string Name, int Capacity, IReadOnlyList<BinItem> Items, string Unit = "")
{
    public int TotalSize => Items.Sum(i => i.Size);

    /// <summary>L1 bound: even with no wasted space you need the total size divided by capacity, rounded up.</summary>
    public int LowerBound => (TotalSize + Capacity - 1) / Capacity;
}

public enum BinRule
{
    NextFit,
    FirstFit,
    BestFit,
    WorstFit,
    FirstFitDecreasing,
    BestFitDecreasing,
}

public enum Probe
{
    None,
    TooFull,
    Fits,
    Chosen,
}

public sealed record BinPackingStep : Step
{
    /// <summary>Item ids in each bin, bottom to top.</summary>
    public IReadOnlyList<int[]> Bins { get; init; } = [];

    /// <summary>Items still waiting, in processing order.</summary>
    public IReadOnlyList<int> Queue { get; init; } = [];

    /// <summary>The item being placed.</summary>
    public int? Item { get; init; }

    /// <summary>What the algorithm found when it checked each existing bin.</summary>
    public IReadOnlyList<Probe> Probes { get; init; } = [];

    /// <summary>Index of the bin the item goes into. Equal to <c>Bins.Count</c> when a new bin is about to open.</summary>
    public int? Target { get; init; }

    /// <summary>The item that just landed, for the drop animation.</summary>
    public int? Placed { get; init; }

    /// <summary>Bins Next Fit has closed and will never look at again.</summary>
    public IReadOnlyList<bool> Closed { get; init; } = [];
}
