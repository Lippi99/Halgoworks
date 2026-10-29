# Halgoworks

Halgoworks is an interactive lab for five classic optimization problems. Each problem has its own page where real algorithms, written in C#, solve an instance step by step in the browser. Every decision is drawn, narrated in plain language and linked to the matching line of pseudo-code.

It's built for anyone who wants to *see* how these algorithms think: students, developers preparing for interviews, or engineers curious about what runs behind logistics, scheduling and cloud tools.

## The five problems

| Problem | Question | Algorithms in the lab | Complexity |
|---|---|---|---|
| **Vehicle routing** | Which routes should a fleet of trucks drive to serve every customer within truck capacity and delivery deadlines, at the lowest fuel cost? | Clarke–Wright savings, sweep, nearest neighbor, 2-opt polishing | NP-hard |
| **Knapsack** | Which items give the most value without going over a weight or budget limit? | Dynamic programming (exact), greedy by value density | NP-hard (pseudo-polynomial DP) |
| **Job-shop scheduling** | In what order should operations run on shared machines so all jobs finish as early as possible? | Giffler–Thompson with six dispatch rules (MWKR, SPT, LPT, LWKR, FCFS, EDD), tabu search on the critical path | Strongly NP-hard |
| **Bin packing** | How do you fit items of different sizes into as few containers as possible? | Next, first, best and worst fit; first fit decreasing and best fit decreasing; an exact branch and bound for the true optimum | NP-hard |
| **Maximum flow** | How much can flow from a source to a sink through a network with limited capacities? | Edmonds–Karp, Ford–Fulkerson (DFS), Dinic, with the minimum cut at the end | Polynomial |

## What each lab shows

- **A live visualization:** the routing map, the DP table, the Gantt chart, the bins and the pipe network.
- **3D views** (Three.js) for routing, knapsack and bin packing.
- **Narration** explaining what the algorithm just did and why.
- **Pseudo-code** with the current line highlighted, plus a variables panel that works like a debugger's watch window.
- **Playback controls:** play/pause, step forward and back, a timeline and speed. Space plays and pauses, the arrow keys step.
- **Comparison charts:** every algorithm run on the same instance, against lower bounds or known optima.
- **A long-form explanation:** the problem, its formal model, why it's hard, the data structures involved, the algorithms, variants and history.
- **Instances:** hand-picked examples (Fisher & Thompson's ft06 and the textbook max-flow network, for example) and a random generator.

One visual rule runs through every page: **amber marks what the algorithm is looking at right now**, in the picture, the narration and the pseudo-code alike.

The home page has a 3D hero: simulated annealing searching a cost landscape. The five verbs, *Route. Choose. Schedule. Pack. Flow.*, act as the navigation.

## How it works

Each algorithm runs to completion and records a list of **steps**. A step is an immutable snapshot of the state plus a title, a narration, the pseudo-code line and the variables to watch. The UI simply plays that list back. That's why you can step backwards, scrub the timeline and change speed freely.

```
Halgoworks.Core (algorithms)  ──►  IReadOnlyList<Step>  ──►  Blazor UI (SVG / Three.js) + Player
```

## Project structure

```
Halgoworks.sln
├── Halgoworks.Core/        Algorithms and step models, with no UI dependencies
│   ├── Common/             Step base type, AlgorithmInfo, formatting helpers
│   ├── Vrp/  Knapsack/  JobShop/  BinPacking/  MaxFlow/
│   │                       Models, instances and the tracing solvers
├── Halgoworks/             Blazor WebAssembly app (.NET 10)
│   ├── Pages/              One page per problem, plus Home
│   ├── Components/         Lab shell (playback, narration, pseudo-code), charts,
│   │                       and per-problem visuals and articles
│   ├── Services/           Player (playback timer), palette, narration markup
│   └── wwwroot/            CSS design system, js/scenes.js (3D), lib/three (vendored)
└── Halgoworks.Tests/       xUnit tests for the algorithms
```

## Running it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project Halgoworks
```

Then open http://localhost:5273. For live reload while editing, use `dotnet watch --project Halgoworks`.

To run the tests:

```bash
dotnet test
```

## Correctness

The tests check the algorithms against brute force wherever that's feasible:

- **Knapsack:** dynamic programming matches an exhaustive search over every subset.
- **Bin packing:** the exact search matches an enumeration of every partition, and every heuristic is checked for overflow and for placing each item exactly once.
- **Maximum flow:** all three algorithms reach the brute-force minimum cut, respect capacities and conservation, and end with a saturated cut whose capacity equals the flow.
- **Job shop:** the small 3×3 instance's optimum (11) is confirmed by enumerating every machine order. Every schedule is validated, and every critical path is checked to be a real chain from time 0 to the makespan. Tabu search reaches ft06's proven optimum of 55.
- **Vehicle routing:** every algorithm serves each customer once, respects capacity and deadlines, and 2-opt never makes a route longer.

## Tech notes

- **Blazor WebAssembly:** the algorithms run in the browser, so playback is smooth and the site can be hosted as static files.
- **Rendering:** 2D visuals are plain SVG generated by Razor components. 3D uses Three.js through JS interop, vendored in `wwwroot/lib/three` so it works offline.
- **Design:** dark ink-navy surfaces with a single amber signal color. The chart palette is validated for color-blind safety and contrast, and the type is Barlow / Barlow Condensed with IBM Plex Mono for code.
- **Accessibility and layout:** keyboard shortcuts, `prefers-reduced-motion` support, and responsive layouts that work down to mobile.
