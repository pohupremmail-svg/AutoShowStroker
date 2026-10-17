using System;
using System.Collections.Generic;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Logging;
using Godot;

namespace Gooner.Engine.Ui;

// A VRM figure standing on the start screen's pedestal, in a transparent 3D view of its own: it
// breathes, sways a little, blinks, and turns its head after the pointer. Loading is godot-vrm's
// runtime path as phase 0 proved it (GoonerSpikes/RESULTS.md, "(c) Nachtrag"); a file that will not
// load leaves the pedestal to the logo, as before.
public partial class CharacterView : SubViewportContainer
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

    // The start screen is rebuilt on a change of language; the figure is handed on to the new one
    // rather than loaded again, which takes up to a second.
    private static (string Path, Node3D Root)? _kept;

    private readonly SubViewport _viewport = new() { TransparentBg = true, OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X };
    private readonly Camera3D _camera = new() { Fov = 30, Current = true };
    private readonly string _path;
    private Node3D? _root;
    private Skeleton3D? _skeleton;
    private BoneMap? _bones;
    private AnimationPlayer? _faces;
    private string? _mood;
    // The blend shapes the model's "blink" expression closes, at the weight it closes them to.
    private readonly List<(MeshInstance3D Mesh, int Shape, float Full)> _lids = [];
    private double _time, _nextBlink = 3, _blinkedAt = -1;
    private Vector2 _gaze;

    public bool Loaded => _root is not null;

    public CharacterView(string path)
    {
        _path = path;
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
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-20, 180, 0), LightEnergy = 1.4f, LightColor = UiTheme.Of(Palette.Accent) });
        _viewport.AddChild(_camera);
        Resized += Frame;
    }

    public override void _Ready()
    {
        // The screen being replaced is only queued for deletion, so its figure is still there to take.
        if (_kept is { } kept && kept.Path == _path && IsInstanceValid(kept.Root))
        {
            kept.Root.GetParent()?.RemoveChild(kept.Root);
            Take(kept.Root);
            return;
        }
        try
        {
            if (Load(_path) is { } root) Take(root);
        }
        catch (Exception e)
        {
            AppLog.Warning(Log, "Could not load the character {0}: {1}", System.IO.Path.GetFileName(_path), e.Message);
        }
    }

    // Left where it is: a new screen takes it over, and otherwise it goes with this one.
    public override void _ExitTree()
    {
        if (_root is not null) _kept = (_path, _root);
    }

    private void Take(Node3D root)
    {
        _root = root;
        _viewport.AddChild(root);
        _skeleton = All<Skeleton3D>(root).FirstOrDefault(s => s.Name == "GeneralSkeleton") ?? All<Skeleton3D>(root).FirstOrDefault();
        _bones = (root.Get("vrm_meta").AsGodotObject() as Resource)?.Get("humanoid_bone_mapping").AsGodotObject() as BoneMap;
        _faces = All<AnimationPlayer>(root).FirstOrDefault();
        _mood = Expression("relaxed") ?? Expression("happy");
        // The mood is set once and the player held still: playing on, it writes every face shape of the
        // model each frame - the lids too, so a blink never got through.
        if (_mood is not null)
        {
            _faces!.Play(_mood);
            _faces.Advance(0);
            _faces.Pause();
        }
        FindLids();
        Frame();
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
            foreach (var skeleton in All<Skeleton3D>(root)) skeleton.ResetBonePoses();
            AppLog.Info(Log, "Character loaded: {0}", System.IO.Path.GetFileName(path));
            return root;
        }
        finally
        {
            foreach (var extension in extensions) GltfDocument.UnregisterGltfDocumentExtension(extension);
        }
    }

    // The camera, so that the feet stand on the pedestal's floor (84% of the way down, Pedestal) and
    // the head sits under the top edge, whatever the figure's height.
    private void Frame()
    {
        if (_skeleton is null || Bone("Head") is not { } head) return;
        var top = _skeleton.GetBoneGlobalRest(head).Origin.Y + 0.2f;
        var half = top / 1.48f; // feet at 0.84, top at 0.10 of the height: top = 0.74 * 2 * half
        var distance = half / MathF.Tan(Mathf.DegToRad(_camera.Fov / 2));
        _camera.Position = new Vector3(0, 0.68f * half, distance);
        FaceTheCamera();
    }

    // Which way it faces comes from the skeleton, not the spec version: a figure facing the camera
    // (+Z) has its left arm at +X.
    private void FaceTheCamera()
    {
        if (_root is null || _skeleton is null || Bone("LeftUpperArm") is not { } left || Bone("RightUpperArm") is not { } right) return;
        var across = _skeleton.GlobalTransform.Basis * (_skeleton.GetBoneGlobalRest(left).Origin - _skeleton.GetBoneGlobalRest(right).Origin);
        if (across.X < 0) _root.RotateY(Mathf.Pi);
    }

    public override void _Process(double delta)
    {
        if (_skeleton is null || !IsVisibleInTree()) return;
        _time += delta;
        var t = (float)_time;

        // The pointer, from the figure's point of view: -1..1 across the window, eased.
        var window = GetViewportRect().Size;
        var pointer = GetGlobalMousePosition() / window * 2 - Vector2.One;
        _gaze = _gaze.Lerp(new Vector2(Math.Clamp(pointer.X, -1, 1), Math.Clamp(pointer.Y, -1, 1)), (float)Math.Min(1, delta * 3));

        // Arms down from the T-pose they come in; a breath; a slow sway; the head after the pointer.
        // Model space throughout (+Y up, +Z towards the viewer), parents first.
        Turn("Hips", Vector3.Up, 3 * MathF.Sin(t * 0.5f));
        Turn("Spine", Vector3.Back, 1.5f * MathF.Sin(t * 0.5f + 1));
        Turn("Chest", Vector3.Right, -1.2f * MathF.Sin(t * 1.6f));
        Turn("LeftUpperArm", Vector3.Back, -72 + 2 * MathF.Sin(t * 1.6f));
        Turn("RightUpperArm", Vector3.Back, 72 - 2 * MathF.Sin(t * 1.6f));
        Turn("LeftLowerArm", Vector3.Right, -14);
        Turn("RightLowerArm", Vector3.Right, -14);
        Turn("Neck", Vector3.Up, 10 * _gaze.X);
        Turn("Head", Vector3.Up, 14 * _gaze.X);
        Turn("Head", Vector3.Right, 10 * _gaze.Y, keep: true);

        Blink();
    }

    // Blinking is done here, not by playing "blink": an expression only writes the shapes it keys, so
    // going back to the mood afterwards left the eyes shut for good.
    private void FindLids()
    {
        if (_faces is null || Expression("blink") is not { } name || _faces.GetNodeOrNull(_faces.RootNode) is not { } root) return;
        var blink = _faces.GetAnimation(name);
        for (var i = 0; i < blink.GetTrackCount(); i++)
        {
            if (blink.TrackGetType(i) != Animation.TrackType.BlendShape || blink.TrackGetKeyCount(i) == 0) continue;
            var path = blink.TrackGetPath(i);
            if (root.GetNodeOrNull(path.GetConcatenatedNames()) is not MeshInstance3D mesh) continue;
            var shape = mesh.FindBlendShapeByName(path.GetConcatenatedSubNames());
            if (shape >= 0) _lids.Add((mesh, shape, blink.TrackGetKeyValue(i, 0).AsSingle()));
        }
    }

    // Shut in 80 ms, held for 60, open again in 120 - and written back to nothing at the end.
    private void Blink()
    {
        if (_lids.Count == 0) return;
        if (_time >= _nextBlink)
        {
            _blinkedAt = _time;
            _nextBlink = _time + 2.5 + Random.Shared.NextDouble() * 3.5;
        }
        if (_blinkedAt < 0) return;
        var since = (float)(_time - _blinkedAt);
        var shut = since < 0.08f ? since / 0.08f : since < 0.14f ? 1 : Math.Max(0, 1 - (since - 0.14f) / 0.12f);
        foreach (var (mesh, shape, full) in _lids) mesh.SetBlendShapeValue(shape, full * shut);
        if (since >= 0.26f) _blinkedAt = -1;
    }

    private string? Expression(string name) =>
        _faces?.GetAnimationList().FirstOrDefault(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    private int? Bone(string human)
    {
        if (_skeleton is null) return null;
        var index = _skeleton.FindBone(human);
        if (index < 0 && _bones?.GetSkeletonBoneName(human).ToString() is { Length: > 0 } own) index = _skeleton.FindBone(own);
        return index >= 0 ? index : null;
    }

    // A turn about an axis of the model's own space, on top of what the parent bone is doing - never
    // about the bone's local axes, which differ from model to model. `keep` adds it to this frame's
    // earlier turn of the same bone instead of starting from its rest.
    private void Turn(string human, Vector3 axis, float degrees, bool keep = false)
    {
        if (_skeleton is null || Bone(human) is not { } index) return;
        var parent = _skeleton.GetBoneParent(index);
        var parentPose = parent >= 0 ? _skeleton.GetBoneGlobalPose(parent).Basis.Orthonormalized() : Basis.Identity;
        var parentRest = parent >= 0 ? _skeleton.GetBoneGlobalRest(parent).Basis.Orthonormalized() : Basis.Identity;
        var from = keep ? _skeleton.GetBoneGlobalPose(index).Basis.Orthonormalized() : parentPose * parentRest.Inverse() * _skeleton.GetBoneGlobalRest(index).Basis.Orthonormalized();
        var desired = new Basis(axis, Mathf.DegToRad(degrees)) * from;
        _skeleton.SetBonePoseRotation(index, (parentPose.Inverse() * desired).GetRotationQuaternion());
    }

    private static IEnumerable<T> All<T>(Node node) where T : Node
    {
        if (node is T t) yield return t;
        foreach (var child in node.GetChildren())
            foreach (var found in All<T>(child)) yield return found;
    }
}
