// JsonBuildRunner.cs — JSON-driven build executor.
// Partial class extension of TraditionalAmericanHome; shares all bot
// infrastructure (client, origin, buildYaw, RezAndSetPrim, etc.).
//
// CLI: dotnet run -- json [path/to/file.json] [--at-friend]

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenMetaverse;

namespace SLHouseBuilder
{
    partial class TraditionalAmericanHome
    {
        // ─────────────────────────────────────────────────────────────────────────
        //  Entry point — same bot setup as RunLegacyAsync, but loads a JSON file
        //  and calls ExecuteBuilding() instead of BuildHouse().
        // ─────────────────────────────────────────────────────────────────────────
        public static async Task RunJsonAsync(string[] args, string jsonPath)
        {
            bool atFriend = Array.Exists(args, a => a.Equals("--at-friend", StringComparison.OrdinalIgnoreCase));

            if (!File.Exists(jsonPath))
            {
                Console.Error.WriteLine($"[Builder] Building file not found: {jsonPath}");
                return;
            }

            BuildingDef def;
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
                def = JsonSerializer.Deserialize<BuildingDef>(File.ReadAllText(jsonPath), opts)!;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Builder] Failed to parse {jsonPath}: {ex.Message}");
                return;
            }

            Console.WriteLine($"[Builder] Loaded '{def.Name}' — {def.Linksets.Sum(ls => ls.Prims.Count)} prims across {def.Linksets.Count} linkset(s).");

            var credsPath = Path.Combine(AppContext.BaseDirectory, "credentials.json");
            if (!File.Exists(credsPath))
            {
                Console.Error.WriteLine($"[Builder] credentials.json not found at: {credsPath}");
                return;
            }
            var creds = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(credsPath));
            string firstName = creds.GetProperty("FirstName").GetString()!;
            string lastName  = creds.GetProperty("LastName").GetString()!;
            string password  = creds.GetProperty("Password").GetString()!;
            if (creds.TryGetProperty("FriendName", out var fnProp))
                friendName = fnProp.GetString() ?? "";

            client.Settings.LOGIN_SERVER = LOGINURI;
            client.Network.LoginProgress += OnLoginProgress;
            client.Network.SimConnected  += OnSimConnected;

            var loginParams = client.Network.DefaultLoginParams(firstName, lastName, password, "HouseBuilder", "1.0");
            Console.WriteLine("[Builder] Logging in…");
            if (!await client.Network.LoginAsync(loginParams))
            {
                Console.WriteLine("[Builder] Login failed: " + client.Network.LoginMessage);
                return;
            }

            Thread.Sleep(4000);

            if (atFriend && !string.IsNullOrWhiteSpace(friendName))
                LocateAndTeleportToFriend();
            else
            {
                if (atFriend)
                    Console.WriteLine("[Builder] --at-friend specified but FriendName is not set. Building at bot position.");
                origin = GetSettledPosition();
            }

            Console.WriteLine($"[Builder] Build origin: {origin}  yaw: {buildYaw * 180f / MathF.PI:F1}°");

            client.Objects.ObjectUpdate += OnPrimEcho;
            ExecuteBuilding(def);
            FinalizeAndLink();

            Console.WriteLine("[Builder] Done! Logging out.");
            client.Network.Logout();
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Generic executor — resolves constants/expressions, maps types, rezzes.
        // ─────────────────────────────────────────────────────────────────────────
        static void ExecuteBuilding(BuildingDef def)
        {
            Console.WriteLine($"[Builder] === Starting JSON build: {def.Name} ===");

            // ── Build constant lookup table ───────────────────────────────────────
            var constants = new Dictionary<string, float>(def.Constants);

            // Evaluate derived constants in declaration order so each can
            // reference previously resolved entries.
            if (def.Derived != null)
            {
                foreach (var (key, expr) in def.Derived)
                    constants[key] = ExprEval.Eval(expr, constants);
            }

            // ── Helpers ───────────────────────────────────────────────────────────
            float EvalEl(JsonElement e)
            {
                return e.ValueKind == JsonValueKind.Number
                    ? e.GetSingle()
                    : ExprEval.Eval(e.GetString()!, constants);
            }

            Color4 ResolveColor(PrimDef p)
            {
                if (p.ColorRgb != null && p.ColorRgb.Length >= 3)
                    return new Color4(p.ColorRgb[0], p.ColorRgb[1], p.ColorRgb[2],
                                      p.ColorRgb.Length >= 4 ? p.ColorRgb[3] : 1f);

                if (p.Color != null && def.Palette.TryGetValue(p.Color, out var rgba))
                    return new Color4(rgba[0], rgba[1], rgba[2],
                                      rgba.Length >= 4 ? rgba[3] : 1f);

                Console.WriteLine($"[Builder] Warning: prim '{p.Name}' has no color — defaulting to white.");
                return new Color4(1f, 1f, 1f, 1f);
            }

            // ── Rez every prim in every linkset ───────────────────────────────────
            foreach (var linkset in def.Linksets)
            {
                Console.WriteLine($"[Builder] Linkset '{linkset.Name}' — {linkset.Prims.Count} prims");

                foreach (var p in linkset.Prims)
                {
                    // Position & size
                    float px = EvalEl(p.Position[0]);
                    float py = EvalEl(p.Position[1]);
                    float pz = EvalEl(p.Position[2]);
                    float sx = EvalEl(p.Size[0]);
                    float sy = EvalEl(p.Size[1]);
                    float sz = EvalEl(p.Size[2]);

                    // Rotation: [pitch, yaw, roll] degrees → quaternion
                    var rot = Quaternion.Identity;
                    if (p.Rotation != null && p.Rotation.Length >= 3)
                    {
                        float pitch = p.Rotation[0] * MathF.PI / 180f;
                        float yaw   = p.Rotation[1] * MathF.PI / 180f;
                        float roll  = p.Rotation[2] * MathF.PI / 180f;
                        rot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw)
                            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, roll)
                            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);
                    }

                    // ConstructionData — varies by type
                    var cd = BuildPrimData(Color4.White);
                    switch (p.Type.ToLowerInvariant())
                    {
                        case "triangle":
                        case "prism":
                            // Gable shape: collapse the Y scale at the path top to 0
                            cd.PathScaleY = 0.0f;
                            break;

                        case "circle":
                        case "cylinder":
                        case "disc":
                            cd.ProfileCurve = ProfileCurve.Circle;
                            break;

                        case "sphere":
                            cd.ProfileCurve = ProfileCurve.HalfCircle;
                            cd.PathCurve    = PathCurve.Circle;
                            break;

                        case "box":
                        default:
                            // Already a box — nothing to change
                            break;
                    }

                    var color    = ResolveColor(p);
                    UUID? texUUID = p.Texture != null ? new UUID(p.Texture) : (UUID?)null;

                    RezAndSetPrim(cd, Offset(px, py, pz), new Vector3(sx, sy, sz),
                                  rot, color, p.Name,
                                  isLight: p.Light, glow: p.Glow, textureUUID: texUUID);
                }
            }

            Console.WriteLine("[Builder] === Build complete! ===");
        }
    }
}
