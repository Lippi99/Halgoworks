namespace Halgoworks.Core.Vrp;

/// <summary>A delivery stop. Coordinates in km; deadline in minutes after the shift starts.</summary>
public sealed record Customer(int Id, double X, double Y, int Demand, int Deadline);

/// <summary>
/// A capacitated routing instance. Node 0 is the depot and node k is customer k (ids start at 1).
/// Trucks leave at the start of the shift and drive at <see cref="Speed"/> km per minute.
/// </summary>
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
    double TruckCost = 40)
{
    public const int ShiftStart = 8 * 60;

    public (double X, double Y) Pos(int node) => node == 0 ? (DepotX, DepotY) : (Customers[node - 1].X, Customers[node - 1].Y);

    public double D(int a, int b)
    {
        var (ax, ay) = Pos(a);
        var (bx, by) = Pos(b);
        return Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }

    public Customer this[int node] => Customers[node - 1];

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
}
