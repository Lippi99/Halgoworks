using Halgoworks.Core.MaxFlow;

namespace Halgoworks.Tests;

public class MaxFlowTests
{
    public static TheoryData<int> Seeds => new() { 1, 2, 3, 4, 5, 6, 7, 8, 13, 21, 34 };

    public static TheoryData<FlowAlgorithm> Algorithms => new() { FlowAlgorithm.EdmondsKarp, FlowAlgorithm.FordFulkerson, FlowAlgorithm.Dinic };

    // Minimum over every s–t cut, by enumerating all node subsets.
    private static int BruteForceMinCut(FlowNetwork network)
    {
        var n = network.Nodes.Count;
        var best = int.MaxValue;
        for (var mask = 0; mask < 1 << n; mask++)
        {
            if ((mask & (1 << network.Source)) == 0 || (mask & (1 << network.Sink)) != 0) continue;
            var cut = network.Edges.Where(e => (mask & (1 << e.From)) != 0 && (mask & (1 << e.To)) == 0).Sum(e => e.Capacity);
            best = Math.Min(best, cut);
        }

        return best;
    }

    private static void AssertValidFlow(FlowNetwork network, IReadOnlyList<int> flow, int total)
    {
        foreach (var e in network.Edges) Assert.InRange(flow[e.Id], 0, e.Capacity);
        foreach (var node in network.Nodes)
        {
            var inflow = network.Edges.Where(e => e.To == node.Id).Sum(e => flow[e.Id]);
            var outflow = network.Edges.Where(e => e.From == node.Id).Sum(e => flow[e.Id]);
            if (node.Id == network.Source) Assert.Equal(total, outflow - inflow);
            else if (node.Id == network.Sink) Assert.Equal(total, inflow - outflow);
            else Assert.Equal(inflow, outflow);
        }
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void Presets_reach_their_known_maximum(FlowAlgorithm algorithm)
    {
        Assert.Equal(23, MaxFlowSolver.Trace(FlowNetworks.Textbook(), algorithm)[^1].Total);
        Assert.Equal(20, MaxFlowSolver.Trace(FlowNetworks.WaterSupply(), algorithm)[^1].Total);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_algorithm_finds_a_valid_maximum_flow_and_a_matching_cut(int seed)
    {
        var network = FlowNetworks.Random(2 + seed % 2, seed);
        var expected = BruteForceMinCut(network);
        Assert.Equal(expected, MaxFlowSolver.Solve(network).Total);

        foreach (var algorithm in new[] { FlowAlgorithm.EdmondsKarp, FlowAlgorithm.FordFulkerson, FlowAlgorithm.Dinic })
        {
            var last = MaxFlowSolver.Trace(network, algorithm)[^1];
            Assert.Equal(expected, last.Total);
            AssertValidFlow(network, last.Flow, last.Total);

            var cut = MaxFlowSolver.CutEdges(network, last.SourceSide!);
            Assert.Equal(expected, cut.Sum(e => e.Capacity));
            Assert.All(cut, e => Assert.Equal(e.Capacity, last.Flow[e.Id]));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_step_keeps_flows_within_capacity(int seed)
    {
        var network = FlowNetworks.Random(3, seed);
        foreach (var step in MaxFlowSolver.Trace(network, FlowAlgorithm.Dinic))
            AssertValidFlow(network, step.Flow, step.Total);
    }
}

public class PushRelabelTests
{
    public static TheoryData<int> Seeds => new() { 1, 2, 3, 4, 5, 6, 7, 8, 13, 21, 34 };

    [Fact]
    public void Presets_reach_their_known_maximum()
    {
        Assert.Equal(23, MaxFlowSolver.Trace(FlowNetworks.Textbook(), FlowAlgorithm.PushRelabel)[^1].Total);
        Assert.Equal(20, MaxFlowSolver.Trace(FlowNetworks.WaterSupply(), FlowAlgorithm.PushRelabel)[^1].Total);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Matches_edmonds_karp_and_keeps_a_valid_preflow_throughout(int seed)
    {
        var network = FlowNetworks.Random(2 + seed % 3, seed);
        var steps = MaxFlowSolver.Trace(network, FlowAlgorithm.PushRelabel);
        var expected = MaxFlowSolver.Solve(network).Total;
        var last = steps[^1];
        Assert.Equal(expected, last.Total);
        Assert.Equal(expected, MaxFlowSolver.CutEdges(network, last.SourceSide!).Sum(e => e.Capacity));

        foreach (var step in steps)
        {
            foreach (var e in network.Edges) Assert.InRange(step.Flow[e.Id], 0, e.Capacity);
            foreach (var node in network.Nodes)
            {
                if (node.Id == network.Source) continue;
                var inflow = network.Edges.Where(e => e.To == node.Id).Sum(e => step.Flow[e.Id]);
                var outflow = network.Edges.Where(e => e.From == node.Id).Sum(e => step.Flow[e.Id]);
                Assert.True(inflow >= outflow, "a preflow never sends out more than it receives");
                Assert.Equal(inflow - outflow, step.Excess![node.Id]);
            }
        }

        // At the end the preflow is a real flow: no excess left anywhere but the sink.
        foreach (var node in network.Nodes.Where(n => n.Id != network.Source && n.Id != network.Sink))
            Assert.Equal(0, last.Excess![node.Id]);
        Assert.Equal(last.Total, MaxFlowSolver.Solve(network, FlowAlgorithm.PushRelabel).Total);
    }
}
