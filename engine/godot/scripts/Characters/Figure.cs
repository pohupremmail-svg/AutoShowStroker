using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gooner.Core.Logging;
using Godot;

namespace Gooner.Engine.Characters;

// A loaded VRM figure: its scene, its skeleton with the humanoid mapping, and its face. One instance
// per model, shared by every view that shows it - the start screen's pedestal and the telepathy frame
// of a session hand it to each other (CharacterView), so a model is loaded once.
public sealed class Figure
{
    private static readonly string[] Feet = ["LeftFoot", "RightFoot"];

    public Node3D Root { get; }
    public Skeleton3D? Skeleton { get; }
    public FaceRig Face { get; }
    private readonly BoneMap? _bones;

    public Figure(Node3D root)
    {
        Root = root;
        Skeleton = All<Skeleton3D>(root).FirstOrDefault(s => s.Name == "GeneralSkeleton") ?? All<Skeleton3D>(root).FirstOrDefault();
        _bones = (root.Get("vrm_meta").AsGodotObject() as Resource)?.Get("humanoid_bone_mapping").AsGodotObject() as BoneMap;
        Face = new FaceRig(All<AnimationPlayer>(root).FirstOrDefault());
        FaceTheCamera();
        SmoothCutouts(root);
    }

    // Hair cards, lashes and lace are cut out - each pixel all there or not, which sorts cleanly but
    // leaves hair with hard, stepped edges (the user saw it on Shadowheart). With the views' MSAA, alpha
    // to coverage turns the cut into a soft edge without giving up that sorting.
    private static void SmoothCutouts(Node root)
    {
        foreach (var mesh in All<MeshInstance3D>(root))
        {
            if (mesh.Mesh is null) continue;
            for (var s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                if ((mesh.GetSurfaceOverrideMaterial(s) ?? mesh.Mesh.SurfaceGetMaterial(s)) is BaseMaterial3D m
                    && m.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor)
                {
                    m.AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.AlphaToCoverageAndToOne;
                    m.AlphaAntialiasingEdge = 0.3f;
                }
        }
    }

    public int? Bone(string human)
    {
        if (Skeleton is null) return null;
        var index = Skeleton.FindBone(human);
        if (index < 0 && _bones?.GetSkeletonBoneName(human).ToString() is { Length: > 0 } own) index = Skeleton.FindBone(own);
        return index >= 0 ? index : null;
    }

    // A bone's rest position in the world its view puts it in - through the skeleton's transform, not
    // in the skeleton's own space: an export from Blender often scales or turns the armature.
    public Vector3 At(int bone) => Skeleton!.GlobalTransform * Skeleton.GetBoneGlobalRest(bone).Origin;

    // Head and the lower of the two feet, in the world; null without a head, or out of the tree.
    public (Vector3 Head, float Feet)? Measure()
    {
        if (Skeleton is null || !Skeleton.IsInsideTree() || Bone("Head") is not { } head) return null;
        var feet = Feet.Select(Bone).OfType<int>().Select(b => At(b).Y).DefaultIfEmpty(0).Min();
        return (At(head), feet);
    }

    // A turn about an axis of the model's own space, on top of what the parent bone is doing - never
    // about the bone's local axes, which differ from model to model. `keep` adds it to this frame's
    // earlier turn of the same bone instead of starting from its rest.
    public void Turn(string human, Vector3 axis, float degrees, bool keep = false)
    {
        if (Skeleton is not { } sk || Bone(human) is not { } index) return;
        var parent = sk.GetBoneParent(index);
        var parentPose = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis.Orthonormalized() : Basis.Identity;
        var parentRest = parent >= 0 ? sk.GetBoneGlobalRest(parent).Basis.Orthonormalized() : Basis.Identity;
        var from = keep ? sk.GetBoneGlobalPose(index).Basis.Orthonormalized() : parentPose * parentRest.Inverse() * sk.GetBoneGlobalRest(index).Basis.Orthonormalized();
        var desired = new Basis(axis, Mathf.DegToRad(degrees)) * from;
        sk.SetBonePoseRotation(index, (parentPose.Inverse() * desired).GetRotationQuaternion());
    }

    // --- in the skeleton's own space (+Y up, +Z towards the viewer), for reaching and gripping ---

    // Where a bone is now, this frame's turns of its parents included.
    public Vector3 Pose(int bone) => Skeleton!.GetBoneGlobalPose(bone).Origin;

    // Where a bone is at rest.
    public Vector3 Rest(int bone) => Skeleton!.GetBoneGlobalRest(bone).Origin;

    // A bone turned about an axis on top of its pose as it is now; its children go with it.
    public void Rotate(int bone, Vector3 axis, float radians)
    {
        if (Skeleton is not { } sk || axis.LengthSquared() < 1e-12f) return;
        var parent = sk.GetBoneParent(bone);
        var parentPose = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis.Orthonormalized() : Basis.Identity;
        var desired = new Basis(axis.Normalized(), radians) * sk.GetBoneGlobalPose(bone).Basis.Orthonormalized();
        sk.SetBonePoseRotation(bone, (parentPose.Inverse() * desired).GetRotationQuaternion());
    }

    // A bone turned so that its child lands on the line towards a point: the step of a reach.
    public void Aim(string human, string child, Vector3 target)
    {
        if (Bone(human) is not { } bone || Bone(child) is not { } next) return;
        var head = Pose(bone);
        var from = Pose(next) - head;
        var to = target - head;
        if (from.LengthSquared() < 1e-10f || to.LengthSquared() < 1e-10f) return;
        Rotate(bone, from.Cross(to), from.AngleTo(to));
    }

    // An arm reaching for a point with its wrist, the elbow bent out towards `pole`.
    public void Reach(string side, Vector3 wrist, Vector3 pole) => Limb($"{side}UpperArm", $"{side}LowerArm", $"{side}Hand", wrist, pole);

    // A leg set down with its ankle at a point, the knee bent towards `pole`, the foot kept flat as it
    // rests: what keeps her feet on the floor while her hips move over the leg that carries her.
    public void Plant(string side, Vector3 ankle, Vector3 pole)
    {
        Limb($"{side}UpperLeg", $"{side}LowerLeg", $"{side}Foot", ankle, pole);
        Hold($"{side}Foot");
    }

    // Two bones reaching for a point with the end of the second: the middle joint found by the bones'
    // lengths and bent out towards `pole`, then the first aimed at it and the second at the point. Out
    // of reach, the limb stretches towards it.
    private void Limb(string first, string second, string end, Vector3 target, Vector3 pole)
    {
        if (Bone(first) is not { } upper || Bone(second) is not { } lower || Bone(end) is not { } tip) return;
        var a = (Rest(lower) - Rest(upper)).Length();
        var b = (Rest(tip) - Rest(lower)).Length();
        var root = Pose(upper);
        var reach = target - root;
        var d = Math.Clamp(reach.Length(), Math.Abs(a - b) + 1e-4f, a + b - 1e-4f);
        var dir = reach.Normalized();
        var along = (a * a - b * b + d * d) / (2 * d);
        var off = MathF.Sqrt(Math.Max(0, a * a - along * along));
        var bend = (pole - dir * pole.Dot(dir)).Normalized();
        var joint = root + dir * along + bend * off;
        Aim(first, second, joint);
        Aim(second, end, root + dir * d);
    }

    // A bone moved by `offset` (skeleton space) from where it rests under its parent: her hips over the
    // leg that carries her.
    public void Shift(string human, Vector3 offset)
    {
        if (Skeleton is not { } sk || Bone(human) is not { } index) return;
        var parent = sk.GetBoneParent(index);
        var toParent = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis.Inverse() : Basis.Identity;
        sk.SetBonePosePosition(index, sk.GetBoneRest(index).Origin + toParent * offset);
    }

    // A bone turned back to the way it faces at rest in the skeleton, whatever its parents did: a foot
    // flat on the floor under a bent knee.
    public void Hold(string human)
    {
        if (Skeleton is not { } sk || Bone(human) is not { } index) return;
        var parent = sk.GetBoneParent(index);
        var parentPose = parent >= 0 ? sk.GetBoneGlobalPose(parent).Basis.Orthonormalized() : Basis.Identity;
        sk.SetBonePoseRotation(index, (parentPose.Inverse() * sk.GetBoneGlobalRest(index).Basis.Orthonormalized()).GetRotationQuaternion());
    }

    // A hand at ease, as it hangs when it holds nothing: from its rest, every finger bent a little at
    // each joint towards the palm, most at the middle one; the thumb a little in. Bent towards the palm
    // the hand itself says is its palm (Palm), so a model whose fingers rest slightly backwards still
    // curls the right way.
    public void Ease(string side)
    {
        RelaxHand(side);
        if (Palm(side) is not { } palm) return;
        foreach (var finger in Fingers)
        {
            var joints = Joints(side, finger).Select(Bone).ToList();
            float[] degrees = finger == "Thumb" ? [4, 8, 10] : [10, 22, 14];
            for (var i = 0; i < joints.Count; i++)
            {
                if (joints[i] is not { } bone) continue;
                var along = i + 1 < joints.Count && joints[i + 1] is { } next ? Pose(next) - Pose(bone)
                    : i > 0 && joints[i - 1] is { } previous ? Pose(bone) - Pose(previous) : Vector3.Zero;
                if (along.LengthSquared() < 1e-12f) continue;
                Rotate(bone, along.Cross(palm), Mathf.DegToRad(degrees[i]));
            }
        }
    }

    private static readonly string[] FingerJoints = ["Proximal", "Intermediate", "Distal"];
    private static readonly string[] ThumbJoints = ["Metacarpal", "Proximal", "Distal"];
    private static readonly string[] Fingers = ["Thumb", "Index", "Middle", "Ring", "Little"];

    private static string[] Joints(string side, string finger) =>
        [.. (finger == "Thumb" ? ThumbJoints : FingerJoints).Select(joint => $"{side}{finger}{joint}")];

    // Back to its rest under its parent: what a hand's bones need each frame before they are bent again,
    // or every frame's bend would add to the last one's.
    public void Relax(string human)
    {
        if (Skeleton is { } sk && Bone(human) is { } bone) sk.SetBonePoseRotation(bone, sk.GetBoneRest(bone).Basis.GetRotationQuaternion());
    }

    // Every finger bone of a hand the model has.
    public IEnumerable<int> HandBones(string side) =>
        Fingers.SelectMany(finger => Joints(side, finger)).Select(Bone).OfType<int>();

    public void RelaxHand(string side)
    {
        foreach (var finger in Fingers)
            foreach (var joint in Joints(side, finger)) Relax(joint);
    }

    // The way the palm faces now, from the hand's own bones: across the knuckles (little finger to index)
    // and along the hand (wrist to middle knuckle), a right hand's palm is the one, a left hand's the
    // other. That is anatomy, so it holds for any model - unlike the fingers' bend at rest, which some
    // models have slightly backwards.
    public Vector3? Palm(string side)
    {
        if (Bone($"{side}Hand") is not { } hand || Bone($"{side}MiddleProximal") is not { } middle
            || Bone($"{side}IndexProximal") is not { } index || (Bone($"{side}LittleProximal") ?? Bone($"{side}RingProximal")) is not { } outer) return null;
        var along = Pose(middle) - Pose(hand);
        var across = Pose(index) - Pose(outer);
        var palm = side == "Right" ? across.Cross(along) : along.Cross(across);
        return palm.LengthSquared() > 1e-12f ? palm.Normalized() : null;
    }

    // Where the front of the body is at a height, in skeleton space: the furthest point towards the
    // viewer among the rest pose's vertices within `halfWidth` of `middle` (across) and `band` of that
    // height - belly and clothes included, so a toy placed in front of it stands clear of both. Null
    // where there is no vertex there.
    public float? FrontAt(float middle, float height, float halfWidth, float band)
    {
        var front = float.NegativeInfinity;
        foreach (var p in RestPoints())
            if (Math.Abs(p.Y - height) < band && Math.Abs(p.X - middle) < halfWidth && p.Z > front) front = p.Z;
        return float.IsNegativeInfinity(front) ? null : front;
    }

    // Every vertex of her meshes as they rest, body and clothes, in skeleton space. Not her hair: it is
    // not solid, and long hair hanging by her sides held her arms out from them.
    public IEnumerable<Vector3> RestPoints()
    {
        if (Skeleton is not { } sk) yield break;
        var toSkeleton = sk.GlobalTransform.AffineInverse();
        foreach (var mesh in All<MeshInstance3D>(Root))
        {
            if (mesh.Mesh is not ArrayMesh am) continue;
            if (mesh.Name.ToString().Contains("hair", StringComparison.OrdinalIgnoreCase)
                || mesh.GetParent()?.Name.ToString().Contains("hair", StringComparison.OrdinalIgnoreCase) == true) continue;
            var toHere = toSkeleton * mesh.GlobalTransform;
            for (var s = 0; s < am.GetSurfaceCount(); s++)
                foreach (var v in am.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    yield return toHere * v;
        }
    }

    // A bone turned so that two directions it carries - `fromA`, and `fromB` as far as it is across
    // `fromA` - become `toA` and `toB`: aiming a hand and rolling it in one.
    public void Orient(int bone, Vector3 fromA, Vector3 fromB, Vector3 toA, Vector3 toB)
    {
        static Basis Frame(Vector3 a, Vector3 b)
        {
            var x = a.Normalized();
            var y = (b - x * b.Dot(x)).Normalized();
            return new Basis(x, y, x.Cross(y));
        }
        var turn = Frame(toA, toB) * Frame(fromA, fromB).Inverse();
        var angle = turn.GetRotationQuaternion().GetAngle();
        if (angle < 1e-5f) return;
        Rotate(bone, turn.GetRotationQuaternion().GetAxis(), angle);
    }

    // A finger closed round a rod: its joints laid one after another on the circle round the rod's axis
    // that passes through its knuckle - each segment a chord of that circle, aimed at the point one
    // segment's length further round it. `way` is the sense it goes round in, right-handed about
    // `axis`: the fingers one way, the thumb the other, as a hand closes on anything. Aiming rather
    // than bending by set angles is what makes it fit any model: long fingers or short, straight at
    // rest or curled. The circle never comes closer to the axis than `radius`, so no finger goes into
    // the rod; the thumb lies right on it. The thumb's root (its metacarpal) stays in the palm.
    public void Wrap(string side, string finger, Vector3 axis, Vector3 onAxis, float radius, int way)
    {
        var joints = Joints(side, finger).Select(Bone).ToList();
        if (finger == "Thumb") joints.RemoveAt(0);
        for (var i = 0; i < joints.Count; i++)
        {
            if (joints[i] is not { } bone) continue;
            var head = Pose(bone);
            var next = i + 1 < joints.Count ? joints[i + 1] : null;
            // The last segment has no bone at its end: it runs on as the one before it, a little shorter.
            Vector3 tip;
            float length;
            if (next is { } n)
            {
                tip = Pose(n);
                length = (Rest(n) - Rest(bone)).Length();
            }
            else if (i > 0 && joints[i - 1] is { } prev)
            {
                length = (Rest(bone) - Rest(prev)).Length() * 0.8f;
                tip = head + (head - Pose(prev)).Normalized() * length;
            }
            else continue;
            var centre = onAxis + axis * (head - onAxis).Dot(axis);
            var out_ = head - centre;
            // The thumb's root sits further out than the knuckles; round a circle through it, it would
            // swing in a wide loop over the hand. It lies on the rod instead.
            var rho = finger == "Thumb" ? radius : Math.Max(out_.Length(), radius);
            if (out_.LengthSquared() < 1e-10f || length <= 0) continue;
            out_ = out_.Normalized() * rho;
            // A chord of `length` turns that far round the circle.
            var turn = 2 * MathF.Asin(Math.Min(1, length / (2 * rho)));
            var target = centre + out_.Rotated(axis, way * turn);
            var from = tip - head;
            var to = target - head;
            if (from.LengthSquared() < 1e-10f || to.LengthSquared() < 1e-10f) continue;
            Rotate(bone, from.Cross(to), Math.Min(from.AngleTo(to), Mathf.DegToRad(110)));
        }
    }

    // Which way it faces comes from the skeleton, not the spec version: a figure facing the camera
    // (+Z) has its left arm at +X.
    private void FaceTheCamera()
    {
        if (Skeleton is null || Bone("LeftUpperArm") is not { } left || Bone("RightUpperArm") is not { } right) return;
        var toRoot = Basis.Identity;
        for (Node? n = Skeleton; n is not null && n != Root; n = n.GetParent())
            if (n is Node3D n3) toRoot = n3.Transform.Basis * toRoot;
        var across = toRoot * (Skeleton.GetBoneGlobalRest(left).Origin - Skeleton.GetBoneGlobalRest(right).Origin);
        if (across.X < 0) Root.RotateY(Mathf.Pi);
    }

    internal static IEnumerable<T> All<T>(Node node) where T : Node
    {
        if (node is T t) yield return t;
        foreach (var child in node.GetChildren())
            foreach (var found in All<T>(child)) yield return found;
    }
}

// The figure's face, driven directly rather than by playing its expressions: godot-vrm makes each VRM
// expression an animation that only writes the shapes it keys, so one played after another leaves the
// first one's shapes behind (the eyes stayed shut). Here every expression is a set of blend shapes at
// their full weight, any number of them are mixed at once - a mood, a blink, the mouth of a vowel -
// and every shape any expression touches is written each frame.
public sealed class FaceRig
{
    private readonly Dictionary<string, List<(MeshInstance3D Mesh, int Shape, float Full)>> _expressions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(MeshInstance3D Mesh, int Shape)> _touched = [];
    private readonly Dictionary<(MeshInstance3D, int), float> _sum = [];

    public FaceRig(AnimationPlayer? player)
    {
        if (player is null || player.GetNodeOrNull(player.RootNode) is not { } root) return;
        player.Stop();
        foreach (var name in player.GetAnimationList())
        {
            if (name == "RESET") continue;
            var animation = player.GetAnimation(name);
            var shapes = new List<(MeshInstance3D, int, float)>();
            for (var i = 0; i < animation.GetTrackCount(); i++)
            {
                if (animation.TrackGetType(i) != Animation.TrackType.BlendShape || animation.TrackGetKeyCount(i) == 0) continue;
                var path = animation.TrackGetPath(i);
                if (root.GetNodeOrNull(path.GetConcatenatedNames()) is not MeshInstance3D mesh) continue;
                var shape = mesh.FindBlendShapeByName(path.GetConcatenatedSubNames());
                if (shape < 0) continue;
                shapes.Add((mesh, shape, animation.TrackGetKeyValue(i, 0).AsSingle()));
                _touched.Add((mesh, shape));
            }
            if (shapes.Count > 0) _expressions[name] = shapes;
        }
    }

    public bool Has(string expression) => _expressions.ContainsKey(expression);

    // Every expression at its weight (0..1); the rest at none.
    public void Apply(IReadOnlyDictionary<string, float> weights)
    {
        _sum.Clear();
        foreach (var (name, weight) in weights)
        {
            if (weight <= 0 || !_expressions.TryGetValue(name, out var shapes)) continue;
            foreach (var (mesh, shape, full) in shapes)
                _sum[(mesh, shape)] = _sum.GetValueOrDefault((mesh, shape)) + full * weight;
        }
        foreach (var (mesh, shape) in _touched)
            if (GodotObject.IsInstanceValid(mesh)) mesh.SetBlendShapeValue(shape, Math.Min(1, _sum.GetValueOrDefault((mesh, shape))));
    }
}

// The one figure loaded at a time, by path, off the main thread. A large model takes minutes, and the
// app stays alive meanwhile. One load at a time: godot-vrm's extensions are registered globally for the
// length of one.
public static class Figures
{
    private const string Log = "Character";

    private static readonly string[] Extensions =
    [
        "res://addons/vrm/vrm_extension.gd",
        "res://addons/vrm/1.0/VRMC_vrm.gd",
        "res://addons/vrm/1.0/VRMC_node_constraint.gd",
        "res://addons/vrm/1.0/VRMC_springBone.gd",
        "res://addons/vrm/1.0/VRMC_materials_hdr_emissiveMultiplier.gd",
        "res://addons/vrm/1.0/VRMC_materials_mtoon.gd",
    ];

    // godot-vrm's extensions are registered for everything Godot reads while a model loads, so nothing
    // else is read as glTF meanwhile (the toy, Toy.cs).
    internal static readonly object OneAtATime = new();
    private static int _pending;
    private static (string Path, Task<Node3D?> Loading, Figure? Figure)? _current;

    // A load still running: quitting then has to end the process rather than take the engine down under
    // the import (Main.Quit).
    public static bool Loading => Volatile.Read(ref _pending) > 0;

    // The figure for that path once it has loaded, else null - and the load started if it is not.
    // Asking for another path lets the previous figure go.
    public static Figure? Get(string path)
    {
        if (_current is not { } current || current.Path != path)
        {
            Release();
            _current = (path, Start(path), null);
            return null;
        }
        if (current.Figure is not null) return current.Figure;
        if (!current.Loading.IsCompleted) return null;
        if (current.Loading.Result is not { } root) return null;
        var figure = new Figure(root);
        _current = (path, current.Loading, figure);
        return figure;
    }

    // Lets the figure go: on a change of model, and before the app quits.
    public static void Release()
    {
        if (_current is not { } old) return;
        _current = null;
        if (old.Figure is { } figure) figure.Root.QueueFree();
        else old.Loading.ContinueWith(done => done.Result?.CallDeferred(Node.MethodName.Free));
    }

    private static Task<Node3D?> Start(string path)
    {
        Interlocked.Increment(ref _pending);
        return Task.Run(() =>
        {
            lock (OneAtATime)
            {
                try
                {
                    return Load(path);
                }
                catch (Exception e)
                {
                    AppLog.Warning(Log, "Could not load the character {0}: {1}", System.IO.Path.GetFileName(path), AppLog.Describe(e));
                    return null;
                }
                finally
                {
                    Interlocked.Decrement(ref _pending);
                }
            }
        });
    }

    private static Node3D? Load(string path)
    {
        // Fresh extension instances for each load, in front of Godot's own runtime one, which would
        // otherwise turn the meshes into MeshInstance3D and free them before godot-vrm sees them.
        // Reversed so that, each going to the front, they end up in the plugin's own order.
        var extensions = new List<GltfDocumentExtension>();
        foreach (var script in Extensions.Reverse())
        {
            var extension = (GltfDocumentExtension)GD.Load<GDScript>(script).New();
            GltfDocument.RegisterGltfDocumentExtension(extension, firstPriority: true);
            extensions.Add(extension);
        }
        try
        {
            var document = new GltfDocument();
            var state = new GltfState();
            // godot-vrm's retarget rewrites the bone rests and leaves skinned meshes distorted at
            // runtime; the bones keep their names, and the model's humanoid mapping says which is which.
            state.SetAdditionalData("vrm/retarget", false);
            // The editor importer's flags: 8 generates tangents (blend shapes need them), 16 binds
            // skins by bone name.
            var error = document.AppendFromFile(path, state, 8u | 16u);
            if (error != Error.Ok)
            {
                AppLog.Warning(Log, "Could not load the character {0}: {1}", System.IO.Path.GetFileName(path), error);
                return null;
            }
            if (document.GenerateScene(state) is not Node3D root) return null;
            foreach (var skeleton in Figure.All<Skeleton3D>(root)) skeleton.ResetBonePoses();
            // No name: it may be one of the user's own, and a file name of theirs belongs only in a
            // warning about it.
            AppLog.Info(Log, "Character loaded");
            return root;
        }
        finally
        {
            foreach (var extension in extensions) GltfDocument.UnregisterGltfDocumentExtension(extension);
        }
    }
}
