namespace Halgoworks.Core.JobShop;

public static class JobShopInstances
{
    /// <summary>Three jobs on three machines, the small example from Google OR-Tools' documentation. Optimum 11.</summary>
    public static JobShopInstance Small() => Build("Three jobs, three machines", 3,
    [
        [(0, 3), (1, 2), (2, 2)],
        [(0, 2), (2, 1), (1, 4)],
        [(1, 4), (2, 3)],
    ], null, 11);

    /// <summary>Fisher &amp; Thompson's 6×6 benchmark (1963), known as ft06 or mt06. Optimum 55.</summary>
    public static JobShopInstance Ft06() => Build("Fisher & Thompson ft06", 6,
    [
        [(2, 1), (0, 3), (1, 6), (3, 7), (5, 3), (4, 6)],
        [(1, 8), (2, 5), (4, 10), (5, 10), (0, 10), (3, 4)],
        [(2, 5), (3, 4), (5, 8), (0, 9), (1, 1), (4, 7)],
        [(1, 5), (0, 5), (2, 5), (3, 3), (4, 8), (5, 9)],
        [(2, 9), (1, 3), (4, 5), (5, 4), (0, 3), (3, 1)],
        [(1, 3), (3, 3), (5, 9), (0, 10), (4, 4), (2, 1)],
    ], null, 55);

    private static readonly string[] WorkshopMachines = ["Saw", "Lathe", "Mill", "Drill", "Weld", "Paint"];

    /// <summary>Every job visits every machine once, in a random order, with a due date.</summary>
    public static JobShopInstance Random(int jobs, int machines, int seed)
    {
        var rng = new Random(seed);
        var routes = new List<(int, int)[]>();
        var dues = new List<int>();
        for (var j = 0; j < jobs; j++)
        {
            var order = Enumerable.Range(0, machines).OrderBy(_ => rng.Next()).ToArray();
            var ops = order.Select(m => (m, rng.Next(1, 10))).ToArray();
            routes.Add(ops);
            var work = ops.Sum(o => o.Item2);
            dues.Add((int)Math.Round(work * (1.3 + rng.NextDouble() * 0.9)));
        }

        var names = machines <= WorkshopMachines.Length ? WorkshopMachines.Take(machines).ToArray() : null;
        return Build($"Workshop #{seed}", machines, routes, dues, null, names);
    }

    private static JobShopInstance Build(string name, int machines, IReadOnlyList<(int Machine, int Duration)[]> routes, IReadOnlyList<int>? dues, int? optimum, string[]? machineNames = null)
    {
        var jobs = routes.Select((ops, j) => new Job(
            j,
            $"J{j + 1}",
            ops.Select((o, i) => new Operation(j, i, o.Machine, o.Duration)).ToList(),
            dues?[j])).ToList();
        var names = machineNames ?? Enumerable.Range(1, machines).Select(m => $"M{m}").ToArray();
        return new JobShopInstance(name, jobs, names, optimum);
    }
}
