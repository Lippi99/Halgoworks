using System.Text.Json;
using Halgoworks.Core;
using Halgoworks.Core.BinPacking;
using Halgoworks.Core.JobShop;
using Halgoworks.Core.Knapsack;
using Halgoworks.Core.MaxFlow;
using Halgoworks.Core.Vrp;

namespace Halgoworks.Tests;

public class SharingTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Records holding lists don't compare by value, so compare their JSON instead.
    private static void AssertRoundTrip<T>(T instance, Func<T?, string?> check) where T : class
    {
        var payload = InstanceCodec.Encode(instance);
        Assert.Matches("^[A-Za-z0-9_-]+$", payload);
        var back = InstanceCodec.Decode(payload, check, out var problem);
        Assert.Null(problem);
        Assert.Equal(JsonSerializer.Serialize(instance, Json), JsonSerializer.Serialize(back, Json));
    }

    [Fact]
    public void Every_preset_and_random_instance_survives_a_round_trip()
    {
        AssertRoundTrip(KnapsackInstances.Camping(), InstanceChecks.Check);
        AssertRoundTrip(KnapsackInstances.GreedyTrap(), InstanceChecks.Check);
        AssertRoundTrip(KnapsackInstances.Random(10, 25, 3), InstanceChecks.Check);
        AssertRoundTrip(BinPackingInstances.CloudServers(), InstanceChecks.Check);
        AssertRoundTrip(BinPackingInstances.Random(20, 20, 8), InstanceChecks.Check);
        AssertRoundTrip(JobShopInstances.Ft06(), InstanceChecks.Check);
        AssertRoundTrip(JobShopInstances.Random(8, 6, 4), InstanceChecks.Check);
        AssertRoundTrip(FlowNetworks.WaterSupply(), InstanceChecks.Check);
        AssertRoundTrip(FlowNetworks.Random(4, 9), InstanceChecks.Check);
        AssertRoundTrip(VrpInstances.Random(25, 7, true, true), InstanceChecks.Check);
    }

    [Fact]
    public void Links_stay_reasonably_short()
    {
        Assert.True(InstanceCodec.Encode(VrpInstances.Random(25, 7, true, true)).Length < 2000);
        Assert.True(InstanceCodec.Encode(JobShopInstances.Random(8, 6, 4)).Length < 1200);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("1not-base64!!")]
    [InlineData("2AAAA")]
    [InlineData("1AAAAAAAA")]
    public void Damaged_links_are_rejected_with_a_reason(string payload)
    {
        Assert.Null(InstanceCodec.Decode<KnapsackInstance>(payload, InstanceChecks.Check, out var problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void Invalid_instances_are_rejected_even_when_well_formed()
    {
        var tooHeavy = KnapsackInstances.Camping() with { Capacity = 5000 };
        Assert.Null(InstanceCodec.Decode(InstanceCodec.Encode(tooHeavy), (Func<KnapsackInstance?, string?>)InstanceChecks.Check, out var p1));
        Assert.NotNull(p1);

        var badMachine = JobShopInstances.Small();
        badMachine = badMachine with { Jobs = [badMachine.Jobs[0] with { Ops = [new Operation(0, 0, 7, 3)] }, .. badMachine.Jobs.Skip(1)] };
        Assert.NotNull(InstanceChecks.Check(badMachine));

        var loop = FlowNetworks.Textbook() with { Edges = [new FlowEdge(0, 2, 2, 5)] };
        Assert.NotNull(InstanceChecks.Check(loop));

        var offMap = VrpInstances.Random(5, 1, false, false);
        offMap = offMap with { Customers = [offMap.Customers[0] with { X = 99 }, .. offMap.Customers.Skip(1)] };
        Assert.NotNull(InstanceChecks.Check(offMap));
    }

    [Fact]
    public void Untraced_solvers_match_their_traces()
    {
        foreach (var seed in new[] { 1, 2, 3, 5, 8 })
        {
            var vrp = VrpInstances.Random(15, seed, seed % 2 == 0, seed % 3 == 0);
            foreach (var algorithm in new[] { VrpAlgorithm.Savings, VrpAlgorithm.Sweep, VrpAlgorithm.NearestNeighbor })
            foreach (var polish in new[] { false, true })
                Assert.Equal(VrpSolver.Trace(vrp, algorithm, polish)[^1].Routes, VrpSolver.Solve(vrp, algorithm, polish));

            var network = FlowNetworks.Random(3, seed);
            foreach (var algorithm in new[] { FlowAlgorithm.EdmondsKarp, FlowAlgorithm.FordFulkerson, FlowAlgorithm.Dinic })
            {
                var last = MaxFlowSolver.Trace(network, algorithm)[^1];
                var solved = MaxFlowSolver.Solve(network, algorithm);
                Assert.Equal(last.Total, solved.Total);
                Assert.Equal(last.Flow, solved.Flow);
                Assert.Equal(last.SourceSide, solved.SourceSide);
            }
        }
    }
}
