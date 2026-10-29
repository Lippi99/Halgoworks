using Halgoworks.Core.BinPacking;
using Halgoworks.Core.JobShop;
using Halgoworks.Core.Knapsack;
using Halgoworks.Core.Matching;
using Halgoworks.Core.MaxFlow;
using Halgoworks.Core.Vrp;

namespace Halgoworks.Core;

/// <summary>
/// Sanity checks for instances that arrive from outside, such as a shared link. They keep instances
/// well-formed and small enough for the step-by-step views. Each returns null when the instance is fine,
/// or a short reason when it isn't.
/// </summary>
public static class InstanceChecks
{
    public static string? Check(KnapsackInstance? k)
    {
        if (k?.Items is null || k.Name is null) return "the example is incomplete";
        if (k.Items.Count is < 1 or > 12) return "it needs between 1 and 12 items";
        if (k.Capacity is < 1 or > 40) return "the capacity must be between 1 and 40";
        for (var i = 0; i < k.Items.Count; i++)
        {
            var item = k.Items[i];
            if (item is null || item.Id != i) return "item ids are out of order";
            if (item.Weight is < 1 or > 100 || item.Value is < 0 or > 10_000) return $"item {i + 1} has an invalid weight or value";
            if (item.Name is null || item.Icon is null) return $"item {i + 1} has no name";
        }

        return null;
    }

    public static string? Check(BinPackingInstance? b)
    {
        if (b?.Items is null || b.Name is null) return "the example is incomplete";
        if (b.Items.Count is < 1 or > 24) return "it needs between 1 and 24 items";
        if (b.Capacity is < 2 or > 100) return "the bin size must be between 2 and 100";
        for (var i = 0; i < b.Items.Count; i++)
        {
            var item = b.Items[i];
            if (item is null || item.Id != i) return "item ids are out of order";
            if (item.Size < 1 || item.Size > b.Capacity) return $"item {i + 1} doesn't fit in a bin";
        }

        return null;
    }

    public static string? Check(JobShopInstance? j)
    {
        if (j?.Jobs is null || j.Machines is null || j.Name is null) return "the example is incomplete";
        if (j.Jobs.Count is < 1 or > 8) return "it needs between 1 and 8 jobs";
        if (j.Machines.Count is < 1 or > 8) return "it needs between 1 and 8 machines";
        for (var id = 0; id < j.Jobs.Count; id++)
        {
            var job = j.Jobs[id];
            if (job?.Ops is null || job.Id != id || job.Name is null) return "job ids are out of order";
            if (job.Ops.Count is < 1 or > 8) return $"{job.Name} needs between 1 and 8 operations";
            for (var k = 0; k < job.Ops.Count; k++)
            {
                var op = job.Ops[k];
                if (op is null || op.Job != id || op.Index != k) return $"{job.Name} has operations out of order";
                if (op.Machine < 0 || op.Machine >= j.Machines.Count) return $"{job.Name} uses a machine that doesn't exist";
                if (op.Duration is < 1 or > 99) return $"{job.Name} has an invalid duration";
            }
        }

        return null;
    }

    public static string? Check(FlowNetwork? f)
    {
        if (f?.Nodes is null || f.Edges is null || f.Name is null) return "the network is incomplete";
        if (f.Nodes.Count is < 2 or > 14) return "it needs between 2 and 14 nodes";
        if (f.Edges.Count is < 1 or > 40) return "it needs between 1 and 40 edges";
        if (f.Source < 0 || f.Source >= f.Nodes.Count || f.Sink < 0 || f.Sink >= f.Nodes.Count || f.Source == f.Sink) return "the source or sink is invalid";
        for (var i = 0; i < f.Nodes.Count; i++)
        {
            var n = f.Nodes[i];
            if (n is null || n.Id != i || n.Label is null) return "node ids are out of order";
            if (n.X is < 0 or > 1 || n.Y is < 0 or > 1) return $"node {n.Label} is off the canvas";
        }

        for (var i = 0; i < f.Edges.Count; i++)
        {
            var e = f.Edges[i];
            if (e is null || e.Id != i) return "edge ids are out of order";
            if (e.From < 0 || e.From >= f.Nodes.Count || e.To < 0 || e.To >= f.Nodes.Count || e.From == e.To) return "an edge points to a node that doesn't exist";
            if (e.Capacity is < 1 or > 999) return "capacities must be between 1 and 999";
        }

        return null;
    }

    public static string? Check(VrpInstance? v)
    {
        if (v?.Customers is null || v.Name is null) return "the map is incomplete";
        if (v.Customers.Count is < 1 or > 30) return "it needs between 1 and 30 customers";
        if (v.Capacity is < 1 or > 1000) return "the truck capacity must be between 1 and 1000";
        if (v.Speed <= 0 || v.Service < 0 || v.FuelPerKm < 0 || v.TruckCost < 0) return "the truck settings are invalid";
        if (!OnMap(v.DepotX, v.DepotY)) return "the depot is off the map";
        for (var i = 0; i < v.Customers.Count; i++)
        {
            var c = v.Customers[i];
            if (c is null || c.Id != i + 1) return "customer ids are out of order";
            if (!OnMap(c.X, c.Y)) return $"customer {c.Id} is off the map";
            if (c.Demand < 1 || c.Demand > v.Capacity) return $"customer {c.Id} needs more than a truck can carry";
            // A truck straight from the depot must be able to make the deadline, or no route can serve it.
            if (v.UseDeadlines && v.D(0, c.Id) / v.Speed > c.Deadline) return $"customer {c.Id}'s deadline is impossible to meet";
        }

        if (v.Geo is { } geo && (geo.Count != 2 || geo[0] is < -85 or > 85 || geo[1] is < -180 or > 180)) return "the map's location is invalid";
        if (v.Road is { } road && (!v.HasRoads || road.Any(d => !double.IsFinite(d) || d < 0 || d > 500))) return "the road distances are invalid";
        return null;
    }

    public static string? Check(MatchingInstance? m)
    {
        if (m?.Left is null || m.Right is null || m.Pairs is null || m.Name is null || m.LeftTitle is null || m.RightTitle is null) return "the example is incomplete";
        if (m.Left.Count is < 1 or > 9 || m.Right.Count is < 1 or > 9) return "each side needs between 1 and 9 members";
        if (m.Left.Any(n => n is null || n.Length > 24) || m.Right.Any(n => n is null || n.Length > 24)) return "a name is missing or too long";
        if (m.Pairs.Count is < 1 or > 81) return "it needs between 1 and 81 allowed pairs";
        if (m.Pairs.Any(p => p is null || p.Left < 0 || p.Left >= m.Left.Count || p.Right < 0 || p.Right >= m.Right.Count)) return "a pair points to someone who doesn't exist";
        if (m.Pairs.Distinct().Count() != m.Pairs.Count) return "a pair is listed twice";
        return null;
    }

    private static bool OnMap(double x, double y) => x is >= 0 and <= 20 && y is >= 0 and <= 20;
}
