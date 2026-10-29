namespace Halgoworks.Core.Knapsack;

public static class KnapsackInstances
{
    public static KnapsackInstance Camping() => new("Camping trip", 15,
    [
        new(0, "Tent", "⛺", 6, 30),
        new(1, "Lantern", "🔦", 2, 9),
        new(2, "Stove", "🍳", 4, 16),
        new(3, "Water filter", "💧", 1, 7),
        new(4, "Camera", "📷", 3, 10),
        new(5, "Jacket", "🧥", 4, 15),
        new(6, "Guitar", "🎸", 5, 12),
    ]);

    /// <summary>The classic case where picking the best value-per-weight first goes wrong.</summary>
    public static KnapsackInstance GreedyTrap() => new("Greedy trap", 10,
    [
        new(0, "Statue", "🗿", 6, 42),
        new(1, "Painting", "🖼️", 5, 30),
        new(2, "Vase", "🏺", 5, 30),
        new(3, "Necklace", "📿", 1, 5),
    ]);

    private static readonly (string Name, string Icon)[] Pool =
    [
        ("Laptop", "💻"), ("Camera", "📷"), ("Headphones", "🎧"), ("Books", "📚"),
        ("Jacket", "🧥"), ("Snacks", "🍫"), ("Water", "💧"), ("Power bank", "🔋"),
        ("Toolkit", "🧰"), ("Guitar", "🎸"), ("Tent", "⛺"), ("Lantern", "🔦"),
        ("First aid", "🩹"), ("Compass", "🧭"), ("Map", "🗺️"), ("Drone", "🛸"),
    ];

    public static KnapsackInstance Random(int count, int capacity, int seed)
    {
        var rng = new Random(seed);
        var picks = Pool.OrderBy(_ => rng.Next()).Take(count).ToList();
        var maxWeight = Math.Max(2, capacity * 2 / 3);
        var items = picks.Select((p, i) =>
        {
            var weight = rng.Next(1, maxWeight + 1);
            var value = Math.Max(1, (int)Math.Round(weight * (1.5 + rng.NextDouble() * 4.5)) + rng.Next(-2, 3));
            return new KnapsackItem(i, p.Name, p.Icon, weight, value);
        }).ToList();
        return new KnapsackInstance($"Random #{seed}", capacity, items);
    }
}
