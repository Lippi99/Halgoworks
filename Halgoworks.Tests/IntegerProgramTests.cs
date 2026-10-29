using Halgoworks.Core;
using Halgoworks.Core.BinPacking;
using Halgoworks.Core.Ip;
using Halgoworks.Core.Knapsack;

namespace Halgoworks.Tests;

public class IntegerProgramTests
{
    [Fact]
    public void Simplex_solves_a_textbook_maximization()
    {
        // Hillier & Lieberman's Wyndor Glass: max 3x + 5y, x ≤ 4, 2y ≤ 12, 3x + 2y ≤ 18 → 36 at (2, 6).
        var lp = Simplex.Maximize([3, 5],
        [
            new LinearConstraint([1, 0], Sense.LessEqual, 4),
            new LinearConstraint([0, 2], Sense.LessEqual, 12),
            new LinearConstraint([3, 2], Sense.LessEqual, 18),
        ]);
        Assert.Equal(LpStatus.Optimal, lp.Status);
        Assert.Equal(36, lp.Objective, 6);
        Assert.Equal(2, lp.X[0], 6);
        Assert.Equal(6, lp.X[1], 6);
    }

    [Fact]
    public void Simplex_handles_greater_equal_and_equality_rows()
    {
        // min x + y with x + 2y ≥ 4 and 3x + y ≥ 6 → 2.8 at (1.6, 1.2).
        var lp = Simplex.Maximize([-1, -1],
        [
            new LinearConstraint([1, 2], Sense.GreaterEqual, 4),
            new LinearConstraint([3, 1], Sense.GreaterEqual, 6),
        ]);
        Assert.Equal(LpStatus.Optimal, lp.Status);
        Assert.Equal(-2.8, lp.Objective, 6);

        var eq = Simplex.Maximize([1, 2], [new LinearConstraint([1, 1], Sense.Equal, 3), new LinearConstraint([0, 1], Sense.LessEqual, 2)]);
        Assert.Equal(5, eq.Objective, 6);
    }

    [Fact]
    public void Simplex_reports_infeasible_and_unbounded_programs()
    {
        Assert.Equal(LpStatus.Infeasible, Simplex.Maximize([1], [new LinearConstraint([1], Sense.LessEqual, 1), new LinearConstraint([1], Sense.GreaterEqual, 2)]).Status);
        Assert.Equal(LpStatus.Unbounded, Simplex.Maximize([1, 1], [new LinearConstraint([1, -1], Sense.LessEqual, 1)]).Status);
    }

    [Theory]
    [MemberData(nameof(KnapsackTests.Seeds), MemberType = typeof(KnapsackTests))]
    public void Knapsack_integer_program_matches_dynamic_programming(int seed)
    {
        var instance = KnapsackInstances.Random(4 + seed % 7, 8 + seed % 17, seed);
        var steps = IntegerProgram.Trace(IntegerProgram.Knapsack(instance));
        var last = steps[^1];
        Assert.Equal(KnapsackSolver.Solve(instance).Value, last.Incumbent!.Value, 6);
        var x = last.IncumbentX!;
        Assert.True(Enumerable.Range(0, x.Count).Sum(i => x[i] * instance.Items[i].Weight) <= instance.Capacity + 1e-9);
        Assert.All(x, v => Assert.True(v is 0 or 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(13)]
    public void Bin_packing_integer_program_matches_the_exact_search(int seed)
    {
        var instance = BinPackingInstances.Random(4 + seed % 3, 10, seed);
        var (model, _, _, _) = IntegerProgram.BinPacking(instance);
        var last = IntegerProgram.Trace(model)[^1];
        Assert.Equal(BinPackingSolver.Optimal(instance), (int)Math.Round(last.Incumbent!.Value));
        Assert.True(last.Tree.Count < IntegerProgram.NodeLimit);
    }

    [Fact]
    public void Every_relaxation_bounds_the_whole_solutions_below_it()
    {
        var steps = IntegerProgram.Trace(IntegerProgram.Knapsack(KnapsackInstances.Camping()));
        var tree = steps[^1].Tree;
        foreach (var node in tree.Where(n => n.Parent >= 0 && !double.IsNaN(n.Bound)))
            Assert.True(node.Bound <= tree[node.Parent].Bound + 1e-6);
        Assert.Equal(68, steps[^1].Incumbent!.Value, 6);
    }
}
