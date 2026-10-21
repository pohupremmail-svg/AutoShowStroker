using System;
using System.Collections.Generic;
using Gooner.Core.App;
using Gooner.Core.Characters;
using Gooner.Engine.Characters;
using Godot;

namespace Gooner.Engine.Ui;

// How much of the figure a view shows: all of it standing on the start screen's pedestal, or from the
// hips up, as in the telepathy frame of a session.
public enum Framing
{
    Pedestal,
    Bust,
}

// A VRM figure in a transparent 3D view of its own: it breathes, sways a little, blinks, turns its head
// after the pointer (on the pedestal) or keeps its eyes on the viewer (in the frame), wears a mood and
// moves its mouth to a line. The figure is shared (Figures): whichever view is on screen takes it into
// its own world, so the pedestal and the frame never load a model twice. A file that will not load
// leaves the view empty, and the pedestal to its logo.
public partial class CharacterView : SubViewportContainer
{
    private readonly SubViewport _viewport = new() { TransparentBg = true, OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X };
    private readonly Camera3D _camera = new() { Fov = 30, Current = true };
    private readonly DirectionalLight3D _rim;
    private readonly string _path;
    private readonly Framing _framing;
    private Figure? _figure;
    private readonly Dictionary<string, float> _weights = new(StringComparer.OrdinalIgnoreCase);
    private double _time, _nextBlink = 3, _blinkedAt = -1;
    private Vector2 _gaze;
    private Reaction? _mood;
    private float _moodWeight;
    private IReadOnlyList<MouthShape> _line = [];
    private double _lineAt = -1;
    // The beat, in the frame: the session clock and the notes either side of it.
    private double _beatNow;
    private double? _lastNote, _nextNote;
    private float _moving;
    private Node3D? _toy;
    private const float ToyRadius = 0.021f;
    private Vector3? _gripAt;

    // On screen: taken into this view's world.
    public bool Loaded => _figure is not null && IsInstanceValid(_figure.Root) && _figure.Root.GetParent() == _viewport;

    public CharacterView(string path, Framing framing = Framing.Pedestal)
    {
        _path = path;
        _framing = framing;
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_viewport);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.62f, 0.56f, 0.66f),
        };
        _viewport.AddChild(new WorldEnvironment { Environment = environment });
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30, 20, 0), LightEnergy = 1.05f });
        // The spotlight's colour from behind, for an edge of it along the figure.
        _rim = new DirectionalLight3D { RotationDegrees = new Vector3(-20, 180, 0), LightEnergy = 1.4f, LightColor = UiTheme.Of(Palette.Accent) };
        _viewport.AddChild(_rim);
        _viewport.AddChild(_camera);
        Resized += Frame;
    }

    // A face for what was just said: the expression eases in and stays until Calm.
    public void React(Reaction reaction) => _mood = reaction;

    public void Calm() => _mood = null;

    // The beat for her to move to: the session clock now, the note before and the note after.
    public void Rhythm(double now, double? lastNote, double? nextNote)
    {
        _beatNow = now;
        _lastNote = lastNote;
        _nextNote = nextNote;
    }

    // The mouth moves to the line, letter by letter (Visemes), from now.
    public void Speak(string text)
    {
        _line = Visemes.Timeline(text);
        _lineAt = _time;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        if (!Loaded && !Take()) return;
        _time += delta;
        var t = (float)_time;
        var figure = _figure!;
        _rim.LightColor = UiTheme.Of(Palette.Accent);

        // The pointer, from the figure's point of view: -1..1 across the window, eased. In the frame she
        // looks at the viewer instead.
        var window = GetViewportRect().Size;
        var pointer = _framing == Framing.Pedestal ? GetGlobalMousePosition() / window * 2 - Vector2.One : Vector2.Zero;
        _gaze = _gaze.Lerp(new Vector2(Math.Clamp(pointer.X, -1, 1), Math.Clamp(pointer.Y, -1, 1)), (float)Math.Min(1, delta * 3));
        var speaking = Speaking();

        // Arms down from the T-pose they come in; a breath; a slow sway; the head after the pointer, and
        // a small nod while she speaks. Model space throughout (+Y up, +Z towards the viewer), parents first.
        figure.Turn("Hips", Vector3.Up, 3 * MathF.Sin(t * 0.5f));
        figure.Turn("Spine", Vector3.Back, 1.5f * MathF.Sin(t * 0.5f + 1));
        figure.Turn("Chest", Vector3.Right, -1.2f * MathF.Sin(t * 1.6f));
        // On the beat (in the frame): leaning into each note, the head nodding with it.
        var down = Stroke(delta);
        // A development look at the grip: --grip-down=<0..1> holds the stroke there.
        if (_framing == Framing.Bust && AppPaths.UserArg("grip-down") is { } held)
            down = float.Parse(held, System.Globalization.CultureInfo.InvariantCulture);
        figure.Turn("Spine", Vector3.Right, 3 * down * _moving, keep: true);
        figure.Turn("LeftUpperArm", Vector3.Back, -72 + 2 * MathF.Sin(t * 1.6f));
        figure.Turn("LeftLowerArm", Vector3.Right, -14);
        if (_framing == Framing.Bust) Grip(figure, down);
        else
        {
            figure.Turn("RightUpperArm", Vector3.Back, 72 - 2 * MathF.Sin(t * 1.6f));
            figure.Turn("RightLowerArm", Vector3.Right, -14);
        }
        figure.Turn("Neck", Vector3.Up, 10 * _gaze.X);
        figure.Turn("Head", Vector3.Up, 14 * _gaze.X + (speaking ? 3 * MathF.Sin(t * 2.3f) : 0));
        figure.Turn("Head", Vector3.Right, 10 * _gaze.Y + 4 * down * _moving + (speaking ? 2.5f * MathF.Sin(t * 7.1f) : 0), keep: true);

        Face(delta);
    }

    // Leaving the screen, the figure is taken out of this view's world, so it outlives it: a view freed
    // (the start screen rebuilt for a new language) must not free the figure the next one takes over.
    public override void _ExitTree()
    {
        if (Loaded) _viewport.RemoveChild(_figure!.Root);
    }

    // Takes the shared figure into this view once it has loaded, faded in the first time.
    private bool Take()
    {
        if (Figures.Get(_path) is not { } figure) return false;
        var arrived = _figure is null;
        _figure = figure;
        figure.Root.GetParent()?.RemoveChild(figure.Root);
        _viewport.AddChild(figure.Root);
        Frame();
        if (arrived)
        {
            Modulate = new Color(1, 1, 1, 0);
            CreateTween().TweenProperty(this, "modulate:a", 1f, 0.6f);
        }
        return true;
    }

    // How far down the stroke is: 1 on a note, 0 halfway between two - one full stroke per note, the
    // beat its frequency. Without a note close by (a pause, the start) she eases to a stop halfway.
    private float Stroke(double delta)
    {
        var active = _framing == Framing.Bust && _lastNote is { } last && _nextNote is { } next
            && next > last && next - last < 2.5 && next - _beatNow < 2.5;
        _moving = Mathf.MoveToward(_moving, active ? 1 : 0, (float)delta * 3);
        if (!active) return 0.5f * _moving + 0.5f * (1 - _moving);
        var phase = (float)((_beatNow - _lastNote!.Value) / (_nextNote!.Value - _lastNote.Value));
        var down = 0.5f * (1 + MathF.Cos(2 * MathF.PI * Math.Clamp(phase, 0, 1)));
        return Mathf.Lerp(0.5f, down, _moving);
    }

    // What the grip measures on a figure, once: where the toy stands and how big it is, which way the
    // palm faces it and the fingers run round it, and how big the hand is. Skeleton space (+Y up, +Z
    // towards the viewer).
    private sealed record GripSetup(Vector3 Root, Vector3 Along, float Length, float Radius,
        Vector3 ToShaft, Vector3 Round, float PalmLength, float Finger, float Scale);

    private GripSetup? _grip;
    private Figure? _gripFor;
    // The fingers' turns under the hand, wrapped once: the hand only slides along the toy, so they do
    // not change from one frame to the next.
    private readonly List<(int Bone, Quaternion Turn)> _closed = [];

    private static GripSetup? MeasureGrip(Figure figure)
    {
        if (figure.Bone("Hips") is not { } hipsBone || figure.Bone("Head") is not { } headBone
            || figure.Bone("RightHand") is not { } hand || figure.Bone("RightMiddleProximal") is not { } knuckle) return null;
        var hips = figure.Rest(hipsBone);
        var scale = Math.Max(0.3f, (figure.Rest(headBone).Y - (figure.Bone("LeftFoot") is { } f ? figure.Rest(f).Y : 0)) / 1.45f);
        var along = new Vector3(0, 1, 0.18f).Normalized();
        var length = 0.16f * scale;
        var radius = ToyRadius * scale;
        // In front of her belly, and clear of whatever she wears there.
        var y = hips.Y - 0.02f * scale;
        var front = figure.FrontAt(y + length / 2, 0.1f * scale, length / 2);
        var root = new Vector3(hips.X, y, Math.Max(hips.Z + 0.13f * scale, front + radius + 0.035f * scale));
        // From her right towards the shaft, and round its front, where the fingers are seen.
        var toShaft = (Vector3.Right - along * Vector3.Right.Dot(along)).Normalized();
        var round = along.Cross(toShaft);
        if (round.Z < 0) round = -round;
        var palmLength = (figure.Rest(knuckle) - figure.Rest(hand)).Length();
        return new GripSetup(root, along, length, radius, toShaft, round, palmLength, palmLength * 0.22f, scale);
    }

    // The right hand round the toy, sliding along it: at its root on a note, up near the tip between
    // two. Per frame only the arm reaches the wrist (two-bone IK) and the hand is turned into the grip -
    // the palm towards the shaft, the knuckles beside it, the fingers round its front; the fingers keep
    // the wrap they were given once, each joint bent by the arc its segment covers round the shaft.
    private void Grip(Figure figure, float down)
    {
        if (_gripFor != figure)
        {
            _gripFor = figure;
            _grip = MeasureGrip(figure);
            _closed.Clear();
        }
        if (_grip is not { } g || figure.Skeleton is not { } sk || figure.Bone("RightHand") is not { } hand
            || figure.Bone("RightMiddleProximal") is not { } knuckle) return;
        var grip = g.Root + g.Along * g.Length * Mathf.Lerp(0.8f, 0.32f, down);
        _gripAt = sk.GlobalTransform * grip;
        if (AppPaths.UserFlag("grip-closeup")) Frame();
        var knuckles = grip - g.ToShaft * (g.Radius + g.Finger * 0.9f);
        var wrist = knuckles - g.Round * g.PalmLength;

        foreach (var bone in new[] { "RightUpperArm", "RightLowerArm", "RightHand" }) figure.Turn(bone, Vector3.Up, 0);
        figure.Reach("Right", wrist, new Vector3(-1, -0.4f, -0.7f));
        // The forearm takes the roll, as a real one does; the wrist only what is left - turned in the
        // wrist alone, a long glove's cuff twisted into a loop.
        if (figure.Bone("RightLowerArm") is { } lower && figure.Palm("Right") is { } before)
        {
            var axis = (figure.Pose(hand) - figure.Pose(lower)).Normalized();
            var now = before - axis * before.Dot(axis);
            var want = g.ToShaft - axis * g.ToShaft.Dot(axis);
            if (now.LengthSquared() > 1e-8f && want.LengthSquared() > 1e-8f)
                figure.Rotate(lower, axis, now.SignedAngleTo(want, axis));
        }
        if (figure.Palm("Right") is { } palm)
            figure.Orient(hand, figure.Pose(knuckle) - figure.Pose(hand), palm, g.Round, g.ToShaft);
        if (_closed.Count == 0)
        {
            // The fingers from the palm round the front, the thumb the other way round the back, to meet
            // them on the far side - as in a fist.
            var way = g.Along.Dot(g.Round.Cross(g.ToShaft)) >= 0 ? 1 : -1;
            figure.RelaxHand("Right");
            foreach (var finger in new[] { "Index", "Middle", "Ring", "Little" })
                figure.Wrap("Right", finger, g.Along, grip, g.Radius + g.Finger * 0.5f, way);
            figure.Wrap("Right", "Thumb", g.Along, grip, g.Radius + g.Finger * 0.5f, -way);
            foreach (var bone in figure.HandBones("Right")) _closed.Add((bone, sk.GetBonePoseRotation(bone)));
        }
        else
            foreach (var (bone, turn) in _closed) sk.SetBonePoseRotation(bone, turn);
        PlaceToy(sk, g.Root, g.Along, g.Length, g.Scale);
    }

    // A stand-in until a proper model comes: a pink shaft with a rounded tip on a flat foot.
    private void PlaceToy(Skeleton3D sk, Vector3 root, Vector3 along, float length, float scale)
    {
        if (_toy is null)
        {
            var radius = ToyRadius * scale;
            var material = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.42f, 0.66f), Roughness = 0.3f, RimEnabled = true, Rim = 0.4f };
            _toy = new Node3D();
            _toy.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius * 1.05f, Height = length, Material = material }, Position = new Vector3(0, length / 2, 0) });
            _toy.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = radius * 1.2f, Height = radius * 2.3f, Material = material }, Position = new Vector3(0, length, 0) });
            _toy.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = radius * 1.5f, Height = radius * 1.4f, Material = material }, Position = Vector3.Zero });
            _viewport.AddChild(_toy);
        }
        var up = (sk.GlobalTransform.Basis * along).Normalized();
        var side = Vector3.Back.Cross(up).Normalized();
        _toy.GlobalTransform = new Transform3D(new Basis(side, up, side.Cross(up)), sk.GlobalTransform * root);
    }

    private bool Speaking() => _lineAt >= 0 && _line.Count > 0 && _time - _lineAt < _line[^1].At + 0.1;

    // The mood eased in or out, a blink now and then, and the mouth of the letter being said.
    private void Face(double delta)
    {
        _weights.Clear();
        var target = _mood is { } mood ? (float)mood.Strength : 0;
        _moodWeight = Mathf.MoveToward(_moodWeight, target, (float)delta * 4);
        if (_mood is { } m && _moodWeight > 0) _weights[m.Expression] = _moodWeight;
        else if (_moodWeight <= 0 && _figure!.Face.Has("relaxed")) _weights["relaxed"] = _framing == Framing.Pedestal ? 1 : 0.3f;

        // Shut in 80 ms, held for 60, open again in 120.
        if (_time >= _nextBlink)
        {
            _blinkedAt = _time;
            _nextBlink = _time + 2.5 + Random.Shared.NextDouble() * 3.5;
        }
        if (_blinkedAt >= 0)
        {
            var since = (float)(_time - _blinkedAt);
            _weights["blink"] = since < 0.08f ? since / 0.08f : since < 0.14f ? 1 : Math.Max(0, 1 - (since - 0.14f) / 0.12f);
            if (since >= 0.26f) _blinkedAt = -1;
        }

        if (Speaking())
        {
            var at = _time - _lineAt;
            var shape = _line[0];
            foreach (var s in _line)
            {
                if (s.At > at) break;
                shape = s;
            }
            // Open quickly into the letter's shape and back towards closed through it.
            var into = (float)((at - shape.At) * Visemes.LettersPerSecond);
            if (shape.Viseme is { } viseme) _weights[viseme] = 0.75f * MathF.Sin(Math.Clamp(into, 0, 1) * MathF.PI * 0.8f + 0.25f);
        }
        _figure!.Face.Apply(_weights);
    }

    // The camera, so that the figure fills the view. On the pedestal the feet stand on its floor (84% of
    // the way down, Pedestal) and the head sits under the top edge; in the frame it shows the figure from
    // the hips up. Measured in the world, whatever the figure's height.
    private void Frame()
    {
        if (!Loaded || _figure!.Measure() is not { } measured) return;
        var (head, feet) = measured;
        var height = head.Y - feet;
        var top = head.Y + height * 0.14f;
        float centre, half;
        if (_framing == Framing.Pedestal)
        {
            var floor = feet - height * 0.06f; // the foot bone sits at the ankle
            half = (top - floor) / 1.48f; // floor at 0.84, top at 0.10 of the view: 0.74 * 2 * half
            centre = floor + 0.68f * half;
        }
        else
        {
            var hips = _figure.Bone("Hips") is { } h ? _figure.At(h).Y : feet + height * 0.53f;
            var bottom = hips - height * 0.03f;
            half = (top - bottom) / 2 * 1.04f;
            centre = (top + bottom) / 2 - half * 0.02f;
            // A narrow frame: the half height that also fits the shoulders across.
            var aspect = Size.Y > 0 ? Size.X / Size.Y : 0.8f;
            half = Math.Max(half, height * 0.16f / Math.Max(0.3f, aspect));
        }
        var distance = half / MathF.Tan(Mathf.DegToRad(_camera.Fov / 2));
        _camera.Position = new Vector3(head.X, centre, head.Z + distance);
        // A development look at the grip: --grip-closeup puts the camera right in front of the hand;
        // --grip-turn=<degrees> carries it round to her left (90: from the side, where thumb and fingers
        // meet) and --grip-tilt=<degrees> up over the hand (40: from above, the thumb behind the shaft seen;
        // much higher and the toy's tip hides the hand).
        if (_framing == Framing.Bust && AppPaths.UserFlag("grip-closeup") && _gripAt is { } at)
        {
            var near = height * 0.07f / MathF.Tan(Mathf.DegToRad(_camera.Fov / 2));
            static float Degrees(string name) => Mathf.DegToRad(AppPaths.UserArg(name) is { } d ? float.Parse(d, System.Globalization.CultureInfo.InvariantCulture) : 0);
            var turn = Degrees("grip-turn");
            var tilt = Degrees("grip-tilt");
            _camera.Position = at + new Vector3(0, 0, near).Rotated(Vector3.Left, tilt).Rotated(Vector3.Up, turn);
            if (turn != 0 || tilt != 0) _camera.LookAt(at);
        }
        _camera.Near = Math.Max(0.01f, distance * 0.01f);
        _camera.Far = Math.Max(100, distance * 10);
    }
}
