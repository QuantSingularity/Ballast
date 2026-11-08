using Ballast.AiModels.Numerics;

namespace Ballast.AiModels.Anomaly;

public sealed class IsolationForest(int trees = 100, int sampleSize = 256, int seed = 7)
{
    private sealed class Node
    {
        public int Feature;
        public double Split;
        public Node? Left;
        public Node? Right;
        public int Size;
    }

    private readonly List<Node> _trees = [];
    private int _psi;

    private static double C(int n)
    {
        if (n <= 1) return 0;
        if (n == 2) return 1;
        return 2.0 * (Math.Log(n - 1) + 0.5772156649) - 2.0 * (n - 1) / n;
    }

    public void Fit(IReadOnlyList<double[]> data)
    {
        if (data.Count < 8) throw new ArgumentException("Need at least 8 samples", nameof(data));
        var rng = new Rng(seed);
        _trees.Clear();
        _psi = Math.Min(sampleSize, data.Count);
        int limit = (int)Math.Ceiling(Math.Log2(_psi));
        for (int t = 0; t < trees; t++)
        {
            var idx = new int[_psi];
            for (int i = 0; i < _psi; i++) idx[i] = rng.NextInt(data.Count);
            _trees.Add(Build(data, idx.ToList(), 0, limit, rng));
        }
    }

    private static Node Build(IReadOnlyList<double[]> data, List<int> idx, int depth, int limit, Rng rng)
    {
        if (depth >= limit || idx.Count <= 1) return new Node { Size = idx.Count };
        int dims = data[0].Length;
        for (int attempt = 0; attempt < dims; attempt++)
        {
            int f = rng.NextInt(dims);
            double min = double.MaxValue, max = double.MinValue;
            foreach (int i in idx) { double v = data[i][f]; if (v < min) min = v; if (v > max) max = v; }
            if (max - min < 1e-12) continue;
            double split = min + rng.NextDouble() * (max - min);
            var left = new List<int>();
            var right = new List<int>();
            foreach (int i in idx) (data[i][f] < split ? left : right).Add(i);
            if (left.Count == 0 || right.Count == 0) continue;
            return new Node
            {
                Feature = f,
                Split = split,
                Size = idx.Count,
                Left = Build(data, left, depth + 1, limit, rng),
                Right = Build(data, right, depth + 1, limit, rng)
            };
        }
        return new Node { Size = idx.Count };
    }

    private static double PathLength(Node node, double[] x)
    {
        int depth = 0;
        while (node.Left is not null && node.Right is not null)
        {
            node = x[node.Feature] < node.Split ? node.Left : node.Right;
            depth++;
        }
        return depth + C(node.Size);
    }

    public double Score(double[] x)
    {
        if (_trees.Count == 0) throw new InvalidOperationException("Model is not fitted");
        double avg = _trees.Average(t => PathLength(t, x));
        return Math.Pow(2.0, -avg / C(_psi));
    }
}
