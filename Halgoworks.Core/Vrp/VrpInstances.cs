namespace Halgoworks.Core.Vrp;

public static class VrpInstances
{
    /// <summary>
    /// Customers on a 20 × 20 km map with the depot in the middle. Deadlines always leave time to
    /// drive straight from the depot, so every customer can be served by some truck.
    /// </summary>
    public static VrpInstance Random(int count, int seed, bool clustered, bool deadlines)
    {
        var rng = new Random(seed);
        const double depotX = 10, depotY = 10;
        var centers = Enumerable.Range(0, rng.Next(3, 5))
            .Select(_ =>
            {
                var angle = rng.NextDouble() * Math.PI * 2;
                var radius = 4.5 + rng.NextDouble() * 4.5;
                return (X: depotX + Math.Cos(angle) * radius, Y: depotY + Math.Sin(angle) * radius);
            })
            .ToList();

        var customers = new List<Customer>();
        for (var id = 1; id <= count; id++)
        {
            double x, y;
            if (clustered)
            {
                var c = centers[rng.Next(centers.Count)];
                x = Math.Clamp(c.X + Gaussian(rng) * 1.4, 0.6, 19.4);
                y = Math.Clamp(c.Y + Gaussian(rng) * 1.4, 0.6, 19.4);
            }
            else
            {
                x = 1 + rng.NextDouble() * 18;
                y = 1 + rng.NextDouble() * 18;
            }

            var demand = rng.Next(1, 7) + (rng.NextDouble() < 0.2 ? rng.Next(1, 3) : 0);
            var direct = Math.Sqrt((x - depotX) * (x - depotX) + (y - depotY) * (y - depotY)) / 0.5;
            var deadline = (int)(Math.Ceiling((direct + 25 + rng.NextDouble() * 190) / 15) * 15);
            customers.Add(new Customer(id, Math.Round(x, 2), Math.Round(y, 2), demand, deadline));
        }

        var total = customers.Sum(c => c.Demand);
        var capacity = Math.Max(customers.Max(c => c.Demand), (int)Math.Ceiling(total / 4.2 / 5) * 5);
        var name = clustered ? $"Neighborhoods #{seed}" : $"City center #{seed}";
        return new VrpInstance(name, depotX, depotY, customers, capacity, deadlines);
    }

    /// <summary>
    /// Many customers for the Scale view, with no deadlines and trucks sized for about ten stops each, plus 20%
    /// slack so plans have room to change.
    /// </summary>
    public static VrpInstance Large(int count, int seed, bool clustered)
    {
        var instance = Random(count, seed, clustered, deadlines: false);
        var capacity = Math.Max(instance.Customers.Max(c => c.Demand), (int)Math.Ceiling(1.2 * instance.TotalDemand / Math.Max(1.0, count / 10.0)));
        return instance with { Name = $"{count} customers #{seed}", Capacity = capacity };
    }

    /// <summary>
    /// Customers inside a real city: the usual generator, shrunk to a box of the given size around
    /// the city center. Distances stay straight-line until road distances are attached.
    /// </summary>
    public static VrpInstance InCity(string city, double centerLat, double centerLon, double boxKm, int count, int seed, bool clustered)
    {
        var plain = Random(count, seed, clustered, deadlines: false);
        var scale = boxKm / 20;
        return plain with
        {
            Name = $"{city} #{seed}",
            DepotX = plain.DepotX * scale,
            DepotY = plain.DepotY * scale,
            Customers = plain.Customers.Select(c => c with { X = Math.Round(c.X * scale, 3), Y = Math.Round(c.Y * scale, 3) }).ToList(),
            Geo = VrpInstance.CornerFor(centerLat, centerLon, boxKm, boxKm),
        };
    }

    private static double Gaussian(Random rng)
    {
        var u1 = 1 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
