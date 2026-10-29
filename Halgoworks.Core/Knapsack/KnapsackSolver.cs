namespace Halgoworks.Core.Knapsack;

public enum GreedyOrder
{
    Density,
    Value,
    Lightest,
}

public static class KnapsackSolver
{
    public static readonly AlgorithmInfo DynamicProgramming = new(
        "dp",
        "Dynamic programming",
        AlgorithmKind.Exact,
        "Builds a table of the best value for every item prefix and every capacity, then reads the answer back out of it.",
        "O(n·W)",
        "O(n·W)",
        [
            "for w ← 0 to W: dp[0][w] ← 0",
            "for i ← 1 to n:",
            "  for w ← 0 to W:",
            "    if weight[i] > w:",
            "      dp[i][w] ← dp[i−1][w]",
            "    else:",
            "      skip ← dp[i−1][w]",
            "      take ← value[i] + dp[i−1][w − weight[i]]",
            "      dp[i][w] ← max(skip, take)",
            "w ← W",
            "for i ← n down to 1:",
            "  if dp[i][w] ≠ dp[i−1][w]:",
            "    pack item i;  w ← w − weight[i]",
            "return dp[n][W]",
        ]);

    public static readonly AlgorithmInfo Greedy = new(
        "greedy",
        "Greedy by value density",
        AlgorithmKind.Heuristic,
        "Packs items in order of value per unit of weight, taking each one that still fits. Fast, but with no guarantee.",
        "O(n log n)",
        "O(n)",
        [
            "for each item: density ← value ÷ weight",
            "sort items by density, highest first",
            "load ← 0",
            "for each item in that order:",
            "  if load + weight ≤ W:",
            "    pack it;  load ← load + weight",
            "  else: leave it out",
            "return the packed items",
        ]);

    public static IReadOnlyList<AlgorithmInfo> All { get; } = [DynamicProgramming, Greedy];

    public static IReadOnlyList<KnapsackStep> Trace(KnapsackInstance instance, string algorithmId) => algorithmId switch
    {
        "greedy" => TraceGreedy(instance),
        _ => TraceDp(instance),
    };

    public static IReadOnlyList<KnapsackStep> TraceDp(KnapsackInstance instance)
    {
        var items = instance.Items;
        var n = items.Count;
        var cap = instance.Capacity;
        var dp = new int[n + 1][];
        for (var i = 0; i <= n; i++)
        {
            dp[i] = new int[cap + 1];
            Array.Fill(dp[i], -1);
        }

        var states = new ItemState[n];
        var order = Enumerable.Range(0, n).ToArray();
        var steps = new List<KnapsackStep>();
        const string fill = "Fill the table";

        for (var w = 0; w <= cap; w++) dp[0][w] = 0;
        steps.Add(new KnapsackStep
        {
            Title = "Row 0: no items, no value",
            Text = $"Cell dp[i][w] will hold the best value you can carry using only the first i items with capacity w. With **no items** every capacity is worth 0, so row 0 is all zeros. The full table has {n + 1} rows and {cap + 1} columns.",
            Line = 0,
            Phase = fill,
            Table = Copy(dp),
            Batch = Enumerable.Range(0, cap + 1).Select(w => new GridCell(0, w)).ToList(),
            Items = (ItemState[])states.Clone(),
            Order = order,
            Watches = [Watch.Of("n", n), Watch.Of("W", cap)],
        });

        for (var i = 1; i <= n; i++)
        {
            var item = items[i - 1];
            states[i - 1] = ItemState.Focus;
            var lastMiss = Math.Min(item.Weight - 1, cap);
            if (lastMiss >= 0)
            {
                for (var w = 0; w <= lastMiss; w++) dp[i][w] = dp[i - 1][w];
                var text = item.Weight > cap
                    ? $"==Item {i}, {item.Name}== weighs {item.Weight}, more than the whole capacity of {cap}. It can never be packed, so the entire row copies the row above."
                    : $"==Item {i}, {item.Name}== weighs {item.Weight}. In capacities 0 to {lastMiss} it doesn't fit, so those cells copy the value from the row above: the best you can do there is to leave it out.";
                steps.Add(new KnapsackStep
                {
                    Title = $"{item.Name} doesn't fit below capacity {item.Weight}",
                    Text = text,
                    Line = 4,
                    Phase = fill,
                    Table = Copy(dp),
                    Batch = Enumerable.Range(0, lastMiss + 1).Select(w => new GridCell(i, w)).ToList(),
                    Items = (ItemState[])states.Clone(),
                    Order = order,
                    Watches = [Watch.Of("i", i), Watch.Of("weight[i]", item.Weight), Watch.Of("value[i]", item.Value)],
                });
            }

            for (var w = item.Weight; w <= cap; w++)
            {
                var skip = dp[i - 1][w];
                var rest = w - item.Weight;
                var take = item.Value + dp[i - 1][rest];
                dp[i][w] = Math.Max(skip, take);
                var verdict = take > skip
                    ? $"Taking it wins: **{take}**."
                    : take == skip
                        ? $"It's a tie, so the cell keeps **{skip}** without the item."
                        : $"Leaving it out wins: **{skip}**.";
                steps.Add(new KnapsackStep
                {
                    Title = $"Capacity {w}: pack the {item.Name.ToLowerInvariant()}?",
                    Text = $"**Skip** keeps the best value without it, the cell above: {skip}. **Take** adds its value {item.Value} to the best use of the remaining capacity {rest}: {item.Value} + {dp[i - 1][rest]} = {take}. {verdict}",
                    Line = 8,
                    Phase = fill,
                    Table = Copy(dp),
                    Cell = new GridCell(i, w),
                    SkipFrom = new GridCell(i - 1, w),
                    TakeFrom = new GridCell(i - 1, rest),
                    SkipValue = skip,
                    TakeValue = take,
                    Took = take > skip,
                    Items = (ItemState[])states.Clone(),
                    Order = order,
                    Watches =
                    [
                        Watch.Of("i", i), Watch.Of("w", w), Watch.Of("weight[i]", item.Weight), Watch.Of("value[i]", item.Value),
                        Watch.Of("skip", skip), Watch.Of("take", take), Watch.Of("dp[i][w]", dp[i][w]),
                    ],
                });
            }

            states[i - 1] = ItemState.Idle;
        }

        // Walk back from the bottom-right cell to recover which items were packed.
        const string back = "Trace back";
        var c = cap;
        var path = new List<GridCell> { new(n, cap) };
        int load = 0, total = 0;
        steps.Add(new KnapsackStep
        {
            Title = "The answer is in the last cell",
            Text = $"dp[{n}][{cap}] = **{dp[n][cap]}** is the best value for all {n} items and the full capacity. To find out which items make it up, walk back up the table from that cell.",
            Line = 9,
            Phase = back,
            Table = Copy(dp),
            Cell = new GridCell(n, cap),
            Path = path.ToList(),
            Items = (ItemState[])states.Clone(),
            Order = order,
            Watches = [Watch.Of("w", c), Watch.Of("dp[n][W]", dp[n][cap])],
        });

        for (var i = n; i >= 1; i--)
        {
            var item = items[i - 1];
            string text;
            int line;
            if (dp[i][c] != dp[i - 1][c])
            {
                states[i - 1] = ItemState.Taken;
                load += item.Weight;
                total += item.Value;
                text = $"dp[{i}][{c}] = {dp[i][c]} is more than the cell above ({dp[i - 1][c]}), so =={item.Name}== must be packed. Jump left by its weight {item.Weight}: capacity {c} → {c - item.Weight}.";
                c -= item.Weight;
                line = 12;
            }
            else
            {
                states[i - 1] = ItemState.Skipped;
                text = $"dp[{i}][{c}] equals the cell above ({dp[i][c]}), so the same value is reachable without ==the {item.Name.ToLowerInvariant()}==. Move straight up.";
                line = 11;
            }

            path.Add(new GridCell(i - 1, c));
            steps.Add(new KnapsackStep
            {
                Title = states[i - 1] == ItemState.Taken ? $"Pack the {item.Name.ToLowerInvariant()}" : $"Leave the {item.Name.ToLowerInvariant()}",
                Text = text,
                Line = line,
                Phase = back,
                Table = Copy(dp),
                Cell = new GridCell(i - 1, c),
                Path = path.ToList(),
                Items = (ItemState[])states.Clone(),
                Order = order,
                Load = load,
                Total = total,
                Watches = [Watch.Of("i", i), Watch.Of("w", c), Watch.Of("load", load), Watch.Of("value", total)],
            });
        }

        var packed = items.Where((_, k) => states[k] == ItemState.Taken).Select(x => x.Name.ToLowerInvariant());
        steps.Add(new KnapsackStep
        {
            Title = $"Best possible value: {total}",
            Text = $"Pack the {Num.List(packed)}: {load} of {cap} capacity for a value of **{total}**. No other combination does better. The table checked {n * (cap + 1)} cells instead of all {Pow2(n)} possible subsets of items.",
            Line = 13,
            Phase = "Done",
            Table = Copy(dp),
            Path = path.ToList(),
            Items = (ItemState[])states.Clone(),
            Order = order,
            Load = load,
            Total = total,
            Optimum = total,
            Watches = [Watch.Of("value", total), Watch.Of("load", load), Watch.Of("cells", n * (cap + 1))],
        });

        return steps;
    }

    public static IReadOnlyList<KnapsackStep> TraceGreedy(KnapsackInstance instance)
    {
        var items = instance.Items;
        var n = items.Count;
        var cap = instance.Capacity;
        var optimum = Solve(instance).Value;
        var states = new ItemState[n];
        var original = Enumerable.Range(0, n).ToArray();
        var steps = new List<KnapsackStep>();

        steps.Add(new KnapsackStep
        {
            Title = "Value per unit of weight",
            Text = "Greedy ranks every item by its **density**, value ÷ weight: how much value each unit of capacity buys. The bet is that dense items are always worth packing first.",
            Line = 0,
            Phase = "Rank",
            Items = (ItemState[])states.Clone(),
            Order = original,
            Watches = [Watch.Of("n", n), Watch.Of("W", cap)],
        });

        var order = SortedOrder(items, GreedyOrder.Density);
        var top = items[order[0]];
        steps.Add(new KnapsackStep
        {
            Title = "Sort by density",
            Text = $"Sorted from densest to least dense. ==The {top.Name.ToLowerInvariant()}== leads with {Num.F2(top.Density)} value per unit. Sorting is the most expensive part of the whole algorithm: O(n log n).",
            Line = 1,
            Phase = "Rank",
            Items = (ItemState[])states.Clone(),
            Order = order,
            Watches = [Watch.Of("first", top.Name), Watch.Of("density", Num.F2(top.Density))],
        });

        int load = 0, total = 0;
        foreach (var k in order)
        {
            var item = items[k];
            states[k] = ItemState.Focus;
            var fits = load + item.Weight <= cap;
            string text;
            if (fits)
            {
                load += item.Weight;
                total += item.Value;
                states[k] = ItemState.Taken;
                text = $"==The {item.Name.ToLowerInvariant()}== (weight {item.Weight}) fits: {load - item.Weight} + {item.Weight} = {load} ≤ {cap}. Pack it. Value so far: **{total}**.";
            }
            else
            {
                states[k] = ItemState.Skipped;
                text = $"==The {item.Name.ToLowerInvariant()}== (weight {item.Weight}) doesn't fit: {load} + {item.Weight} = {load + item.Weight} > {cap}. Greedy never reconsiders, so it's left behind for good.";
            }

            steps.Add(new KnapsackStep
            {
                Title = fits ? $"Pack the {item.Name.ToLowerInvariant()}" : $"Skip the {item.Name.ToLowerInvariant()}",
                Text = text,
                Line = fits ? 5 : 6,
                Phase = "Pack",
                Items = (ItemState[])states.Clone(),
                Order = order,
                Load = load,
                Total = total,
                Watches = [Watch.Of("item", item.Name), Watch.Of("density", Num.F2(item.Density)), Watch.Of("load", load), Watch.Of("value", total)],
            });
        }

        var gap = optimum - total;
        var verdict = gap == 0
            ? $"Here greedy happens to match the optimum of {optimum}, but that's luck, not a guarantee. Try the greedy trap example to see it fail."
            : $"The optimum is **{optimum}**, so greedy left {gap} on the table ({Num.Pct((double)gap / optimum)} worse). Packing the densest item first can block a better combination of the remaining ones.";
        steps.Add(new KnapsackStep
        {
            Title = $"Greedy packs a value of {total}",
            Text = $"Greedy used {load} of {cap} capacity for a value of **{total}**. {verdict}",
            Line = 7,
            Phase = "Done",
            Items = (ItemState[])states.Clone(),
            Order = order,
            Load = load,
            Total = total,
            Optimum = optimum,
            Watches = [Watch.Of("greedy", total), Watch.Of("optimum", optimum), Watch.Of("gap", gap)],
        });

        return steps;
    }

    /// <summary>Exact answer via dynamic programming, without recording steps.</summary>
    public static KnapsackResult Solve(KnapsackInstance instance)
    {
        var items = instance.Items;
        var n = items.Count;
        var cap = instance.Capacity;
        var dp = new int[n + 1, cap + 1];
        for (var i = 1; i <= n; i++)
        {
            var item = items[i - 1];
            for (var w = 0; w <= cap; w++)
            {
                dp[i, w] = dp[i - 1, w];
                if (item.Weight <= w) dp[i, w] = Math.Max(dp[i, w], item.Value + dp[i - 1, w - item.Weight]);
            }
        }

        var chosen = new List<int>();
        var c = cap;
        for (var i = n; i >= 1; i--)
        {
            if (dp[i, c] == dp[i - 1, c]) continue;
            chosen.Add(i - 1);
            c -= items[i - 1].Weight;
        }

        chosen.Reverse();
        return new KnapsackResult(dp[n, cap], chosen.Sum(i => items[i].Weight), chosen);
    }

    /// <summary>Greedy packing without recording steps.</summary>
    public static KnapsackResult SolveGreedy(KnapsackInstance instance, GreedyOrder by)
    {
        int load = 0, total = 0;
        var chosen = new List<int>();
        foreach (var k in SortedOrder(instance.Items, by))
        {
            var item = instance.Items[k];
            if (load + item.Weight > instance.Capacity) continue;
            load += item.Weight;
            total += item.Value;
            chosen.Add(k);
        }

        return new KnapsackResult(total, load, chosen);
    }

    /// <summary>
    /// Every subset of items as a bitmask with its weight and value. Used for the search-space view
    /// and to check the DP in tests. Only call this for small n.
    /// </summary>
    public static IReadOnlyList<(int Mask, int Weight, int Value)> Enumerate(KnapsackInstance instance)
    {
        var n = instance.Items.Count;
        var result = new (int, int, int)[1 << n];
        for (var mask = 0; mask < 1 << n; mask++)
        {
            int w = 0, v = 0;
            for (var i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                w += instance.Items[i].Weight;
                v += instance.Items[i].Value;
            }

            result[mask] = (mask, w, v);
        }

        return result;
    }

    private static int[] SortedOrder(IReadOnlyList<KnapsackItem> items, GreedyOrder by) => by switch
    {
        GreedyOrder.Value => Enumerable.Range(0, items.Count).OrderByDescending(i => items[i].Value).ThenBy(i => i).ToArray(),
        GreedyOrder.Lightest => Enumerable.Range(0, items.Count).OrderBy(i => items[i].Weight).ThenBy(i => i).ToArray(),
        _ => Enumerable.Range(0, items.Count).OrderByDescending(i => items[i].Density).ThenBy(i => i).ToArray(),
    };

    private static int[][] Copy(int[][] table) => table.Select(row => (int[])row.Clone()).ToArray();

    private static string Pow2(int n) => n < 63 ? (1L << n).ToString(System.Globalization.CultureInfo.InvariantCulture) : $"2^{n}";
}
