using System;
using System.Collections.Generic;
using System.Linq;
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
    // The beat, in the frame: the session clock, the two notes either side of it and one more beyond
    // each (StrokeMotion settles a note's depth by the gaps either side).
    private double _beatNow;
    private double? _noteBefore, _lastNote, _nextNote, _noteAfter;
    private float _moving, _stroke = 0.5f;
    // How she stands while nothing else is asked of her; a view of its own, so the pedestal and the
    // frame never move as one.
    private readonly IdleMotion _idle = new(Random.Shared.Next());
    private readonly FaceMotion _faceMotion = new(Random.Shared.Next());
    private float _glance;
    private (Figure Of, float Height, Vector3 LeftAnkle, Vector3 RightAnkle)? _stance;
    private Node3D? _toy;
    private bool _toyIsModel;
    private const float ToyRadius = 0.021f;
    private Vector3? _gripAt;

    // On screen: taken into this view's world.
    public bool Loaded => _figure is not null && IsInstanceValid(_figure.Root) && _figure.Root.GetParent() == _viewport;

    public CharacterView(string path, Framing framing = Framing.Pedestal)
    {
        _path = path;
        _framing = framing;
        // Read ahead, while she is still on the pedestal, for the moment she holds it.
        Toy.Load(AppPaths.Resources is { } res ? System.IO.Path.Combine(res, "props") : null);
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

    // The beat for her to move to: the session clock now, the notes before and after it, and the one
    // beyond each of those.
    public void Rhythm(double now, double? noteBefore, double? lastNote, double? nextNote, double? noteAfter)
    {
        _beatNow = now;
        _noteBefore = noteBefore;
        _lastNote = lastNote;
        _nextNote = nextNote;
        _noteAfter = noteAfter;
    }

    // Where her mouth is in this view, for a speech bubble to point at: a twentieth of her height below
    // the middle of her eyes. Not the head bone - that sits at the mouth on one model and at the neck
    // on the next; the eyes are where they look (checked on four models). Null until she is here.
    public Vector2? Mouth()
    {
        if (!Loaded || _figure!.Skeleton is not { } sk || _figure.Measure() is not { } measured) return null;
        Vector3? At(string human) => _figure.Bone(human) is { } bone ? sk.GlobalTransform * _figure.Pose(bone) : null;
        if ((At("LeftEye") is { } left && At("RightEye") is { } right ? (left + right) / 2 : At("Head")) is not { } eyes) return null;
        var mouth = eyes - Vector3.Up * 0.05f * (measured.Head.Y - measured.Feet);
        return _camera.IsPositionBehind(mouth) ? null : _camera.UnprojectPosition(mouth);
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

        // Standing as a person stands (IdleMotion): her weight on one leg and then the other, her hips
        // over it and her shoulders leaning against it; a breath, in quicker than out; a slow wander
        // that never repeats; arms hanging loose, not alike; hands at ease; eyes that rest and dart.
        // Then what the view asks of her: the pointer followed on the pedestal, the beat and the toy in
        // the frame, a nod while she speaks. Model space throughout (+Y up, +Z towards the viewer),
        // parents first.
        // In the frame she holds the toy against her: her weight moves less there.
        var weight = (float)_idle.Weight(_time) * (_framing == Framing.Bust ? 0.4f : 1);
        var breath = (float)_idle.Breath(_time);
        float Wander(int part) => (float)_idle.Drift(_time, part);
        var stance = Stance(figure);
        // Never still: her hips drift a little every way and settle with each breath, and sit low enough
        // that neither knee locks - so the legs under them keep giving a little too.
        figure.Shift("Hips", new Vector3(
            0.028f * weight + 0.005f * Wander(13),
            -0.008f * Math.Abs(weight) - 0.006f - 0.002f * breath + 0.003f * Wander(14),
            0.005f * Wander(15)) * stance.Height);
        figure.Turn("Hips", Vector3.Up, 3 * weight + 2.5f * Wander(0));
        figure.Turn("Hips", Vector3.Back, 6 * weight, keep: true);
        figure.Turn("Spine", Vector3.Back, -3.5f * weight + 1.2f * Wander(1));
        // On the beat (in the frame): leaning into each note, the head nodding with it.
        var down = Stroke(delta);
        // A development look at the grip: --grip-down=<0..1> holds the stroke there.
        if (_framing == Framing.Bust && AppPaths.UserArg("grip-down") is { } held)
            down = float.Parse(held, System.Globalization.CultureInfo.InvariantCulture);
        figure.Turn("Spine", Vector3.Right, 3 * down * _moving + 0.8f * Wander(2), keep: true);
        figure.Turn("Chest", Vector3.Back, -2 * weight);
        figure.Turn("Chest", Vector3.Right, -1.6f * breath, keep: true);
        figure.Turn("LeftShoulder", Vector3.Back, 1.4f * breath);
        figure.Turn("RightShoulder", Vector3.Back, -1.4f * breath);
        var hang = Hang(figure);
        figure.Turn("LeftUpperArm", Vector3.Back, hang.Left + 1.5f * Wander(3) - breath);
        figure.Turn("LeftUpperArm", Vector3.Right, -5 + 2 * Wander(4), keep: true);
        figure.Turn("LeftLowerArm", Vector3.Right, -18 + 3 * Wander(5));
        figure.Ease("Left");
        if (_framing == Framing.Bust) Grip(figure, down);
        else
        {
            figure.Turn("RightUpperArm", Vector3.Back, hang.Right + 1.5f * Wander(10) + breath);
            figure.Turn("RightUpperArm", Vector3.Right, -3 + 2 * Wander(11), keep: true);
            figure.Turn("RightLowerArm", Vector3.Right, -24 + 3 * Wander(12));
            figure.Ease("Right");
            // The leg that carries her stays where it stands while her hips move over it; the free one
            // eases off - its knee forward, its heel up, set down a little ahead and out.
            foreach (var (side, ankle, outward, free) in new[]
                     { ("Left", stance.LeftAnkle, 1f, Math.Max(0, -weight)), ("Right", stance.RightAnkle, -1f, Math.Max(0, weight)) })
            {
                figure.Turn($"{side}UpperLeg", Vector3.Up, 0);
                figure.Turn($"{side}LowerLeg", Vector3.Up, 0);
                var restless = new Vector3(0.012f * outward + 0.005f * Wander(outward > 0 ? 16 : 17), 0.03f, 0.045f + 0.008f * Wander(outward > 0 ? 18 : 19));
                figure.Plant(side, ankle + restless * free * stance.Height, new Vector3(0.2f * outward, 0, 1));
                figure.Turn($"{side}Foot", Vector3.Right, 14 * free, keep: true);
            }
        }
        // Her eyes rest and dart (IdleMotion.Gaze); a glance aside takes her head along, slower.
        var (lookX, lookY) = _idle.Gaze(_time);
        _glance = Mathf.Lerp(_glance, Math.Abs(lookX) > 0.55 ? (float)lookX : 0, (float)Math.Min(1, delta * 3));
        figure.Turn("Neck", Vector3.Up, 10 * _gaze.X + 4 * _glance + 2 * Wander(6));
        figure.Turn("Head", Vector3.Up, 14 * _gaze.X + 6 * _glance + 2.5f * Wander(7) + (speaking ? 3 * MathF.Sin(t * 2.3f) : 0));
        figure.Turn("Head", Vector3.Right, 10 * _gaze.Y + 2 * Wander(8) + 4 * down * _moving + (speaking ? 2.5f * MathF.Sin(t * 7.1f) : 0), keep: true);
        figure.Turn("Head", Vector3.Back, 2.5f * Wander(9) - 1.5f * weight, keep: true);
        foreach (var eye in new[] { "LeftEye", "RightEye" })
        {
            figure.Turn(eye, Vector3.Up, 8 * (float)lookX);
            figure.Turn(eye, Vector3.Right, 5 * (float)lookY, keep: true);
        }

        Face(delta);
    }

    // Her height and where her feet stand, at rest in the skeleton - measured once per figure.
    private (Figure Of, float Height, Vector3 LeftAnkle, Vector3 RightAnkle) Stance(Figure figure)
    {
        if (_stance is { } known && known.Of == figure) return known;
        Vector3 Rest(string human) => figure.Bone(human) is { } bone ? figure.Rest(bone) : Vector3.Zero;
        var (left, right) = (Rest("LeftFoot"), Rest("RightFoot"));
        _stance = (figure, Math.Max(0.3f, Rest("Head").Y - Math.Min(left.Y, right.Y)), left, right);
        return _stance.Value;
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

    // How far down the stroke is: 1 at the bottom of a whole stroke, 0 at the top - one stroke per note,
    // shorter on a fast beat (StrokeMotion). Without a note close by (a pause, the start) she eases to a
    // stop halfway, from wherever the last stroke left her hand.
    private float Stroke(double delta)
    {
        var down = _framing == Framing.Bust ? StrokeMotion.Down(_beatNow, _noteBefore, _lastNote, _nextNote, _noteAfter) : null;
        _moving = Mathf.MoveToward(_moving, down is null ? 0 : 1, (float)delta * 3);
        if (down is { } d) _stroke = (float)d;
        return Mathf.Lerp(0.5f, _stroke, _moving);
    }

    // What the grip measures on a figure, once: where the toy stands and how big it is, which way the
    // palm faces it and the fingers run round it, and how big the hand is. Skeleton space (+Y up, +Z
    // towards the viewer).
    private sealed record GripSetup(Vector3 Root, Vector3 Along, float Length, float Radius,
        Vector3 ToShaft, Vector3 Round, float PalmLength, float Finger, float Scale, Vector3 Pole);

    private GripSetup? _grip;
    private Figure? _gripFor;
    // The fingers' turns under the hand, wrapped once: the hand only slides along the toy, so they do
    // not change from one frame to the next.
    private readonly List<(int Bone, Quaternion Turn)> _closed = [];

    // How thick an arm is taken to be, at a person's size, wherever it is kept clear of her body.
    private const float Thickness = 0.035f;

    private static System.Numerics.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

    // Her body as it rests, every third point of it - what her arms are kept clear of. Read once per figure.
    private List<System.Numerics.Vector3> Body(Figure figure)
    {
        if (_bodyFor != figure)
        {
            _bodyFor = figure;
            _body = [.. figure.RestPoints().Where((_, i) => i % 3 == 0).Select(N)];
        }
        return _body;
    }

    private Figure? _bodyFor;
    private List<System.Numerics.Vector3> _body = [];

    // How far down each free arm hangs (ArmHang): by her side, unless her hips are in the way.
    private (float Left, float Right) Hang(Figure figure)
    {
        if (_hangFor == figure) return _hang;
        _hangFor = figure;
        _hang = (-77, 76);
        if (figure.Bone("Head") is not { } head) return _hang;
        var scale = Math.Max(0.3f, (figure.Rest(head).Y - (figure.Bone("LeftFoot") is { } f ? figure.Rest(f).Y : 0)) / 1.45f);
        float For(string side, float preferred)
        {
            if (figure.Bone($"{side}UpperArm") is not { } upper || figure.Bone($"{side}LowerArm") is not { } lower
                || figure.Bone($"{side}Hand") is not { } hand) return preferred;
            var shoulder = figure.Rest(upper);
            var tip = figure.Bone($"{side}MiddleProximal") is { } knuckle ? figure.Rest(knuckle) : figure.Rest(hand);
            var length = (figure.Rest(lower) - shoulder).Length() + (figure.Rest(hand) - figure.Rest(lower)).Length() + (tip - figure.Rest(hand)).Length();
            return ArmHang.Pick(N(shoulder), N((figure.Rest(lower) - shoulder).Normalized()), length, preferred, Body(figure), Thickness * scale);
        }
        _hang = (For("Left", -77), For("Right", 76));
        return _hang;
    }

    private Figure? _hangFor;
    private (float Left, float Right) _hang;

    private static GripSetup? MeasureGrip(Figure figure, ToyShape? toy, List<System.Numerics.Vector3> body)
    {
        if (figure.Bone("Hips") is not { } hipsBone || figure.Bone("Head") is not { } headBone || figure.Bone("RightHand") is not { } hand
            || figure.Bone("RightMiddleProximal") is not { } knuckle || figure.Bone("RightUpperArm") is not { } upper
            || figure.Bone("RightLowerArm") is not { } lower) return null;
        var hips = figure.Rest(hipsBone);
        var scale = Math.Max(0.3f, (figure.Rest(headBone).Y - (figure.Bone("LeftFoot") is { } f ? figure.Rest(f).Y : 0)) / 1.45f);
        var along = new Vector3(0, 1, 0.18f).Normalized();
        // The toy's own measure (res/props), at her size; the stand-in's without one.
        var length = (toy?.Length ?? 0.16f) * scale;
        var radius = (toy?.Radius ?? ToyRadius) * scale;
        var behind = toy is { } t ? t.Behind * scale : 1.5f * radius;
        var halfWidth = Math.Max(0.12f, toy?.HalfWidth ?? 0) * scale;
        // A set gap in front of her body - the front of her belly and whatever she wears there, over the
        // toy's whole height - from what sticks out of the toy furthest towards her (its balls, its foot).
        // A deep belly puts it further out.
        var y = hips.Y - 0.02f * scale;
        var front = figure.FrontAt(hips.X, y + length / 2, halfWidth, length / 2 + behind) ?? hips.Z + 0.1f * scale;
        var root = new Vector3(hips.X, y, front + behind + 0.03f * scale);
        // From her right towards the shaft, and round its front, where the fingers are seen.
        var toShaft = (Vector3.Right - along * Vector3.Right.Dot(along)).Normalized();
        var round = along.Cross(toShaft);
        if (round.Z < 0) round = -round;
        var palmLength = (figure.Rest(knuckle) - figure.Rest(hand)).Length();
        var finger = palmLength * 0.22f;
        // Her hand closes round the shaft from in front, so her wrist is a hand's length behind her
        // knuckles: the toy stands far enough out for the wrist to be clear of her too, or her forearm
        // ran into her belly (the user saw it on a wide figure and in a hoodie).
        var wristX = hips.X - (radius + finger * 0.9f);
        if (figure.FrontAt(wristX, y + length * 0.56f, 0.03f * scale, length * 0.3f) is { } wristFront)
            root.Z = Math.Max(root.Z, wristFront + palmLength * round.Z + Thickness * scale);
        // Her arm is never stretched: an arm too short to reach both ends of the stroke out there first
        // holds the toy a little higher, and then brings it back towards her, into her belly if it must
        // (the user's rule) - short arms put her wrist into her belly where it could have been held higher.
        var shoulder = figure.Rest(upper);
        var arm = 0.95f * ((figure.Rest(lower) - shoulder).Length() + (figure.Rest(hand) - figure.Rest(lower)).Length());
        Vector3 Wrist(Vector3 at, float share) =>
            at + along * length * share - toShaft * (radius + finger * 0.9f) - round * palmLength;
        bool OutOfReach() => (Wrist(root, 0.8f) - shoulder).Length() > arm || (Wrist(root, 0.32f) - shoulder).Length() > arm;
        var raised = 0f;
        while (raised < 0.06f * scale && OutOfReach())
        {
            root.Y += 0.002f * scale;
            raised += 0.002f * scale;
        }
        while (root.Z > hips.Z && OutOfReach())
            root.Z -= 0.002f * scale;
        // Her elbow out to her side and back, unless that takes her arm through her waist on its way to
        // the toy: then where it does not.
        var pole = ElbowChoice.Pick(N(shoulder), (figure.Rest(lower) - shoulder).Length(), (figure.Rest(hand) - figure.Rest(lower)).Length(),
            [N(Wrist(root, 0.8f)), N(Wrist(root, 0.32f))], body, Thickness * scale, N(new Vector3(-1, -0.4f, -0.7f)));
        return new GripSetup(root, along, length, radius, toShaft, round, palmLength, finger, scale, new Vector3(pole.X, pole.Y, pole.Z));
    }

    // The right hand round the toy, sliding along it: at its root on a note, up near the tip between
    // two. Per frame only the arm reaches the wrist (two-bone IK) and the hand is turned into the grip -
    // the palm towards the shaft, the knuckles beside it, the fingers round its front; the fingers keep
    // the wrap they were given once, each joint bent by the arc its segment covers round the shaft.
    private void Grip(Figure figure, float down)
    {
        if (_gripFor != figure)
        {
            // Her hand waits the moment the toy takes to read: measured on the stand-in, it would close on
            // a shaft of the wrong size.
            if (!Toy.Settled) return;
            _gripFor = figure;
            _grip = MeasureGrip(figure, Toy.Loaded?.Shape, Body(figure));
            _closed.Clear();
            _toy?.QueueFree();
            _toy = null;
            _toyIsModel = false;
        }
        if (_grip is not { } g || figure.Skeleton is not { } sk || figure.Bone("RightHand") is not { } hand
            || figure.Bone("RightMiddleProximal") is not { } knuckle) return;
        // The toy goes where her hips go, or her swaying belly would take her hand into it.
        var root = g.Root + (figure.Bone("Hips") is { } hips ? figure.Pose(hips) - figure.Rest(hips) : Vector3.Zero);
        var share = Mathf.Lerp(0.8f, 0.32f, down);
        PlaceToy(sk, root, g.Along, g.Length, g.Scale);
        // On the middle of the shaft at that height: a real one curves, and a straight line through its
        // foot left her fingers standing off it.
        var grip = _toy is { } placed && Toy.Loaded is { } toy && _toyIsModel
            ? sk.GlobalTransform.AffineInverse() * (placed.GlobalTransform * toy.Centre(share, g.Scale))
            : root + g.Along * g.Length * share;
        _gripAt = sk.GlobalTransform * grip;
        if (AppPaths.UserFlag("grip-closeup")) Frame();
        var knuckles = grip - g.ToShaft * (g.Radius + g.Finger * 0.9f);
        var wrist = knuckles - g.Round * g.PalmLength;

        foreach (var bone in new[] { "RightUpperArm", "RightLowerArm", "RightHand" }) figure.Turn(bone, Vector3.Up, 0);
        figure.Reach("Right", wrist, g.Pole);
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
    }

    // The toy (res/props) at her size - or, without one, a stand-in: a pink shaft with a rounded tip on a
    // flat foot. Its balls go towards her (+Z of its own, turned away from the viewer below).
    private void PlaceToy(Skeleton3D sk, Vector3 root, Vector3 along, float length, float scale)
    {
        if (_toy is null && Toy.Loaded is { } toy)
        {
            _toyIsModel = true;
            _toy = toy.Make(scale);
            _viewport.AddChild(_toy);
        }
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
        // Beneath everything a face that is never blank (FaceMotion) - heated while she strokes.
        var aroused = _framing == Framing.Bust ? _moving : 0;
        foreach (var (name, weight) in _faceMotion.At(_time, _idle.Breath(_time), aroused)) _weights[name] = (float)weight;
        if (_framing == Framing.Pedestal) _weights["relaxed"] = _weights.GetValueOrDefault("relaxed") + 0.4f;
        // A little smile once a line is said.
        if (_lineAt >= 0 && _line.Count > 0 && _time - _lineAt - _line[^1].At is > 0 and < 1.5 and var after)
            _weights["happy"] = _weights.GetValueOrDefault("happy") + 0.3f * MathF.Sin(MathF.PI * (float)after / 1.5f);
        // The callout's face over it; a cross or a sad one takes the smile away as it comes.
        var target = _mood is { } mood ? (float)mood.Strength : 0;
        _moodWeight = Mathf.MoveToward(_moodWeight, target, (float)delta * 4);
        if (_mood is { } m && _moodWeight > 0)
        {
            if (m.Expression is "angry" or "sad") _weights["happy"] = _weights.GetValueOrDefault("happy") * (1 - _moodWeight);
            _weights[m.Expression] = Math.Min(1, _weights.GetValueOrDefault(m.Expression) + _moodWeight);
        }
        // Heavy lids and a smile that narrows the eyes never add up to closed ones.
        _weights["blink"] = _weights.GetValueOrDefault("blink") * (1 - Math.Min(1, _weights.GetValueOrDefault("happy")));

        // Shut in 80 ms, held for 60, open again in 120.
        if (_time >= _nextBlink)
        {
            _blinkedAt = _time;
            _nextBlink = _time + 2.5 + Random.Shared.NextDouble() * 3.5;
        }
        if (_blinkedAt >= 0)
        {
            var since = (float)(_time - _blinkedAt);
            _weights["blink"] = Math.Max(_weights.GetValueOrDefault("blink"), since < 0.08f ? since / 0.08f : since < 0.14f ? 1 : Math.Max(0, 1 - (since - 0.14f) / 0.12f));
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
            if (shape.Viseme is { } viseme)
                _weights[viseme] = Math.Max(_weights.GetValueOrDefault(viseme), 0.75f * MathF.Sin(Math.Clamp(into, 0, 1) * MathF.PI * 0.8f + 0.25f));
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
        // --face-closeup: the camera on her face, to judge its expressions.
        if (_framing == Framing.Bust && AppPaths.UserFlag("face-closeup"))
            _camera.Position = new Vector3(head.X, head.Y + height * 0.05f, head.Z + height * 0.075f / MathF.Tan(Mathf.DegToRad(_camera.Fov / 2)));
        _camera.Near = Math.Max(0.01f, distance * 0.01f);
        _camera.Far = Math.Max(100, distance * 10);
    }
}
