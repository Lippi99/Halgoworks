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
