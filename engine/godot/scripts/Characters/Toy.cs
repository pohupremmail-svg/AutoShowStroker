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
// it is read (ToyShape) - so another model only needs dropping in. None there, or one that will not
// read, and her hand holds the stand-in CharacterView builds from primitives. Like the bundled
// characters, the props stay on this machine (gitignored) until there is one the app may pass on.
public sealed class Toy
{
    private const string Log = "Toy";
    private static Task<Toy?>? _loading;

    // The model as a scene to copy from: a node of its own kept for that never belongs to a tree, and
    // was left behind when the app quit (Godot reported its mesh and material leaked).
    private readonly PackedScene _model;

    public ToyShape Shape { get; }

    private Toy(PackedScene model, ToyShape shape)
    {
        _model = model;
        Shape = shape;
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

    private static Toy? Read(string? props)
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
