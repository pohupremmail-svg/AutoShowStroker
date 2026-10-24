using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gooner.Core.Characters;
using Gooner.Core.Logging;
using Godot;

namespace Gooner.Engine.Characters;

// The toy in her hand: the first .glb in res/props, read once off the main thread and measured where
// it is read (ToyShape) - so another model only needs dropping in. Like the bundled characters, the
// props stay on this machine (gitignored, never packaged). None there, or one that will not read, and
// her hand holds the app's own (Gooner.Core.Characters.ToyModel), built from code and measured the same
// way - the one every package has. Only if even that fails does CharacterView build its stand-in.
public sealed class Toy
{
    private const string Log = "Toy";
    private static Task<Toy?>? _loading;

    // The model as a scene to copy from: a node of its own kept for that never belongs to a tree, and
    // was left behind when the app quit (Godot reported its mesh and material leaked).
    private readonly PackedScene _model;

    public ToyShape Shape { get; }

    // The app's own toy's material, shared by every copy of it; null for a model from res/props, which
    // keeps the colours it came with.
    private readonly ShaderMaterial? _material;
    private string? _tint;

    private Toy(PackedScene model, ToyShape shape, ShaderMaterial? material = null)
    {
        _model = model;
        Shape = shape;
        _material = material;
    }

    // The colour the settings ask for; the window sets it every frame, and a copy is made in it.
    public static string Wanted { get; set; } = ToyColours.Default;

    // The colour picked under Settings > Look (ToyColours), on every copy at once. Cheap to call every
    // frame: it only touches the material when the colour changes.
    public void Tint(string name)
    {
        if (_material is null || name == _tint) return;
        _tint = name;
        var tint = ToyColours.Of(name);
        foreach (var (parameter, colour) in new[] { ("body", tint.Body), ("body_low", tint.BodyLow), ("head", tint.Head), ("vein", tint.Vein), ("sac", tint.Sac) })
            _material.SetShaderParameter(parameter, new Color(colour.X, colour.Y, colour.Z));
        _material.SetShaderParameter("vein_share", ToyTint.VeinShare);
    }

    // Starts reading the toy, once.
    public static void Load(string? props) => _loading ??= Task.Run(() => Read(props));

    // Whether reading has finished, and what it gave: a toy, or null for the stand-in.
    public static bool Settled => _loading is { IsCompleted: true };
    public static Toy? Loaded => _loading is { IsCompletedSuccessfully: true } done ? done.Result : null;

    // A copy of the model at `scale`, its foot at the node's origin and its shaft up +Y, its balls
    // towards +Z - the side CharacterView turns towards her.
    public Node3D Make(float scale)
    {
        Tint(Wanted);
        var holder = new Node3D();
        var model = _model.Instantiate<Node3D>();
        var turn = Shape.BallsTowardsViewer ? Basis.Identity : new Basis(Vector3.Up, Mathf.Pi);
        var foot = new Vector3(Shape.Foot.X, Shape.Foot.Y, Shape.Foot.Z);
        model.Transform = new Transform3D(turn.Scaled(Vector3.One * scale), -(turn * foot) * scale);
        holder.AddChild(model);
        return holder;
    }

    // Where the middle of its shaft is a share of the way up, in the node Make gave, at the same scale.
    public Vector3 Centre(float share, float scale)
    {
        var turn = Shape.BallsTowardsViewer ? Basis.Identity : new Basis(Vector3.Up, Mathf.Pi);
        var at = Shape.CentreAt(share) - Shape.Foot;
        return turn * new Vector3(at.X, at.Y, at.Z) * scale;
    }

    private static Toy? Read(string? props) => FromProps(props) ?? Built();

    // The app's own, from ToyModel: one mesh, coloured per vertex, in a silicone that glows a little
    // where light comes through it.
    private static Toy? Built()
    {
        try
        {
            var built = ToyModel.Build();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = built.Positions.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = built.Normals.Select(n => new Vector3(n.X, n.Y, n.Z)).ToArray();
            // Not a colour but what part each vertex is; toy.gdshader colours it (see Tint).
            arrays[(int)Mesh.ArrayType.Color] = built.Regions.Select(r => new Color(r.X, r.Y, r.Z, r.W)).ToArray();
            // Godot's front faces wind clockwise; ToyModel's counter-clockwise, seen from outside.
            var indices = new int[built.Indices.Count];
            for (var t = 0; t < indices.Length; t += 3)
                (indices[t], indices[t + 1], indices[t + 2]) = (built.Indices[t], built.Indices[t + 2], built.Indices[t + 1]);
            arrays[(int)Mesh.ArrayType.Index] = indices;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/toy.gdshader") };
            mesh.SurfaceSetMaterial(0, material);
            var model = new Node3D { Name = "Toy" };
            var instance = new MeshInstance3D { Name = "Mesh", Mesh = mesh };
            model.AddChild(instance);
            instance.Owner = model;
            if (ToyShape.Measure(built.Positions) is not { } shape)
            {
                model.Free();
                return null;
            }
            var packed = new PackedScene();
            var packing = packed.Pack(model);
            model.Free();
            if (packing != Error.Ok) return null;
            AppLog.Info(Log, "Toy built");
            return new Toy(packed, shape, material);
        }
#pragma warning disable CA1031 // Without it her hand holds the stand-in; nothing here may cost her the frame.
        catch (Exception e)
#pragma warning restore CA1031
        {
            AppLog.Warning(Log, "Could not build the toy: {0}", AppLog.Describe(e));
            return null;
        }
    }

    private static Toy? FromProps(string? props)
    {
        if (props is null || !Directory.Exists(props)) return null;
        var path = Directory.GetFiles(props, "*.glb").Order(StringComparer.Ordinal).FirstOrDefault();
        if (path is null) return null;
        lock (Figures.OneAtATime)
        {
            try
            {
                var document = new GltfDocument();
                var state = new GltfState();
                var error = document.AppendFromFile(path, state);
                if (error != Error.Ok)
                {
                    AppLog.Warning(Log, "Could not load the toy {0}: {1}", Path.GetFileName(path), error);
                    return null;
                }
                if (document.GenerateScene(state) is not Node3D model) return null;
                var points = new List<System.Numerics.Vector3>();
                foreach (var mesh in Figure.All<MeshInstance3D>(model))
                {
                    if (mesh.Mesh is not ArrayMesh am) continue;
                    var toModel = Transform3D.Identity;
                    for (Node? n = mesh; n is Node3D n3 && n != model; n = n.GetParent()) toModel = n3.Transform * toModel;
                    for (var s = 0; s < am.GetSurfaceCount(); s++)
                        foreach (var v in am.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                        {
                            var p = toModel * v;
                            points.Add(new(p.X, p.Y, p.Z));
                        }
                }
                if (ToyShape.Measure(points) is not { } shape)
                {
                    model.Free();
                    return null;
                }
                var packed = new PackedScene();
                var packing = packed.Pack(model);
                model.Free();
                if (packing != Error.Ok) return null;
                AppLog.Info(Log, "Toy loaded");
                return new Toy(packed, shape);
            }
            catch (Exception e)
            {
                AppLog.Warning(Log, "Could not load the toy {0}: {1}", Path.GetFileName(path), AppLog.Describe(e));
                return null;
            }
        }
    }
}
