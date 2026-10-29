namespace Halgoworks.Core.MaxFlow;

public static class FlowNetworks
{
    /// <summary>The network from Cormen et al., Introduction to Algorithms, figure 26.1. Maximum flow 23.</summary>
    public static FlowNetwork Textbook() => new("Textbook network",
    [
        new(0, "s", 0.00, 0.50),
        new(1, "v1", 0.32, 0.10),
        new(2, "v2", 0.32, 0.90),
        new(3, "v3", 0.68, 0.10),
        new(4, "v4", 0.68, 0.90),
        new(5, "t", 1.00, 0.50),
    ],
    [
        new(0, 0, 1, 16),
        new(1, 0, 2, 13),
        new(2, 2, 1, 4),
        new(3, 1, 3, 12),
        new(4, 3, 2, 9),
        new(5, 2, 4, 14),
        new(6, 4, 3, 7),
        new(7, 3, 5, 20),
        new(8, 4, 5, 4),
    ], 0, 5);

    /// <summary>A water network whose bottleneck is the pipes feeding the towers. Maximum flow 20.</summary>
    public static FlowNetwork WaterSupply() => new("City water supply",
    [
        new(0, "Reservoir", 0.00, 0.50),
        new(1, "Pump A", 0.27, 0.10),
        new(2, "Pump B", 0.27, 0.90),
        new(3, "Junction", 0.50, 0.50),
        new(4, "Tower N", 0.74, 0.10),
        new(5, "Tower S", 0.74, 0.90),
        new(6, "City", 1.00, 0.50),
    ],
    [
        new(0, 0, 1, 15),
        new(1, 0, 2, 10),
        new(2, 1, 2, 3),
        new(3, 1, 3, 6),
        new(4, 2, 3, 7),
        new(5, 1, 4, 5),
        new(6, 2, 5, 4),
        new(7, 3, 4, 5),
        new(8, 3, 5, 6),
        new(9, 4, 6, 14),
        new(10, 5, 6, 12),
    ], 0, 6);

    /// <summary>A random layered network: source, a few middle layers, sink.</summary>
    public static FlowNetwork Random(int middleLayers, int seed)
    {
        var rng = new Random(seed);
        var layers = new List<List<int>>();
        var nodes = new List<FlowNode>();
        var edges = new List<FlowEdge>();
        var letter = 'a';

        void AddLayer(int count, double x)
        {
            var layer = new List<int>();
            for (var k = 0; k < count; k++)
            {
                var y = count == 1 ? 0.5 : 0.08 + 0.84 * k / (count - 1);
                var label = x == 0 ? "s" : x >= 1 ? "t" : (letter++).ToString();
                nodes.Add(new FlowNode(nodes.Count, label, x, y));
                layer.Add(nodes.Count - 1);
            }

            layers.Add(layer);
        }

        AddLayer(1, 0);
        for (var l = 0; l < middleLayers; l++) AddLayer(rng.Next(2, 4), (l + 1.0) / (middleLayers + 1));
        AddLayer(1, 1);

        var exists = new HashSet<(int, int)>();
        void Connect(int from, int to, int min, int max)
        {
            if (from == to || exists.Contains((from, to)) || exists.Contains((to, from))) return;
            exists.Add((from, to));
            edges.Add(new FlowEdge(edges.Count, from, to, rng.Next(min, max + 1)));
        }

        for (var l = 0; l + 1 < layers.Count; l++)
        {
            var here = layers[l];
            var next = layers[l + 1];
            // Every node gets at least one way forward and one way in.
            foreach (var u in here) Connect(u, next[rng.Next(next.Count)], 3, 16);
            foreach (var v in next)
                if (!edges.Any(e => e.To == v))
                    Connect(here[rng.Next(here.Count)], v, 3, 16);
            foreach (var u in here)
            foreach (var v in next)
                if (rng.NextDouble() < 0.35)
                    Connect(u, v, 2, 14);
        }

        // A few vertical edges inside middle layers give the residual graph something to reroute through.
        for (var l = 1; l + 1 < layers.Count; l++)
        {
            var layer = layers[l];
            for (var k = 0; k + 1 < layer.Count; k++)
            {
                if (rng.NextDouble() >= 0.4) continue;
                if (rng.Next(2) == 0) Connect(layer[k], layer[k + 1], 2, 8);
                else Connect(layer[k + 1], layer[k], 2, 8);
            }
        }

        return new FlowNetwork($"Random #{seed}", nodes, edges, 0, nodes.Count - 1);
    }
}
