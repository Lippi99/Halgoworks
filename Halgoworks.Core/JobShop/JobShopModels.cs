namespace Halgoworks.Core.JobShop;

public sealed record Operation(int Job, int Index, int Machine, int Duration);

public sealed record Job(int Id, string Name, IReadOnlyList<Operation> Ops, int? Due)
{
    public int TotalWork => Ops.Sum(o => o.Duration);
}

public sealed record JobShopInstance(string Name, IReadOnlyList<Job> Jobs, IReadOnlyList<string> Machines, int? Optimum)
{
    public bool HasDueDates => Jobs.All(j => j.Due is not null);

    /// <summary>No schedule can beat the longest job or the busiest machine.</summary>
    public int LowerBound => Math.Max(
        Jobs.Max(j => j.TotalWork),
        Enumerable.Range(0, Machines.Count).Max(m => Jobs.SelectMany(j => j.Ops).Where(o => o.Machine == m).Sum(o => o.Duration)));

    public int OperationCount => Jobs.Sum(j => j.Ops.Count);
}

public readonly record struct OpRef(int Job, int Index);

public sealed record ScheduledOp(int Job, int Index, int Machine, int Start, int End);

/// <summary>An operation waiting to be dispatched, at its earliest possible time.</summary>
public sealed record Candidate(int Job, int Index, int Machine, int Start, int End, bool Conflict, bool Earliest, bool Chosen);

public enum DispatchRule
{
    Mwkr,
    Spt,
    Lpt,
    Lwkr,
    Fcfs,
    Edd,
}

public sealed record JobShopStep : Step
{
    public IReadOnlyList<ScheduledOp> Scheduled { get; init; } = [];

    public IReadOnlyList<Candidate> Candidates { get; init; } = [];

    /// <summary>Giffler–Thompson's cut time: the earliest finish among waiting operations.</summary>
    public int? Cut { get; init; }

    /// <summary>The machine being decided.</summary>
    public int? Machine { get; init; }

    public OpRef? Focus { get; init; }

    public IReadOnlyList<OpRef> Critical { get; init; } = [];

    public int Makespan { get; init; }

    /// <summary>Tabu search: the two operations whose order was swapped.</summary>
    public IReadOnlyList<OpRef> Swapped { get; init; } = [];

    /// <summary>Tabu search: makespan after each iteration, and the best so far.</summary>
    public IReadOnlyList<int> History { get; init; } = [];

    public IReadOnlyList<int> BestHistory { get; init; } = [];
}
