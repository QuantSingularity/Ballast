namespace Ballast.Core;

/// <summary>Seeded random numbers for reproducible demo data. Not for security purposes.</summary>
public sealed class DeterministicRng(int seed)
{
    private readonly Random _random = new(seed);
    private double? _spare;

    public double NextDouble() => _random.NextDouble();

    public int NextInt(int maxExclusive) => _random.Next(maxExclusive);

    public double NextNormal()
    {
        if (_spare is double s) { _spare = null; return s; }
        double u1 = 1.0 - _random.NextDouble();
        double u2 = _random.NextDouble();
        double mag = Math.Sqrt(-2.0 * Math.Log(u1));
        _spare = mag * Math.Sin(2.0 * Math.PI * u2);
        return mag * Math.Cos(2.0 * Math.PI * u2);
    }
}
