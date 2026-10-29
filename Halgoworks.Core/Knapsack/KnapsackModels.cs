namespace Halgoworks.Core.Knapsack;

public sealed record KnapsackItem(int Id, string Name, string Icon, int Weight, int Value)
{
    public double Density => (double)Value / Weight;
}

public sealed record KnapsackInstance(string Name, int Capacity, IReadOnlyList<KnapsackItem> Items);

public enum ItemState
{
    Idle,
    Focus,
    Taken,
    Skipped,
}

public readonly record struct GridCell(int Row, int Col);

public sealed record KnapsackStep : Step
{
    /// <summary>Snapshot of the DP table, rows = items 0..n, columns = capacity 0..W. -1 marks a cell not computed yet. Null for greedy.</summary>
    public int[][]? Table { get; init; }

    /// <summary>The cell being decided.</summary>
    public GridCell? Cell { get; init; }

    /// <summary>Cells filled together in one step (row 0, or capacities an item can't fit in).</summary>
    public IReadOnlyList<GridCell> Batch { get; init; } = [];

    public GridCell? SkipFrom { get; init; }
    public GridCell? TakeFrom { get; init; }
    public int? SkipValue { get; init; }
    public int? TakeValue { get; init; }
    public bool? Took { get; init; }

    /// <summary>Cells visited while tracing the answer back through the table.</summary>
    public IReadOnlyList<GridCell> Path { get; init; } = [];

    /// <summary>State of each item, indexed like the instance's items.</summary>
    public IReadOnlyList<ItemState> Items { get; init; } = [];

    /// <summary>Order in which items are shown (greedy sorts them by density).</summary>
    public IReadOnlyList<int> Order { get; init; } = [];

    /// <summary>Weight and value packed so far.</summary>
    public int Load { get; init; }
    public int Total { get; init; }

    /// <summary>Optimal value, shown next to heuristic results.</summary>
    public int? Optimum { get; init; }
}

public sealed record KnapsackResult(int Value, int Weight, IReadOnlyList<int> Chosen);
