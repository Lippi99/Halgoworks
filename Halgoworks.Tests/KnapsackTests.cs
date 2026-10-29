using Halgoworks.Core.Knapsack;

namespace Halgoworks.Tests;

public class KnapsackTests
{
    public static TheoryData<int> Seeds => new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 42, 99 };

    private static int BruteForce(KnapsackInstance instance) =>
        KnapsackSolver.Enumerate(instance).Where(s => s.Weight <= instance.Capacity).Max(s => s.Value);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Dp_matches_brute_force(int seed)
    {
        var instance = KnapsackInstances.Random(4 + seed % 6, 8 + seed % 17, seed);
        var result = KnapsackSolver.Solve(instance);

        Assert.Equal(BruteForce(instance), result.Value);
        Assert.True(result.Weight <= instance.Capacity);
        Assert.Equal(result.Value, result.Chosen.Sum(i => instance.Items[i].Value));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Dp_trace_ends_at_the_optimum_with_consistent_items(int seed)
    {
        var instance = KnapsackInstances.Random(4 + seed % 6, 8 + seed % 17, seed);
        var last = KnapsackSolver.TraceDp(instance)[^1];
        var taken = Enumerable.Range(0, instance.Items.Count).Where(i => last.Items[i] == ItemState.Taken).ToList();

        Assert.Equal(KnapsackSolver.Solve(instance).Value, last.Total);
        Assert.Equal(last.Total, taken.Sum(i => instance.Items[i].Value));
        Assert.Equal(last.Load, taken.Sum(i => instance.Items[i].Weight));
        Assert.All(last.Table!.SelectMany(row => row), v => Assert.True(v >= 0));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Greedy_trace_matches_untraced_greedy_and_never_beats_the_optimum(int seed)
    {
        var instance = KnapsackInstances.Random(4 + seed % 6, 8 + seed % 17, seed);
        var last = KnapsackSolver.TraceGreedy(instance)[^1];

        Assert.Equal(KnapsackSolver.SolveGreedy(instance, GreedyOrder.Density).Value, last.Total);
        Assert.True(last.Total <= last.Optimum);
        Assert.True(last.Load <= instance.Capacity);
    }

    [Fact]
    public void Presets_have_the_documented_answers()
    {
        Assert.Equal(68, KnapsackSolver.Solve(KnapsackInstances.Camping()).Value);
        Assert.Equal(62, KnapsackSolver.SolveGreedy(KnapsackInstances.Camping(), GreedyOrder.Density).Value);
        Assert.Equal(60, KnapsackSolver.Solve(KnapsackInstances.GreedyTrap()).Value);
        Assert.Equal(47, KnapsackSolver.SolveGreedy(KnapsackInstances.GreedyTrap(), GreedyOrder.Density).Value);
    }

    [Fact]
    public void Every_cell_is_filled_exactly_once_in_the_trace()
    {
        var instance = KnapsackInstances.Camping();
        var steps = KnapsackSolver.TraceDp(instance);
        var filled = steps.TakeWhile(s => s.Phase == "Fill the table")
            .SelectMany(s => s.Cell is { } c ? new[] { c } : s.Batch)
            .ToList();

        Assert.Equal((instance.Items.Count + 1) * (instance.Capacity + 1), filled.Count);
        Assert.Equal(filled.Count, filled.Distinct().Count());
    }
}
