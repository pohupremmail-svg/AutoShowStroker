using System.Numerics;
using Gooner.Core.Localization;

namespace Gooner.Core.Characters;

// One colour of the app's own toy, in sRGB: the shaft (and lower down, towards the foot), the head,
// the veins and the sac. What a vertex gets is mixed from them by what part it is (ToyMesh.Regions) -
// in the toy's shader (godot/shaders/toy.gdshader, which mixes the same way), so a colour picked in
// the settings is on it at once.
public sealed record ToyTint(Vector3 Body, Vector3 BodyLow, Vector3 Head, Vector3 Vein, Vector3 Sac)
{
    // Regions: X head, Y vein, Z sac, W near the foot.
    public Vector3 Albedo(Vector4 regions)
    {
        var colour = Vector3.Lerp(Body, BodyLow, regions.W);
        colour = Vector3.Lerp(colour, Sac, regions.Z);
        colour = Vector3.Lerp(colour, Head, regions.X);
        return Vector3.Lerp(colour, Vein, VeinShare * regions.Y);
    }

    // How far a vein's colour goes: it is skin over a vein, not the vein itself.
    public const float VeinShare = 0.55f;
}

// The colours the app's own toy comes in (Settings > Look): skin from dark to rosy, then colours no
// skin has - the user's list, 2026-10-24: "von dunkelhäutig über rosafarben bis hin zu
// fantasiefarben, wie Oger-farben, Orc, Pink, Blau".
public static class ToyColours
{
    public const string Default = "fair";

    public static IReadOnlyList<string> Skin { get; } = ["ebony", "dark", "brown", "tan", "olive", "fair", "rosy"];

    public static IReadOnlyList<string> Fantasy { get; } = ["ogre", "orc", "pink", "blue", "purple"];

    public static IReadOnlyList<string> Names { get; } = [.. Skin, .. Fantasy];

    // Translated where they are shown.
    public static IReadOnlyDictionary<string, string> Labels { get; } = new Dictionary<string, string>
    {
        ["ebony"] = L10n.N("Ebony"),
        ["dark"] = L10n.N("Dark brown"),
        ["brown"] = L10n.N("Brown"),
        ["tan"] = L10n.N("Tan"),
        ["olive"] = L10n.N("Olive"),
        ["fair"] = L10n.N("Fair"),
        ["rosy"] = L10n.N("Rosy"),
        ["ogre"] = L10n.N("Ogre"),
        ["orc"] = L10n.N("Orc"),
        ["pink"] = L10n.N("Pink"),
        ["blue"] = L10n.N("Blue"),
        ["purple"] = L10n.N("Purple"),
    };

    private static readonly Dictionary<string, ToyTint> Tints = new()
    {
        ["ebony"] = new(C(0.25f, 0.16f, 0.11f), C(0.21f, 0.13f, 0.09f), C(0.31f, 0.14f, 0.14f), C(0.18f, 0.11f, 0.11f), C(0.21f, 0.13f, 0.09f)),
        ["dark"] = new(C(0.40f, 0.25f, 0.17f), C(0.35f, 0.21f, 0.14f), C(0.44f, 0.20f, 0.20f), C(0.29f, 0.18f, 0.18f), C(0.34f, 0.21f, 0.14f)),
        ["brown"] = new(C(0.58f, 0.38f, 0.26f), C(0.52f, 0.33f, 0.22f), C(0.57f, 0.31f, 0.28f), C(0.42f, 0.27f, 0.27f), C(0.50f, 0.32f, 0.22f)),
        ["tan"] = new(C(0.76f, 0.55f, 0.40f), C(0.70f, 0.49f, 0.35f), C(0.74f, 0.42f, 0.38f), C(0.56f, 0.40f, 0.42f), C(0.67f, 0.46f, 0.34f)),
        ["olive"] = new(C(0.76f, 0.64f, 0.42f), C(0.70f, 0.57f, 0.37f), C(0.72f, 0.48f, 0.40f), C(0.56f, 0.46f, 0.42f), C(0.68f, 0.54f, 0.36f)),
        ["fair"] = new(C(0.90f, 0.68f, 0.60f), C(0.84f, 0.60f, 0.53f), C(0.86f, 0.50f, 0.51f), C(0.68f, 0.47f, 0.58f), C(0.80f, 0.56f, 0.50f)),
        ["rosy"] = new(C(0.95f, 0.72f, 0.70f), C(0.90f, 0.64f, 0.62f), C(0.90f, 0.48f, 0.55f), C(0.74f, 0.50f, 0.62f), C(0.88f, 0.62f, 0.62f)),
        ["ogre"] = new(C(0.56f, 0.58f, 0.38f), C(0.48f, 0.50f, 0.32f), C(0.64f, 0.50f, 0.38f), C(0.38f, 0.42f, 0.30f), C(0.50f, 0.52f, 0.34f)),
        ["orc"] = new(C(0.36f, 0.56f, 0.28f), C(0.30f, 0.48f, 0.23f), C(0.28f, 0.42f, 0.21f), C(0.22f, 0.38f, 0.20f), C(0.32f, 0.50f, 0.25f)),
        ["pink"] = new(C(0.98f, 0.45f, 0.70f), C(0.94f, 0.38f, 0.64f), C(0.90f, 0.26f, 0.56f), C(0.80f, 0.25f, 0.55f), C(0.95f, 0.40f, 0.66f)),
        ["blue"] = new(C(0.30f, 0.55f, 0.95f), C(0.25f, 0.47f, 0.88f), C(0.20f, 0.38f, 0.84f), C(0.18f, 0.32f, 0.70f), C(0.27f, 0.50f, 0.92f)),
        ["purple"] = new(C(0.62f, 0.36f, 0.88f), C(0.55f, 0.30f, 0.82f), C(0.48f, 0.22f, 0.76f), C(0.42f, 0.20f, 0.62f), C(0.58f, 0.33f, 0.85f)),
    };

    private static Vector3 C(float r, float g, float b) => new(r, g, b);

    public static ToyTint Of(string name) => Tints.TryGetValue(name, out var tint) ? tint : Tints[Default];
}
