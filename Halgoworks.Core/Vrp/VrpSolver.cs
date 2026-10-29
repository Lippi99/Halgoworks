namespace Halgoworks.Core.Vrp;

public static class VrpSolver
{
    private static readonly string[] TwoOptCode =
    [
        "for each route:   ▹ 2-opt polish",
        "  repeat until no swap helps:",
        "    for each pair of edges (a, b), (c, d) on the route:",
        "      if d(a,c) + d(b,d) < d(a,b) + d(c,d): reverse b … c",
    ];

    public static bool IsMetaheuristic(VrpAlgorithm algorithm) => algorithm is VrpAlgorithm.SimulatedAnnealing or VrpAlgorithm.Genetic;

    public static AlgorithmInfo Info(VrpAlgorithm algorithm, bool polish)
    {
        if (algorithm == VrpAlgorithm.SimulatedAnnealing) return VrpMetaheuristics.AnnealingInfo;
        if (algorithm == VrpAlgorithm.Genetic) return VrpMetaheuristics.GeneticInfo;
        var info = algorithm switch
        {
            VrpAlgorithm.Sweep => new AlgorithmInfo(
                "sweep", "Sweep", AlgorithmKind.Heuristic,
                "Rotates a ray around the depot and groups customers into trucks in angular order, a natural fit for customers spread around a central depot.",
                "O(n² ) with cheapest insertion", "O(n)",
                [
                    "angle(c) ← direction of c as seen from the depot",
                    "sort customers by angle, starting at the widest gap",
                    "start an empty route",
                    "for each customer c in angle order:",
                    "  if c fits: insert c where it adds the least distance",
                    "  else: close the route; start a new one with c",
                    "return the routes",
                ]),
            VrpAlgorithm.NearestNeighbor => new AlgorithmInfo(
                "nn", "Nearest neighbor", AlgorithmKind.Heuristic,
                "Each truck keeps driving to the closest customer it can still serve, and goes home when nothing fits.",
                "O(n²)", "O(n)",
                [
                    "while customers remain:",
                    "  a new truck leaves the depot",
                    "  repeat:",
                    "    c ← nearest unserved customer that fits (load, deadline)",
                    "    if there is none: return to the depot; stop",
                    "    drive to c; load ← load + demand(c)",
                    "return the routes",
                ]),
            _ => new AlgorithmInfo(
                "savings", "Clarke–Wright savings", AlgorithmKind.Heuristic,
                "Starts with one truck per customer and keeps joining the two routes whose merge saves the most distance.",
                "O(n² log n)", "O(n²)",
                [
                    "start: one route per customer (depot → c → depot)",
                    "for every pair i, j: s(i, j) ← d(0,i) + d(0,j) − d(i,j)",
                    "sort the pairs by saving, largest first",
                    "for each pair (i, j) in that order:",
                    "  if i and j are on different routes,",
                    "     both are next to the depot on their routes,",
                    "     and the joined route fits (load, deadlines):",
                    "    join the two routes with the link i — j",
                    "return the routes",
                ]),
        };

        return polish ? info with { Code = [.. info.Code, .. TwoOptCode] } : info;
    }

    public static IReadOnlyList<VrpStep> Trace(VrpInstance instance, VrpAlgorithm algorithm, bool polish)
    {
        if (algorithm == VrpAlgorithm.SimulatedAnnealing) return VrpMetaheuristics.TraceAnnealing(instance);
        if (algorithm == VrpAlgorithm.Genetic) return VrpMetaheuristics.TraceGenetic(instance);
        var tracer = Run(instance, algorithm, polish, record: true);
        return tracer.Colorize();
    }

    /// <summary>Final routes only. Skips the per-step snapshots, so it scales to large instances.</summary>
    public static IReadOnlyList<int[]> Solve(VrpInstance instance, VrpAlgorithm algorithm, bool polish) => algorithm switch
    {
        VrpAlgorithm.SimulatedAnnealing => VrpMetaheuristics.SolveAnnealing(instance),
        VrpAlgorithm.Genetic => VrpMetaheuristics.SolveGenetic(instance),
        _ => Run(instance, algorithm, polish, record: false).FinalRoutes(),
    };

    private static Tracer Run(VrpInstance instance, VrpAlgorithm algorithm, bool polish, bool record)
    {
        var tracer = new Tracer(instance, algorithm, record);
        switch (algorithm)
        {
            case VrpAlgorithm.Sweep: tracer.Sweep(); break;
            case VrpAlgorithm.NearestNeighbor: tracer.Nearest(); break;
            default: tracer.Savings(); break;
        }

        var baseLines = Info(algorithm, false).Code.Count;
        if (polish) tracer.Polish(baseLines);
        // The final step points at the base algorithm's "return the routes", which is its last line.
        tracer.Finish(baseLines);
        return tracer;
    }

    public static RouteStats Evaluate(VrpInstance instance, IReadOnlyList<int> route)
    {
        double t = 0, distance = 0;
        int previous = 0, load = 0, late = 0;
        int? firstLate = null;
        var arrivals = new List<double>(route.Count);
        foreach (var c in route)
        {
            var leg = instance.D(previous, c);
            distance += leg;
            t += leg / instance.Speed;
            arrivals.Add(t);
            if (instance.UseDeadlines && t > instance[c].Deadline + 1e-9)
            {
                late++;
                firstLate ??= c;
            }

            t += instance.Service;
            load += instance[c].Demand;
            previous = c;
        }

        var back = instance.D(previous, 0);
        return new RouteStats(load, distance + back, t + back / instance.Speed, arrivals, late, firstLate);
    }

    public static bool Feasible(VrpInstance instance, IReadOnlyList<int> route)
    {
        var stats = Evaluate(instance, route);
        return stats.Load <= instance.Capacity && stats.OnTime;
    }

    public static double TotalDistance(VrpInstance instance, IEnumerable<IReadOnlyList<int>> routes) =>
        routes.Sum(r => r.Count == 0 ? 0 : Evaluate(instance, r).Distance);

    public static double Cost(VrpInstance instance, IReadOnlyList<int[]> routes) =>
        TotalDistance(instance, routes) * instance.FuelPerKm + routes.Count(r => r.Length > 0) * instance.TruckCost;

    private sealed class Tracer(VrpInstance inst, VrpAlgorithm algorithm, bool record)
    {
        private readonly List<VrpStep> _steps = [];
        private List<List<int>> _routes = [];

        private static string C(int node) => node == 0 ? "the depot" : $"C{node}";

        private static string Km(double km) => $"{Num.F1(km)} km";

        private double Total() => TotalDistance(inst, _routes);

        private IReadOnlyList<int[]> Snapshot() => _routes.Where(r => r.Count > 0).Select(r => r.ToArray()).ToList();

        private IReadOnlyList<int> Pending() =>
            inst.Customers.Select(c => c.Id).Where(id => !_routes.Any(r => r.Contains(id))).ToList();

        public IReadOnlyList<int[]> FinalRoutes() => Snapshot();

        private void Add(VrpStep step)
        {
            if (record) _steps.Add(Snap(step));
        }

        private VrpStep Snap(VrpStep step) => step with
        {
            Routes = Snapshot(),
            Distance = Total(),
            Pending = Pending(),
        };

        private string Reason(IReadOnlyList<int> route)
        {
            var stats = Evaluate(inst, route);
            if (stats.Load > inst.Capacity) return $"the truck would carry {stats.Load} > {inst.Capacity}";
            if (stats.FirstLate is { } late)
            {
                var arrival = stats.Arrivals[route.ToList().IndexOf(late)];
                return $"{C(late)} would be reached at {VrpInstance.Clock(arrival)}, after its {VrpInstance.Clock(inst[late].Deadline)} deadline";
            }

            return "it breaks a constraint";
        }

        // ------------------------------------------------------------ Clarke–Wright savings

        public void Savings()
        {
            var n = inst.Customers.Count;
            _routes = inst.Customers.Select(c => new List<int> { c.Id }).ToList();
            Add(new VrpStep
            {
                Title = "One truck per customer",
                Text = $"The worst sensible plan: {n} trucks, each driving out to a single customer and straight back. That covers {Km(Total())}. Every merge from here on removes a trip back to the depot.",
                Line = 0,
                Phase = "Setup",
                Watches = [Watch.Of("customers", n), Watch.Of("capacity", inst.Capacity), Watch.Of("distance", Km(Total()))],
            });

            var pairs = new List<(int I, int J, double S)>();
            for (var i = 1; i <= n; i++)
            for (var j = i + 1; j <= n; j++)
                pairs.Add((i, j, inst.D(0, i) + inst.D(0, j) - inst.D(i, j)));
            pairs = pairs.OrderByDescending(p => p.S).ThenBy(p => p.I).ThenBy(p => p.J).ToList();
            var status = new SavingsStatus[pairs.Count];

            List<SavingsRow> Window(int current)
            {
                var from = Math.Max(0, Math.Min(current - 3, pairs.Count - 9));
                return pairs.Skip(from).Take(9)
                    .Select((p, k) => new SavingsRow(p.I, p.J, p.S, status[from + k], from + k == current))
                    .ToList();
            }

            var top = pairs[0];
            Add(new VrpStep
            {
                Title = $"Rank all {pairs.Count} pairs by saving",
                Text = $"Serving j right after i, instead of returning to the depot in between, saves s(i, j) = d(0,i) + d(0,j) − d(i,j). The best pair, =={C(top.I)} and {C(top.J)}==, saves {Km(top.S)}. The list is worked through from the top.",
                Line = 2,
                Phase = "Rank",
                Savings = Window(0),
                Focus = [top.I, top.J],
                Link = [top.I, top.J],
                Watches = [Watch.Of("pairs", pairs.Count), Watch.Of("best saving", Km(top.S))],
            });

            var skipped = 0;
            for (var k = 0; k < pairs.Count; k++)
            {
                var (i, j, s) = pairs[k];
                if (s <= 1e-9) break;
                var ri = _routes.First(r => r.Contains(i));
                var rj = _routes.First(r => r.Contains(j));
                if (ri == rj || !IsEnd(ri, i) || !IsEnd(rj, j))
                {
                    status[k] = SavingsStatus.Skipped;
                    skipped++;
                    continue;
                }

                var left = ri[^1] == i ? ri : Enumerable.Reverse(ri).ToList();
                var right = rj[0] == j ? rj : Enumerable.Reverse(rj).ToList();
                var joined = left.Concat(right).ToList();
                var reversed = Enumerable.Reverse(joined).ToList();
                var feasible = Feasible(inst, joined) ? joined : Feasible(inst, reversed) ? reversed : null;
                var skippedNote = skipped == 0 ? "" : $" (Skipped {skipped} pair{(skipped == 1 ? "" : "s")} above it: one customer was already inside a route, or both were on the same one.)";
                skipped = 0;

                if (feasible is null)
                {
                    status[k] = SavingsStatus.Rejected;
                    Add(new VrpStep
                    {
                        Title = $"Can't join {C(i)} and {C(j)}",
                        Text = $"Linking =={C(i)}–{C(j)}== would save {Km(s)}, but {Reason(joined)}. Skip it.{skippedNote}",
                        Line = 6,
                        Phase = "Merge",
                        Savings = Window(k),
                        Focus = [i, j],
                        Link = [i, j],
                        LinkOk = false,
                        Watches = [Watch.Of("pair", $"{C(i)}, {C(j)}"), Watch.Of("saving", Km(s)), Watch.Of("load", Evaluate(inst, joined).Load), Watch.Of("trucks", _routes.Count)],
                    });
                    continue;
                }

                var at = _routes.IndexOf(ri);
                _routes[at] = feasible;
                _routes.Remove(rj);
                status[k] = SavingsStatus.Merged;
                var load = Evaluate(inst, feasible).Load;
                Add(new VrpStep
                {
                    Title = $"Join {C(i)} and {C(j)}: −{Km(s)}",
                    Text = $"=={C(i)}== and =={C(j)}== both sit at the end of their routes, and together they carry {load} of {inst.Capacity}. Joining them saves {Km(s)}: {_routes.Count} trucks, {Km(Total())} in total.{skippedNote}",
                    Line = 7,
                    Phase = "Merge",
                    Savings = Window(k),
                    Focus = [i, j],
                    Link = [i, j],
                    LinkOk = true,
                    Active = _routes.IndexOf(feasible),
                    Watches = [Watch.Of("pair", $"{C(i)}, {C(j)}"), Watch.Of("saving", Km(s)), Watch.Of("load", $"{load}/{inst.Capacity}"), Watch.Of("trucks", _routes.Count)],
                });
            }
        }

        private static bool IsEnd(List<int> route, int c) => route[0] == c || route[^1] == c;

        // ------------------------------------------------------------ Sweep

        public void Sweep()
        {
            var angle = inst.Customers.ToDictionary(c => c.Id, c => Normalize(Math.Atan2(c.Y - inst.DepotY, c.X - inst.DepotX)));
            var sorted = angle.OrderBy(a => a.Value).ThenBy(a => a.Key).Select(a => a.Key).ToList();
            var gapAt = 0;
            var widest = -1.0;
            for (var k = 0; k < sorted.Count; k++)
            {
                var next = sorted[(k + 1) % sorted.Count];
                var gap = Normalize(angle[next] - angle[sorted[k]]);
                if (sorted.Count == 1) gap = Math.PI * 2;
                if (gap <= widest) continue;
                widest = gap;
                gapAt = (k + 1) % sorted.Count;
            }

            var order = sorted.Skip(gapAt).Concat(sorted.Take(gapAt)).ToList();
            var startAngle = angle[order[0]] - Math.Min(widest / 2, 0.35);
            _routes = [];
            Add(new VrpStep
            {
                Title = "Measure every customer's angle",
                Text = $"Seen from the depot, each customer sits in some direction. Sorting by that angle lets one ray sweep clockwise around the map, picking customers up in order. It starts in the widest empty sector ({Num.F1(widest * 180 / Math.PI)}°) so no natural group gets split, first reaching =={C(order[0])}==.",
                Line = 1,
                Phase = "Sort",
                Sweep = startAngle,
                Focus = [order[0]],
                Watches = [Watch.Of("customers", order.Count), Watch.Of("widest gap", $"{Num.F1(widest * 180 / Math.PI)}°")],
            });

            var route = new List<int>();
            _routes.Add(route);
            foreach (var c in order)
            {
                var degrees = $"{Num.F1(angle[c] * 180 / Math.PI)}°";
                var (pos, added) = CheapestInsertion(route, c);
                if (pos >= 0)
                {
                    route.Insert(pos, c);
                    var load = Evaluate(inst, route).Load;
                    var where = route.Count == 1
                        ? $"It opens truck {_routes.Count}."
                        : $"It goes {Between(route, pos)}, adding {Km(added)}.";
                    Add(new VrpStep
                    {
                        Title = $"Ray reaches {C(c)}",
                        Text = $"=={C(c)}== (demand {inst[c].Demand}) fits in truck {_routes.Count}: load {load} of {inst.Capacity}. {where}",
                        Line = 4,
                        Phase = "Sweep",
                        Sweep = angle[c],
                        Focus = [c],
                        Active = _routes.Count - 1,
                        Watches = [Watch.Of("angle", degrees), Watch.Of("truck", _routes.Count), Watch.Of("load", $"{load}/{inst.Capacity}"), Watch.Of("added", Km(added))],
                    });
                    continue;
                }

                var why = Reason(route.Append(c).ToList());
                route = [c];
                _routes.Add(route);
                Add(new VrpStep
                {
                    Title = $"Truck {_routes.Count - 1} is full",
                    Text = $"=={C(c)}== doesn't fit in truck {_routes.Count - 1}: {why}. That truck is closed and truck {_routes.Count} starts with {C(c)}.",
                    Line = 5,
                    Phase = "Sweep",
                    Sweep = angle[c],
                    Focus = [c],
                    Active = _routes.Count - 1,
                    Watches = [Watch.Of("angle", degrees), Watch.Of("truck", _routes.Count), Watch.Of("load", $"{inst[c].Demand}/{inst.Capacity}")],
                });
            }
        }

        private (int Position, double Added) CheapestInsertion(List<int> route, int c)
        {
            var best = (Position: -1, Added: double.MaxValue);
            for (var pos = 0; pos <= route.Count; pos++)
            {
                var before = pos == 0 ? 0 : route[pos - 1];
                var after = pos == route.Count ? 0 : route[pos];
                var added = inst.D(before, c) + inst.D(c, after) - inst.D(before, after);
                if (added >= best.Added) continue;
                var candidate = new List<int>(route);
                candidate.Insert(pos, c);
                if (Feasible(inst, candidate)) best = (pos, added);
            }

            return best;
        }

        private static string Between(List<int> route, int pos)
        {
            var before = pos == 0 ? 0 : route[pos - 1];
            var after = pos == route.Count - 1 ? 0 : route[pos + 1];
            return $"between {C(before)} and {C(after)}";
        }

        private static double Normalize(double a)
        {
            var twoPi = Math.PI * 2;
            a %= twoPi;
            return a < 0 ? a + twoPi : a;
        }

        // ------------------------------------------------------------ Nearest neighbor

        public void Nearest()
        {
            _routes = [];
            var unserved = inst.Customers.Select(c => c.Id).ToHashSet();
            while (unserved.Count > 0)
            {
                var route = new List<int>();
                _routes.Add(route);
                var truck = _routes.Count;
                int at = 0, load = 0;
                double clock = 0;
                while (true)
                {
                    var rejected = new List<string>();
                    int? chosen = null;
                    foreach (var c in unserved.OrderBy(c => inst.D(at, c)).ThenBy(c => c))
                    {
                        var arrival = clock + inst.D(at, c) / inst.Speed;
                        if (load + inst[c].Demand > inst.Capacity) rejected.Add($"{C(c)} needs {inst[c].Demand} but only {inst.Capacity - load} is left");
                        else if (inst.UseDeadlines && arrival > inst[c].Deadline) rejected.Add($"{C(c)} would be reached at {VrpInstance.Clock(arrival)}, after its {VrpInstance.Clock(inst[c].Deadline)} deadline");
                        else
                        {
                            chosen = c;
                            break;
                        }
                    }

                    // A fresh truck can always reach any single customer; this only guards bad input.
                    if (chosen is null && route.Count == 0) chosen = unserved.OrderBy(c => inst.D(0, c)).First();

                    if (chosen is not { } next)
                    {
                        Add(new VrpStep
                        {
                            Title = $"Truck {truck} heads home",
                            Text = $"No unserved customer fits in truck {truck} any more ({Num.List(rejected.Take(2))}{(rejected.Count > 2 ? ", and so on" : "")}). It returns to the depot carrying {load} of {inst.Capacity}.",
                            Line = 4,
                            Phase = $"Truck {truck}",
                            Link = [at, 0],
                            LinkOk = true,
                            Active = truck - 1,
                            Watches = [Watch.Of("truck", truck), Watch.Of("load", $"{load}/{inst.Capacity}"), Watch.Of("left", unserved.Count)],
                        });
                        break;
                    }

                    var leg = inst.D(at, next);
                    var closer = rejected.Count == 0 ? "" : $" Closer options don't work: {Num.List(rejected.Take(2))}.";
                    var from = at;
                    clock += leg / inst.Speed;
                    var arriveAt = clock;
                    clock += inst.Service;
                    load += inst[next].Demand;
                    route.Add(next);
                    unserved.Remove(next);
                    at = next;
                    var leaves = route.Count == 1 ? $"Truck {truck} leaves the depot. " : "";
                    var time = inst.UseDeadlines ? $" Arrives {VrpInstance.Clock(arriveAt)}, deadline {VrpInstance.Clock(inst[next].Deadline)}." : "";
                    Add(new VrpStep
                    {
                        Title = $"Truck {truck} drives to {C(next)}",
                        Text = $"{leaves}The nearest customer that still fits is =={C(next)}==, {Km(leg)} away.{closer} Load is now {load} of {inst.Capacity}.{time}",
                        Line = 5,
                        Phase = $"Truck {truck}",
                        Link = [from, next],
                        LinkOk = true,
                        Focus = [next],
                        Active = truck - 1,
                        Watches = [Watch.Of("truck", truck), Watch.Of("at", C(next)), Watch.Of("leg", Km(leg)), Watch.Of("load", $"{load}/{inst.Capacity}"), Watch.Of("clock", VrpInstance.Clock(arriveAt))],
                    });
                }
            }
        }

        // ------------------------------------------------------------ 2-opt

        public void Polish(int firstLine)
        {
            var before = Total();
            Add(new VrpStep
            {
                Title = "Polish each route with 2-opt",
                Text = $"Construction heuristics often leave routes that cross themselves. 2-opt removes two edges of a route and reconnects it the other way round, reversing the stops in between. It repeats while that makes the route shorter. Current total: {Km(before)}.",
                Line = firstLine,
                Phase = "Polish",
                Watches = [Watch.Of("routes", _routes.Count), Watch.Of("distance", Km(before))],
            });

            for (var r = 0; r < _routes.Count; r++)
            {
                var improved = true;
                while (improved)
                {
                    improved = false;
                    var full = new List<int> { 0 };
                    full.AddRange(_routes[r]);
                    full.Add(0);
                    for (var i = 0; i + 3 < full.Count && !improved; i++)
                    for (var j = i + 2; j + 1 < full.Count && !improved; j++)
                    {
                        int a = full[i], b = full[i + 1], c = full[j], d = full[j + 1];
                        var delta = inst.D(a, c) + inst.D(b, d) - inst.D(a, b) - inst.D(c, d);
                        if (delta > -1e-6) continue;
                        var candidate = full.Take(i + 1).Concat(full.Skip(i + 1).Take(j - i).Reverse()).Concat(full.Skip(j + 1)).ToList();
                        candidate = candidate.Skip(1).Take(candidate.Count - 2).ToList();
                        if (!Feasible(inst, candidate)) continue;
                        _routes[r] = candidate;
                        improved = true;
                        Add(new VrpStep
                        {
                            Title = $"Truck {r + 1}: uncross {C(b)} and {C(c)}",
                            Text = $"Swap edges =={C(a)}–{C(b)}== and =={C(c)}–{C(d)}== for {C(a)}–{C(c)} and {C(b)}–{C(d)}, reversing the stops in between. Truck {r + 1} saves {Km(-delta)}.",
                            Line = firstLine + 3,
                            Phase = "Polish",
                            Removed = [[a, b], [c, d]],
                            Added = [[a, c], [b, d]],
                            Focus = new[] { a, b, c, d }.Where(x => x != 0).Distinct().ToList(),
                            Active = r,
                            Watches = [Watch.Of("truck", r + 1), Watch.Of("saved", Km(-delta)), Watch.Of("distance", Km(Total()))],
                        });
                    }
                }
            }
        }

        // ------------------------------------------------------------ Wrap-up

        public void Finish(int codeLines)
        {
            var routes = Snapshot();
            var distance = Total();
            var fuel = distance * inst.FuelPerKm;
            var trucks = routes.Count;
            var fixedCost = trucks * inst.TruckCost;
            var loads = routes.Select(r => Evaluate(inst, r).Load).ToList();
            var fill = (double)loads.Sum() / (trucks * inst.Capacity);
            var timing = inst.UseDeadlines ? " Every delivery arrives before its deadline." : "";
            var name = algorithm switch
            {
                VrpAlgorithm.Sweep => "The sweep",
                VrpAlgorithm.NearestNeighbor => "Nearest neighbor",
                _ => "The savings algorithm",
            };
            Add(new VrpStep
            {
                Title = $"{trucks} trucks, {Km(distance)}",
                Text = $"{name} serves all {inst.Customers.Count} customers with **{trucks} trucks** driving **{Km(distance)}** in total, with trucks {Num.Pct(fill)} full on average.{timing} At ${Num.F2(inst.FuelPerKm)} per km for fuel plus ${inst.TruckCost:0} per truck, the day costs **${fuel + fixedCost:0}**.",
                Line = codeLines - 1,
                Phase = "Done",
                Final = true,
                Watches = [Watch.Of("trucks", trucks), Watch.Of("distance", Km(distance)), Watch.Of("fuel", $"${fuel:0}"), Watch.Of("total cost", $"${fuel + fixedCost:0}")],
            });
        }

        /// <summary>
        /// Gives every route the color of the final truck its customers end up in, so colors follow the
        /// truck through merges. During savings, single-stop routes stay neutral until the end.
        /// </summary>
        public IReadOnlyList<VrpStep> Colorize()
        {
            var final = _steps[^1].Routes;
            var order = Enumerable.Range(0, final.Count)
                .OrderBy(r => algorithm == VrpAlgorithm.Savings ? Bearing(final[r]) : r)
                .ToList();
            var colorOf = new Dictionary<int, int>();
            for (var slot = 0; slot < order.Count; slot++)
                foreach (var c in final[order[slot]])
                    colorOf[c] = slot;

            var building = algorithm == VrpAlgorithm.Savings;
            return _steps.Select(step => step with
            {
                Colors = step.Routes
                    .Select(r => building && r.Length < 2 && step.Phase is "Setup" or "Rank" or "Merge" ? -1 : colorOf[r[0]])
                    .ToList(),
            }).ToList();
        }

        private double Bearing(int[] route)
        {
            var x = route.Average(c => inst[c].X) - inst.DepotX;
            var y = route.Average(c => inst[c].Y) - inst.DepotY;
            return Normalize(Math.Atan2(y, x));
        }
    }
}
