using Halgoworks.Core.Vrp;

namespace Halgoworks.Tests;

public class VrpTests
{
    public static TheoryData<int, bool, bool> Cases()
    {
        var data = new TheoryData<int, bool, bool>();
        foreach (var seed in new[] { 1, 2, 3, 4, 7, 11, 19 })
        foreach (var clustered in new[] { false, true })
        foreach (var deadlines in new[] { false, true })
            data.Add(seed, clustered, deadlines);
        return data;
    }

    private static readonly VrpAlgorithm[] Algorithms = [VrpAlgorithm.Savings, VrpAlgorithm.Sweep, VrpAlgorithm.NearestNeighbor];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_algorithm_serves_each_customer_once_within_capacity_and_deadlines(int seed, bool clustered, bool deadlines)
    {
        var instance = VrpInstances.Random(8 + seed % 15, seed, clustered, deadlines);
        foreach (var algorithm in Algorithms)
        foreach (var polish in new[] { false, true })
        {
            var steps = VrpSolver.Trace(instance, algorithm, polish);
            var routes = steps[^1].Routes;

            Assert.Equal(instance.Customers.Select(c => c.Id).OrderBy(c => c), routes.SelectMany(r => r).OrderBy(c => c));
            Assert.All(routes, r => Assert.True(VrpSolver.Feasible(instance, r)));
            Assert.Equal(VrpSolver.TotalDistance(instance, routes), steps[^1].Distance, 6);
            Assert.All(steps, s => Assert.Equal(s.Routes.Count, s.Colors.Count));
            Assert.All(steps[^1].Colors, c => Assert.True(c >= 0));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Two_opt_never_makes_routes_longer(int seed, bool clustered, bool deadlines)
    {
        var instance = VrpInstances.Random(8 + seed % 15, seed, clustered, deadlines);
        foreach (var algorithm in Algorithms)
        {
            var raw = VrpSolver.TotalDistance(instance, VrpSolver.Solve(instance, algorithm, false));
            var polished = VrpSolver.TotalDistance(instance, VrpSolver.Solve(instance, algorithm, true));
            Assert.True(polished <= raw + 1e-9);
        }
    }

    [Fact]
    public void Savings_merges_everything_into_one_route_when_capacity_is_unlimited()
    {
        var instance = VrpInstances.Random(12, 5, false, false) with { Capacity = 10_000 };
        Assert.Single(VrpSolver.Solve(instance, VrpAlgorithm.Savings, false));
    }

    [Fact]
    public void Savings_distance_never_grows_while_merging()
    {
        var instance = VrpInstances.Random(15, 3, false, false);
        var distances = VrpSolver.Trace(instance, VrpAlgorithm.Savings, false).Select(s => s.Distance).ToList();
        for (var k = 1; k < distances.Count; k++) Assert.True(distances[k] <= distances[k - 1] + 1e-9);
    }
}

public class VrpMetaheuristicTests
{
    public static TheoryData<int, bool, bool> Cases() => VrpTests.Cases();

    private static void AssertValidPlan(VrpInstance instance, IReadOnlyList<int[]> routes)
    {
        Assert.Equal(instance.Customers.Select(c => c.Id).OrderBy(c => c), routes.SelectMany(r => r).OrderBy(c => c));
        Assert.All(routes, r => Assert.True(VrpSolver.Feasible(instance, r)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Annealing_and_genetic_produce_feasible_plans_no_worse_than_their_start(int seed, bool clustered, bool deadlines)
    {
        var instance = VrpInstances.Random(8 + seed % 15, seed, clustered, deadlines);

        var sa = VrpMetaheuristics.TraceAnnealing(instance, 1500);
        AssertValidPlan(instance, sa[^1].Routes);
        Assert.True(sa[^1].Distance <= sa[0].Distance + 1e-9);
        Assert.Equal(sa[^1].Routes, VrpMetaheuristics.SolveAnnealing(instance, 1500));
        Assert.All(sa.Zip(sa.Skip(1)), p => Assert.True(p.Second.BestHistory[^1] <= p.First.BestHistory[^1] + 1e-9));

        var ga = VrpMetaheuristics.TraceGenetic(instance, 25, 20);
        AssertValidPlan(instance, ga[^1].Routes);
        Assert.True(ga[^1].Distance <= ga[0].Distance + 1e-9);
        Assert.Equal(VrpSolver.TotalDistance(instance, ga[^1].Routes), VrpSolver.TotalDistance(instance, VrpMetaheuristics.SolveGenetic(instance, 25, 20)), 6);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Split_cuts_any_order_into_feasible_routes_no_longer_than_one_truck_per_customer(int seed, bool clustered, bool deadlines)
    {
        var instance = VrpInstances.Random(8 + seed % 15, seed, clustered, deadlines);
        var order = instance.Customers.Select(c => c.Id).OrderBy(id => (id * 7919) % 101).ToArray();
        var routes = VrpMetaheuristics.Split(instance, order);
        AssertValidPlan(instance, routes);
        Assert.Equal(order, routes.SelectMany(r => r));
        var star = instance.Customers.Sum(c => 2 * instance.D(0, c.Id));
        Assert.True(VrpSolver.TotalDistance(instance, routes) <= star + 1e-9);
    }
}
