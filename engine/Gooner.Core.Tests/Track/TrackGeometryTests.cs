using Gooner.Core.Track;

namespace Gooner.Core.Tests.Track;

// Where each note track draws a note that is still `ahead` of its beat - 1 at the far end of the
// lead time, 0 on the beat. The contract every track keeps: at 0 the note is on the hit zone,
// wherever the track has bent, swayed or turned to, so it lands exactly on its click (the click is
// mixed against the same moment the note is drawn from). The rest is how they get there.
public class TrackGeometryTests
{
    private const float W = 1600, H = 900;

    public static TheoryData<double> Moments => new() { 0.0, 1.7, 12.3, 3600.0 };

    // --- highway ---

    [Theory]
    [MemberData(nameof(Moments))]
    public void on_the_highway_a_note_on_its_beat_is_on_the_hit_line(double time)
    {
        var lane = new HighwayLane(W, H);

        var at = lane.At(0, time);

        Assert.Equal(lane.CenterX, at.X, 3);
        Assert.Equal(lane.HitY, at.Y, 3);
        Assert.Equal(1f, at.Scale, 5);
    }

    [Fact]
    public void the_highway_runs_from_the_hit_line_up_to_its_horizon_and_narrows()
    {
        var lane = new HighwayLane(W, H);
        var points = Enumerable.Range(0, 11).Select(i => lane.At(i / 10.0, 5.0)).ToList();

        Assert.All(points.Zip(points.Skip(1)), p => Assert.True(p.Second.Y < p.First.Y && p.Second.HalfWidth < p.First.HalfWidth));
        Assert.True(points[^1].Y > lane.Horizon);
    }

    // --- hypno ring ---

    [Theory]
    [MemberData(nameof(Moments))]
    public void on_the_hypno_ring_a_note_on_its_beat_is_on_the_ring(double time)
    {
        foreach (var wandering in new[] { false, true })
        {
            var ring = new HypnoRing(W, H, wandering);
            var centre = new TrackPoint(400, 300);
            foreach (var arm in new[] { 0, 1 })
            {
                var at = ring.At(0, arm, time, centre);

                Assert.Equal(ring.Radius, MathF.Sqrt(MathF.Pow(at.X - centre.X, 2) + MathF.Pow(at.Y - centre.Y, 2)), 2);
            }
        }
    }

    [Fact]
    public void the_ring_spirals_in_from_further_out()
    {
        var ring = new HypnoRing(W, H, wandering: false);
        var radii = Enumerable.Range(0, 11).Select(i => ring.RadiusAt(i / 10.0)).ToList();

        Assert.Equal(ring.Radius, radii[0]);
        Assert.All(radii.Zip(radii.Skip(1)), r => Assert.True(r.Second > r.First));
    }

    // Smaller when it wanders, so the spiral around it stays on the picture wherever it goes.
    [Fact]
    public void a_wandering_ring_is_smaller() =>
        Assert.True(new HypnoRing(W, H, wandering: true).RadiusAt(1) < new HypnoRing(W, H, wandering: false).RadiusAt(1));

    // --- stream ---

    [Fact]
    public void on_the_stream_a_note_on_its_beat_is_on_the_hit_zone_and_comes_from_the_right()
    {
        var lane = new StreamLane(W, H);

        Assert.Equal(lane.HitX, lane.X(0), 3);
        Assert.True(lane.X(1) > lane.X(0.5) && lane.X(0.5) > lane.HitX);
        Assert.True(lane.X(1) < W);
    }

    // --- tunnel ---

    [Theory]
    [MemberData(nameof(Moments))]
    public void in_the_tunnel_a_note_on_its_beat_is_on_the_frame(double time)
    {
        var tunnel = new TunnelLane(W, H);
        foreach (var beat in new[] { 0.5, 2.25, 100.0 })
        {
            var at = tunnel.At(0, beat, time);
            var dx = (at.X - tunnel.CenterX) / tunnel.Frame;
            var dy = (at.Y - tunnel.CenterY) / (tunnel.Frame * TunnelLane.Squash);

            Assert.Equal(1f, MathF.Sqrt(dx * dx + dy * dy), 3);
        }
    }

    [Fact]
    public void the_tunnel_comes_out_of_its_depth()
    {
        var tunnel = new TunnelLane(W, H);
        var radii = Enumerable.Range(0, 11).Select(i => tunnel.RadiusAt(i / 10.0)).ToList();

        Assert.Equal(tunnel.Frame, radii[0]);
        Assert.All(radii.Zip(radii.Skip(1)), r => Assert.True(r.Second < r.First));
    }
}
