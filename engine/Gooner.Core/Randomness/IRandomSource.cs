namespace Gooner.Core.Randomness;

// Where the app's dice come from. Injectable so a test - or the parity harness, replaying the
// draws the Python side made - can say exactly what comes up. NextDouble is random.uniform(0, 1),
// NextIndex(n) the index random.choice picks out of n.
public interface IRandomSource
{
    double NextDouble();

    int NextIndex(int count);
}

public sealed class SystemRandomSource(Random? random = null) : IRandomSource
{
    private readonly Random _random = random ?? Random.Shared;

    public double NextDouble() => _random.NextDouble();

    public int NextIndex(int count) => _random.Next(count);
}

public static class RandomSourceExtensions
{
    // random.choice(items).
    public static T Choice<T>(this IRandomSource random, IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(items);
        return items[random.NextIndex(items.Count)];
    }

    // random.uniform(a, b), computed the way CPython does: a + (b - a) * random().
    public static double Uniform(this IRandomSource random, double a, double b)
    {
        ArgumentNullException.ThrowIfNull(random);
        return a + (b - a) * random.NextDouble();
    }

    // random.randint(a, b), both ends included: a + randbelow(b - a + 1).
    public static int RandInt(this IRandomSource random, int a, int b)
    {
        ArgumentNullException.ThrowIfNull(random);
        return a + random.NextIndex(b - a + 1);
    }
}
