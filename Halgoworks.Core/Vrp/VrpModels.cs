using System.Text.Json.Serialization;

namespace Halgoworks.Core.Vrp;

/// <summary>A delivery stop. Coordinates in km; deadline in minutes after the shift starts.</summary>
public sealed record Customer(int Id, double X, double Y, int Demand, int Deadline);

/// <summary>
/// A capacitated routing instance. Node 0 is the depot and node k is customer k (ids start at 1).
/// Trucks leave at the start of the shift and drive at <see cref="Speed"/> km per minute.
/// </summary>
/// <param name="Road">Optional road distances in km, row-major over nodes 0..n. When present (and the right size) every
/// algorithm uses them instead of straight lines.</param>
/// <param name="Geo">Optional [latitude, longitude] of the map's top-left corner, for instances placed on a real city.
/// X runs east and Y runs south from it, in km.</param>
public sealed record VrpInstance(
    string Name,
    double DepotX,
    double DepotY,
    IReadOnlyList<Customer> Customers,
    int Capacity,
    bool UseDeadlines,
    double Speed = 0.5,
    int Service = 5,
    double FuelPerKm = 0.35,
    double TruckCost = 40,
    IReadOnlyList<double>? Road = null,
    IReadOnlyList<double>? Geo = null)
{
    private const double KmPerDegreeLat = 110.574;
    private const double KmPerDegreeLon = 111.320;

    public const int ShiftStart = 8 * 60;

    public (double X, double Y) Pos(int node) => node == 0 ? (DepotX, DepotY) : (Customers[node - 1].X, Customers[node - 1].Y);

    [JsonIgnore]
    public bool HasRoads => Road is { } road && road.Count == (Customers.Count + 1) * (Customers.Count + 1);

    public double D(int a, int b)
    {
        if (HasRoads) return Road![a * (Customers.Count + 1) + b];
        var (ax, ay) = Pos(a);
        var (bx, by) = Pos(b);
        return Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }

    public Customer this[int node] => Customers[node - 1];

    /// <summary>Latitude and longitude of a node, for instances on a real map.</summary>
    public (double Lat, double Lon) LatLon(int node)
    {
        if (Geo is not [var lat0, var lon0]) throw new InvalidOperationException("This instance isn't on a real map.");
        var (x, y) = Pos(node);
        return (lat0 - y / KmPerDegreeLat, lon0 + x / (KmPerDegreeLon * Math.Cos(lat0 * Math.PI / 180)));
    }

    /// <summary>The inverse of <see cref="LatLon"/>: map km for a latitude and longitude.</summary>
    public static (double X, double Y) ToKm(IReadOnlyList<double> geo, double lat, double lon) =>
        ((lon - geo[1]) * KmPerDegreeLon * Math.Cos(geo[0] * Math.PI / 180), (geo[0] - lat) * KmPerDegreeLat);

    /// <summary>The top-left corner that puts a box of the given size, in km, around a city center.</summary>
    public static double[] CornerFor(double centerLat, double centerLon, double widthKm, double heightKm) =>
    [
        centerLat + heightKm / 2 / KmPerDegreeLat,
        centerLon - widthKm / 2 / (KmPerDegreeLon * Math.Cos(centerLat * Math.PI / 180)),
    ];

    [JsonIgnore]
    public int TotalDemand => Customers.Sum(c => c.Demand);

    public static string Clock(double minutesAfterStart)
    {
        var total = ShiftStart + (int)Math.Round(minutesAfterStart);
        return $"{total / 60:00}:{total % 60:00}";
    }
}

public sealed record RouteStats(int Load, double Distance, double Duration, IReadOnlyList<double> Arrivals, int LateStops, int? FirstLate)
{
    public bool OnTime => LateStops == 0;
}

public enum VrpAlgorithm
{
    Savings,
    Sweep,
    NearestNeighbor,
    SimulatedAnnealing,
    Genetic,
}

public enum SavingsStatus
{
    Pending,
    Merged,
    Rejected,
    Skipped,
}

public sealed record SavingsRow(int I, int J, double Saving, SavingsStatus Status, bool Current);

public sealed record VrpStep : Step
{
    /// <summary>Customer ids on each route, in driving order. Depot at both ends is implied.</summary>
    public IReadOnlyList<int[]> Routes { get; init; } = [];

    /// <summary>Palette slot for each route; -1 for routes drawn neutral (single-stop routes during savings).</summary>
    public IReadOnlyList<int> Colors { get; init; } = [];

    public double Distance { get; init; }

    public IReadOnlyList<int> Focus { get; init; } = [];

    /// <summary>A link under consideration, as [from, to] node ids.</summary>
    public int[]? Link { get; init; }

    public bool? LinkOk { get; init; }

    /// <summary>Index of the route being built or changed.</summary>
    public int? Active { get; init; }

    /// <summary>Sweep ray angle in radians, measured in map coordinates (y grows downward).</summary>
    public double? Sweep { get; init; }

    public IReadOnlyList<SavingsRow> Savings { get; init; } = [];

    /// <summary>2-opt: the two edges taken out and the two put in, as [a, b] node pairs.</summary>
    public IReadOnlyList<int[]> Removed { get; init; } = [];

    public IReadOnlyList<int[]> Added { get; init; } = [];

    /// <summary>Customers not on any route yet.</summary>
    public IReadOnlyList<int> Pending { get; init; } = [];

    public bool Final { get; init; }

    /// <summary>Metaheuristics: distance of the current plan (or population average) and of the best plan, per snapshot.</summary>
    public IReadOnlyList<double> History { get; init; } = [];

    public IReadOnlyList<double> BestHistory { get; init; } = [];
}
