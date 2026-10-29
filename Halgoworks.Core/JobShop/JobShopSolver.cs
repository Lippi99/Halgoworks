namespace Halgoworks.Core.JobShop;

public static class JobShopSolver
{
    public static IReadOnlyList<DispatchRule> Rules { get; } =
        [DispatchRule.Mwkr, DispatchRule.Spt, DispatchRule.Lpt, DispatchRule.Lwkr, DispatchRule.Fcfs, DispatchRule.Edd];

    public static string Short(DispatchRule rule) => rule switch
    {
        DispatchRule.Mwkr => "MWKR",
        DispatchRule.Spt => "SPT",
        DispatchRule.Lpt => "LPT",
        DispatchRule.Lwkr => "LWKR",
        DispatchRule.Fcfs => "FCFS",
        _ => "EDD",
    };

    public static string Describe(DispatchRule rule) => rule switch
    {
        DispatchRule.Mwkr => "most work remaining",
        DispatchRule.Spt => "shortest operation first",
        DispatchRule.Lpt => "longest operation first",
        DispatchRule.Lwkr => "least work remaining",
        DispatchRule.Fcfs => "first come, first served",
        _ => "earliest due date",
    };

    public static AlgorithmInfo GifflerThompsonInfo(DispatchRule rule) => new(
        "gt",
        $"Giffler–Thompson ({Short(rule)})",
        AlgorithmKind.Heuristic,
        $"Builds an active schedule one operation at a time. Whenever operations compete for a machine, a priority rule decides; this run uses {Describe(rule)}.",
        "O(n²·m)",
        "O(n·m)",
        [
            "S ← the first operation of every job",
            "while S is not empty:",
            "  for each o in S: start(o) ← max(job ready, machine ready)",
            "  o* ← the operation in S that could finish first;  M ← its machine",
            "  C ← operations in S that need M and could start before o* ends",
            $"  pick o from C by the rule: {Describe(rule)}",
            "  schedule o at start(o); update job and machine ready times",
            "  replace o in S with the next operation of its job",
            "return the schedule;  makespan ← latest finish",
        ]);

    public static readonly AlgorithmInfo TabuInfo = new(
        "tabu",
        "Tabu search",
        AlgorithmKind.LocalSearch,
        "Starts from a dispatching schedule and keeps swapping adjacent operations on the critical path, with a short memory that stops it from undoing recent moves.",
        "O(N) per move evaluated",
        "O(N + tabu list)",
        [
            "s ← a Giffler–Thompson schedule (MWKR);  best ← s",
            "repeat for a fixed number of iterations:",
            "  find a critical path: the chain of operations that sets the makespan",
            "  moves ← swaps of adjacent critical operations on the same machine",
            "  pick the best move that isn't tabu (or beats best anyway)",
            "  apply it; forbid undoing it for the next few iterations",
            "  if s beats best: best ← s",
            "return best",
        ]);

    public static string Label(int job, int index) => $"J{job + 1}·{index + 1}";

    public static int Makespan(IReadOnlyList<ScheduledOp> schedule) => schedule.Count == 0 ? 0 : schedule.Max(o => o.End);

    // ---------------------------------------------------------------- Giffler–Thompson

    /// <summary>Dispatches without recording steps.</summary>
    public static List<ScheduledOp> Solve(JobShopInstance instance, DispatchRule rule)
    {
        var state = new Dispatch(instance);
        while (!state.Done)
        {
            var (_, _, conflict) = state.Candidates();
            state.Schedule(Pick(instance, state, conflict, rule));
        }

        return state.Scheduled;
    }

    public static IReadOnlyList<JobShopStep> TraceDispatch(JobShopInstance instance, DispatchRule rule)
    {
        var steps = new List<JobShopStep>();
        var state = new Dispatch(instance);
        var machines = instance.Machines;
        var lb = instance.LowerBound;

        var (first, _, _) = state.Candidates();
        steps.Add(new JobShopStep
        {
            Title = "Every job is waiting to start",
            Text = $"{instance.Jobs.Count} jobs must each visit machines in a fixed order, and a machine handles one operation at a time. The first operation of every job is ready at time 0. No schedule can finish before **{lb}**: that's the longest job or the busiest machine.",
            Line = 0,
            Phase = "Setup",
            Candidates = first.Select(c => c with { Conflict = false, Earliest = false }).ToList(),
            Watches = [Watch.Of("jobs", instance.Jobs.Count), Watch.Of("machines", machines.Count), Watch.Of("lower bound", lb)],
        });

        var round = 0;
        while (!state.Done)
        {
            round++;
            var (cands, earliest, conflict) = state.Candidates();
            var chosen = Pick(instance, state, conflict, rule);
            var m = earliest.Machine;
            var marked = cands.Select(c => c with
            {
                Conflict = conflict.Contains(c),
                Earliest = c == earliest,
                Chosen = c == chosen,
            }).ToList();

            string choice;
            if (conflict.Count == 1)
            {
                choice = $"Only =={Label(chosen.Job, chosen.Index)}== needs {machines[m]} before then, so there's nothing to decide.";
            }
            else
            {
                var options = Num.List(conflict.Select(c => $"{Label(c.Job, c.Index)} ({RuleValue(instance, state, c, rule)})"));
                choice = $"{conflict.Count} operations want {machines[m]} before then: {options}. The rule ({Describe(rule)}) picks =={Label(chosen.Job, chosen.Index)}==.";
            }

            steps.Add(new JobShopStep
            {
                Title = $"Who gets {machines[m]}?",
                Text = $"Of all waiting operations, {Label(earliest.Job, earliest.Index)} could finish first, on {machines[m]} at t = {earliest.End}. {choice}",
                Line = 5,
                Phase = "Dispatch",
                Scheduled = state.Scheduled.ToList(),
                Candidates = marked,
                Cut = earliest.End,
                Machine = m,
                Focus = new OpRef(chosen.Job, chosen.Index),
                Makespan = Makespan(state.Scheduled),
                Watches =
                [
                    Watch.Of("waiting", cands.Count), Watch.Of("cut time", earliest.End), Watch.Of("machine", machines[m]),
                    Watch.Of("conflicts", conflict.Count), Watch.Of("chosen", Label(chosen.Job, chosen.Index)),
                ],
            });

            state.Schedule(chosen);
            var job = instance.Jobs[chosen.Job];
            var nextNote = chosen.Index + 1 < job.Ops.Count
                ? $"{job.Name}'s next operation ({machines[job.Ops[chosen.Index + 1].Machine]}, {job.Ops[chosen.Index + 1].Duration}) joins the waiting set."
                : $"That was {job.Name}'s last operation: it's complete at t = {chosen.End}.";
            var delayed = conflict.Where(c => c != chosen).ToList();
            var delayNote = delayed.Count == 0 ? "" : $" {Num.List(delayed.Select(c => Label(c.Job, c.Index)))} must now wait until at least {chosen.End}.";
            steps.Add(new JobShopStep
            {
                Title = $"{Label(chosen.Job, chosen.Index)} runs {chosen.Start}–{chosen.End}",
                Text = $"=={Label(chosen.Job, chosen.Index)}== takes {machines[m]} from {chosen.Start} to {chosen.End}.{delayNote} {nextNote}",
                Line = 6,
                Phase = "Dispatch",
                Scheduled = state.Scheduled.ToList(),
                Focus = new OpRef(chosen.Job, chosen.Index),
                Machine = m,
                Makespan = Makespan(state.Scheduled),
                Watches = [Watch.Of("scheduled", $"{state.Scheduled.Count}/{instance.OperationCount}"), Watch.Of("makespan so far", Makespan(state.Scheduled))],
            });
        }

        var schedule = state.Scheduled;
        var makespan = Makespan(schedule);
        var critical = CriticalPath(instance, schedule);
        steps.Add(new JobShopStep
        {
            Title = $"Makespan {makespan}",
            Text = $"All {instance.OperationCount} operations are scheduled; the last one finishes at **{makespan}**. {Verdict(instance, makespan)} The highlighted chain is the **critical path**: each operation on it starts the moment the previous one ends, so delaying any of them delays everything. Improving the schedule means reordering operations on that path.{Tardiness(instance, schedule)}",
            Line = 8,
            Phase = "Done",
            Scheduled = schedule.ToList(),
            Critical = critical,
            Makespan = makespan,
            Watches = [Watch.Of("makespan", makespan), Watch.Of("lower bound", lb), Watch.Of("optimum", instance.Optimum?.ToString() ?? "unknown"), Watch.Of("critical ops", critical.Count)],
        });

        return steps;
    }

    private static Candidate Pick(JobShopInstance instance, Dispatch state, List<Candidate> conflict, DispatchRule rule) =>
        conflict
            .OrderBy(c => Priority(instance, state, c, rule))
            .ThenBy(c => c.Start)
            .ThenBy(c => c.Job)
            .First();

    // Lower is better.
    private static double Priority(JobShopInstance instance, Dispatch state, Candidate c, DispatchRule rule) => rule switch
    {
        DispatchRule.Spt => c.End - c.Start,
        DispatchRule.Lpt => -(c.End - c.Start),
        DispatchRule.Mwkr => -state.Remaining(c.Job),
        DispatchRule.Lwkr => state.Remaining(c.Job),
        DispatchRule.Fcfs => c.Start,
        _ => instance.Jobs[c.Job].Due ?? int.MaxValue,
    };

    private static string RuleValue(JobShopInstance instance, Dispatch state, Candidate c, DispatchRule rule) => rule switch
    {
        DispatchRule.Spt or DispatchRule.Lpt => $"takes {c.End - c.Start}",
        DispatchRule.Mwkr or DispatchRule.Lwkr => $"{state.Remaining(c.Job)} work left",
        DispatchRule.Fcfs => $"ready at {c.Start}",
        _ => instance.Jobs[c.Job].Due is { } due ? $"due {due}" : "no due date",
    };

    private static string Verdict(JobShopInstance instance, int makespan)
    {
        var lb = instance.LowerBound;
        if (makespan == lb) return $"That equals the lower bound of {lb}, so it's optimal.";
        if (instance.Optimum is { } opt)
            return makespan == opt
                ? $"That matches the proven optimum of {opt} for this instance."
                : $"The proven optimum for this instance is {opt}, so this schedule is {Num.Pct((double)(makespan - opt) / opt)} longer.";
        return $"The lower bound is {lb}, so it's at most {Num.Pct((double)(makespan - lb) / lb)} above the optimum.";
    }

    private static string Tardiness(JobShopInstance instance, IReadOnlyList<ScheduledOp> schedule)
    {
        if (!instance.HasDueDates) return "";
        var late = instance.Jobs
            .Select(j => (Job: j, End: schedule.Where(o => o.Job == j.Id).Max(o => o.End)))
            .Where(x => x.End > x.Job.Due)
            .ToList();
        if (late.Count == 0) return " Every job meets its due date.";
        var total = late.Sum(x => x.End - x.Job.Due!.Value);
        return $" {late.Count} job{(late.Count == 1 ? " is" : "s are")} late ({Num.List(late.Select(x => $"{x.Job.Name} by {x.End - x.Job.Due}"))}), {total} time units of tardiness in total.";
    }

    /// <summary>Waiting set, ready times and the growing schedule for Giffler–Thompson.</summary>
    private sealed class Dispatch(JobShopInstance instance)
    {
        private readonly int[] _next = new int[instance.Jobs.Count];
        private readonly int[] _jobReady = new int[instance.Jobs.Count];
        private readonly int[] _machineReady = new int[instance.Machines.Count];

        public List<ScheduledOp> Scheduled { get; } = [];

        public bool Done => Scheduled.Count == instance.OperationCount;

        public int Remaining(int job) => instance.Jobs[job].Ops.Skip(_next[job]).Sum(o => o.Duration);

        public (List<Candidate> All, Candidate Earliest, List<Candidate> Conflict) Candidates()
        {
            var all = new List<Candidate>();
            foreach (var job in instance.Jobs)
            {
                if (_next[job.Id] >= job.Ops.Count) continue;
                var op = job.Ops[_next[job.Id]];
                var start = Math.Max(_jobReady[job.Id], _machineReady[op.Machine]);
                all.Add(new Candidate(job.Id, op.Index, op.Machine, start, start + op.Duration, false, false, false));
            }

            if (all.Count == 0) return (all, null!, []);
            var earliest = all.OrderBy(c => c.End).ThenBy(c => c.Job).First();
            var conflict = all.Where(c => c.Machine == earliest.Machine && c.Start < earliest.End).ToList();
            return (all, earliest, conflict);
        }

        public void Schedule(Candidate c)
        {
            Scheduled.Add(new ScheduledOp(c.Job, c.Index, c.Machine, c.Start, c.End));
            _jobReady[c.Job] = c.End;
            _machineReady[c.Machine] = c.End;
            _next[c.Job]++;
        }
    }

    // ---------------------------------------------------------------- Critical path and evaluation

    /// <summary>
    /// A chain of operations from time 0 to the makespan where each starts exactly when its
    /// job or machine predecessor ends. Works for any semi-active schedule.
    /// </summary>
    public static IReadOnlyList<OpRef> CriticalPath(JobShopInstance instance, IReadOnlyList<ScheduledOp> schedule)
    {
        if (schedule.Count == 0) return [];
        var byOp = schedule.ToDictionary(o => new OpRef(o.Job, o.Index));
        var byMachine = schedule.GroupBy(o => o.Machine).ToDictionary(g => g.Key, g => g.OrderBy(o => o.Start).ToList());
        var current = schedule.OrderByDescending(o => o.End).ThenBy(o => o.Job).First();
        var path = new List<OpRef> { new(current.Job, current.Index) };
        while (current.Start > 0)
        {
            var onMachine = byMachine[current.Machine];
            var pos = onMachine.IndexOf(current);
            var machinePred = pos > 0 ? onMachine[pos - 1] : null;
            var jobPred = current.Index > 0 ? byOp[new OpRef(current.Job, current.Index - 1)] : null;
            ScheduledOp? previous = machinePred is not null && machinePred.End == current.Start ? machinePred
                : jobPred is not null && jobPred.End == current.Start ? jobPred
                : null;
            if (previous is null) break;
            current = previous;
            path.Add(new OpRef(current.Job, current.Index));
        }

        path.Reverse();
        return path;
    }

    /// <summary>Checks job order, machine exclusivity and durations. Returns null when valid.</summary>
    public static string? Validate(JobShopInstance instance, IReadOnlyList<ScheduledOp> schedule)
    {
        if (schedule.Count != instance.OperationCount) return "wrong number of operations";
        foreach (var job in instance.Jobs)
        {
            var ops = schedule.Where(o => o.Job == job.Id).OrderBy(o => o.Index).ToList();
            if (ops.Count != job.Ops.Count) return $"{job.Name} is missing operations";
            for (var i = 0; i < ops.Count; i++)
            {
                if (ops[i].End - ops[i].Start != job.Ops[i].Duration || ops[i].Machine != job.Ops[i].Machine) return $"{Label(job.Id, i)} has the wrong machine or duration";
                if (i > 0 && ops[i].Start < ops[i - 1].End) return $"{Label(job.Id, i)} starts before its predecessor ends";
            }
        }

        foreach (var group in schedule.GroupBy(o => o.Machine))
        {
            var ops = group.OrderBy(o => o.Start).ToList();
            for (var k = 1; k < ops.Count; k++)
                if (ops[k].Start < ops[k - 1].End) return $"{Label(ops[k].Job, ops[k].Index)} overlaps on machine {group.Key}";
        }

        return null;
    }

    // ---------------------------------------------------------------- Tabu search

    public static IReadOnlyList<JobShopStep> TraceTabu(JobShopInstance instance, int iterations = 80, int tenure = 8)
    {
        var model = new SequenceModel(instance, Solve(instance, DispatchRule.Mwkr));
        var steps = new List<JobShopStep>();
        var machines = instance.Machines;
        var lb = instance.LowerBound;
        var target = instance.Optimum ?? lb;

        var current = model.Evaluate(model.Initial)!;
        var seq = model.Initial;
        var best = current;
        var history = new List<int> { current.Makespan };
        var bestHistory = new List<int> { current.Makespan };
        var tabu = new Dictionary<(int, int), int>();

        steps.Add(new JobShopStep
        {
            Title = $"Start from MWKR: makespan {current.Makespan}",
            Text = $"Local search needs a starting point: the Giffler–Thompson schedule with the most-work-remaining rule, makespan **{current.Makespan}**. The highlighted **critical path** decides that number; only reordering operations on it can shorten the schedule.",
            Line = 0,
            Phase = "Start",
            Scheduled = current.Schedule,
            Critical = current.Critical,
            Makespan = current.Makespan,
            History = history.ToList(),
            BestHistory = bestHistory.ToList(),
            Watches = [Watch.Of("makespan", current.Makespan), Watch.Of("best", best.Makespan), Watch.Of("lower bound", lb)],
        });

        var stopReason = $"After {iterations} iterations the search stops.";
        for (var iter = 1; iter <= iterations; iter++)
        {
            if (best.Makespan <= target)
            {
                stopReason = best.Makespan == lb
                    ? "The best schedule meets the lower bound, so nothing can beat it: the search stops early."
                    : $"The best schedule matches the proven optimum of {target}, so the search stops early.";
                break;
            }

            var moves = model.Moves(current);
            if (moves.Count == 0)
            {
                stopReason = "The critical path has no two operations on the same machine, so no swap can help: this schedule is optimal.";
                break;
            }

            var evaluated = moves
                .Select(m => (Move: m, Seq: model.Swap(seq, m), Result: (Evaluation?)null))
                .Select(x => x with { Result = model.Evaluate(x.Seq) })
                .Where(x => x.Result is not null)
                .ToList();
            if (evaluated.Count == 0)
            {
                stopReason = "No legal swap is left.";
                break;
            }

            bool IsTabu((int A, int B) m) => tabu.TryGetValue(m, out var until) && until >= iter;
            var admissible = evaluated.Where(x => !IsTabu(x.Move) || x.Result!.Makespan < best.Makespan).ToList();
            var aspiration = false;
            (int A, int B) move;
            int[][] nextSeq;
            Evaluation next;
            if (admissible.Count > 0)
            {
                var pick = admissible.OrderBy(x => x.Result!.Makespan).ThenBy(x => x.Move.A).First();
                aspiration = IsTabu(pick.Move);
                (move, nextSeq, next) = (pick.Move, pick.Seq, pick.Result!);
            }
            else
            {
                // Everything is tabu: take the move that has been tabu the longest.
                var pick = evaluated.OrderBy(x => tabu[x.Move]).First();
                (move, nextSeq, next) = (pick.Move, pick.Seq, pick.Result!);
            }

            var before = current.Makespan;
            tabu[(move.B, move.A)] = iter + tenure;
            current = next;
            seq = nextSeq;
            var improvedBest = current.Makespan < best.Makespan;
            if (improvedBest) best = current;

            history.Add(current.Makespan);
            bestHistory.Add(best.Makespan);

            var a = model.Ref(move.A);
            var b = model.Ref(move.B);
            var m = instance.Jobs[a.Job].Ops[a.Index].Machine;
            var delta = current.Makespan - before;
            var effect = delta < 0
                ? $"makespan {before} → **{current.Makespan}**."
                : delta == 0
                    ? $"makespan stays at {current.Makespan}. A sideways move still changes the critical path, which opens new moves."
                    : $"every move makes things worse this time, so tabu search takes the least bad one ({before} → {current.Makespan}) instead of stopping. That's how it climbs out of local optima.";
            var notes = (aspiration ? " The move was tabu, but it beats the best schedule so far, so the ban is waived." : "")
                        + (improvedBest ? $" New best: **{best.Makespan}**." : "");
            steps.Add(new JobShopStep
            {
                Title = $"Iteration {iter}: swap {Label(a.Job, a.Index)} and {Label(b.Job, b.Index)}",
                Text = $"{moves.Count} swap{(moves.Count == 1 ? "" : "s")} available on the critical path. Swapping =={Label(a.Job, a.Index)} and {Label(b.Job, b.Index)}== on {machines[m]}: {effect}{notes} Swapping them back is now tabu for {tenure} iterations.",
                Line = improvedBest ? 6 : 5,
                Phase = "Search",
                Scheduled = current.Schedule,
                Critical = current.Critical,
                Swapped = [a, b],
                Makespan = current.Makespan,
                History = history.ToList(),
                BestHistory = bestHistory.ToList(),
                Watches =
                [
                    Watch.Of("iteration", iter), Watch.Of("moves", moves.Count), Watch.Of("makespan", current.Makespan),
                    Watch.Of("best", best.Makespan), Watch.Of("tabu list", tabu.Count(t => t.Value >= iter)),
                ],
            });
        }

        steps.Add(new JobShopStep
        {
            Title = $"Best makespan {best.Makespan}",
            Text = $"{stopReason} The best schedule found has makespan **{best.Makespan}**, down from {history[0]}. {Verdict(instance, best.Makespan)}{Tardiness(instance, best.Schedule)}",
            Line = 7,
            Phase = "Done",
            Scheduled = best.Schedule,
            Critical = best.Critical,
            Makespan = best.Makespan,
            History = history.ToList(),
            BestHistory = bestHistory.ToList(),
            Watches = [Watch.Of("best", best.Makespan), Watch.Of("start", history[0]), Watch.Of("lower bound", lb), Watch.Of("optimum", instance.Optimum?.ToString() ?? "unknown")],
        });

        return steps;
    }

    public sealed record Evaluation(int Makespan, List<ScheduledOp> Schedule, List<OpRef> Critical, List<int> CriticalIds);

    /// <summary>
    /// Turns an operation order for every machine into the earliest schedule that respects it. Returns null if an order lists the
    /// wrong operations, or if the orders contradict the jobs' own sequences (a cycle: every operation waiting on another).
    /// </summary>
    public static Evaluation? EvaluateOrder(JobShopInstance instance, IReadOnlyList<IReadOnlyList<OpRef>> machineOrders)
    {
        if (machineOrders.Count != instance.Machines.Count) return null;
        var model = new SequenceModel(instance, []);
        var seq = new int[machineOrders.Count][];
        for (var m = 0; m < machineOrders.Count; m++)
        {
            var expected = instance.Jobs.SelectMany(j => j.Ops).Where(o => o.Machine == m).Select(o => new OpRef(o.Job, o.Index)).ToHashSet();
            var order = machineOrders[m];
            if (order.Count != expected.Count || !order.All(expected.Contains) || order.Distinct().Count() != order.Count) return null;
            seq[m] = order.Select(model.Id).ToArray();
        }

        return model.Evaluate(seq);
    }

    /// <summary>A deliberately plain starting order: on every machine, jobs simply go in number order.</summary>
    public static IReadOnlyList<IReadOnlyList<OpRef>> JobOrder(JobShopInstance instance) => Enumerable.Range(0, instance.Machines.Count)
        .Select(m => (IReadOnlyList<OpRef>)instance.Jobs.SelectMany(j => j.Ops).Where(o => o.Machine == m).OrderBy(o => o.Job).ThenBy(o => o.Index)
            .Select(o => new OpRef(o.Job, o.Index)).ToList())
        .ToList();

    /// <summary>The machine orders behind a schedule (operations sorted by start time on each machine).</summary>
    public static IReadOnlyList<IReadOnlyList<OpRef>> OrderOf(JobShopInstance instance, IReadOnlyList<ScheduledOp> schedule) => Enumerable.Range(0, instance.Machines.Count)
        .Select(m => (IReadOnlyList<OpRef>)schedule.Where(o => o.Machine == m).OrderBy(o => o.Start).Select(o => new OpRef(o.Job, o.Index)).ToList())
        .ToList();

    /// <summary>A schedule as one operation order per machine, evaluated as a longest-path problem.</summary>
    private sealed class SequenceModel
    {
        private readonly JobShopInstance _instance;
        private readonly int[] _offset;
        private readonly int[] _job;
        private readonly int[] _index;
        private readonly int[] _machine;
        private readonly int[] _duration;

        public SequenceModel(JobShopInstance instance, IReadOnlyList<ScheduledOp> start)
        {
            _instance = instance;
            _offset = new int[instance.Jobs.Count];
            var ops = new List<Operation>();
            foreach (var job in instance.Jobs)
            {
                _offset[job.Id] = ops.Count;
                ops.AddRange(job.Ops);
            }

            _job = ops.Select(o => o.Job).ToArray();
            _index = ops.Select(o => o.Index).ToArray();
            _machine = ops.Select(o => o.Machine).ToArray();
            _duration = ops.Select(o => o.Duration).ToArray();
            Initial = Enumerable.Range(0, instance.Machines.Count)
                .Select(m => start.Where(o => o.Machine == m).OrderBy(o => o.Start).Select(o => _offset[o.Job] + o.Index).ToArray())
                .ToArray();
        }

        public int[][] Initial { get; }

        public OpRef Ref(int id) => new(_job[id], _index[id]);

        public int Id(OpRef op) => _offset[op.Job] + op.Index;

        public int[][] Swap(int[][] seq, (int A, int B) move)
        {
            var m = _machine[move.A];
            var copy = (int[][])seq.Clone();
            var line = (int[])seq[m].Clone();
            var i = Array.IndexOf(line, move.A);
            (line[i], line[i + 1]) = (line[i + 1], line[i]);
            copy[m] = line;
            return copy;
        }

        /// <summary>Pairs (a, b) of consecutive critical operations on one machine; swapping puts b first.</summary>
        public List<(int A, int B)> Moves(Evaluation e)
        {
            var moves = new List<(int, int)>();
            for (var k = 0; k + 1 < e.CriticalIds.Count; k++)
            {
                int a = e.CriticalIds[k], b = e.CriticalIds[k + 1];
                if (_machine[a] == _machine[b]) moves.Add((a, b));
            }

            return moves;
        }

        /// <summary>Earliest start times by topological order. Returns null if the orders contain a cycle.</summary>
        public Evaluation? Evaluate(int[][] seq)
        {
            var n = _job.Length;
            var machinePred = Enumerable.Repeat(-1, n).ToArray();
            var machineSucc = Enumerable.Repeat(-1, n).ToArray();
            foreach (var line in seq)
                for (var k = 1; k < line.Length; k++)
                {
                    machinePred[line[k]] = line[k - 1];
                    machineSucc[line[k - 1]] = line[k];
                }

            int JobPred(int id) => _index[id] > 0 ? id - 1 : -1;
            int JobSucc(int id) => _index[id] + 1 < _instance.Jobs[_job[id]].Ops.Count ? id + 1 : -1;

            var indegree = new int[n];
            for (var id = 0; id < n; id++) indegree[id] = (JobPred(id) >= 0 ? 1 : 0) + (machinePred[id] >= 0 ? 1 : 0);
            var queue = new Queue<int>(Enumerable.Range(0, n).Where(id => indegree[id] == 0));
            var start = new int[n];
            var done = 0;
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                done++;
                var jp = JobPred(u);
                var mp = machinePred[u];
                start[u] = Math.Max(jp >= 0 ? start[jp] + _duration[jp] : 0, mp >= 0 ? start[mp] + _duration[mp] : 0);
                foreach (var v in new[] { JobSucc(u), machineSucc[u] })
                    if (v >= 0 && --indegree[v] == 0)
                        queue.Enqueue(v);
            }

            if (done < n) return null;

            var end = Enumerable.Range(0, n).Select(id => start[id] + _duration[id]).ToArray();
            var makespan = end.Max();
            var cur = Array.IndexOf(end, makespan);
            var path = new List<int> { cur };
            while (start[cur] > 0)
            {
                var mp = machinePred[cur];
                var jp = JobPred(cur);
                if (mp >= 0 && end[mp] == start[cur]) cur = mp;
                else if (jp >= 0 && end[jp] == start[cur]) cur = jp;
                else break;
                path.Add(cur);
            }

            path.Reverse();
            var schedule = Enumerable.Range(0, n).Select(id => new ScheduledOp(_job[id], _index[id], _machine[id], start[id], end[id])).ToList();
            return new Evaluation(makespan, schedule, path.Select(Ref).ToList(), path);
        }
    }
}
