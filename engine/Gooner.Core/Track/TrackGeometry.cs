namespace Gooner.Core.Track;

// A point on the window, in pixels.
public readonly record struct TrackPoint(float X, float Y);

// A point on the highway: where, how much perspective has shrunk it, and how wide the lane is there.
public readonly record struct LanePoint(float X, float Y, float Scale, float HalfWidth);

// The note tracks' geometry. Every track places a note by how far it still is from its beat -
// `ahead`, 1 at the far end of the lead time and 0 on the beat - and every one puts a note on its
// beat on the hit zone, however the track bends, sways or turns. That is what lands a note on its
// click: both are worked out from the same moment.

// A: a lane running into the picture, swaying gently as it goes; the notes come towards you.
public sealed class HighwayLane(float width, float height)
{
    // How much further away the far end is than the hit line.
    public const float Depth = 6.5f;

    public float CenterX => width / 2;
    public float HitY => height * 0.84f;
    public float Horizon => height * 0.47f;
    public float HalfWidth => Math.Min(width * 0.2f, 360);

    public LanePoint At(double ahead, double time)
    {
        var z = (float)Math.Max(0, ahead);
        var scale = 1 / (1 + z * Depth);
        // Swaying more the further away; not at all on the hit line.
        var sway = (float)(Math.Sin(time * 0.33) * width * 0.05 * z * z + Math.Sin(time * 0.19 + 1.3) * width * 0.025 * z);
        return new LanePoint(CenterX + sway, Horizon + (HitY - Horizon) * scale, scale, HalfWidth * scale);
    }
}

// B: a ring the notes spiral into from two arms, turning slowly. Smaller when it wanders, so the
// spiral stays on the picture wherever the ring goes.
public sealed class HypnoRing(float width, float height, bool wandering)
{
    private float Short => Math.Min(width, height);

    public float Radius => Short * (wandering ? 0.085f : 0.105f);
    public float Reach => Short * (wandering ? 0.31f : 0.43f);

    public float RadiusAt(double ahead) => Radius + (Reach - Radius) * (float)Math.Pow(Math.Max(0, ahead), 0.9);

    public TrackPoint At(double ahead, int arm, double time, TrackPoint centre)
    {
        var angle = -Math.PI / 2 + (arm & 1) * Math.PI + Math.Max(0, ahead) * Math.PI * 1.4 + time * 0.12;
        var r = RadiusAt(ahead);
        return new TrackPoint(centre.X + (float)Math.Cos(angle) * r, centre.Y + (float)Math.Sin(angle) * r);
    }
}

// C: today's strip along the bottom, notes coming in from the right to a hit zone near the left.
public sealed class StreamLane(float width, float height)
{
    public const float Left = 48;
    public float Right => width - 48;
    public float Height => Math.Max(72, height * 0.085f);
    public float Top => height - Height - 94;
    public float CenterY => Top + Height / 2;
    public float HitX => Left + (Right - Left) * 0.12f;

    public float X(double ahead) => HitX + (float)Math.Max(0, ahead) * (Right - 26 - HitX);
}

// G: a tunnel you fly through - rings rush out of its depth to a frame around the picture, the notes
// along its wall, each at its own angle round it, so they come in turning like a corkscrew.
public sealed class TunnelLane(float width, float height)
{
    public const float Depth = 7f;

    // The frame is an ellipse a little flatter than a circle.
    public const float Squash = 0.9f;

    public float CenterX => width / 2;
    public float CenterY => height * 0.47f;
    public float Frame => Math.Min(width, height) * 0.47f;

    public float RadiusAt(double ahead) => Frame / (1 + (float)Math.Max(0, ahead) * Depth);

    // `beat` is the note's own moment on the session clock: it fixes where round the tunnel it runs.
    public TrackPoint At(double ahead, double beat, double time)
    {
        var angle = AngleOf(beat, time);
        var r = RadiusAt(ahead);
        return new TrackPoint(CenterX + (float)Math.Cos(angle) * r, CenterY + (float)Math.Sin(angle) * r * Squash);
    }

    public static double AngleOf(double beat, double time) => Math.PI / 2 + beat * 0.9 + time * 0.12;
}
