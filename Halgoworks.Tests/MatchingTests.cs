using Halgoworks.Core;
using Halgoworks.Core.Matching;
using Halgoworks.Core.MaxFlow;

namespace Halgoworks.Tests;

public class MatchingTests
{
    public static TheoryData<int> Seeds => new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 21, 34 };

    // Largest matching by trying every choice for each left node.
    private static int BruteForce(MatchingInstance m)
    {
        var best = 0;
        var used = new bool[m.Right.Count];

        void Assign(int left, int count)
        {
            if (count + (m.Left.Count - left) <= best) return;
            if (left == m.Left.Count)
            {
                best = Math.Max(best, count);
                return;
            }

            Assign(left + 1, count);
            foreach (var p in m.Pairs.Where(p => p.Left == left && !used[p.Right]))
            {
                used[p.Right] = true;
                Assign(left + 1, count + 1);
                used[p.Right] = false;
            }
        }

        Assign(0, 0);
        return best;
    }

    private static void AssertValidMatching(MatchingInstance m, IReadOnlyList<int> matched)
    {
        var pairs = matched.Select(i => m.Pairs[i]).ToList();
        Assert.Equal(pairs.Count, pairs.Select(p => p.Left).Distinct().Count());
        Assert.Equal(pairs.Count, pairs.Select(p => p.Right).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_flow_algorithm_finds_a_maximum_matching(int seed)
    {
        var m = MatchingInstances.Random(3 + seed % 6, 3 + seed * 7 % 6, 0.25 + seed % 4 * 0.12, seed);
        Assert.Null(InstanceChecks.Check(m));
        var expected = BruteForce(m);
        foreach (var algorithm in new[] { FlowAlgorithm.EdmondsKarp, FlowAlgorithm.FordFulkerson, FlowAlgorithm.Dinic, FlowAlgorithm.PushRelabel })
        {
            var steps = MatchingSolver.Trace(m, algorithm);
            Assert.Equal(expected, steps[^1].Matched.Count);
            Assert.All(steps.Where(s => s.Phase != "Preflow" && s.Phase != "Push and relabel"), s => AssertValidMatching(m, s.Matched));
        }

        var greedy = MatchingSolver.Greedy(m);
        AssertValidMatching(m, greedy);
        Assert.True(greedy.Count <= expected);
    }

    [Fact]
    public void Presets_behave_as_described()
    {
        Assert.Equal(6, MatchingSolver.Trace(MatchingInstances.DevelopersAndTickets(), FlowAlgorithm.EdmondsKarp)[^1].Matched.Count);
        var trap = MatchingInstances.GreedyTrap();
        Assert.Equal(4, MatchingSolver.Trace(trap, FlowAlgorithm.EdmondsKarp)[^1].Matched.Count);
        Assert.True(MatchingSolver.Greedy(trap).Count < 4);
        Assert.Null(InstanceChecks.Check(MatchingInstances.DevelopersAndTickets()));
    }

    [Fact]
    public void Augmenting_paths_alternate_and_add_one_pair_each()
    {
        var steps = MatchingSolver.Trace(MatchingInstances.GreedyTrap(), FlowAlgorithm.FordFulkerson);
        foreach (var path in steps.Where(s => s.PathAdds.Count > 0 && s.Flow.Pushed is null))
            Assert.Equal(path.PathAdds.Count, path.PathRemoves.Count + 1);
    }
}
