using Gooner.Core.Randomness;

namespace Gooner.Core.Tests.Support;

// The C# side of monkeypatching random.uniform / random.choice: fixed answers where a test needs
// them, a seeded generator for everything else.
internal sealed class ScriptedRandom(int seed = 0) : IRandomSource
{
    private readonly Random _fallback = new(seed);

    public Func<double>? Uniform { get; set; }

    public Func<int, int>? Choice { get; set; }

    public double NextDouble() => Uniform?.Invoke() ?? _fallback.NextDouble();

    public int NextIndex(int count) => Choice?.Invoke(count) ?? _fallback.Next(count);
}
