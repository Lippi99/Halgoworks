using Halgoworks.Core.MaxFlow;

namespace Halgoworks.Core.Matching;

public sealed record AllowedPair(int Left, int Right);

/// <summary>Two groups and the pairs that may be matched, such as developers and the tickets they can handle.</summary>
public sealed record MatchingInstance(
    string Name,
    string LeftTitle,
    string RightTitle,
    IReadOnlyList<string> Left,
    IReadOnlyList<string> Right,
    IReadOnlyList<AllowedPair> Pairs);

public sealed record MatchingStep : Step
{
    /// <summary>Indices into the instance's pairs that are matched right now.</summary>
    public IReadOnlyList<int> Matched { get; init; } = [];

    /// <summary>The augmenting path's pair edges: added ones become matches, removed ones are undone.</summary>
    public IReadOnlyList<int> PathAdds { get; init; } = [];

    public IReadOnlyList<int> PathRemoves { get; init; } = [];

    /// <summary>Pairs examined in this step of the search.</summary>
    public IReadOnlyList<int> Scanned { get; init; } = [];

    /// <summary>Nodes in focus, as (side, index): side 0 is left, 1 is right.</summary>
    public IReadOnlyList<(int Side, int Index)> Focus { get; init; } = [];

    /// <summary>The same moment in the underlying flow network.</summary>
    public MaxFlowStep Flow { get; init; } = new();
}

public static class MatchingInstances
{
    public static MatchingInstance DevelopersAndTickets() => new(
        "Developers and tickets", "Developers", "Tickets",
        ["Ana", "Bruno", "Chen", "Dara", "Eli", "Femi"],
        ["Login bug", "Payments API", "Mobile crash", "Search", "Data export", "Dark mode"],
        [
            new(0, 0), new(0, 1),
            new(1, 0), new(1, 3),
            new(2, 1), new(2, 2), new(2, 4),
            new(3, 2),
            new(4, 3), new(4, 5),
            new(5, 2), new(5, 4),
        ]);

    /// <summary>Greedy gets stuck here: the obvious first choices block a perfect matching.</summary>
    public static MatchingInstance GreedyTrap() => new(
        "Greedy trap", "Students", "Projects",
        ["Ivy", "Jon", "Kai", "Lea"],
        ["Robot", "Game", "Website", "App"],
        [
            new(0, 0), new(0, 1),
            new(1, 0),
            new(2, 1), new(2, 2),
            new(3, 2), new(3, 3),
        ]);

    public static MatchingInstance Random(int left, int right, double density, int seed)
    {
        var rng = new Random(seed);
        var pairs = new List<AllowedPair>();
        for (var l = 0; l < left; l++)
        {
            var any = false;
            for (var r = 0; r < right; r++)
            {
                if (rng.NextDouble() >= density) continue;
                pairs.Add(new AllowedPair(l, r));
                any = true;
            }

            if (!any) pairs.Add(new AllowedPair(l, rng.Next(right)));
        }

        return new MatchingInstance($"Random #{seed}", "Workers", "Tasks",
            Enumerable.Range(1, left).Select(i => $"W{i}").ToList(),
            Enumerable.Range(1, right).Select(i => $"T{i}").ToList(),
            pairs.OrderBy(p => p.Left).ThenBy(p => p.Right).ToList());
    }
}

public static class MatchingSolver
{
    public static AlgorithmInfo Info(FlowAlgorithm algorithm)
    {
        var flow = MaxFlowSolver.Info(algorithm);
        return flow with
        {
            Summary = $"Turn the matching into a flow network (source → {{left}} → {{right}} → sink, every pipe of capacity 1) and run {flow.Name}. Each unit of flow is one matched pair.",
        };
    }

    /// <summary>
    /// The reduction: node 0 is the source, then the left group, then the right group, then the sink.
    /// Edges are source→left, then one edge per allowed pair, then right→sink, all with capacity 1.
    /// </summary>
    public static FlowNetwork ToNetwork(MatchingInstance m)
    {
        var nodes = new List<FlowNode> { new(0, "source", 0, 0.5) };
        for (var i = 0; i < m.Left.Count; i++) nodes.Add(new FlowNode(nodes.Count, m.Left[i], 0.33, Spread(i, m.Left.Count)));
        for (var i = 0; i < m.Right.Count; i++) nodes.Add(new FlowNode(nodes.Count, m.Right[i], 0.67, Spread(i, m.Right.Count)));
        var sink = nodes.Count;
        nodes.Add(new FlowNode(sink, "sink", 1, 0.5));

        var edges = new List<FlowEdge>();
        for (var i = 0; i < m.Left.Count; i++) edges.Add(new FlowEdge(edges.Count, 0, 1 + i, 1));
        foreach (var p in m.Pairs) edges.Add(new FlowEdge(edges.Count, 1 + p.Left, 1 + m.Left.Count + p.Right, 1));
        for (var i = 0; i < m.Right.Count; i++) edges.Add(new FlowEdge(edges.Count, 1 + m.Left.Count + i, sink, 1));
        return new FlowNetwork(m.Name, nodes, edges, 0, sink);
    }

    private static double Spread(int i, int count) => count == 1 ? 0.5 : 0.04 + 0.92 * i / (count - 1);

    public static IReadOnlyList<MatchingStep> Trace(MatchingInstance m, FlowAlgorithm algorithm)
    {
        var network = ToNetwork(m);
        var firstPair = m.Left.Count;
        var flowSteps = MaxFlowSolver.Trace(network, algorithm);
        var steps = new List<MatchingStep>();

        int? PairOf(Arc a) => a.Edge >= firstPair && a.Edge < firstPair + m.Pairs.Count ? a.Edge - firstPair : null;
        string Name(AllowedPair p) => $"{m.Left[p.Left]}–{m.Right[p.Right]}";
        (int, int)? NodeOf(int node) => node >= 1 && node <= m.Left.Count ? (0, node - 1)
            : node > m.Left.Count && node < network.Sink ? (1, node - 1 - m.Left.Count)
            : null;

        foreach (var f in flowSteps)
        {
            var matched = Enumerable.Range(0, m.Pairs.Count).Where(i => f.Flow[firstPair + i] > 0).ToList();
            var adds = f.Path.Where(a => a.Forward).Select(PairOf).OfType<int>().ToList();
            var removes = f.Path.Where(a => !a.Forward).Select(PairOf).OfType<int>().ToList();
            var scanned = f.Scanned.Select(PairOf).OfType<int>().ToList();
            var focus = Enumerable.Range(0, network.Nodes.Count)
                .Where(v => f.Nodes.Count > v && f.Nodes[v] == NodeState.Current)
                .Select(NodeOf).OfType<(int, int)>().ToList();

            var step = new MatchingStep
            {
                Title = f.Title,
                Text = f.Text,
                Line = f.Line,
                Phase = f.Phase,
                Watches = f.Watches,
                Matched = matched,
                PathAdds = adds,
                PathRemoves = removes,
                Scanned = scanned,
                Focus = focus,
                Flow = f,
            };

            // Retell the flow steps that matter in matching terms; the search steps already read well with names.
            if (f.Phase == "Setup")
            {
                step = step with
                {
                    Title = "Turn it into a flow problem",
                    Text = $"Add a **source** connected to every {Singular(m.LeftTitle)} and a **sink** reached from every {Singular(m.RightTitle)}, and give every pipe capacity 1. A {Singular(m.LeftTitle)} can then send at most one unit, a {Singular(m.RightTitle)} can receive at most one, and every unit of flow that gets through picks one allowed pair. So the **maximum flow is the maximum matching**.",
                };
            }
            else if (f.Path.Count > 0 && f.Pushed is null)
            {
                var detail = removes.Count == 0
                    ? $"It simply adds {Name(m.Pairs[adds[0]])}: both are still free."
                    : $"It alternates: add {Num.List(adds.Select(i => Name(m.Pairs[i])))}, and undo {Num.List(removes.Select(i => Name(m.Pairs[i])))}. Rerouting an existing match frees someone for a new one.";
                step = step with
                {
                    Title = removes.Count == 0 ? "A free pair: match them" : "An augmenting path through existing matches",
                    Text = $"The search found a path from the source to the sink. {detail} Flipping the path always gives **one more** matched pair than before.",
                };
            }
            else if (f.Pushed is not null)
            {
                step = step with
                {
                    Title = $"{matched.Count} pair{(matched.Count == 1 ? "" : "s")} matched",
                    Text = $"Push one unit along the path. Matched now: {Num.List(matched.Select(i => Name(m.Pairs[i])))}.",
                };
            }
            else if (f.Phase == "Done")
            {
                var unmatchedLeft = Enumerable.Range(0, m.Left.Count).Where(l => !matched.Any(i => m.Pairs[i].Left == l)).Select(l => m.Left[l]).ToList();
                var perfect = matched.Count == Math.Min(m.Left.Count, m.Right.Count);
                step = step with
                {
                    Title = $"Maximum matching: {matched.Count} pairs",
                    Text = $"No augmenting path is left, so no assignment can match more than **{matched.Count}**. " + (perfect
                        ? "Everyone on the smaller side got a partner: a perfect matching."
                        : $"{Num.List(unmatchedLeft)} stay{(unmatchedLeft.Count == 1 ? "s" : "")} unmatched, and the minimum cut proves it isn't the algorithm's fault: some group of {m.LeftTitle.ToLowerInvariant()} simply has too few allowed {m.RightTitle.ToLowerInvariant()} between them (Hall's theorem)."),
                };
            }

            steps.Add(step);
        }

        return steps;
    }

    /// <summary>Indices of the matched pairs, via max flow without recording steps. Used by the Scale view.</summary>
    public static IReadOnlyList<int> Solve(MatchingInstance m, FlowAlgorithm algorithm)
    {
        var result = MaxFlowSolver.Solve(ToNetwork(m), algorithm);
        var first = m.Left.Count;
        return Enumerable.Range(0, m.Pairs.Count).Where(i => result.Flow[first + i] > 0).ToList();
    }

    /// <summary>Takes allowed pairs in order whenever both sides are still free. Fast, not always maximum.</summary>
    public static IReadOnlyList<int> Greedy(MatchingInstance m)
    {
        var usedLeft = new HashSet<int>();
        var usedRight = new HashSet<int>();
        var chosen = new List<int>();
        for (var i = 0; i < m.Pairs.Count; i++)
        {
            var p = m.Pairs[i];
            if (usedLeft.Contains(p.Left) || usedRight.Contains(p.Right)) continue;
            usedLeft.Add(p.Left);
            usedRight.Add(p.Right);
            chosen.Add(i);
        }

        return chosen;
    }

    private static string Singular(string title) => title.ToLowerInvariant().TrimEnd('s');
}
