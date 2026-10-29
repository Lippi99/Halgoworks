using Halgoworks.Core;
using Halgoworks.Core.BinPacking;
using Halgoworks.Core.JobShop;
using Halgoworks.Core.Knapsack;
using Halgoworks.Core.Matching;
using Halgoworks.Core.MaxFlow;
using Halgoworks.Core.Vrp;

namespace Halgoworks.Tests;

/// <summary>The large-instance generators and untraced solvers behind the Scale and Race views, and real-map instances.</summary>
public class ScaleTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Knapsack_branch_and_bound_solve_matches_dynamic_programming(int seed)
    {
        var instance = KnapsackInstances.Large(60, seed);
        var (best, nodes, complete) = KnapsackBranchAndBound.Solve(instance);
        Assert.True(complete);
        Assert.True(nodes > 0);
        Assert.Equal(KnapsackSolver.Solve(instance).Value, best.Value);
        Assert.True(best.Weight <= instance.Capacity);
        Assert.Equal(best.Value, best.Chosen.Sum(i => instance.Items[i].Value));
    }

    [Fact]
    public void Knapsack_branch_and_bound_reports_when_it_gives_up()
    {
        var (best, nodes, complete) = KnapsackBranchAndBound.Solve(KnapsackInstances.Large(120, 5), nodeLimit: 50);
        Assert.False(complete);
        Assert.Equal(50, nodes);
        Assert.True(best.Value >= 0);
    }

    [Fact]
    public void Large_knapsack_has_unique_ids_and_room_for_some_items()
    {
        var instance = KnapsackInstances.Large(200, 9);
        Assert.Equal(200, instance.Items.Count);
        Assert.Equal(Enumerable.Range(0, 200), instance.Items.Select(i => i.Id));
        Assert.True(instance.Capacity > instance.Items.Max(i => i.Weight));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    public void Job_shop_random_names_every_machine(int machines)
    {
        var instance = JobShopInstances.Random(15, machines, 4);
        Assert.Equal(machines, instance.Machines.Count);
        foreach (var rule in JobShopSolver.Rules)
            Assert.Null(JobShopSolver.Validate(instance, JobShopSolver.Solve(instance, rule)));
    }

    [Fact]
    public void Large_routing_instance_gives_feasible_plans_from_every_algorithm()
    {
        var instance = VrpInstances.Large(120, 3, clustered: true);
        Assert.Equal(120, instance.Customers.Count);
        foreach (var algorithm in Enum.GetValues<VrpAlgorithm>().Where(a => !VrpSolver.IsMetaheuristic(a)))
        {
            var routes = VrpSolver.Solve(instance, algorithm, polish: true);
            Assert.Equal(Enumerable.Range(1, 120), routes.SelectMany(r => r).Order());
            Assert.All(routes, r => Assert.True(VrpSolver.Feasible(instance, r)));
        }
    }

    [Fact]
    public void Metaheuristic_runs_report_a_non_increasing_best_history()
    {
        var instance = VrpInstances.Random(25, 11, clustered: false, deadlines: false);
        foreach (var run in new[] { VrpMetaheuristics.RunAnnealing(instance, 1500), VrpMetaheuristics.RunGenetic(instance, 20, 20) })
        {
            Assert.NotEmpty(run.BestHistory);
            Assert.Equal(run.History.Count, run.BestHistory.Count);
            for (var i = 1; i < run.BestHistory.Count; i++) Assert.True(run.BestHistory[i] <= run.BestHistory[i - 1] + 1e-9);
            Assert.Equal(run.BestHistory[^1], VrpSolver.TotalDistance(instance, run.Best), 6);
        }
    }

    [Fact]
    public void Large_flow_network_solves_the_same_with_every_algorithm()
    {
        var network = FlowNetworks.Large(8, 7, 2);
        Assert.Equal(58, network.Nodes.Count);
        var totals = Enum.GetValues<FlowAlgorithm>().Select(a => MaxFlowSolver.Solve(network, a).Total).Distinct().ToList();
        Assert.Single(totals);
        Assert.True(totals[0] > 0);
    }

    // Push–relabel once stopped early on big networks, leaving a preflow: right total, but excess stuck inside.
    [Fact]
    public void Every_algorithm_ends_with_a_real_flow_on_big_networks()
    {
        var networks = new[] { FlowNetworks.Large(16, 10, 3), MatchingSolver.ToNetwork(MatchingInstances.Random(120, 120, 2.0 / 120, 1)) };
        foreach (var network in networks)
        foreach (var algorithm in Enum.GetValues<FlowAlgorithm>())
        {
            var r = MaxFlowSolver.Solve(network, algorithm);
            var balance = new int[network.Nodes.Count];
            foreach (var e in network.Edges)
            {
                Assert.InRange(r.Flow[e.Id], 0, e.Capacity);
                balance[e.From] -= r.Flow[e.Id];
                balance[e.To] += r.Flow[e.Id];
            }

            for (var v = 0; v < balance.Length; v++)
                if (v != network.Source && v != network.Sink)
                    Assert.True(balance[v] == 0, $"{algorithm}: node {v} keeps {balance[v]}");
            Assert.Equal(r.Total, balance[network.Sink]);
        }
    }

    [Fact]
    public void Matching_solve_is_a_maximum_matching_for_every_algorithm()
    {
        var m = MatchingInstances.Random(60, 60, 0.05, 8);
        var sizes = new List<int>();
        foreach (var algorithm in Enum.GetValues<FlowAlgorithm>())
        {
            var pairs = MatchingSolver.Solve(m, algorithm).Select(i => m.Pairs[i]).ToList();
            Assert.Equal(pairs.Count, pairs.Select(p => p.Left).Distinct().Count());
            Assert.Equal(pairs.Count, pairs.Select(p => p.Right).Distinct().Count());
            sizes.Add(pairs.Count);
        }

        Assert.Single(sizes.Distinct());
        Assert.True(sizes[0] >= MatchingSolver.Greedy(m).Count);
    }

    [Fact]
    public void Road_distances_replace_straight_lines_when_the_matrix_fits()
    {
        var plain = VrpInstances.Random(3, 1, clustered: false, deadlines: false);
        var road = Enumerable.Range(0, 16).Select(i => i / 4 == i % 4 ? 0.0 : 7.5).ToList();
        var withRoads = plain with { Road = road };
        Assert.True(withRoads.HasRoads);
        Assert.Equal(7.5, withRoads.D(0, 2));
        Assert.Equal(0, withRoads.D(3, 3));
        Assert.Null(InstanceChecks.Check(withRoads));

        // A matrix of the wrong size (say, after adding a customer) is ignored, and rejected from links.
        var stale = withRoads with { Customers = [.. plain.Customers, plain.Customers[0] with { Id = 4 }] };
        Assert.False(stale.HasRoads);
        Assert.Equal(plain.D(0, 2), stale.D(0, 2));
        Assert.NotNull(InstanceChecks.Check(stale));
    }

    [Fact]
    public void City_instances_round_trip_between_km_and_latitude_longitude()
    {
        var instance = VrpInstances.InCity("Berlin", 52.52, 13.405, 10, 12, 5, clustered: false);
        Assert.NotNull(instance.Geo);
        var (lat, lon) = instance.LatLon(0);
        Assert.Equal(52.52, lat, 3);
        Assert.Equal(13.405, lon, 3);
        foreach (var c in instance.Customers)
        {
            var (clat, clon) = instance.LatLon(c.Id);
            var (x, y) = VrpInstance.ToKm(instance.Geo!, clat, clon);
            Assert.Equal(c.X, x, 6);
            Assert.Equal(c.Y, y, 6);
            Assert.InRange(c.X, 0, 10);
        }

        Assert.Null(InstanceChecks.Check(instance));
    }

    [Fact]
    public void Large_bin_packing_rules_stay_valid()
    {
        var instance = BinPackingInstances.Random(200, 100, 6);
        foreach (var rule in BinPackingSolver.Rules)
        {
            var bins = BinPackingSolver.Solve(instance, rule);
            Assert.Equal(200, bins.Sum(b => b.Count));
            Assert.All(bins, b => Assert.True(b.Sum(i => instance.Items[i].Size) <= 100));
            Assert.True(bins.Count >= instance.LowerBound);
        }
    }
}
