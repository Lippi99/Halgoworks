using Halgoworks.Core.BinPacking;
using Halgoworks.Core.Knapsack;

namespace Halgoworks.Core.Ip;

/// <summary>A 0-1 integer program: every variable is either 0 or 1.</summary>
public sealed record IpModel(
    string Name,
    IReadOnlyList<string> Variables,
    double[] Objective,
    bool Maximize,
    IReadOnlyList<LinearConstraint> Constraints,
    bool IntegralObjective);

public sealed record IpStep : Step
{
    public IReadOnlyList<SearchNode> Tree { get; init; } = [];
    public int? FocusNode { get; init; }
    public int? BestNode { get; init; }

    /// <summary>The LP solution at the node in focus (fixed variables included), or null when it's infeasible.</summary>
    public IReadOnlyList<double>? X { get; init; }

    /// <summary>Variables fixed by branching at the node in focus: 0, 1, or null when free.</summary>
    public IReadOnlyList<int?> Fixed { get; init; } = [];

    public double? Bound { get; init; }
    public double? Incumbent { get; init; }
    public IReadOnlyList<double>? IncumbentX { get; init; }
    public int Lps { get; init; }
}

public static class IntegerProgram
{
    public const int NodeLimit = 400;

    public static readonly AlgorithmInfo Info = new(
        "ip", "Integer program", AlgorithmKind.Exact,
        "Writes the problem as a 0-1 integer program, solves its linear relaxation with the simplex method, and branches on fractional variables until the answer is whole and proven.",
        "Exponential worst case; each node is one LP", "O(rows × columns) per LP",
        [
            "relax: let each x be any number between 0 and 1 (a linear program)",
            "best ← none",
            "solve(node):",
            "  solve the LP with this node's fixed variables (simplex)",
            "  if the LP is infeasible: cut it",
            "  if its value can't beat best: cut it",
            "  if every x is 0 or 1: best ← this solution",
            "  else: x ← most fractional variable; solve(x = 1), then solve(x = 0)",
            "solve(root);  return best",
        ]);

    // ------------------------------------------------------------ Models

    public static IpModel Knapsack(KnapsackInstance k)
    {
        var n = k.Items.Count;
        return new IpModel(
            k.Name,
            Enumerable.Range(1, n).Select(i => $"x{Sub(i)}").ToList(),
            k.Items.Select(i => (double)i.Value).ToArray(),
            true,
            [new LinearConstraint(k.Items.Select(i => (double)i.Weight).ToArray(), Sense.LessEqual, k.Capacity)],
            true);
    }

    /// <summary>
    /// Assignment model: x(i,j) = item i in bin j, y(j) = bin j used. Items are taken largest first and item i may only
    /// use bins 1..i, and bins are used in order: both remove symmetric copies of the same packing.
    /// </summary>
    public static (IpModel Model, int[] Items, int Bins, int[,] VarOf) BinPacking(BinPackingInstance b)
    {
        var items = b.Items.OrderByDescending(i => i.Size).ThenBy(i => i.Id).Select(i => i.Id).ToArray();
        var size = b.Items.ToDictionary(i => i.Id, i => i.Size);
        var n = items.Length;
        var bins = BinPackingSolver.Solve(b, BinRule.FirstFitDecreasing).Count;
        var names = new List<string>();
        var varOf = new int[n, bins];
        for (var i = 0; i < n; i++)
        for (var j = 0; j < bins; j++)
        {
            varOf[i, j] = -1;
            if (j > i) continue;
            varOf[i, j] = names.Count;
            names.Add($"x{Sub(i + 1)},{Sub(j + 1)}");
        }

        var yStart = names.Count;
        for (var j = 0; j < bins; j++) names.Add($"y{Sub(j + 1)}");
        var count = names.Count;
        var objective = new double[count];
        for (var j = 0; j < bins; j++) objective[yStart + j] = 1;

        var rows = new List<LinearConstraint>();
        for (var i = 0; i < n; i++)
        {
            var a = new double[count];
            for (var j = 0; j < bins; j++)
                if (varOf[i, j] >= 0) a[varOf[i, j]] = 1;
            rows.Add(new LinearConstraint(a, Sense.Equal, 1));
        }

        for (var j = 0; j < bins; j++)
        {
            var a = new double[count];
            for (var i = 0; i < n; i++)
                if (varOf[i, j] >= 0) a[varOf[i, j]] = size[items[i]];
            a[yStart + j] = -b.Capacity;
            rows.Add(new LinearConstraint(a, Sense.LessEqual, 0));
        }

        for (var j = 0; j + 1 < bins; j++)
        {
            var a = new double[count];
            a[yStart + j + 1] = 1;
            a[yStart + j] = -1;
            rows.Add(new LinearConstraint(a, Sense.LessEqual, 0));
        }

        return (new IpModel(b.Name, names, objective, false, rows, true), items, bins, varOf);
    }

    public static string Sub(int value) => string.Concat(value.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => "₀₁₂₃₄₅₆₇₈₉"[c - '0']));

    // ------------------------------------------------------------ Branch and bound

    public static IReadOnlyList<IpStep> Trace(IpModel model, Func<int, string>? describe = null)
    {
        var n = model.Variables.Count;
        var sign = model.Maximize ? 1.0 : -1.0;
        var nodes = new List<SearchNode>();
        var steps = new List<IpStep>();
        var fixedVars = new int?[n];
        double? best = null;
        double[]? bestX = null;
        int? bestNode = null;
        var lps = 0;
        var stopped = false;
        string Var(int j) => describe?.Invoke(j) ?? model.Variables[j];
        string V(double v) => Num.F2(v).TrimEnd('0').TrimEnd('.');
        string Goal(double v) => model.IntegralObjective ? V(Math.Round(v, 6)) : V(v);

        // Internally everything is a maximization; `sign` converts back for display.
        bool CanBeat(double internalBound)
        {
            if (best is null) return true;
            var b = model.IntegralObjective ? Math.Floor(internalBound + 1e-6) : internalBound;
            return b > sign * best.Value + 1e-6;
        }

        void Add(IpStep step) => steps.Add(step with
        {
            Tree = nodes.ToList(),
            BestNode = bestNode,
            Incumbent = best,
            IncumbentX = bestX,
            Fixed = fixedVars.ToArray(),
            Lps = lps,
        });

        steps.Add(new IpStep
        {
            Title = "Write it as an integer program",
            Text = $"Each yes-or-no decision becomes a variable that must be 0 or 1. The goal and the limits are linear in those variables: {model.Variables.Count} variables and {model.Constraints.Count} constraint{(model.Constraints.Count == 1 ? "" : "s")}. Integer programming solvers start by **relaxing** the 0-or-1 rule: each variable may take any value in between, which turns the problem into a linear program that the simplex method solves fast.",
            Line = 0,
            Phase = "Model",
            Fixed = new int?[n],
            Watches = [Watch.Of("variables", n), Watch.Of("constraints", model.Constraints.Count), Watch.Of("goal", model.Maximize ? "maximize" : "minimize")],
        });

        void Solve(int parent, int depth, string branch)
        {
            if (stopped) return;
            if (nodes.Count >= NodeLimit)
            {
                stopped = true;
                return;
            }

            var id = nodes.Count;
            var lp = SolveRelaxation(model, fixedVars, sign);
            lps++;
            if (lp is null)
            {
                nodes.Add(new SearchNode(id, parent, depth, branch, double.NaN, double.NaN, NodeStatus.PrunedInfeasible, "the relaxation has no solution"));
                Add(new IpStep
                {
                    Title = $"Infeasible: cut node #{id + 1}",
                    Text = $"With {branch}, not even the relaxed problem has a solution: the fixed choices already break a constraint. Nothing below this node can work, so it's cut.",
                    Line = 4,
                    Phase = "Search",
                    FocusNode = id,
                    Watches = [Watch.Of("node", $"#{id + 1}"), Watch.Of("fixed", fixedVars.Count(f => f is not null)), Watch.Of("LPs solved", lps)],
                });
                return;
            }

            var (x, internalValue) = lp.Value;
            var bound = sign * internalValue;
            var fractional = Enumerable.Range(0, n).Where(j => fixedVars[j] is null && Math.Abs(x[j] - Math.Round(x[j])) > 1e-6).ToList();
            var values = x;

            if (!CanBeat(internalValue))
            {
                nodes.Add(new SearchNode(id, parent, depth, branch, bound, bound, NodeStatus.PrunedBound, $"LP {V(bound)}, best {Goal(best!.Value)}"));
                var rounding = model.IntegralObjective && Math.Abs(bound - Math.Round(bound)) > 1e-6
                    ? $" Since the answer must be a whole number, that really means {(model.Maximize ? "at most" : "at least")} {Goal(model.Maximize ? Math.Floor(bound + 1e-6) : Math.Ceiling(bound - 1e-6))}."
                    : "";
                Add(new IpStep
                {
                    Title = $"Can't win: cut node #{id + 1}",
                    Text = $"The relaxation here is worth **{V(bound)}**.{rounding} The best whole solution so far is {Goal(best.Value)}, and adding the 0-or-1 rule back can only make things worse, so this branch is cut.",
                    Line = 5,
                    Phase = "Search",
                    FocusNode = id,
                    X = values,
                    Bound = bound,
                    Watches = [Watch.Of("node", $"#{id + 1}"), Watch.Of("LP value", V(bound)), Watch.Of("best", Goal(best.Value)), Watch.Of("LPs solved", lps)],
                });
                return;
            }

            if (fractional.Count == 0)
            {
                var previous = best;
                best = bound;
                bestX = values.Select(v => Math.Round(v)).ToArray();
                bestNode = id;
                nodes.Add(new SearchNode(id, parent, depth, branch, bound, bound, NodeStatus.Solution, $"whole solution worth {Goal(bound)}"));
                Add(new IpStep
                {
                    Title = $"Whole solution: {Goal(bound)}",
                    Text = $"Every variable in the relaxation's answer is already 0 or 1, so it's a real solution worth **{Goal(bound)}**{(previous is null ? ", the first one found" : $", better than {Goal(previous.Value)}")}. From now on any branch whose relaxation can't beat it is cut.",
                    Line = 6,
                    Phase = "Search",
                    FocusNode = id,
                    X = values,
                    Bound = bound,
                    Watches = [Watch.Of("node", $"#{id + 1}"), Watch.Of("value", Goal(bound)), Watch.Of("LPs solved", lps)],
                });
                return;
            }

            var pick = fractional.OrderBy(j => Math.Abs(x[j] - 0.5)).ThenBy(j => j).First();
            nodes.Add(new SearchNode(id, parent, depth, branch, bound, bound, NodeStatus.Branched, $"LP {V(bound)}, branch on {model.Variables[pick]}"));
            var intro = id == 0
                ? $"The simplex method solves the relaxation: value **{V(bound)}**. Because the relaxation allows more than the real problem, no 0/1 solution can {(model.Maximize ? "beat" : "go below")} it. "
                : $"The relaxation here is worth **{V(bound)}**{(best is null ? "" : $", still better than the best so far ({Goal(best.Value)})")}. ";
            Add(new IpStep
            {
                Title = id == 0 ? $"Root relaxation: {V(bound)}" : $"Branch at node #{id + 1}",
                Text = intro + $"But {fractional.Count} variable{(fractional.Count == 1 ? " is" : "s are")} fractional. The most undecided is =={Var(pick)} = {V(x[pick])}==, so split on it: one branch forces it to 1, the other to 0.",
                Line = id == 0 ? 3 : 7,
                Phase = "Search",
                FocusNode = id,
                X = values,
                Bound = bound,
                Watches = [Watch.Of("node", $"#{id + 1}"), Watch.Of("LP value", V(bound)), Watch.Of("fractional", fractional.Count), Watch.Of("branch on", model.Variables[pick]), Watch.Of("LPs solved", lps)],
            });

            fixedVars[pick] = 1;
            Solve(id, depth + 1, $"{model.Variables[pick]} = 1");
            fixedVars[pick] = 0;
            Solve(id, depth + 1, $"{model.Variables[pick]} = 0");
            fixedVars[pick] = null;
        }

        Solve(-1, 0, "root");

        var verdict = stopped
            ? $"The search hit its limit of {NodeLimit} nodes, so the best solution found ({(best is null ? "none" : Goal(best.Value))}) isn't proven optimal. Real solvers add cutting planes and smarter branching to finish."
            : best is null
                ? "No 0/1 solution exists."
                : $"Every branch is explored or cut, so **{Goal(best.Value)}** is proven optimal. It took {lps} linear programs and {nodes.Count} nodes. Commercial solvers such as Gurobi, CPLEX and HiGHS start exactly like this, then add presolve, cutting planes and heuristics to go much faster.";
        steps.Add(new IpStep
        {
            Title = best is null ? "No solution" : stopped ? $"Best found: {Goal(best.Value)}" : $"Proven optimal: {Goal(best.Value)}",
            Text = verdict,
            Line = 8,
            Phase = "Done",
            Tree = nodes.ToList(),
            BestNode = bestNode,
            Incumbent = best,
            IncumbentX = bestX,
            X = bestX,
            Bound = best,
            Fixed = new int?[n],
            Lps = lps,
            Watches = [Watch.Of("best", best is null ? "none" : Goal(best.Value)), Watch.Of("LPs solved", lps), Watch.Of("nodes", nodes.Count)],
        });
        return steps;
    }

    /// <summary>The LP relaxation with some variables fixed, as (full solution, objective in maximize form), or null if infeasible.</summary>
    public static (double[] X, double Value)? SolveRelaxation(IpModel model, IReadOnlyList<int?> fixedVars, double sign)
    {
        var n = model.Variables.Count;
        var free = Enumerable.Range(0, n).Where(j => fixedVars[j] is null).ToArray();
        var constant = 0.0;
        for (var j = 0; j < n; j++)
            if (fixedVars[j] is { } v) constant += sign * model.Objective[j] * v;

        var rows = new List<LinearConstraint>();
        foreach (var r in model.Constraints)
        {
            var rhs = r.Rhs;
            for (var j = 0; j < n; j++)
                if (fixedVars[j] is { } v) rhs -= r.Coefficients[j] * v;
            var a = free.Select(j => r.Coefficients[j]).ToArray();
            if (a.All(v => Math.Abs(v) < 1e-12))
            {
                var ok = r.Sense switch { Sense.LessEqual => rhs >= -1e-9, Sense.GreaterEqual => rhs <= 1e-9, _ => Math.Abs(rhs) <= 1e-9 };
                if (!ok) return null;
                continue;
            }

            rows.Add(new LinearConstraint(a, r.Sense, rhs));
        }

        for (var k = 0; k < free.Length; k++)
        {
            var a = new double[free.Length];
            a[k] = 1;
            rows.Add(new LinearConstraint(a, Sense.LessEqual, 1));
        }

        var x = new double[n];
        for (var j = 0; j < n; j++) x[j] = fixedVars[j] ?? 0;
        if (free.Length == 0) return (x, constant);

        var lp = Simplex.Maximize(free.Select(j => sign * model.Objective[j]).ToArray(), rows);
        if (lp.Status != LpStatus.Optimal) return null;
        for (var k = 0; k < free.Length; k++) x[free[k]] = lp.X[k];
        return (x, constant + lp.Objective);
    }
}
