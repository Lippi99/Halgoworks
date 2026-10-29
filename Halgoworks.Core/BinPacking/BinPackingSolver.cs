namespace Halgoworks.Core.BinPacking;

public static class BinPackingSolver
{
    public static IReadOnlyList<BinRule> Rules { get; } =
    [
        BinRule.NextFit, BinRule.FirstFit, BinRule.BestFit, BinRule.WorstFit, BinRule.FirstFitDecreasing, BinRule.BestFitDecreasing,
    ];

    public static bool IsDecreasing(BinRule rule) => rule is BinRule.FirstFitDecreasing or BinRule.BestFitDecreasing;

    public static string ShortName(BinRule rule) => rule switch
    {
        BinRule.NextFit => "Next fit",
        BinRule.FirstFit => "First fit",
        BinRule.BestFit => "Best fit",
        BinRule.WorstFit => "Worst fit",
        BinRule.FirstFitDecreasing => "First fit decreasing",
        _ => "Best fit decreasing",
    };

    public static AlgorithmInfo Info(BinRule rule) => rule switch
    {
        BinRule.NextFit => new("nf", "Next fit", AlgorithmKind.Heuristic,
            "Keeps a single bin open. When an item doesn't fit, that bin is closed for good and a new one opens.",
            "O(n)", "O(1) open bins",
            [
                "open one bin b",
                "for each item x:",
                "  if x fits in b: put x in b",
                "  else: close b for good",
                "        open a new bin b; put x in b",
                "return the bins",
            ]),
        BinRule.FirstFit => new("ff", "First fit", AlgorithmKind.Heuristic,
            "Scans the bins from the left and drops each item into the first one with room.",
            "O(n²) as written", "O(n)",
            [
                "for each item x:",
                "  for each open bin b, left to right:",
                "    if x fits in b: put x in b; next item",
                "  if no bin fits: open a new bin; put x there",
                "return the bins",
            ]),
        BinRule.BestFit => new("bf", "Best fit", AlgorithmKind.Heuristic,
            "Puts each item in the bin it fills most tightly, keeping roomy bins free for big items.",
            "O(n²) as written", "O(n)",
            [
                "for each item x:",
                "  among bins where x fits, pick the one with the least free space",
                "  if there is one: put x in it",
                "  else: open a new bin; put x there",
                "return the bins",
            ]),
        BinRule.WorstFit => new("wf", "Worst fit", AlgorithmKind.Heuristic,
            "Puts each item in the roomiest bin. It spreads items evenly, which usually wastes space.",
            "O(n²) as written", "O(n)",
            [
                "for each item x:",
                "  among bins where x fits, pick the one with the most free space",
                "  if there is one: put x in it",
                "  else: open a new bin; put x there",
                "return the bins",
            ]),
        BinRule.FirstFitDecreasing => new("ffd", "First fit decreasing", AlgorithmKind.Heuristic,
            "Sorts items largest first, then runs first fit. Big items claim bins early and small ones fill the gaps.",
            "O(n log n)", "O(n)",
            [
                "sort items by size, largest first",
                "for each item x:",
                "  for each open bin b, left to right:",
                "    if x fits in b: put x in b; next item",
                "  if no bin fits: open a new bin; put x there",
                "return the bins",
            ]),
        _ => new("bfd", "Best fit decreasing", AlgorithmKind.Heuristic,
            "Sorts items largest first, then runs best fit.",
            "O(n log n)", "O(n)",
            [
                "sort items by size, largest first",
                "for each item x:",
                "  among bins where x fits, pick the one with the least free space",
                "  if there is one: put x in it",
                "  else: open a new bin; put x there",
                "return the bins",
            ]),
    };

    /// <summary>Packs without recording steps. Returns the item ids in each bin.</summary>
    public static List<List<int>> Solve(BinPackingInstance instance, BinRule rule)
    {
        var size = instance.Items.ToDictionary(i => i.Id, i => i.Size);
        var bins = new List<List<int>>();
        var free = new List<int>();
        var closed = new List<bool>();
        foreach (var id in ProcessingOrder(instance, rule))
        {
            var (target, _) = Choose(rule, free, size[id]);
            if (target == bins.Count)
            {
                if (rule == BinRule.NextFit && bins.Count > 0) closed[^1] = true;
                bins.Add([]);
                free.Add(instance.Capacity);
                closed.Add(false);
            }

            bins[target].Add(id);
            free[target] -= size[id];
        }

        return bins;
    }

    public static IReadOnlyList<BinPackingStep> Trace(BinPackingInstance instance, BinRule rule)
    {
        var cap = instance.Capacity;
        var size = instance.Items.ToDictionary(i => i.Id, i => i.Size);
        var unit = instance.Unit;
        string Sz(int s) => unit.Length == 0 ? s.ToString() : $"{s} {unit}";
        string ItemName(int id) => $"item #{id + 1} (size {Sz(size[id])})";

        var decreasing = IsDecreasing(rule);
        var offset = decreasing ? 1 : 0;
        var bins = new List<List<int>>();
        var free = new List<int>();
        var closed = new List<bool>();
        var steps = new List<BinPackingStep>();

        IReadOnlyList<int[]> Snapshot() => bins.Select(b => b.ToArray()).ToList();

        var input = instance.Items.Select(i => i.Id).ToList();
        steps.Add(new BinPackingStep
        {
            Title = $"{instance.Items.Count} items, bins of {Sz(cap)}",
            Text = $"The items add up to {Sz(instance.TotalSize)}. Even a perfect packing with no wasted space needs ⌈{instance.TotalSize} ÷ {cap}⌉ = **{instance.LowerBound} bins**. That lower bound is the yardstick for every heuristic.",
            Line = rule == BinRule.NextFit ? 0 : -1,
            Phase = "Setup",
            Queue = input,
            Watches = [Watch.Of("items", instance.Items.Count), Watch.Of("capacity", cap), Watch.Of("lower bound", instance.LowerBound)],
        });

        var order = ProcessingOrder(instance, rule);
        if (decreasing)
        {
            steps.Add(new BinPackingStep
            {
                Title = "Sort largest first",
                Text = $"Big items are the hard part: they need a bin almost to themselves. Placing them first lets the small ones fill the gaps they leave. The largest is ==item #{order[0] + 1}== at {Sz(size[order[0]])}.",
                Line = 0,
                Phase = "Sort",
                Queue = order,
                Watches = [Watch.Of("largest", size[order[0]]), Watch.Of("smallest", size[order[^1]])],
            });
        }

        for (var k = 0; k < order.Count; k++)
        {
            var id = order[k];
            var s = size[id];
            var (target, probes) = Choose(rule, free, s);
            var opensBin = target == bins.Count;
            var reason = Explain(rule, probes, free, s, target, opensBin, Sz);

            steps.Add(new BinPackingStep
            {
                Title = $"Where does item #{id + 1} go?",
                Text = $"=={char.ToUpperInvariant(ItemName(id)[0])}{ItemName(id)[1..]}==. {reason}",
                Line = rule switch
                {
                    BinRule.NextFit => opensBin ? 3 : 2,
                    BinRule.FirstFit or BinRule.FirstFitDecreasing => opensBin ? offset + 3 : offset + 2,
                    _ => offset + 1,
                },
                Phase = "Pack",
                Bins = Snapshot(),
                Queue = order.Skip(k).ToList(),
                Item = id,
                Probes = probes,
                Target = target,
                Closed = closed.ToList(),
                Watches = WatchesFor(free, s, target, opensBin, bins.Count, cap),
            });

            if (opensBin)
            {
                if (rule == BinRule.NextFit && bins.Count > 0) closed[^1] = true;
                bins.Add([]);
                free.Add(cap);
                closed.Add(false);
            }

            bins[target].Add(id);
            free[target] -= s;
            var used = cap - free[target];
            steps.Add(new BinPackingStep
            {
                Title = opensBin ? $"Open bin {target + 1}" : $"Into bin {target + 1}",
                Text = opensBin
                    ? $"Item #{id + 1} starts ==bin {target + 1}==, which now holds {Sz(used)} of {Sz(cap)}. {bins.Count} bin{(bins.Count == 1 ? "" : "s")} in use."
                    : $"Item #{id + 1} goes into ==bin {target + 1}==, which now holds {Sz(used)} of {Sz(cap)} ({Sz(free[target])} free).",
                Line = rule switch
                {
                    BinRule.NextFit => opensBin ? 4 : 2,
                    BinRule.FirstFit or BinRule.FirstFitDecreasing => opensBin ? offset + 3 : offset + 2,
                    _ => opensBin ? offset + 3 : offset + 2,
                },
                Phase = "Pack",
                Bins = Snapshot(),
                Queue = order.Skip(k + 1).ToList(),
                Target = target,
                Placed = id,
                Closed = closed.ToList(),
                Watches = [Watch.Of("bin", target + 1), Watch.Of("fill", $"{used}/{cap}"), Watch.Of("bins used", bins.Count)],
            });
        }

        var count = bins.Count;
        var lb = instance.LowerBound;
        var optimum = instance.Items.Count <= 24 ? Optimal(instance) : null;
        var waste = count * cap - instance.TotalSize;
        string verdict;
        if (count == lb)
            verdict = "That equals the lower bound, so this packing is **optimal**: no algorithm can do better on these items.";
        else if (optimum == count)
            verdict = $"That's above the lower bound of {lb}, but an exhaustive search confirms {count} is the true optimum for these items. The bound isn't always reachable.";
        else if (optimum is { } opt)
            verdict = $"An exhaustive search finds a packing with **{opt} bins**, so this heuristic used {count - opt} more than necessary.";
        else
            verdict = $"The lower bound is {lb}, so it uses at most {count - lb} more bins than the optimum.";

        steps.Add(new BinPackingStep
        {
            Title = $"Done: {count} bins",
            Text = $"{ShortName(rule)} packed everything into **{count} bins** with {Sz(waste)} of empty space ({Num.Pct((double)instance.TotalSize / (count * cap))} full on average). {verdict}",
            Line = Info(rule).Code.Count - 1,
            Phase = "Done",
            Bins = Snapshot(),
            Closed = closed.ToList(),
            Watches = [Watch.Of("bins", count), Watch.Of("lower bound", lb), Watch.Of("optimum", optimum?.ToString() ?? "unknown"), Watch.Of("wasted", waste)],
        });

        return steps;
    }

    /// <summary>
    /// Exact minimum number of bins by depth-first branch and bound. Returns null if the search
    /// exceeds <paramref name="nodeLimit"/> nodes before proving optimality.
    /// </summary>
    public static int? Optimal(BinPackingInstance instance, int nodeLimit = 200_000)
    {
        var sizes = instance.Items.Select(i => i.Size).OrderByDescending(s => s).ToArray();
        var n = sizes.Length;
        var cap = instance.Capacity;
        var lb = instance.LowerBound;
        var best = Solve(instance, BinRule.FirstFitDecreasing).Count;
        if (best == lb) return best;

        var suffix = new int[n + 1];
        for (var i = n - 1; i >= 0; i--) suffix[i] = suffix[i + 1] + sizes[i];
        var free = new int[n];
        var used = 0;
        var nodes = 0;
        var aborted = false;

        void Search(int k)
        {
            if (aborted || best == lb) return;
            if (++nodes > nodeLimit)
            {
                aborted = true;
                return;
            }

            if (k == n)
            {
                best = used;
                return;
            }

            var totalFree = 0;
            for (var b = 0; b < used; b++) totalFree += free[b];
            var need = suffix[k] - totalFree;
            if (used + (need > 0 ? (need + cap - 1) / cap : 0) >= best) return;

            var s = sizes[k];
            for (var b = 0; b < used; b++)
            {
                if (free[b] < s || SeenBefore(free, b)) continue;
                free[b] -= s;
                Search(k + 1);
                free[b] += s;
                if (aborted || best == lb) return;
            }

            if (used + 1 >= best) return;
            free[used] = cap - s;
            used++;
            Search(k + 1);
            used--;
        }

        Search(0);
        return aborted ? null : best;
    }

    // Two bins with the same free space are interchangeable for the rest of the search.
    private static bool SeenBefore(int[] free, int b)
    {
        for (var a = 0; a < b; a++)
            if (free[a] == free[b]) return true;
        return false;
    }

    private static List<int> ProcessingOrder(BinPackingInstance instance, BinRule rule) => IsDecreasing(rule)
        ? instance.Items.OrderByDescending(i => i.Size).ThenBy(i => i.Id).Select(i => i.Id).ToList()
        : instance.Items.Select(i => i.Id).ToList();

    // Next fit only ever looks at the newest bin: every earlier bin has been closed.
    private static (int Target, Probe[] Probes) Choose(BinRule rule, List<int> free, int size)
    {
        var probes = new Probe[free.Count];
        var target = free.Count;
        switch (rule)
        {
            case BinRule.NextFit:
                if (free.Count > 0)
                {
                    var last = free.Count - 1;
                    probes[last] = free[last] >= size ? Probe.Chosen : Probe.TooFull;
                    if (free[last] >= size) target = last;
                }

                break;

            case BinRule.FirstFit:
            case BinRule.FirstFitDecreasing:
                for (var b = 0; b < free.Count; b++)
                {
                    if (free[b] >= size)
                    {
                        probes[b] = Probe.Chosen;
                        target = b;
                        break;
                    }

                    probes[b] = Probe.TooFull;
                }

                break;

            default:
                var bestFree = -1;
                var worst = rule == BinRule.WorstFit;
                for (var b = 0; b < free.Count; b++)
                {
                    if (free[b] < size)
                    {
                        probes[b] = Probe.TooFull;
                        continue;
                    }

                    probes[b] = Probe.Fits;
                    var better = bestFree < 0 || (worst ? free[b] > bestFree : free[b] < bestFree);
                    if (!better) continue;
                    bestFree = free[b];
                    target = b;
                }

                if (target < free.Count) probes[target] = Probe.Chosen;
                break;
        }

        return (target, probes);
    }

    private static string Explain(BinRule rule, Probe[] probes, List<int> free, int size, int target, bool opensBin, Func<int, string> sz)
    {
        if (free.Count == 0) return "There are no bins yet, so the first one opens.";

        var tooFull = Enumerable.Range(0, probes.Length).Where(b => probes[b] == Probe.TooFull).ToList();
        string Bins(IEnumerable<int> bins) => Num.List(bins.Select(b => $"bin {b + 1} ({sz(free[b])} free)"));
        string Rejected()
        {
            if (tooFull.Count == 0) return "";
            if (tooFull.Count > 4) return $"{tooFull.Count} bins are too full. ";
            return tooFull.Count == 1 ? $"Bin {tooFull[0] + 1} ({sz(free[tooFull[0]])} free) is too full. " : $"{Capitalize(Bins(tooFull))} are too full. ";
        }

        switch (rule)
        {
            case BinRule.NextFit:
                var open = free.Count - 1;
                return opensBin
                    ? $"Next fit only looks at the open bin, bin {open + 1}, which has {sz(free[open])} free: not enough. Bin {open + 1} is closed for good and bin {open + 2} opens, even if an older bin had room."
                    : $"Next fit only looks at the open bin, bin {open + 1}. It has {sz(free[open])} free, so the item fits.";

            case BinRule.FirstFit:
            case BinRule.FirstFitDecreasing:
                return opensBin
                    ? $"{Rejected()}No open bin has room, so a new bin opens."
                    : $"{Rejected()}Bin {target + 1} has {sz(free[target])} free: the first bin that fits.";

            default:
                if (opensBin) return $"{Rejected()}It doesn't fit in any open bin, so a new bin opens.";
                var fits = Enumerable.Range(0, probes.Length).Where(b => probes[b] is Probe.Fits or Probe.Chosen).ToList();
                var pick = rule == BinRule.WorstFit ? "the roomiest" : "the tightest fit";
                var candidates = fits.Count > 4 ? $"{fits.Count} bins" : Bins(fits);
                return $"It fits in {candidates}. {(rule == BinRule.WorstFit ? "Worst" : "Best")} fit picks {pick}: bin {target + 1}, leaving {sz(free[target] - size)} free.";
        }
    }

    private static IReadOnlyList<Watch> WatchesFor(List<int> free, int size, int target, bool opensBin, int binCount, int cap) =>
    [
        Watch.Of("size", size),
        Watch.Of("bins open", binCount),
        Watch.Of("target", opensBin ? "new bin" : $"bin {target + 1}"),
        Watch.Of("free there", opensBin ? cap : free[target]),
    ];

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
