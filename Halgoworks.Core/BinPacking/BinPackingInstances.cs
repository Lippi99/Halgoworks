namespace Halgoworks.Core.BinPacking;

public static class BinPackingInstances
{
    public static BinPackingInstance Random(int count, int capacity, int seed)
    {
        var rng = new Random(seed);
        var items = Enumerable.Range(0, count)
            .Select(i =>
            {
                // Two uniform draws averaged: more medium items than tiny or huge ones.
                var r = (rng.NextDouble() + rng.NextDouble()) / 2;
                var size = Math.Clamp((int)Math.Round(1 + r * (capacity * 0.8 - 1)), 1, capacity);
                return new BinItem(i, size);
            })
            .ToList();
        return new BinPackingInstance($"Random #{seed}", capacity, items);
    }

    /// <summary>Small items arrive first and fill bins that the big ones later can't share.</summary>
    public static BinPackingInstance SmallFirst() => new("Small items first", 10,
        new[] { 3, 3, 3, 3, 3, 3, 7, 7, 7 }.Select((s, i) => new BinItem(i, s)).ToList());

    /// <summary>Virtual machines (GB of RAM) placed on 64 GB hosts.</summary>
    public static BinPackingInstance CloudServers() => new("Cloud servers", 64,
        new[] { 32, 16, 8, 24, 12, 4, 40, 8, 16, 20, 28, 6, 10, 14 }.Select((s, i) => new BinItem(i, s)).ToList(),
        "GB");
}
