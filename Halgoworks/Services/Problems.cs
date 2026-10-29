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
    string Algorithms)
{
    /// <summary>Page title for search results: the terms people search for, before the site name.</summary>
    public string SeoTitle { get; init; } = Name;

    /// <summary>Meta description for search results and link previews, about 150 characters.</summary>
    public string SeoDescription { get; init; } = Summary;

    /// <summary>Topics for structured data (what the page teaches).</summary>
    public IReadOnlyList<string> Teaches { get; init; } = [];
}

public static class Problems
{
    public static readonly ProblemInfo Routing = new(
        "routing", "Vehicle routing", "Routing", "Route",
        "Plan routes for a fleet of trucks delivering to many customers, within truck capacity and delivery deadlines, at the lowest fuel cost.",
        ["Parcel delivery", "Logistics", "Fleet management"],
        "NP-hard", false,
        "Clarke–Wright savings, sweep, nearest neighbor, 2-opt, simulated annealing, genetic algorithm")
    {
        SeoTitle = "Vehicle routing problem (VRP) visualized: savings, 2-opt, annealing",
        SeoDescription = "See how Clarke–Wright savings, sweep, 2-opt, simulated annealing and genetic algorithms plan delivery routes, step by step, on maps and real city streets.",
        Teaches = ["Vehicle routing problem", "Clarke–Wright savings algorithm", "2-opt", "Simulated annealing", "Genetic algorithm"],
    };

    public static readonly ProblemInfo Knapsack = new(
        "knapsack", "Knapsack", "Knapsack", "Choose",
        "Choose the most valuable combination of items without going over a weight or budget limit.",
        ["Budget allocation", "Cargo loading", "Investment selection"],
        "NP-hard", false,
        "Dynamic programming, branch and bound, integer programming, greedy by density")
    {
        SeoTitle = "Knapsack problem visualized: dynamic programming and branch and bound",
        SeoDescription = "Watch the 0/1 knapsack problem solved step by step: the dynamic programming table, a branch-and-bound search tree, an integer program and greedy rules.",
        Teaches = ["0/1 knapsack problem", "Dynamic programming", "Branch and bound", "Integer programming", "Greedy algorithm"],
    };

    public static readonly ProblemInfo JobShop = new(
        "job-shop", "Job-shop scheduling", "Job shop", "Schedule",
        "Order operations on shared machines so every job finishes as early as possible, respecting each job's sequence.",
        ["Manufacturing", "Employee scheduling", "Timetabling"],
        "NP-hard", false,
        "Giffler–Thompson with six dispatch rules, tabu search")
    {
        SeoTitle = "Job-shop scheduling visualized: dispatch rules and tabu search",
        SeoDescription = "Watch job-shop scheduling algorithms build Gantt charts step by step: Giffler–Thompson with six dispatch rules, and tabu search along the critical path.",
        Teaches = ["Job-shop scheduling", "Giffler–Thompson algorithm", "Dispatch rules", "Tabu search", "Critical path"],
    };

    public static readonly ProblemInfo BinPacking = new(
        "bin-packing", "Bin packing", "Bin packing", "Pack",
        "Fit items of different sizes into as few fixed-size containers as possible.",
        ["Cloud server allocation", "Warehouse packing", "Memory management"],
        "NP-hard", false,
        "Next, first, best and worst fit, sorted variants, integer programming")
    {
        SeoTitle = "Bin packing problem visualized: first fit, best fit, FFD",
        SeoDescription = "Watch bin packing heuristics place items step by step: next fit, first fit, best fit, worst fit, first fit decreasing, and an exact search for the optimum.",
        Teaches = ["Bin packing problem", "First fit decreasing", "Best fit", "Approximation algorithms", "Integer programming"],
    };

    public static readonly ProblemInfo MaxFlow = new(
        "max-flow", "Maximum flow", "Max flow", "Flow",
        "Push as much as possible from a source to a sink through a network whose links have limited capacity.",
        ["Network bandwidth", "Water distribution", "Supply chains"],
        "Polynomial time", true,
        "Edmonds–Karp, Ford–Fulkerson, Dinic, push–relabel")
    {
        SeoTitle = "Maximum flow visualized: Edmonds–Karp, Dinic, push–relabel",
        SeoDescription = "Watch maximum flow algorithms push flow through a network step by step: Ford–Fulkerson, Edmonds–Karp, Dinic and push–relabel, ending at the minimum cut.",
        Teaches = ["Maximum flow problem", "Ford–Fulkerson method", "Edmonds–Karp algorithm", "Dinic's algorithm", "Push–relabel algorithm", "Minimum cut"],
    };

    public static readonly ProblemInfo Matching = new(
        "matching", "Bipartite matching", "Matching", "Match",
        "Pair up as many members of two groups as possible, using only allowed pairs and nobody twice. Solved by turning it into a maximum flow.",
        ["Job assignment", "Scheduling shifts", "Reviewer assignment"],
        "Polynomial time", true,
        "Reduction to max flow: Edmonds–Karp, Ford–Fulkerson, Dinic; greedy for comparison")
    {
        SeoTitle = "Bipartite matching visualized: augmenting paths via max flow",
        SeoDescription = "Watch maximum bipartite matching solved step by step by turning it into a maximum flow problem, with augmenting paths and a greedy comparison.",
        Teaches = ["Maximum bipartite matching", "Augmenting paths", "Maximum flow reduction", "Hopcroft–Karp"],
    };

    public static readonly IReadOnlyList<ProblemInfo> All = [Routing, Knapsack, JobShop, BinPacking, MaxFlow, Matching];
}
