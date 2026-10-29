namespace Halgoworks.Core.MaxFlow;

public sealed record MaxFlowResult(int Total, IReadOnlyList<int> Flow, IReadOnlyList<bool> SourceSide, int Augmentations);

public static class MaxFlowSolver
{
    public static readonly AlgorithmInfo EdmondsKarpInfo = new(
        "ek", "Edmonds–Karp", AlgorithmKind.Exact,
        "Ford–Fulkerson with breadth-first search: always augments along a shortest path, which bounds the number of rounds.",
        "O(V·E²)", "O(V + E)",
        [
            "flow ← 0 on every edge",
            "while true:",
            "  breadth-first search from s in the residual graph",
            "  if t was not reached: stop",
            "  path ← follow the search's parent arcs back from t",
            "  b ← smallest spare capacity on the path",
            "  for each arc on the path:",
            "    push b (forward: add flow, backward: cancel flow)",
            "  total ← total + b",
            "S ← nodes the last search reached;  cut ← edges from S to the rest",
            "return total",
        ]);

    public static readonly AlgorithmInfo FordFulkersonInfo = new(
        "ff", "Ford–Fulkerson (DFS)", AlgorithmKind.Exact,
        "The original method: any augmenting path will do. Depth-first search finds one quickly, but long detours can mean many rounds.",
        "O(E·f*) for integer capacities", "O(V + E)",
        [
            "flow ← 0 on every edge",
            "while a depth-first search finds a path s ⇝ t in the residual graph:",
            "  go as deep as possible along arcs with spare capacity",
            "  at a dead end, back up and try the next arc",
            "  b ← smallest spare capacity on the path",
            "  push b along the path (cancel flow on backward arcs)",
            "  total ← total + b",
            "S ← nodes the last search reached;  cut ← edges from S to the rest",
            "return total",
        ]);

    public static readonly AlgorithmInfo DinicInfo = new(
        "dinic", "Dinic", AlgorithmKind.Exact,
        "Works in phases: a BFS layers the graph by distance from s, then DFS saturates every shortest path at once (a blocking flow).",
        "O(V²·E)", "O(V + E)",
        [
            "flow ← 0 on every edge",
            "while BFS from s gives every node a level and t is reachable:",
            "  keep only arcs that go from level k to level k + 1",
            "  while DFS finds a path s ⇝ t in that level graph:",
            "    b ← smallest spare capacity on the path",
            "    push b along the path",
            "    skip arcs that are now full (dead ends are never retried)",
            "S ← nodes with a level;  cut ← edges from S to the rest",
            "return total",
        ]);

    public static readonly AlgorithmInfo PushRelabelInfo = new(
        "pr", "Push–relabel", AlgorithmKind.Exact,
        "No augmenting paths at all: nodes hold excess flow and push it downhill to lower neighbors, rising when they get stuck, until nothing more can reach the sink.",
        "O(V³) with a FIFO queue", "O(V + E)",
        [
            "height(s) ← number of nodes;  flood every edge out of s",
            "while some node u other than s and t holds excess:",
            "  if u has an arc with spare room to v, one level lower:",
            "    push min(excess(u), spare room) along it",
            "  else:",
            "    relabel: height(u) ← 1 + lowest neighbor reachable with spare room",
            "S ← nodes reachable from s;  cut ← edges from S to the rest",
            "return excess(t)",
        ]);

    public static AlgorithmInfo Info(FlowAlgorithm algorithm) => algorithm switch
    {
        FlowAlgorithm.PushRelabel => PushRelabelInfo,
        FlowAlgorithm.FordFulkerson => FordFulkersonInfo,
        FlowAlgorithm.Dinic => DinicInfo,
        _ => EdmondsKarpInfo,
    };

    public static IReadOnlyList<MaxFlowStep> Trace(FlowNetwork network, FlowAlgorithm algorithm) => Run(network, algorithm, record: true).Steps;

    /// <summary>Maximum flow by the given algorithm, without recording steps.</summary>
    public static MaxFlowResult Solve(FlowNetwork network, FlowAlgorithm algorithm)
    {
        var tracer = Run(network, algorithm, record: false);
        return tracer.Result;
    }

    private static Tracer Run(FlowNetwork network, FlowAlgorithm algorithm, bool record)
    {
        var tracer = new Tracer(network, record);
        switch (algorithm)
        {
            case FlowAlgorithm.FordFulkerson: tracer.FordFulkerson(); break;
            case FlowAlgorithm.Dinic: tracer.Dinic(); break;
            case FlowAlgorithm.PushRelabel: tracer.PushRelabel(); break;
            default: tracer.EdmondsKarp(); break;
        }

        return tracer;
    }

    /// <summary>Maximum flow by Edmonds–Karp, without recording steps.</summary>
    public static MaxFlowResult Solve(FlowNetwork network)
    {
        var g = new Residual(network);
        var total = 0;
        var rounds = 0;
        while (true)
        {
            var parent = new Arc?[network.Nodes.Count];
            var seen = new bool[network.Nodes.Count];
            var queue = new Queue<int>();
            queue.Enqueue(network.Source);
            seen[network.Source] = true;
            while (queue.Count > 0 && !seen[network.Sink])
            {
                var u = queue.Dequeue();
                foreach (var a in g.Out[u])
                {
                    var v = g.Head(a);
                    if (seen[v] || g.Spare(a) <= 0) continue;
                    seen[v] = true;
                    parent[v] = a;
                    queue.Enqueue(v);
                }
            }

            if (!seen[network.Sink]) return new MaxFlowResult(total, g.Flow.ToArray(), seen, rounds);

            var path = g.PathTo(parent, network.Source, network.Sink);
            var b = path.Min(g.Spare);
            foreach (var a in path) g.Push(a, b);
            total += b;
            rounds++;
        }
    }

    /// <summary>Edges crossing from the source side to the sink side of a cut.</summary>
    public static IReadOnlyList<FlowEdge> CutEdges(FlowNetwork network, IReadOnlyList<bool> sourceSide) =>
        network.Edges.Where(e => sourceSide[e.From] && !sourceSide[e.To]).ToList();

    private sealed class Residual
    {
        private readonly FlowNetwork _net;

        public Residual(FlowNetwork net)
        {
            _net = net;
            Flow = new int[net.Edges.Count];
            Out = Enumerable.Range(0, net.Nodes.Count).Select(_ => new List<Arc>()).ToArray();
            foreach (var e in net.Edges)
            {
                Out[e.From].Add(new Arc(e.Id, true));
                Out[e.To].Add(new Arc(e.Id, false));
            }
        }

        public int[] Flow { get; }

        /// <summary>Residual arcs leaving each node, in edge order.</summary>
        public List<Arc>[] Out { get; }

        public int Spare(Arc a) => a.Forward ? _net.Edges[a.Edge].Capacity - Flow[a.Edge] : Flow[a.Edge];

        public int Head(Arc a) => a.Forward ? _net.Edges[a.Edge].To : _net.Edges[a.Edge].From;

        public int Tail(Arc a) => a.Forward ? _net.Edges[a.Edge].From : _net.Edges[a.Edge].To;

        public void Push(Arc a, int amount)
        {
            if (a.Forward) Flow[a.Edge] += amount;
            else Flow[a.Edge] -= amount;
        }

        public List<Arc> PathTo(Arc?[] parent, int s, int t)
        {
            var path = new List<Arc>();
            for (var v = t; v != s; v = Tail(parent[v]!.Value)) path.Add(parent[v]!.Value);
            path.Reverse();
            return path;
        }
    }

    /// <summary>Runs one algorithm and records every step.</summary>
    private sealed class Tracer(FlowNetwork net, bool record)
    {
        private readonly Residual _g = new(net);
        private readonly List<MaxFlowStep> _steps = [];
        private readonly int _s = net.Source;
        private readonly int _t = net.Sink;
        private int _total;
        private int _augmentations;

        private string L(int v) => net.Nodes[v].Label;

        private string ArcName(Arc a) => $"{L(_g.Tail(a))}→{L(_g.Head(a))}";

        private string EdgeName(FlowEdge e) => $"{L(e.From)}→{L(e.To)}";

        private string PathName(IReadOnlyList<Arc> path) =>
            string.Join(" → ", new[] { L(_s) }.Concat(path.Select(a => L(_g.Head(a)))));

        private MaxFlowStep Snap(MaxFlowStep step) => step with
        {
            Flow = _g.Flow.ToArray(),
            Total = _total,
            Augmentations = _augmentations,
            Nodes = step.Nodes.Count == 0 ? new NodeState[net.Nodes.Count] : step.Nodes,
        };

        private bool[] _sourceSide = [];

        public IReadOnlyList<MaxFlowStep> Steps => _steps;

        public MaxFlowResult Result => new(_total, _g.Flow.ToArray(), _sourceSide, _augmentations);

        private void Add(MaxFlowStep step)
        {
            if (record) _steps.Add(Snap(step));
        }

        private void Start(string how) => Add(StartStep(how));

        private MaxFlowStep StartStep(string how) => new()
        {
            Title = "Every pipe starts empty",
            Text = $"Each edge is labelled flow / capacity. {how} The **residual graph** says where more can go: a forward arc has capacity − flow to spare, and a backward arc can cancel flow that was already sent.",
            Line = 0,
            Phase = "Setup",
            Watches = [Watch.Of("nodes", net.Nodes.Count), Watch.Of("edges", net.Edges.Count), Watch.Of("total", 0)],
        };

        public IReadOnlyList<MaxFlowStep> EdmondsKarp()
        {
            Start($"Edmonds–Karp repeatedly searches for a path from =={L(_s)}== to =={L(_t)}== with spare capacity and pushes as much as that path allows.");
            var round = 0;
            while (true)
            {
                round++;
                var phase = $"Search {round}";
                var n = net.Nodes.Count;
                var state = new NodeState[n];
                var parent = new Arc?[n];
                var queue = new Queue<int>();
                var tree = new List<Arc>();
                queue.Enqueue(_s);
                state[_s] = NodeState.Frontier;
                var found = false;

                while (queue.Count > 0 && !found)
                {
                    var u = queue.Dequeue();
                    state[u] = NodeState.Current;
                    var discovered = new List<Arc>();
                    var full = new List<Arc>();
                    foreach (var a in _g.Out[u])
                    {
                        var v = _g.Head(a);
                        if (state[v] != NodeState.Idle) continue;
                        if (_g.Spare(a) <= 0)
                        {
                            if (a.Forward) full.Add(a);
                            continue;
                        }

                        parent[v] = a;
                        state[v] = NodeState.Frontier;
                        queue.Enqueue(v);
                        discovered.Add(a);
                        tree.Add(a);
                        if (v == _t)
                        {
                            found = true;
                            break;
                        }
                    }

                    // Narration is only built when steps are recorded; untraced runs (Scale mode) skip it.
                    if (record)
                    {
                        var intro = u == _s && round == 1
                            ? "Breadth-first search explores the residual graph one layer at a time, so the first path it finds uses the fewest edges. "
                            : "";
                        var news = discovered.Count == 0
                            ? $"From =={L(u)}== there is nowhere new to go."
                            : $"From =={L(u)}== it reaches {Num.List(discovered.Select(a => $"{L(_g.Head(a))} ({_g.Spare(a)} spare{(a.Forward ? "" : ", backward")})"))}.";
                        var blocked = full.Count == 0 ? "" : $" {Num.List(full.Select(ArcName))} {(full.Count == 1 ? "is" : "are")} full.";
                        var reached = found ? $" That includes =={L(_t)}==, so the search stops." : "";
                        Add(new MaxFlowStep
                        {
                            Title = $"Search: expand {L(u)}",
                            Text = intro + news + blocked + reached,
                            Line = 2,
                            Phase = phase,
                            Nodes = (NodeState[])state.Clone(),
                            Frontier = queue.ToList(),
                            Tree = tree.ToList(),
                            Scanned = discovered,
                            Watches = [Watch.Of("expand", L(u)), Watch.Of("queue", queue.Count == 0 ? "empty" : string.Join(", ", queue.Select(L))), Watch.Of("total", _total)],
                        });
                    }
                    state[u] = NodeState.Visited;
                }

                if (!found)
                {
                    Finish(state.Select(x => x != NodeState.Idle).ToArray(), "breadth-first search", 3, 9);
                    return _steps;
                }

                Augment(_g.PathTo(parent, _s, _t), state, tree, phase, pathLine: 5, pushLine: 7);
            }
        }

        public IReadOnlyList<MaxFlowStep> FordFulkerson()
        {
            Start($"Ford–Fulkerson looks for **any** path from =={L(_s)}== to =={L(_t)}== with spare capacity. This version uses depth-first search: it dives along the first open arc it sees and backs up at dead ends.");
            var round = 0;
            while (true)
            {
                round++;
                var phase = $"Search {round}";
                var n = net.Nodes.Count;
                var visited = new bool[n];
                var dead = new bool[n];
                var parent = new Arc?[n];
                var next = new int[n];
                var stack = new List<int> { _s };
                visited[_s] = true;
                var found = false;

                NodeState[] States()
                {
                    var st = new NodeState[n];
                    for (var v = 0; v < n; v++) st[v] = dead[v] ? NodeState.Visited : NodeState.Idle;
                    foreach (var v in stack) st[v] = NodeState.Frontier;
                    if (stack.Count > 0) st[stack[^1]] = NodeState.Current;
                    return st;
                }

                List<Arc> StackArcs() => stack.Skip(1).Select(v => parent[v]!.Value).ToList();

                while (stack.Count > 0)
                {
                    var u = stack[^1];
                    if (u == _t)
                    {
                        found = true;
                        break;
                    }

                    Arc? taken = null;
                    while (next[u] < _g.Out[u].Count)
                    {
                        var a = _g.Out[u][next[u]++];
                        var v = _g.Head(a);
                        if (visited[v] || _g.Spare(a) <= 0) continue;
                        visited[v] = true;
                        parent[v] = a;
                        stack.Add(v);
                        taken = a;
                        break;
                    }

                    if (taken is { } arc)
                    {
                        // Narration is only built when steps are recorded; untraced runs (Scale mode) skip it.
                        if (record)
                        {
                            var v = _g.Head(arc);
                            var kind = arc.Forward ? "" : ", a backward arc that would cancel flow";
                            Add(new MaxFlowStep
                            {
                                Title = $"Go deeper: {ArcName(arc)}",
                                Text = $"From {L(u)}, the first unexplored arc with spare capacity is =={ArcName(arc)}== ({_g.Spare(arc)} spare{kind}).{(v == _t ? $" It reaches {L(_t)}: path found." : "")}",
                                Line = 2,
                                Phase = phase,
                                Nodes = States(),
                                Frontier = stack.AsEnumerable().Reverse().ToList(),
                                Tree = StackArcs(),
                                Scanned = [arc],
                                Watches = [Watch.Of("at", L(v)), Watch.Of("depth", stack.Count - 1), Watch.Of("total", _total)],
                            });
                        }
                    }
                    else
                    {
                        stack.RemoveAt(stack.Count - 1);
                        dead[u] = true;
                        // Narration is only built when steps are recorded; untraced runs (Scale mode) skip it.
                        if (record)
                        {
                            var back = stack.Count > 0
                                ? $"Back up to {L(stack[^1])} and try its next arc."
                                : $"The search is back at {L(_s)} with nothing left to try.";
                            Add(new MaxFlowStep
                            {
                                Title = $"Dead end at {L(u)}",
                                Text = $"Every arc out of =={L(u)}== is full or leads somewhere already explored. {back}",
                                Line = 3,
                                Phase = phase,
                                Nodes = States(),
                                Frontier = stack.AsEnumerable().Reverse().ToList(),
                                Tree = StackArcs(),
                                Watches = [Watch.Of("dead end", L(u)), Watch.Of("depth", Math.Max(0, stack.Count - 1)), Watch.Of("total", _total)],
                            });
                        }
                    }
                }

                if (!found)
                {
                    Finish(visited, "depth-first search", 1, 7);
                    return _steps;
                }

                Augment(StackArcs(), record ? States() : [], record ? StackArcs() : [], phase, pathLine: 4, pushLine: 5);
            }
        }

        public IReadOnlyList<MaxFlowStep> Dinic()
        {
            Start($"Dinic works in phases. Each phase labels nodes by their distance from =={L(_s)}==, then pushes flow along **all** the shortest paths before measuring distances again.");
            var phaseNo = 0;
            var n = net.Nodes.Count;
            while (true)
            {
                phaseNo++;
                var phase = $"Phase {phaseNo}";
                var level = Enumerable.Repeat(-1, n).ToArray();
                var queue = new Queue<int>();
                level[_s] = 0;
                queue.Enqueue(_s);
                var levelArcs = new List<Arc>();
                while (queue.Count > 0)
                {
                    var u = queue.Dequeue();
                    foreach (var a in _g.Out[u])
                    {
                        var v = _g.Head(a);
                        if (_g.Spare(a) <= 0) continue;
                        if (level[v] < 0)
                        {
                            level[v] = level[u] + 1;
                            queue.Enqueue(v);
                        }

                        if (level[v] == level[u] + 1) levelArcs.Add(a);
                    }
                }

                var reachable = level.Select(l => l >= 0).ToArray();
                var states = reachable.Select(r => r ? NodeState.Visited : NodeState.Idle).ToArray();
                if (level[_t] < 0)
                {
                    Finish(reachable, "level search", 1, 7, level);
                    return _steps;
                }

                var byLevel = Enumerable.Range(0, n).Where(v => level[v] >= 0).GroupBy(v => level[v]).OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}: {string.Join(", ", g.Select(L))}");
                Add(new MaxFlowStep
                {
                    Title = $"Phase {phaseNo}: build the level graph",
                    Text = $"BFS gives each node its distance from {L(_s)} ({string.Join("; ", byLevel)}). Only arcs that step exactly one level deeper are kept, so every path in this phase is a shortest path. =={L(_t)}== is {level[_t]} steps away.",
                    Line = 2,
                    Phase = phase,
                    Nodes = states,
                    Tree = levelArcs,
                    Level = level.ToArray(),
                    Watches = [Watch.Of("phase", phaseNo), Watch.Of("dist(t)", level[_t]), Watch.Of("total", _total)],
                });

                var next = new int[n];
                var pushed = 0;
                while (true)
                {
                    var path = new List<Arc>();
                    if (!Advance(_s, level, next, path))
                    {
                        Add(new MaxFlowStep
                        {
                            Title = "Blocking flow reached",
                            Text = pushed == 0
                                ? "No path remains in this level graph."
                                : $"Every shortest path now crosses a full arc, so this phase is done after pushing {pushed} units. Distances to {L(_t)} can only grow from here: rebuild the levels.",
                            Line = 6,
                            Phase = phase,
                            Nodes = states,
                            Level = level.ToArray(),
                            Watches = [Watch.Of("phase", phaseNo), Watch.Of("pushed", pushed), Watch.Of("total", _total)],
                        });
                        break;
                    }

                    path.Reverse();
                    var before = _total;
                    Augment(path, states, levelArcs, phase, pathLine: 4, pushLine: 5, level);
                    pushed += _total - before;
                }
            }
        }

        public void PushRelabel()
        {
            var n = net.Nodes.Count;
            var height = new int[n];
            var excess = new int[n];
            var next = new int[n];
            var active = new Queue<int>();
            var queued = new bool[n];

            NodeState[] States(int? current)
            {
                var st = new NodeState[n];
                foreach (var v in active) st[v] = NodeState.Frontier;
                if (current is { } c) st[c] = NodeState.Current;
                return st;
            }

            void Snapshot(MaxFlowStep step, int? current)
            {
                if (!record) return;
                Add(step with
                {
                    Nodes = States(current),
                    Frontier = active.ToList(),
                    Height = height.ToArray(),
                    Excess = excess.ToArray(),
                });
            }

            void Activate(int v)
            {
                if (v == _s || v == _t || excess[v] <= 0 || queued[v]) return;
                queued[v] = true;
                active.Enqueue(v);
            }

            Snapshot(StartStep($"Push–relabel never searches for whole paths. It treats each node as a reservoir at some **height**: flow only runs downhill, one level at a time, and a node that can't get rid of its excess is lifted up."), null);

            height[_s] = n;
            var flooded = new List<Arc>();
            foreach (var a in _g.Out[_s].Where(a => a.Forward))
            {
                var amount = _g.Spare(a);
                if (amount <= 0) continue;
                _g.Push(a, amount);
                excess[_s] -= amount;
                excess[_g.Head(a)] += amount;
                flooded.Add(a);
            }

            _total = excess[_t];
            foreach (var a in flooded) Activate(_g.Head(a));
            Snapshot(new MaxFlowStep
            {
                Title = $"Lift {L(_s)} and flood its pipes",
                Text = $"=={L(_s)}== is raised to height {n}, the number of nodes, so flow never comes back to it unless it truly can't reach {L(_t)}. Every pipe leaving it is filled to capacity: {Num.List(flooded.Select(a => $"{ArcName(a)} ({_g.Flow[a.Edge]})"))}. Those nodes now hold **excess** they must pass on. This isn't a valid flow yet; it's a *preflow*.",
                Line = 0,
                Phase = "Preflow",
                Scanned = flooded,
                Watches = [Watch.Of($"height({L(_s)})", n), Watch.Of("active nodes", active.Count), Watch.Of($"at {L(_t)}", excess[_t])],
            }, null);

            // FIFO push–relabel does O(n³) operations at most; the guard only protects against a bug looping forever.
            var limit = 4L * n * n * n + 1000;
            long guard = 0;
            int pushes = 0, relabels = 0;
            while (active.Count > 0 && guard++ < limit)
            {
                var u = active.Peek();
                Arc? admissible = null;
                while (next[u] < _g.Out[u].Count)
                {
                    var a = _g.Out[u][next[u]];
                    if (_g.Spare(a) > 0 && height[u] == height[_g.Head(a)] + 1)
                    {
                        admissible = a;
                        break;
                    }

                    next[u]++;
                }

                if (admissible is { } arc)
                {
                    var v = _g.Head(arc);
                    var amount = Math.Min(excess[u], _g.Spare(arc));
                    _g.Push(arc, amount);
                    pushes++;
                    excess[u] -= amount;
                    excess[v] += amount;
                    _total = excess[_t];
                    var empty = excess[u] == 0;
                    if (empty) queued[active.Dequeue()] = false;
                    Activate(v);
                    var where = v == _t
                        ? $" It reaches the sink: {_total} units have arrived."
                        : v == _s
                            ? $" It flows back to {L(_s)}: this excess can't reach {L(_t)}."
                            : "";
                    var direction = arc.Forward ? "" : ", cancelling flow sent earlier";
                    Snapshot(new MaxFlowStep
                    {
                        Title = $"Push {amount}: {ArcName(arc)}",
                        Text = $"=={L(u)}== (height {height[u]}) holds {excess[u] + amount} excess and {L(v)} sits one level lower (height {height[v]}). Push {amount} along {ArcName(arc)}{direction}.{where}{(empty ? $" {L(u)} is empty now." : "")}",
                        Line = 3,
                        Phase = "Push and relabel",
                        Scanned = [arc],
                        Pushed = amount,
                        Watches = [Watch.Of("node", L(u)), Watch.Of("height", height[u]), Watch.Of("excess left", excess[u]), Watch.Of($"at {L(_t)}", _total)],
                    }, u);
                    continue;
                }

                // No downhill arc with room: lift u just above its lowest neighbor it can still send to.
                var options = _g.Out[u].Where(a => _g.Spare(a) > 0).ToList();
                var lowest = options.MinBy(a => height[_g.Head(a)]);
                var old = height[u];
                height[u] = height[_g.Head(lowest)] + 1;
                relabels++;
                next[u] = 0;
                Snapshot(new MaxFlowStep
                {
                    Title = $"Relabel {L(u)}: height {old} → {height[u]}",
                    Text = $"=={L(u)}== still holds {excess[u]} units, but every arc it could use goes level or uphill. Lift it to {height[u]}, one above its lowest neighbor with spare room ({L(_g.Head(lowest))} at {height[_g.Head(lowest)]}), so the excess can flow there next.",
                    Line = 5,
                    Phase = "Push and relabel",
                    Watches = [Watch.Of("node", L(u)), Watch.Of("height", $"{old} → {height[u]}"), Watch.Of("excess", excess[u]), Watch.Of($"at {L(_t)}", _total)],
                }, u);
            }

            _total = excess[_t];
            var reached = new bool[n];
            var queue = new Queue<int>([_s]);
            reached[_s] = true;
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                foreach (var a in _g.Out[u])
                {
                    var v = _g.Head(a);
                    if (reached[v] || _g.Spare(a) <= 0) continue;
                    reached[v] = true;
                    queue.Enqueue(v);
                }
            }

            Finish(reached, "search from the source", 1, 6, height: height, excess: excess,
                stopText: $"No node holds excess any more: every unit that left {L(_s)} either reached =={L(_t)}== or flowed back. The preflow is now a real flow, and a final search from {L(_s)} confirms {L(_t)} is out of reach.",
                effort: $"It took {pushes} pushes and {relabels} relabels, and never searched for a path.");
        }

        // Dinic's DFS: follows level-increasing arcs, advancing each node's pointer past dead ends for good.
        private bool Advance(int u, int[] level, int[] next, List<Arc> path)
        {
            if (u == _t) return true;
            for (; next[u] < _g.Out[u].Count; next[u]++)
            {
                var a = _g.Out[u][next[u]];
                var v = _g.Head(a);
                if (_g.Spare(a) <= 0 || level[v] != level[u] + 1) continue;
                if (!Advance(v, level, next, path)) continue;
                path.Add(a);
                return true;
            }

            return false;
        }

        private void Augment(List<Arc> path, NodeState[] states, List<Arc> tree, string phase, int pathLine, int pushLine, int[]? level = null)
        {
            var spare = path.Select(_g.Spare).ToList();
            var b = spare.Min();
            var bottleneck = path[spare.IndexOf(b)];
            var backward = path.Where(a => !a.Forward).ToList();
            var backNote = backward.Count == 0
                ? ""
                : $" {Num.List(backward.Select(ArcName))} {(backward.Count == 1 ? "runs" : "run")} backwards against an edge: pushing there cancels flow sent earlier and reroutes it.";
            Add(new MaxFlowStep
            {
                Title = $"Augmenting path: {b} unit{(b == 1 ? "" : "s")}",
                Text = $"The path is {PathName(path)}. Spare capacity along it: {string.Join(", ", spare)}. The smallest, **{b}** on =={ArcName(bottleneck)}==, limits the whole path: it's the bottleneck.{backNote}",
                Line = pathLine,
                Phase = phase,
                Nodes = states,
                Tree = tree.ToList(),
                Path = path.ToList(),
                Bottleneck = bottleneck,
                Level = level?.ToArray(),
                Watches = [Watch.Of("path", PathName(path)), Watch.Of("bottleneck", b), Watch.Of("total", _total)],
            });

            foreach (var a in path) _g.Push(a, b);
            _total += b;
            _augmentations++;
            var saturated = path.Where(a => a.Forward && _g.Spare(a) == 0).Select(ArcName).ToList();
            var note = saturated.Count == 0 ? "" : $" {Num.List(saturated)} {(saturated.Count == 1 ? "is" : "are")} now full.";
            Add(new MaxFlowStep
            {
                Title = $"Push {b}: total flow {_total}",
                Text = $"Add {b} to every forward arc on the path and cancel {b} on every backward arc. Total flow out of {L(_s)} is now **{_total}**.{note}",
                Line = pushLine,
                Phase = phase,
                Nodes = states,
                Path = path.ToList(),
                Bottleneck = bottleneck,
                Pushed = b,
                Level = level?.ToArray(),
                Watches = [Watch.Of("pushed", b), Watch.Of("total", _total), Watch.Of("paths so far", _augmentations)],
            });
        }

        private void Finish(bool[] reached, string search, int stopLine, int cutLine, int[]? level = null, int[]? height = null, int[]? excess = null,
            string? stopText = null, string? effort = null)
        {
            _sourceSide = reached;
            var states = reached.Select(r => r ? NodeState.Visited : NodeState.Idle).ToArray();
            Add(new MaxFlowStep
            {
                Title = $"No path to {L(_t)}",
                Text = stopText ?? $"The {search} can't reach =={L(_t)}==: every route there crosses a pipe that is already full, or would need to cancel flow that isn't there. No augmenting path means the flow can't grow.",
                Line = stopLine,
                Phase = "Min cut",
                Nodes = states,
                Level = level?.ToArray(),
                Height = height?.ToArray(),
                Excess = excess?.ToArray(),
                Watches = [Watch.Of("reached", reached.Count(r => r)), Watch.Of("total", _total)],
            });

            var cut = CutEdges(net, reached);
            var side = string.Join(", ", Enumerable.Range(0, reached.Length).Where(v => reached[v]).Select(L));
            var cutText = Num.List(cut.Select(e => $"{EdgeName(e)} ({e.Capacity})"));
            Add(new MaxFlowStep
            {
                Title = $"Maximum flow = minimum cut = {_total}",
                Text = $"The nodes the last search reached, S = {{{side}}}, form one side of a **minimum cut**. The edges leaving S are {cutText}: all full, and their capacities add up to {cut.Sum(e => e.Capacity)}, exactly the flow. No flow can exceed the capacity of any cut, so **{_total}** is the maximum. {effort ?? $"It took {_augmentations} augmenting path{(_augmentations == 1 ? "" : "s")}."}",
                Line = cutLine,
                Phase = "Done",
                Nodes = states,
                SourceSide = reached,
                Level = level?.ToArray(),
                Height = height?.ToArray(),
                Excess = excess?.ToArray(),
                Watches = [Watch.Of("max flow", _total), Watch.Of("cut edges", cut.Count), Watch.Of("paths", _augmentations)],
            });
        }
    }
}
