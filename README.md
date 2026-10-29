# Halgoworks

Halgoworks is an interactive lab for six classic optimization problems. Each problem has its own page where real algorithms, written in C#, solve an instance step by step in the browser. Every decision is drawn, narrated in plain language and linked to the matching line of pseudo-code.

It's built for anyone who wants to *see* how these algorithms think: students, developers preparing for interviews, or engineers curious about what runs behind logistics, scheduling and cloud tools.

## The six problems

| Problem | Question | Algorithms in the lab | Complexity |
|---|---|---|---|
| **Vehicle routing** | Which routes should a fleet of trucks drive to serve every customer within truck capacity and delivery deadlines, at the lowest fuel cost? | Clarke–Wright savings, sweep, nearest neighbor, 2-opt polishing, simulated annealing, genetic algorithm | NP-hard |
| **Knapsack** | Which items give the most value without going over a weight or budget limit? | Dynamic programming, branch and bound with a search tree, integer programming (LP relaxation + branch and bound), greedy by value density | NP-hard (pseudo-polynomial DP) |
| **Job-shop scheduling** | In what order should operations run on shared machines so all jobs finish as early as possible? | Giffler–Thompson with six dispatch rules (MWKR, SPT, LPT, LWKR, FCFS, EDD), tabu search on the critical path | Strongly NP-hard |
| **Bin packing** | How do you fit items of different sizes into as few containers as possible? | Next, first, best and worst fit; first fit decreasing and best fit decreasing; an exact branch and bound for the true optimum; integer programming | NP-hard |
| **Maximum flow** | How much can flow from a source to a sink through a network with limited capacities? | Edmonds–Karp, Ford–Fulkerson (DFS), Dinic, push–relabel (with a 3D heights view), with the minimum cut at the end | Polynomial |
| **Bipartite matching** | How many pairs can be formed between two groups, using only allowed pairs and nobody twice? | Reduction to max flow (Edmonds–Karp, Ford–Fulkerson, Dinic; push–relabel in Scale), greedy for comparison | Polynomial |

## What each lab shows

- **A live visualization:** the routing map, the DP table, the Gantt chart, the bins and the pipe network.
- **3D views** (Three.js) for routing, knapsack and bin packing.
- **Narration** explaining what the algorithm just did and why.
- **Pseudo-code** with the current line highlighted, plus a variables panel that works like a debugger's watch window.
- **Playback controls:** play/pause, step forward and back, a timeline and speed. Space plays and pauses, the arrow keys step.
- **Comparison charts:** every algorithm run on the same instance, against lower bounds or known optima.
- **A long-form explanation:** the problem, its formal model, why it's hard, the data structures involved, the algorithms, variants and history.
- **Instances:** hand-picked examples (Fisher & Thompson's ft06 and the textbook max-flow network, for example) and a random generator.

## Four ways to use a lab

Every lab has a **Mode** switch:

- **Watch** steps through one algorithm, as described above. Tick **Edit** to change the instance on the canvas: drag customers and network nodes, add or delete them, click a capacity to change it, or edit item weights, bin sizes and job routes. The algorithm re-runs as you edit.
- **Race** runs two or three algorithms side by side on one timeline, each with its own picture, narration and live score. It shows who finishes in the fewest steps and who reaches the best result, which are often not the same.
- **Play** (routing, knapsack, job shop and bin packing) lets you solve the instance yourself, then compares your answer with every algorithm, the lower bound and the optimum where it's known. Illegal moves are refused with the reason: a full truck, a missed deadline, an overfull bin, or a machine order that would deadlock.
- **Scale** generates a much bigger instance and times every algorithm on it: up to 200 customers, 200 knapsack items, 300 items to pack into bins, 20 × 10 job shops, networks of 160+ nodes, or 300 + 300 people to match. Iterative searches get a convergence chart.

**Share links:** "Copy link" encodes the instance, algorithm, step, view and mode (including your moves in Play, the racers in Race, and the size and seed in Scale) into the URL, so whoever opens it sees exactly the same thing.

## Real streets for routing

The routing lab's **Map** menu also offers real cities: Berlin, Paris, Madrid, Lisbon and São Paulo. Customers are placed inside a box around the city center and drawn on an OpenStreetMap map (Leaflet).

- **Road distances** come from the public [OSRM](https://project-osrm.org/) demo server: one distance table per map, averaged over both directions because the algorithms assume symmetric distances. Every algorithm then optimizes real driving distance.
- **Finished routes** are drawn along the actual streets, one route request each.
- **Fallback:** if OSRM is unreachable, busy or slow, the lab says so, uses straight-line distances instead, and offers to try again.
- **Fair use:** requests are queued at most one per second, as the demo server's policy asks, and cached for the session. Clicking the map adds a customer and fetches a new table.

The Grid and 3D views still work for real cities; share links carry the road distances with them.

One visual rule runs through every page: **amber marks what the algorithm is looking at right now**, in the picture, the narration and the pseudo-code alike.

The home page has a 3D hero: simulated annealing searching a cost landscape. The six verbs, *Route. Choose. Schedule. Pack. Flow. Match.*, act as the navigation.

## How it works

Each algorithm runs to completion and records a list of **steps**. A step is an immutable snapshot of the state plus a title, a narration, the pseudo-code line and the variables to watch. The UI simply plays that list back. That's why you can step backwards, scrub the timeline and change speed freely.

```
Halgoworks.Core (algorithms)  ──►  IReadOnlyList<Step>  ──►  Blazor UI (SVG / Three.js) + Player
```

Recording narration costs time and memory, so every algorithm also has an untraced `Solve` path. Scale mode uses those, together with convergence histories from the iterative searches. In Scale mode, annealing and the genetic algorithm start from the savings plan, and the annealing moves are *granular*: a customer only moves next to one of its nearest neighbors, which keeps big instances tractable.

## Project structure

```
Halgoworks.sln
├── Halgoworks.Core/        Algorithms and step models, with no UI dependencies
│   ├── Common/             Step base type, AlgorithmInfo, formatting helpers
│   ├── Vrp/  Knapsack/  JobShop/  BinPacking/  MaxFlow/  Matching/  Ip/
│   │                       Models, instances and the tracing solvers
├── Halgoworks/             Blazor WebAssembly app (.NET 10)
│   ├── Pages/              One page per problem, plus Home
│   ├── Components/         Lab shell (playback, narration, pseudo-code), charts,
│   │                       and per-problem visuals and articles
│   ├── Services/           Player (playback timer), palette, narration markup,
│   │                       share-link codec, OSRM road distances
│   └── wwwroot/            CSS design system, js/scenes.js (3D), js/drag.js (pointer
│                           editing), js/realmap.js, lib/three and lib/leaflet (vendored)
├── Halgoworks.Tests/       xUnit tests for the algorithms
└── tools/                  Build helpers: pre-rendering and link-preview images (Node)
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

## Deploying

The site is static files. Publish it, then pre-render it for search engines and link previews:

```bash
dotnet publish Halgoworks -c Release -o publish
cd tools
npm install
SITE_URL=https://your.domain npm run prerender
```

Upload `publish/wwwroot` to any static host. GitHub Pages, Netlify and Cloudflare Pages work as they are: they serve `/knapsack` from `knapsack.html` and unknown paths from `404.html` with a real 404 status.

**Why pre-render?** Blazor WebAssembly builds pages in the browser, so without this step every URL would serve the same nearly empty `index.html`. Crawlers that don't run JavaScript (Bing, DuckDuckGo, and the link previews in Slack, WhatsApp, LinkedIn and X) would see nothing. The script (`tools/prerender.mjs`):

- Opens each page in a headless browser (Edge or Chrome; set `BROWSER_CHANNEL` to choose).
- Saves the rendered HTML as a static file. Blazor still starts on top and takes over.
- Writes `sitemap.xml`, `robots.txt` and `404.html`, plus fresh `.br` and `.gz` copies of everything it writes.

Every page also sets its own:

- title and meta description;
- canonical URL (share links with query strings point back to the clean page);
- Open Graph and Twitter card tags, with 1200 × 630 preview images in `wwwroot/og`;
- schema.org JSON-LD (`WebSite` and `ItemList` on the home page, `LearningResource` on each lab).

These come from the `SeoHead` component and the SEO fields in `Services/Problems.cs`. To refresh the preview images, run the site and then `npm run og-images` in `tools`.

## Correctness

The tests check the algorithms against brute force wherever that's feasible:

- **Knapsack:** dynamic programming matches an exhaustive search over every subset.
- **Bin packing:** the exact search matches an enumeration of every partition, and every heuristic is checked for overflow and for placing each item exactly once.
- **Maximum flow:** all four algorithms reach the brute-force minimum cut and end with a saturated cut whose capacity equals the flow. Path-based steps conserve flow; push–relabel steps are valid preflows.
- **Matching:** every flow algorithm finds the brute-force maximum matching, and each augmenting path adds exactly one pair.
- **Integer programming:** the simplex solver matches textbook LPs (including infeasible and unbounded ones), and branch and bound matches dynamic programming and the exact bin-packing search.
- **Job shop:** the small 3×3 instance's optimum (11) is confirmed by enumerating every machine order. Every schedule is validated, and every critical path is checked to be a real chain from time 0 to the makespan. Tabu search reaches ft06's proven optimum of 55.
- **Vehicle routing:** every algorithm serves each customer once, respects capacity and deadlines, and 2-opt never makes a route longer. Annealing and the genetic algorithm never end worse than their start.
- **Scale mode:** the large generators produce valid instances, untraced branch and bound matches dynamic programming, and every flow algorithm ends with a real flow (conservation at every node) even on networks with hundreds of nodes.
- **Sharing:** every instance type survives an encode/decode round trip, and malformed or oversized links are rejected with a reason.

## Tech notes

- **Blazor WebAssembly:** the algorithms run in the browser, so playback is smooth and the site can be hosted as static files.
- **Rendering:** 2D visuals are plain SVG generated by Razor components. 3D uses Three.js through JS interop, vendored in `wwwroot/lib/three` so it works offline. Street maps use Leaflet (vendored in `wwwroot/lib/leaflet`) with OpenStreetMap tiles.
- **Network:** the app is static files plus two optional outside services for the real-city maps: OpenStreetMap tiles and the OSRM demo server. Everything else works offline.
- **Design:** dark ink-navy surfaces with a single amber signal color. The chart palette is validated for color-blind safety and contrast, and the type is Barlow / Barlow Condensed with IBM Plex Mono for code.
- **Accessibility and layout:** keyboard shortcuts, `prefers-reduced-motion` support, and responsive layouts that work down to mobile.
