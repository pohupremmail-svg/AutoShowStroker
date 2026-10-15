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

    // random.choices(population, weights, k=1)[0], by CPython's algorithm: the weights summed up
    // one by one, a fraction of the total, and the first running sum past it.
    public static T Choices<T>(this IRandomSource random, IReadOnlyList<T> population, IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(weights);
        var cumulative = new double[weights.Count];
        for (var i = 0; i < weights.Count; i++)
            cumulative[i] = i == 0 ? weights[0] : cumulative[i - 1] + weights[i];
        var total = cumulative[^1] + 0.0;
        if (total <= 0.0)
            throw new ArgumentException("Total of weights must be greater than zero", nameof(weights));
        var x = random.NextDouble() * total;
        // bisect_right over cumulative[0 .. n-1)
        int lo = 0, hi = cumulative.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (x < cumulative[mid]) hi = mid;
            else lo = mid + 1;
        }
        return population[lo];
    }

    // random.randint(a, b), both ends included: a + randbelow(b - a + 1).
    public static int RandInt(this IRandomSource random, int a, int b)
    {
        ArgumentNullException.ThrowIfNull(random);
        return a + random.NextIndex(b - a + 1);
    }
}
