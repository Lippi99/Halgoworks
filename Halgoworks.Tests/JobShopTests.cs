using Halgoworks.Core.JobShop;
using Xunit.Abstractions;

namespace Halgoworks.Tests;

public class JobShopTests(ITestOutputHelper output)
{
    public static TheoryData<int> Seeds => new() { 1, 2, 3, 5, 8, 13 };

    private static IEnumerable<JobShopInstance> Instances(int seed)
    {
        yield return JobShopInstances.Small();
        yield return JobShopInstances.Ft06();
        yield return JobShopInstances.Random(3 + seed % 5, 2 + seed % 5, seed);
    }

    // Best makespan over every combination of machine orders (semi-active schedules).
    private static int BruteForce(JobShopInstance instance)
    {
        var perMachine = Enumerable.Range(0, instance.Machines.Count)
            .Select(m => instance.Jobs.SelectMany(j => j.Ops).Where(o => o.Machine == m).ToList())
            .ToList();
        var orders = perMachine.Select(ops => Permutations(ops).ToList()).ToList();
        var best = int.MaxValue;

        void Choose(int m, List<List<Operation>> chosen)
        {
            if (m == orders.Count)
            {
                if (Simulate(instance, chosen) is { } makespan) best = Math.Min(best, makespan);
                return;
            }

            foreach (var order in orders[m])
            {
                chosen.Add(order);
                Choose(m + 1, chosen);
                chosen.RemoveAt(chosen.Count - 1);
            }
        }

        Choose(0, []);
        return best;
    }

    private static int? Simulate(JobShopInstance instance, List<List<Operation>> machineOrders)
    {
        var end = new Dictionary<Operation, int>();
        var progress = true;
        while (end.Count < instance.OperationCount && progress)
        {
            progress = false;
            foreach (var order in machineOrders)
            for (var k = 0; k < order.Count; k++)
            {
                var op = order[k];
                if (end.ContainsKey(op)) continue;
                var jobReady = op.Index == 0 ? 0 : end.GetValueOrDefault(instance.Jobs[op.Job].Ops[op.Index - 1], -1);
                var machineReady = k == 0 ? 0 : end.GetValueOrDefault(order[k - 1], -1);
                if (jobReady < 0 || machineReady < 0) continue;
                end[op] = Math.Max(jobReady, machineReady) + op.Duration;
                progress = true;
            }
        }

        return end.Count == instance.OperationCount ? end.Values.Max() : null;
    }

    private static IEnumerable<List<T>> Permutations<T>(List<T> items)
    {
        if (items.Count <= 1)
        {
            yield return items.ToList();
            yield break;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, k) => k != i).ToList();
            foreach (var p in Permutations(rest)) yield return [items[i], .. p];
        }
    }

    private static void AssertCriticalPath(IReadOnlyList<ScheduledOp> schedule, IReadOnlyList<OpRef> path)
    {
        var byRef = schedule.ToDictionary(o => new OpRef(o.Job, o.Index));
        var ops = path.Select(r => byRef[r]).ToList();
        Assert.Equal(0, ops[0].Start);
        Assert.Equal(JobShopSolver.Makespan(schedule), ops[^1].End);
        for (var k = 1; k < ops.Count; k++)
        {
            Assert.Equal(ops[k - 1].End, ops[k].Start);
            Assert.True(ops[k - 1].Job == ops[k].Job || ops[k - 1].Machine == ops[k].Machine);
        }
    }

    [Fact]
    public void Small_instance_optimum_is_eleven()
    {
        Assert.Equal(11, BruteForce(JobShopInstances.Small()));
    }

    [Fact]
    public void Ft06_lower_bound_is_the_longest_job()
    {
        Assert.Equal(47, JobShopInstances.Ft06().LowerBound);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_rule_builds_a_valid_schedule_with_a_real_critical_path(int seed)
    {
        foreach (var instance in Instances(seed))
        foreach (var rule in JobShopSolver.Rules)
        {
            var schedule = JobShopSolver.Solve(instance, rule);
            Assert.Null(JobShopSolver.Validate(instance, schedule));
            Assert.True(JobShopSolver.Makespan(schedule) >= instance.LowerBound);

            var last = JobShopSolver.TraceDispatch(instance, rule)[^1];
            Assert.Equal(schedule.OrderBy(o => o.Job).ThenBy(o => o.Index), last.Scheduled.OrderBy(o => o.Job).ThenBy(o => o.Index));
            AssertCriticalPath(last.Scheduled, last.Critical);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Tabu_search_never_ends_worse_than_it_started(int seed)
    {
        foreach (var instance in Instances(seed))
        {
            var steps = JobShopSolver.TraceTabu(instance);
            var first = steps[0];
            var last = steps[^1];

            Assert.Null(JobShopSolver.Validate(instance, last.Scheduled));
            Assert.True(last.Makespan <= first.Makespan);
            Assert.True(last.Makespan >= instance.LowerBound);
            if (instance.Optimum is { } optimum) Assert.True(last.Makespan >= optimum);
            AssertCriticalPath(last.Scheduled, last.Critical);
            foreach (var step in steps) Assert.Null(JobShopSolver.Validate(instance, step.Scheduled));
            output.WriteLine($"{instance.Name}: {first.Makespan} → {last.Makespan} (LB {instance.LowerBound}, opt {instance.Optimum})");
        }
    }
}

public class JobShopOrderTests
{
    [Fact]
    public void Evaluating_a_rules_own_order_reproduces_its_makespan()
    {
        var instance = JobShopInstances.Ft06();
        foreach (var rule in JobShopSolver.Rules.Where(r => r != DispatchRule.Edd))
        {
            var schedule = JobShopSolver.Solve(instance, rule);
            var evaluated = JobShopSolver.EvaluateOrder(instance, JobShopSolver.OrderOf(instance, schedule));
            Assert.NotNull(evaluated);
            Assert.True(evaluated.Makespan <= JobShopSolver.Makespan(schedule));
            Assert.Null(JobShopSolver.Validate(instance, evaluated.Schedule));
        }
    }

    [Fact]
    public void The_plain_job_order_is_valid_and_contradictions_are_rejected()
    {
        var instance = JobShopInstances.Small();
        var plain = JobShopSolver.EvaluateOrder(instance, JobShopSolver.JobOrder(instance));
        Assert.NotNull(plain);
        Assert.Null(JobShopSolver.Validate(instance, plain.Schedule));

        // J1 runs M1 then M2, J2 runs M1 then M3 then M2. Putting J2 first on M2 but J1 first on M1 is fine;
        // a cycle needs two machines ordered against each other. Build one on ft06: J1 needs M3 before M1, J2 needs M2 before M3.
        var ft = JobShopInstances.Ft06();
        var order = JobShopSolver.JobOrder(ft).Select(o => o.ToList()).ToList();
        // On M2 (index 1) put J1's op before J2's first op, and on M3 (index 2) put J2's op before J1's first op:
        // J1·1 (M3) → J1·2 (M1) → J1·3 (M2) must precede J2·1 (M2) → J2·2 (M3), while J2·2 precedes J1·1 on M3: a cycle.
        order[2].Remove(new OpRef(1, 1));
        order[2].Insert(0, new OpRef(1, 1));
        Assert.Null(JobShopSolver.EvaluateOrder(ft, order));

        var wrong = JobShopSolver.JobOrder(ft).Select(o => o.ToList()).ToList();
        wrong[0].RemoveAt(0);
        Assert.Null(JobShopSolver.EvaluateOrder(ft, wrong));
    }
}
