namespace Halgoworks.Core.Knapsack;

/// <summary>
/// Exact knapsack by depth-first branch and bound. Each node fixes the next item (densest first) as taken or
/// skipped; a node is cut when it's too heavy or when its optimistic bound can't beat the best packing found.
/// </summary>
public static class KnapsackBranchAndBound
{
    public static readonly AlgorithmInfo Info = new(
        "bnb",
        "Branch and bound",
        AlgorithmKind.Exact,
        "Explores the take-or-skip decision tree, but cuts every branch whose best possible outcome can't beat the best packing found so far.",
        "O(2ⁿ) worst case",
        "O(n) per path",
        [
            "sort items by value ÷ weight, highest first",
            "best ← 0",
            "explore(node):",
            "  if load > W: cut it (too heavy)",
            "  bound ← value + greedy fill of the rest, one fraction allowed",
            "  if bound ≤ best: cut it (can't win)",
            "  if every item is decided: best ← value",
            "  else: explore(take next item), then explore(skip it)",
            "explore(root);  return best",
        ]);

    /// <summary>The same search without recording steps, for the Scale view. Gives up after <paramref name="nodeLimit"/> nodes.</summary>
    public static (KnapsackResult Best, long Nodes, bool Complete) Solve(KnapsackInstance instance, long nodeLimit = 5_000_000)
    {
        var items = instance.Items;
        var n = items.Count;
        var order = Enumerable.Range(0, n).OrderByDescending(i => items[i].Density).ThenBy(i => i).ToArray();
        var w = order.Select(i => items[i].Weight).ToArray();
        var v = order.Select(i => items[i].Value).ToArray();
        var taken = new bool[n];
        var bestTaken = new bool[n];
        var best = -1;
        long nodes = 0;
        var complete = true;

        double Bound(int level, int room, int value)
        {
            double bound = value;
            for (var k = level; k < n; k++)
            {
                if (w[k] > room) return bound + (double)v[k] * room / w[k];
                room -= w[k];
                bound += v[k];
            }

            return bound;
        }

        void Explore(int level, int room, int value)
        {
            if (++nodes > nodeLimit)
            {
                complete = false;
                return;
            }

            if (level == n)
            {
                if (value <= best) return;
                best = value;
                Array.Copy(taken, bestTaken, n);
                return;
            }

            if (Bound(level, room, value) <= best + 1e-9) return;
            if (w[level] <= room)
            {
                taken[level] = true;
                Explore(level + 1, room - w[level], value + v[level]);
                taken[level] = false;
            }

            if (complete) Explore(level + 1, room, value);
        }

        Explore(0, instance.Capacity, 0);
        var chosen = Enumerable.Range(0, n).Where(k => bestTaken[k]).Select(k => order[k]).OrderBy(i => i).ToList();
        return (new KnapsackResult(Math.Max(0, best), chosen.Sum(i => items[i].Weight), chosen), Math.Min(nodes, nodeLimit), complete);
    }

    public static IReadOnlyList<KnapsackStep> Trace(KnapsackInstance instance)
    {
        var items = instance.Items;
        var n = items.Count;
        var cap = instance.Capacity;
        var order = Enumerable.Range(0, n).OrderByDescending(i => items[i].Density).ThenBy(i => i).ToArray();
        var nodes = new List<SearchNode>();
        var steps = new List<KnapsackStep>();
        var decided = new bool?[n];
        var best = 0;
        int? bestNode = null;
        var cuts = 0;

        string Name(int k) => items[order[k]].Name.ToLowerInvariant();
        string B(double v) => Num.F1(v);

        double Bound(int level, int weight, int value)
        {
            var room = cap - weight;
            double bound = value;
            for (var k = level; k < n; k++)
            {
                var item = items[order[k]];
                if (item.Weight <= room)
                {
                    room -= item.Weight;
                    bound += item.Value;
                    continue;
                }

                bound += (double)item.Value * room / item.Weight;
                break;
            }

            return bound;
        }

        ItemState[] States(int level)
        {
            var states = new ItemState[n];
            for (var k = 0; k < n; k++)
                states[order[k]] = decided[k] switch { true => ItemState.Taken, false => ItemState.Skipped, _ => ItemState.Idle };
            if (level < n) states[order[level]] = ItemState.Focus;
            return states;
        }

        void Add(string title, string text, int line, int focus, int level, int weight, int value, double bound) => steps.Add(new KnapsackStep
        {
            Title = title,
            Text = text,
            Line = line,
            Phase = "Search",
            Tree = nodes.ToList(),
            FocusNode = focus,
            BestNode = bestNode,
            Items = States(level),
            Order = order,
            Load = weight,
            Total = value,
            Watches =
            [
                Watch.Of("node", $"#{focus + 1}"), Watch.Of("decided", $"{level}/{n}"), Watch.Of("load", $"{weight}/{cap}"),
                Watch.Of("value", value), Watch.Of("bound", B(bound)), Watch.Of("best", best),
            ],
        });

        void Explore(int parent, int level, int weight, int value, string branch)
        {
            var id = nodes.Count;
            if (weight > cap)
            {
                cuts++;
                nodes.Add(new SearchNode(id, parent, level, branch, double.NaN, value, NodeStatus.PrunedInfeasible, $"load {weight} > {cap}"));
                Add($"Too heavy: cut node #{id + 1}",
                    $"Taking ==the {Name(level - 1)}== brings the load to {weight}, over the capacity of {cap}. Nothing below this node can be feasible, so the whole branch is cut.",
                    3, id, level, weight, value, double.NaN);
                return;
            }

            var bound = Bound(level, weight, value);
            if (bound <= best + 1e-9)
            {
                cuts++;
                nodes.Add(new SearchNode(id, parent, level, branch, bound, value, NodeStatus.PrunedBound, $"bound {B(bound)} ≤ best {best}"));
                var leaves = level < 31 ? (1L << (n - level)).ToString(System.Globalization.CultureInfo.InvariantCulture) : "many";
                Add($"Can't win: cut node #{id + 1}",
                    $"Even in the best case, this branch reaches **{B(bound)}**, no more than the best packing already found ({best}). It's cut without looking further, skipping up to {leaves} complete packings below it.",
                    5, id, level, weight, value, bound);
                return;
            }

            if (level == n)
            {
                var previous = best;
                best = value;
                bestNode = id;
                nodes.Add(new SearchNode(id, parent, level, branch, bound, value, NodeStatus.Solution, $"value {value}, load {weight}"));
                Add($"New best: {value}",
                    $"Every item is decided: value **{value}** with a load of {weight} of {cap}. That beats the previous best of {previous}, and from now on any branch whose bound can't exceed {value} is cut.",
                    6, id, level, weight, value, bound);
                return;
            }

            nodes.Add(new SearchNode(id, parent, level, branch, bound, value, NodeStatus.Branched, $"load {weight}, value {value}, bound {B(bound)}"));
            var intro = id == 0
                ? $"The root has decided nothing yet. Its **bound**, {B(bound)}, assumes the rest could be packed greedily, even taking a fraction of the first item that doesn't fit. No real packing can do better, so it's an upper bound. "
                : $"Load {weight} of {cap}, value {value}. Its bound, **{B(bound)}**, still beats the best so far ({best}), so this branch might hold something better. ";
            Add(id == 0 ? "Start at the root" : $"Explore node #{id + 1}",
                intro + $"Next decision: ==the {Name(level)}==. Try taking it first, then skipping it.",
                id == 0 ? 4 : 7, id, level, weight, value, bound);

            var item = items[order[level]];
            decided[level] = true;
            Explore(id, level + 1, weight + item.Weight, value + item.Value, $"take {item.Name}");
            decided[level] = false;
            Explore(id, level + 1, weight, value, $"skip {item.Name}");
            decided[level] = null;
        }

        steps.Add(new KnapsackStep
        {
            Title = "Sort by value per kg",
            Text = "Branch and bound decides items one at a time, densest first, so good packings show up early and their value can be used to cut weaker branches. Each node of the tree fixes one more item as taken or skipped.",
            Line = 0,
            Phase = "Setup",
            Items = new ItemState[n],
            Order = order,
            Watches = [Watch.Of("n", n), Watch.Of("W", cap), Watch.Of("full tree", $"{(1L << (n + 1)) - 1} nodes")],
        });

        Explore(-1, 0, 0, 0, "start");

        var full = (1L << (n + 1)) - 1;
        var bestPath = new List<int>();
        for (var id = bestNode ?? -1; id >= 0; id = nodes[id].Parent) bestPath.Add(id);
        var finalStates = new ItemState[n];
        foreach (var id in bestPath)
        {
            var node = nodes[id];
            if (node.Depth == 0) continue;
            finalStates[order[node.Depth - 1]] = node.Branch.StartsWith("take ", StringComparison.Ordinal) ? ItemState.Taken : ItemState.Skipped;
        }

        var load = Enumerable.Range(0, n).Where(i => finalStates[i] == ItemState.Taken).Sum(i => items[i].Weight);
        steps.Add(new KnapsackStep
        {
            Title = $"Proven optimal: {best}",
            Text = $"Every branch is either explored or cut, so **{best}** is the best possible value: no need to trust a heuristic. The search visited {nodes.Count} nodes and cut {cuts} branches, out of {full} nodes in the complete decision tree.",
            Line = 8,
            Phase = "Done",
            Tree = nodes.ToList(),
            BestNode = bestNode,
            Items = finalStates,
            Order = order,
            Load = load,
            Total = best,
            Optimum = best,
            Watches = [Watch.Of("best", best), Watch.Of("nodes", nodes.Count), Watch.Of("cuts", cuts), Watch.Of("full tree", full.ToString(System.Globalization.CultureInfo.InvariantCulture))],
        });

        return steps;
    }
}
