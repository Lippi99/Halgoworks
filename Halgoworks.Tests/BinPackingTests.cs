using Halgoworks.Core.BinPacking;

namespace Halgoworks.Tests;

public class BinPackingTests
{
    public static TheoryData<int> Seeds => new() { 1, 2, 3, 4, 5, 6, 7, 8, 11, 23, 57 };

    private static void AssertValid(BinPackingInstance instance, IReadOnlyList<IReadOnlyList<int>> bins)
    {
        var sizes = instance.Items.ToDictionary(i => i.Id, i => i.Size);
        Assert.Equal(instance.Items.Select(i => i.Id).OrderBy(i => i), bins.SelectMany(b => b).OrderBy(i => i));
        Assert.All(bins, b => Assert.True(b.Sum(id => sizes[id]) <= instance.Capacity));
        Assert.True(bins.Count >= instance.LowerBound);
    }

    // Tries every way to split the items into groups that fit.
    private static int BruteForce(BinPackingInstance instance)
    {
        var sizes = instance.Items.Select(i => i.Size).ToArray();
        var best = sizes.Length;
        var loads = new List<int>();

        void Place(int k)
        {
            if (loads.Count >= best) return;
            if (k == sizes.Length)
            {
                best = loads.Count;
                return;
            }

            for (var b = 0; b < loads.Count; b++)
            {
                if (loads[b] + sizes[k] > instance.Capacity) continue;
                loads[b] += sizes[k];
                Place(k + 1);
                loads[b] -= sizes[k];
            }

            loads.Add(sizes[k]);
            Place(k + 1);
            loads.RemoveAt(loads.Count - 1);
        }

        Place(0);
        return best;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_rule_packs_every_item_without_overflow(int seed)
    {
        var instance = BinPackingInstances.Random(8 + seed % 10, 10 + seed % 3 * 5, seed);
        foreach (var rule in BinPackingSolver.Rules)
        {
            var bins = BinPackingSolver.Solve(instance, rule);
            AssertValid(instance, bins);

            var traced = BinPackingSolver.Trace(instance, rule)[^1].Bins;
            Assert.Equal(bins.Select(b => b.ToArray()), traced);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Exact_search_matches_brute_force_and_beats_every_heuristic(int seed)
    {
        var instance = BinPackingInstances.Random(6 + seed % 4, 10, seed);
        var optimum = BinPackingSolver.Optimal(instance);

        Assert.Equal(BruteForce(instance), optimum);
        Assert.All(BinPackingSolver.Rules, rule => Assert.True(BinPackingSolver.Solve(instance, rule).Count >= optimum));
    }

    [Fact]
    public void Small_items_first_preset_punishes_first_fit()
    {
        var instance = BinPackingInstances.SmallFirst();

        Assert.Equal(5, BinPackingSolver.Solve(instance, BinRule.FirstFit).Count);
        Assert.Equal(4, BinPackingSolver.Solve(instance, BinRule.FirstFitDecreasing).Count);
        Assert.Equal(4, BinPackingSolver.Optimal(instance));
    }

    [Fact]
    public void Next_fit_only_ever_fills_the_newest_bin()
    {
        var instance = BinPackingInstances.Random(14, 10, 3);
        foreach (var step in BinPackingSolver.Trace(instance, BinRule.NextFit).Where(s => s.Target is not null && s.Placed is null))
            Assert.True(step.Target >= step.Bins.Count - 1);
    }
}
