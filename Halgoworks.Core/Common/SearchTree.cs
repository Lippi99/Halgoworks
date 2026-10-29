namespace Halgoworks.Core;

public enum NodeStatus
{
    /// <summary>Visited and split into children.</summary>
    Branched,

    /// <summary>Cut off: its optimistic bound can't beat the best answer found so far.</summary>
    PrunedBound,

    /// <summary>Cut off: it breaks a constraint, so nothing below it is feasible.</summary>
    PrunedInfeasible,

    /// <summary>A complete solution.</summary>
    Solution,
}

/// <summary>
/// One node of a branch-and-bound search tree. <see cref="Branch"/> labels the decision on the edge from
/// its parent, such as "take Tent" or "x₃ ≤ 0".
/// </summary>
public sealed record SearchNode(int Id, int Parent, int Depth, string Branch, double Bound, double Value, NodeStatus Status, string Detail);
