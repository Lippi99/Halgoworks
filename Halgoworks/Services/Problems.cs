namespace Halgoworks.Services;

public sealed record ProblemInfo(
    string Slug,
    string Name,
    string NavName,
    string Verb,
    string Summary,
    IReadOnlyList<string> Uses,
    string Complexity,
    bool Polynomial,
    string Algorithms);

public static class Problems
{
    public static readonly ProblemInfo Routing = new(
        "routing", "Vehicle routing", "Routing", "Route",
        "Plan routes for a fleet of trucks delivering to many customers, within truck capacity and delivery deadlines, at the lowest fuel cost.",
        ["Parcel delivery", "Logistics", "Fleet management"],
        "NP-hard", false,
        "Clarke–Wright savings, sweep, nearest neighbor, 2-opt");

    public static readonly ProblemInfo Knapsack = new(
        "knapsack", "Knapsack", "Knapsack", "Choose",
        "Choose the most valuable combination of items without going over a weight or budget limit.",
        ["Budget allocation", "Cargo loading", "Investment selection"],
        "NP-hard", false,
        "Dynamic programming, greedy by density");

    public static readonly ProblemInfo JobShop = new(
        "job-shop", "Job-shop scheduling", "Job shop", "Schedule",
        "Order operations on shared machines so every job finishes as early as possible, respecting each job's sequence.",
        ["Manufacturing", "Employee scheduling", "Timetabling"],
        "NP-hard", false,
        "Giffler–Thompson with six dispatch rules, tabu search");

    public static readonly ProblemInfo BinPacking = new(
        "bin-packing", "Bin packing", "Bin packing", "Pack",
        "Fit items of different sizes into as few fixed-size containers as possible.",
        ["Cloud server allocation", "Warehouse packing", "Memory management"],
        "NP-hard", false,
        "Next, first, best and worst fit, plus sorted variants");

    public static readonly ProblemInfo MaxFlow = new(
        "max-flow", "Maximum flow", "Max flow", "Flow",
        "Push as much as possible from a source to a sink through a network whose links have limited capacity.",
        ["Network bandwidth", "Water distribution", "Supply chains"],
        "Polynomial time", true,
        "Edmonds–Karp, Ford–Fulkerson, Dinic");

    public static readonly IReadOnlyList<ProblemInfo> All = [Routing, Knapsack, JobShop, BinPacking, MaxFlow];
}
