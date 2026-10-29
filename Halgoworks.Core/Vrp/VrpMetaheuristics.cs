namespace Halgoworks.Core.Vrp;

/// <summary>
/// Simulated annealing and a genetic algorithm for capacitated routing with optional deadlines.
/// Both are seeded, so the same instance always plays back the same way (and share links reproduce it).
/// </summary>
/// <summary>A metaheuristic's final plan plus its convergence: the current (or average) distance and the best so far, per snapshot.</summary>
public sealed record MetaheuristicRun(IReadOnlyList<int[]> Best, IReadOnlyList<double> History, IReadOnlyList<double> BestHistory);

public static class VrpMetaheuristics
{
    private const int Seed = 1234;

    public static readonly AlgorithmInfo AnnealingInfo = new(
        "sa", "Simulated annealing", AlgorithmKind.LocalSearch,
        "Starts from a quick nearest-neighbor plan and keeps trying small random changes. Improvements are always kept; worse plans are sometimes accepted, less and less often as it cools.",
        "O(iterations × n)", "O(n)",
        [
            "s ← nearest-neighbor routes;  best ← s;  T ← T₀",
            "repeat N times:",
            "  s′ ← s with one random move: relocate, swap, 2-opt* or 2-opt",
            "  if s′ breaks capacity or a deadline: discard it",
            "  Δ ← distance(s′) − distance(s)",
            "  if Δ < 0 or random() < e^(−Δ / T): s ← s′",
            "  if s is shorter than best: best ← s",
            "  T ← T × α   ▹ cool down",
            "return best",
        ]);

    public static readonly AlgorithmInfo GeneticInfo = new(
        "ga", "Genetic algorithm", AlgorithmKind.LocalSearch,
        "Evolves a population of visiting orders. Children mix two parents' orders and mutate a little; each order is cut into the best possible set of truck routes by a split procedure.",
        "O(generations × population × n²)", "O(population × n)",
        [
            "population ← random visiting orders, plus one sweep order",
            "repeat for G generations:",
            "  keep the two best orders unchanged",
            "  pick parents by tournament (best of 3 at random)",
            "  child ← order crossover of the two parents",
            "  mutate the child: swap two customers or reverse a stretch",
            "  split each order into feasible truck routes (shortest path)",
            "  fitness ← total distance of those routes",
            "return the best routes found",
        ]);

    // ------------------------------------------------------------ Shared pieces

    private sealed record Route(int Color, List<int> Stops);

    private static double Distance(VrpInstance inst, IEnumerable<Route> routes) => routes.Sum(r => VrpSolver.Evaluate(inst, r.Stops).Distance);

    private static VrpStep Snapshot(VrpInstance inst, IReadOnlyList<Route> routes, VrpStep step) => step with
    {
        Routes = routes.Where(r => r.Stops.Count > 0).Select(r => r.Stops.ToArray()).ToList(),
        Colors = routes.Where(r => r.Stops.Count > 0).Select(r => r.Color).ToList(),
        Distance = Distance(inst, routes),
    };

    private static string C(int node) => node == 0 ? "the depot" : $"C{node}";

    private static string Km(double km) => $"{Num.F1(km)} km";

    // ------------------------------------------------------------ Simulated annealing

    public static IReadOnlyList<VrpStep> TraceAnnealing(VrpInstance inst, int iterations = 4000) => Annealing(inst, iterations, null, record: true).Steps;

    public static IReadOnlyList<int[]> SolveAnnealing(VrpInstance inst, int iterations = 4000) => Annealing(inst, iterations, null, record: false).Best;

    /// <summary>
    /// Untraced annealing that still reports how the distance evolved, for convergence charts. It starts from
    /// <paramref name="start"/> when given (it must be feasible), or from a nearest-neighbor plan.
    /// </summary>
    public static MetaheuristicRun RunAnnealing(VrpInstance inst, int iterations = 4000, IReadOnlyList<int[]>? start = null, double? startDetour = null)
    {
        var (_, best, history, bestHistory) = Annealing(inst, iterations, start, record: false, startDetour);
        return new MetaheuristicRun(best, history, bestHistory);
    }

    private static (List<VrpStep> Steps, IReadOnlyList<int[]> Best, List<double> History, List<double> BestHistory) Annealing(VrpInstance inst, int iterations, IReadOnlyList<int[]>? from, bool record, double? startDetour = null)
    {
        var rng = new Random(Seed);
        var steps = new List<VrpStep>();
        var start = from ?? VrpSolver.Solve(inst, VrpAlgorithm.NearestNeighbor, false);
        var current = start.Select((r, i) => new Route(i, r.ToList())).ToList();
        var cost = Distance(inst, current);
        var best = Clone(current);
        var bestCost = cost;
        var startCost = cost;
        var history = new List<double> { cost };
        var bestHistory = new List<double> { cost };

        // Start warm enough that a 1.5 km detour is accepted about half the time, and cool to almost nothing.
        // A good starting plan (given by the caller) only needs polishing, so it starts much cooler.
        var t0 = (startDetour ?? (from is null ? 1.5 : 0.15)) / Math.Log(2);
        var near = Neighbors(inst, Math.Min(10, inst.Customers.Count - 1));
        const double tEnd = 0.01;
        var alpha = Math.Pow(tEnd / t0, 1.0 / iterations);
        var temperature = t0;
        var every = Math.Max(1, iterations / 70);
        int accepted = 0, uphill = 0, rejected = 0, infeasible = 0;

        void Add(VrpStep step)
        {
            if (record) steps.Add(Snapshot(inst, current, step with { History = history.ToList(), BestHistory = bestHistory.ToList() }));
        }

        Add(new VrpStep
        {
            Title = "Start from nearest neighbor",
            Text = $"Simulated annealing needs a starting plan, any plan. A quick nearest-neighbor run gives {current.Count} trucks and **{Km(cost)}**. From here it will try {iterations} small random changes, keeping the shortest plan it ever sees.",
            Line = 0,
            Phase = "Start",
            Watches = [Watch.Of("distance", Km(cost)), Watch.Of("trucks", current.Count), Watch.Of("temperature", Num.F2(temperature))],
        });

        for (var iter = 1; iter <= iterations; iter++)
        {
            var move = RandomMove(inst, current, rng, near);
            temperature *= alpha;
            if (move is null)
            {
                infeasible++;
                continue;
            }

            var candidateCost = Distance(inst, move.Routes);
            var delta = candidateCost - cost;
            var accept = delta < -1e-9 || rng.NextDouble() < Math.Exp(-delta / temperature);
            if (!accept)
            {
                rejected++;
            }
            else
            {
                accepted++;
                if (delta > 1e-9) uphill++;
                current = move.Routes;
                cost = candidateCost;
            }

            var newBest = cost < bestCost - 1e-9;
            if (newBest)
            {
                best = Clone(current);
                bestCost = cost;
            }

            if (!newBest && iter % every != 0 && iter != iterations) continue;
            history.Add(cost);
            bestHistory.Add(bestCost);
            var summary = $"Since the last snapshot: {accepted} changes kept ({uphill} of them uphill), {rejected} rejected, {infeasible} broke a constraint.";
            accepted = uphill = rejected = infeasible = 0;
            var heat = temperature > 0.5
                ? "It's still hot, so detours that make the plan longer are often accepted: that's how it escapes poor local layouts."
                : temperature > 0.08
                    ? "It's cooling: uphill moves get rarer."
                    : "It's almost frozen: only improvements get through now.";
            Add(new VrpStep
            {
                Title = newBest ? $"Iteration {iter}: new best {Km(bestCost)}" : $"Iteration {iter}: {Km(cost)}",
                Text = newBest
                    ? $"{move.Describe(Km(delta))} That's the shortest plan so far: **{Km(bestCost)}**. {summary}"
                    : $"Temperature {Num.F2(temperature)}. {heat} {summary}",
                Line = newBest ? 6 : 5,
                Phase = "Anneal",
                Removed = newBest ? move.Removed : [],
                Added = newBest ? move.Added : [],
                Focus = newBest ? move.Focus : [],
                Watches =
                [
                    Watch.Of("iteration", iter), Watch.Of("temperature", Num.F2(temperature)), Watch.Of("current", Km(cost)),
                    Watch.Of("best", Km(bestCost)), Watch.Of("trucks", current.Count(r => r.Stops.Count > 0)),
                ],
            });
        }

        current = best;
        var saved = startCost - bestCost;
        Add(new VrpStep
        {
            Title = $"{best.Count(r => r.Stops.Count > 0)} trucks, {Km(bestCost)}",
            Text = $"Cooled down. The best plan found uses **{best.Count(r => r.Stops.Count > 0)} trucks** and **{Km(bestCost)}**, {Km(saved)} shorter than the starting plan ({Num.Pct(saved / startCost)} saved). Simulated annealing gives no guarantee of optimality, but random restarts and slower cooling usually help.",
            Line = 8,
            Phase = "Done",
            Final = true,
            Watches = [Watch.Of("best", Km(bestCost)), Watch.Of("start", Km(startCost)), Watch.Of("trucks", best.Count(r => r.Stops.Count > 0))],
        });

        return (steps, best.Where(r => r.Stops.Count > 0).Select(r => r.Stops.ToArray()).ToList(), history, bestHistory);
    }

    private static List<Route> Clone(IEnumerable<Route> routes) => routes.Select(r => r with { Stops = r.Stops.ToList() }).ToList();

    private sealed record Move(List<Route> Routes, string Kind, IReadOnlyList<int[]> Removed, IReadOnlyList<int[]> Added, IReadOnlyList<int> Focus, string What)
    {
        public string Describe(string delta) => $"{What} ({delta}).";
    }

    // One random neighbor, or null if the change breaks capacity or a deadline.
    /// <summary>Each customer's nearest fellow customers, closest first.</summary>
    private static int[][] Neighbors(VrpInstance inst, int k)
    {
        var n = inst.Customers.Count;
        var near = new int[n + 1][];
        near[0] = [];
        for (var c = 1; c <= n; c++)
            near[c] = Enumerable.Range(1, n).Where(d => d != c).OrderBy(d => inst.D(c, d)).Take(k).ToArray();
        return near;
    }

    // Moves are random but local: a customer is moved next to, swapped with, or linked to one of its nearest
    // neighbors. Fully random moves almost always land far away on big maps, so nearly all of them get rejected.
    private static Move? RandomMove(VrpInstance inst, List<Route> routes, Random rng, int[][] near)
    {
        var next = Clone(routes);
        var routeOf = new int[inst.Customers.Count + 1];
        for (var r = 0; r < next.Count; r++)
            foreach (var stop in next[r].Stops)
                routeOf[stop] = r;
        int Prev(List<int> s, int i) => i == 0 ? 0 : s[i - 1];
        int Next(List<int> s, int i) => i == s.Count - 1 ? 0 : s[i + 1];

        var c = rng.Next(1, inst.Customers.Count + 1);
        if (near[c].Length == 0) return null;
        var d = near[c][rng.Next(near[c].Length)];
        var a = routeOf[c];
        var b = routeOf[d];

        switch (rng.Next(4))
        {
            case 0:
            {
                // Relocate c right before or after its neighbor d (possibly on another truck).
                var from = next[a].Stops;
                var i = from.IndexOf(c);
                var removed = new List<int[]> { new[] { Prev(from, i), c }, new[] { c, Next(from, i) } };
                from.RemoveAt(i);
                var to = next[b].Stops;
                var j = to.IndexOf(d) + rng.Next(2);
                to.Insert(j, c);
                var added = new List<int[]> { new[] { Prev(to, j), c }, new[] { c, Next(to, j) } };
                if (!Feasible(inst, next[a]) || !Feasible(inst, next[b])) return null;
                var what = a == b ? $"Moved =={C(c)}== next to {C(d)} on its own route" : $"Moved =={C(c)}== next to {C(d)}, on a different truck";
                return new Move(next, "relocate", removed, added, [c], what);
            }
            case 1:
            {
                // Swap c with a customer next to its neighbor d, on another truck: c takes that place near d.
                if (a == b) return null;
                var ra = next[a].Stops;
                var rb = next[b].Stops;
                var i = ra.IndexOf(c);
                var j = Math.Clamp(rb.IndexOf(d) + rng.Next(-1, 2), 0, rb.Count - 1);
                (ra[i], rb[j]) = (rb[j], ra[i]);
                if (!Feasible(inst, next[a]) || !Feasible(inst, next[b])) return null;
                return new Move(next, "swap", [], [new[] { Prev(ra, i), ra[i] }, new[] { ra[i], Next(ra, i) }, new[] { Prev(rb, j), rb[j] }, new[] { rb[j], Next(rb, j) }],
                    [ra[i], rb[j]], $"Swapped =={C(ra[i])}== and =={C(rb[j])}== between two trucks");
            }
            case 2:
            {
                // 2-opt*: exchange the tails of two routes so that c drives straight on to d.
                if (a == b) return null;
                var ra = next[a].Stops;
                var rb = next[b].Stops;
                var i = ra.IndexOf(c) + 1;
                var j = rb.IndexOf(d);
                var tailA = ra.Skip(i).ToList();
                var tailB = rb.Skip(j).ToList();
                var cutA = new[] { i == 0 ? 0 : ra[i - 1], i < ra.Count ? ra[i] : 0 };
                var cutB = new[] { j == 0 ? 0 : rb[j - 1], j < rb.Count ? rb[j] : 0 };
                ra.RemoveRange(i, ra.Count - i);
                rb.RemoveRange(j, rb.Count - j);
                ra.AddRange(tailB);
                rb.AddRange(tailA);
                if (!Feasible(inst, next[a]) || !Feasible(inst, next[b])) return null;
                return new Move(next, "2-opt*", [cutA, cutB], [new[] { cutA[0], cutB[1] }, new[] { cutB[0], cutA[1] }],
                    new[] { cutA[0], cutA[1], cutB[0], cutB[1] }.Where(x => x != 0).Distinct().ToList(), "Exchanged the ends of two truck routes (2-opt*)");
            }
            default:
            {
                // 2-opt inside one route: reverse the stretch between c and its neighbor d.
                if (a != b) return null;
                var r = next[a].Stops;
                var (i, j) = (r.IndexOf(c), r.IndexOf(d));
                if (i > j) (i, j) = (j, i);
                i++;
                if (i >= j) return null;
                var before = Prev(r, i);
                var after = Next(r, j);
                var removed = new List<int[]> { new[] { before, r[i] }, new[] { r[j], after } };
                r.Reverse(i, j - i + 1);
                if (!Feasible(inst, next[a])) return null;
                return new Move(next, "2-opt", removed, [new[] { before, r[i] }, new[] { r[j], after }], [r[i], r[j]],
                    $"Reversed the stretch between =={C(r[j])}== and =={C(r[i])}== on one route (2-opt)");
            }
        }
    }

    private static bool Feasible(VrpInstance inst, Route route) => route.Stops.Count == 0 || VrpSolver.Feasible(inst, route.Stops);

    // ------------------------------------------------------------ Genetic algorithm

    public static IReadOnlyList<VrpStep> TraceGenetic(VrpInstance inst, int generations = 70, int population = 36) => Genetic(inst, generations, population, null, record: true).Steps;

    public static IReadOnlyList<int[]> SolveGenetic(VrpInstance inst, int generations = 70, int population = 36) => Genetic(inst, generations, population, null, record: false).Best;

    /// <summary>
    /// Untraced genetic search that still reports the population average and best per generation. When
    /// <paramref name="seed"/> is given, its routes joined end to end join the first population as one more giant tour.
    /// </summary>
    public static MetaheuristicRun RunGenetic(VrpInstance inst, int generations = 70, int population = 36, IReadOnlyList<int[]>? seed = null)
    {
        var (_, best, history, bestHistory) = Genetic(inst, generations, population, seed, record: false);
        return new MetaheuristicRun(best, history, bestHistory);
    }

    private sealed record Individual(int[] Order, List<int[]> Routes, double Cost);

    private static (List<VrpStep> Steps, IReadOnlyList<int[]> Best, List<double> History, List<double> BestHistory) Genetic(VrpInstance inst, int generations, int size, IReadOnlyList<int[]>? seed, bool record)
    {
        var rng = new Random(Seed);
        var n = inst.Customers.Count;
        var steps = new List<VrpStep>();
        var history = new List<double>();
        var bestHistory = new List<double>();

        Individual Make(int[] order)
        {
            var routes = Split(inst, order);
            return new Individual(order, routes, VrpSolver.TotalDistance(inst, routes));
        }

        // Seed with one angular (sweep) order; the rest are random.
        var sweepOrder = inst.Customers
            .OrderBy(c => Math.Atan2(c.Y - inst.DepotY, c.X - inst.DepotX))
            .Select(c => c.Id).ToArray();
        var pop = new List<Individual> { Make(sweepOrder) };
        // Splitting the seed's giant tour is at least as good as the seed itself, since the split is optimal for its order.
        if (seed is not null) pop.Add(Make(seed.SelectMany(r => r).ToArray()));
        while (pop.Count < size)
        {
            var order = Enumerable.Range(1, n).OrderBy(_ => rng.Next()).ToArray();
            pop.Add(Make(order));
        }

        pop = pop.OrderBy(i => i.Cost).ToList();
        var best = pop[0];
        var startBest = best.Cost;

        void Add(Individual shown, VrpStep step)
        {
            if (!record) return;
            var routes = Colored(inst, shown.Routes);
            steps.Add(step with
            {
                Routes = routes.Select(r => r.Stops.ToArray()).ToList(),
                Colors = routes.Select(r => r.Color).ToList(),
                Distance = shown.Cost,
                History = history.ToList(),
                BestHistory = bestHistory.ToList(),
            });
        }

        history.Add(pop.Average(i => i.Cost));
        bestHistory.Add(best.Cost);
        Add(best, new VrpStep
        {
            Title = $"Generation 0: {size} random plans",
            Text = $"Each individual is just an **order** to visit all {n} customers, like one giant tour. A *split* procedure cuts that order into truck routes in the best possible way, respecting capacity{(inst.UseDeadlines ? " and deadlines" : "")}. Most random orders are poor (average {Km(history[0])}); the best starting plan is {Km(best.Cost)}.",
            Line = 0,
            Phase = "Start",
            Watches = [Watch.Of("population", size), Watch.Of("best", Km(best.Cost)), Watch.Of("average", Km(history[0]))],
        });

        for (var g = 1; g <= generations; g++)
        {
            var next = pop.Take(2).ToList();
            while (next.Count < size)
            {
                var a = Tournament(pop, rng);
                var b = Tournament(pop, rng);
                var child = OrderCrossover(a.Order, b.Order, rng);
                if (rng.NextDouble() < 0.5)
                {
                    var i = rng.Next(n);
                    var j = rng.Next(n);
                    (child[i], child[j]) = (child[j], child[i]);
                }
                else
                {
                    var i = rng.Next(n);
                    var j = rng.Next(i, n);
                    Array.Reverse(child, i, j - i + 1);
                }

                next.Add(Make(child));
            }

            pop = next.OrderBy(i => i.Cost).ToList();
            var improved = pop[0].Cost < best.Cost - 1e-9;
            var gain = best.Cost - pop[0].Cost;
            if (improved) best = pop[0];
            history.Add(pop.Average(i => i.Cost));
            bestHistory.Add(best.Cost);
            Add(best, new VrpStep
            {
                Title = improved ? $"Generation {g}: new best {Km(best.Cost)}" : $"Generation {g}: best {Km(best.Cost)}",
                Text = improved
                    ? $"A child beat every plan seen so far by {Km(gain)}: **{Km(best.Cost)}** with {best.Routes.Count} trucks. The population average is now {Km(history[^1])}, as good orders spread through crossover."
                    : $"No improvement this generation. The two best plans survive unchanged, and the population average is {Km(history[^1])}: good stretches of visiting order keep spreading.",
                Line = improved ? 7 : 4,
                Phase = "Evolve",
                Watches = [Watch.Of("generation", g), Watch.Of("best", Km(best.Cost)), Watch.Of("average", Km(history[^1])), Watch.Of("trucks", best.Routes.Count)],
            });
        }

        Add(best, new VrpStep
        {
            Title = $"{best.Routes.Count} trucks, {Km(best.Cost)}",
            Text = $"After {generations} generations the best plan uses **{best.Routes.Count} trucks** and **{Km(best.Cost)}**, down from {Km(startBest)} in the first generation. Like annealing it carries no guarantee, but it explores many very different plans at once.",
            Line = 8,
            Phase = "Done",
            Final = true,
            Watches = [Watch.Of("best", Km(best.Cost)), Watch.Of("first generation", Km(startBest)), Watch.Of("trucks", best.Routes.Count)],
        });

        return (steps, best.Routes, history, bestHistory);
    }

    private static Individual Tournament(List<Individual> pop, Random rng)
    {
        var best = pop[rng.Next(pop.Count)];
        for (var k = 0; k < 2; k++)
        {
            var other = pop[rng.Next(pop.Count)];
            if (other.Cost < best.Cost) best = other;
        }

        return best;
    }

    // OX: copy a slice from the first parent, fill the rest in the second parent's order.
    private static int[] OrderCrossover(int[] a, int[] b, Random rng)
    {
        var n = a.Length;
        var i = rng.Next(n);
        var j = rng.Next(i, n);
        var child = new int[n];
        var used = new HashSet<int>();
        for (var k = i; k <= j; k++)
        {
            child[k] = a[k];
            used.Add(a[k]);
        }

        var pos = (j + 1) % n;
        for (var k = 0; k < n; k++)
        {
            var c = b[(j + 1 + k) % n];
            if (used.Contains(c)) continue;
            child[pos] = c;
            pos = (pos + 1) % n;
        }

        return child;
    }

    /// <summary>
    /// Prins' split: the best way to cut a giant tour into consecutive truck routes, as a shortest path over
    /// cut positions. Each candidate route is extended one stop at a time, so the whole split is O(n²).
    /// </summary>
    public static List<int[]> Split(VrpInstance inst, IReadOnlyList<int> order)
    {
        var n = order.Count;
        var cost = Enumerable.Repeat(double.MaxValue, n + 1).ToArray();
        var from = new int[n + 1];
        cost[0] = 0;
        for (var i = 0; i < n; i++)
        {
            if (cost[i] == double.MaxValue) continue;
            int load = 0, previous = 0;
            double distance = 0, clock = 0;
            for (var j = i; j < n; j++)
            {
                var c = order[j];
                load += inst[c].Demand;
                if (load > inst.Capacity) break;
                var leg = inst.D(previous, c);
                distance += leg;
                clock += leg / inst.Speed;
                if (inst.UseDeadlines && clock > inst[c].Deadline + 1e-9) break;
                clock += inst.Service;
                previous = c;
                var total = cost[i] + distance + inst.D(c, 0);
                if (total >= cost[j + 1]) continue;
                cost[j + 1] = total;
                from[j + 1] = i;
            }
        }

        var routes = new List<int[]>();
        for (var end = n; end > 0; end = from[end]) routes.Add(order.Skip(from[end]).Take(end - from[end]).ToArray());
        routes.Reverse();
        return routes;
    }

    // Colors by bearing around the depot, so a truck heading the same way keeps its color between generations.
    private static List<Route> Colored(VrpInstance inst, IEnumerable<int[]> routes) => routes
        .OrderBy(r => Math.Atan2(r.Average(c => inst[c].Y) - inst.DepotY, r.Average(c => inst[c].X) - inst.DepotX))
        .Select((r, i) => new Route(i, r.ToList()))
        .ToList();
}
