// BuildingDef.cs — JSON data model for a building definition.
//
// position/size elements can be either JSON numbers (e.g. 1.5) or
// expression strings (e.g. "WALL_BASE + FLOOR1_H / 2") that reference
// entries in the constants/derived dictionaries.
//
// rotation is always [pitch, yaw, roll] in degrees. Omit if identity.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SLHouseBuilder
{
    class BuildingDef
    {
        [JsonPropertyName("name")]      public string                     Name      { get; set; } = "";
        [JsonPropertyName("constants")] public Dictionary<string, float>  Constants { get; set; } = new();

        // Derived constants — evaluated in order; each may reference earlier
        // entries and anything already in Constants.
        // Example: { "WALL_BASE": "FOUNDATION_H", "FLOOR1_TOP": "WALL_BASE + FLOOR1_H" }
        [JsonPropertyName("derived")]   public Dictionary<string, string>? Derived  { get; set; }

        [JsonPropertyName("palette")]   public Dictionary<string, float[]> Palette  { get; set; } = new();
        [JsonPropertyName("linksets")]  public List<LinksetDef>             Linksets { get; set; } = new();
    }

    class LinksetDef
    {
        [JsonPropertyName("name")]  public string        Name  { get; set; } = "";
        [JsonPropertyName("root")]  public string        Root  { get; set; } = "";
        [JsonPropertyName("prims")] public List<PrimDef> Prims { get; set; } = new();
    }

    class PrimDef
    {
        [JsonPropertyName("name")]     public string       Name    { get; set; } = "";

        // Prim shape — maps to SL profile/path type:
        //   "box"      → square profile, line path  (default)
        //   "triangle" → PathScaleY=0 taper (gable / prism shape)
        //   "circle"   → circle profile, line path  (cylinder / disc)
        //   "sphere"   → half-circle profile, circle path
        [JsonPropertyName("type")]     public string       Type    { get; set; } = "box";

        // Each element is either a JSON number or an expression string.
        [JsonPropertyName("position")] public JsonElement[] Position { get; set; } = new JsonElement[3];
        [JsonPropertyName("size")]     public JsonElement[] Size     { get; set; } = new JsonElement[3];

        // [pitch, yaw, roll] in degrees.  Omit or null for identity rotation.
        [JsonPropertyName("rotation")] public float[]?     Rotation { get; set; }

        // Named palette key — use this OR color_rgb, not both.
        [JsonPropertyName("color")]     public string?  Color    { get; set; }

        // Direct RGBA [r, g, b] or [r, g, b, a] — overrides color if present.
        [JsonPropertyName("color_rgb")] public float[]? ColorRgb { get; set; }

        [JsonPropertyName("light")]   public bool    Light   { get; set; }
        [JsonPropertyName("glow")]    public float   Glow    { get; set; }

        // UUID string for a face texture.
        [JsonPropertyName("texture")] public string? Texture { get; set; }
    }
}
