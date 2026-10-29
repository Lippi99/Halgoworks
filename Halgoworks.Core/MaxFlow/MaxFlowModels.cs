namespace Halgoworks.Core.MaxFlow;

/// <summary>A node with layout coordinates in [0, 1] × [0, 1].</summary>
public sealed record FlowNode(int Id, string Label, double X, double Y);

public sealed record FlowEdge(int Id, int From, int To, int Capacity);

public sealed record FlowNetwork(string Name, IReadOnlyList<FlowNode> Nodes, IReadOnlyList<FlowEdge> Edges, int Source, int Sink);

/// <summary>
/// An arc of the residual graph. A forward arc follows its edge and can carry capacity − flow more units;
/// a backward arc runs against its edge and can cancel up to flow units.
/// </summary>
public readonly record struct Arc(int Edge, bool Forward);

public enum NodeState
{
    Idle,
    Frontier,
    Visited,
    Current,
}

public enum FlowAlgorithm
{
    EdmondsKarp,
    FordFulkerson,
    Dinic,
}

public sealed record MaxFlowStep : Step
{
    /// <summary>Flow on each edge, indexed by edge id.</summary>
    public IReadOnlyList<int> Flow { get; init; } = [];

    public int Total { get; init; }

    public IReadOnlyList<NodeState> Nodes { get; init; } = [];

    /// <summary>The BFS queue or DFS stack, front first.</summary>
    public IReadOnlyList<int> Frontier { get; init; } = [];

    /// <summary>Arcs by which the search reached each node.</summary>
    public IReadOnlyList<Arc> Tree { get; init; } = [];

    /// <summary>Arcs examined in this step.</summary>
    public IReadOnlyList<Arc> Scanned { get; init; } = [];

    /// <summary>The augmenting path, from source to sink.</summary>
    public IReadOnlyList<Arc> Path { get; init; } = [];

    public Arc? Bottleneck { get; init; }

    /// <summary>Units pushed in this step.</summary>
    public int? Pushed { get; init; }

    /// <summary>After the last search: true for nodes on the source side of the minimum cut.</summary>
    public IReadOnlyList<bool>? SourceSide { get; init; }

    /// <summary>Dinic's BFS levels (-1 when unreachable).</summary>
    public IReadOnlyList<int>? Level { get; init; }

    public int Augmentations { get; init; }
}
