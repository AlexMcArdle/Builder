// TraditionalAmericanHome.cs
// LibreMetaverse bot script to build a traditional American story-and-a-half home
// with 2-car attached garage and 3-4 bedrooms in Second Life.
//
// HOW TO USE:
//   1. Add OpenMetaverse.dll (LibreMetaverse) as a reference in your project.
//   2. Fill in your SL bot credentials below.
//   3. Stand your bot avatar at the desired build origin in-world.
//   4. Run. The house will be built relative to the bot's current position.
//
// PRIM BUDGET: ~85 prims
// FOOTPRINT:   ~18m wide x 14m deep (house) + 9m x 12m (garage)
// HEIGHT:      ~11m ridge at peak
//
// Z CONVENTION: All building Z values are measured from origin.Z (avatar feet = ground).
//   Walls/floors start at Z=0. The foundation slab is slightly buried so it
//   always kisses the ground regardless of any SimPosition offset.

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using OpenMetaverse;

namespace SLHouseBuilder
{
    class TraditionalAmericanHome
    {
        // ── Bot credentials — loaded from credentials.json ───────────────────────
        private const string LOGINURI  = Settings.AGNI_LOGIN_SERVER;

        // ── Build settings ────────────────────────────────────────────────────────
        private const float DELAY_MS = 300f;   // ms between rez calls (be kind to the sim)

        // ── Height constants (tune to adjust overall proportions) ─────────────────
        private const float FOUNDATION_H  = 0.5f;   // slab thickness (mostly buried)
        private const float FLOOR1_H      = 4.0f;   // first-floor ceiling height
        private const float FLOOR2_DECK_H = 0.2f;   // second-floor deck thickness
        private const float KNEE_H        = 2.0f;   // half-story knee-wall height
        private const float GARAGE_H      = 3.5f;   // garage wall height

        // Cumulative Z landmarks (all relative to origin.Z = ground)
        private const float FLOOR1_TOP  = FLOOR1_H;
        private const float FLOOR2_BASE = FLOOR1_H + FLOOR2_DECK_H;
        private const float ROOF_BASE   = FLOOR1_H + FLOOR2_DECK_H + KNEE_H;

        private static GridClient client = new GridClient();
        private static Vector3    origin;   // set from avatar position at login

        // ── Colors ────────────────────────────────────────────────────────────────
        private static readonly Color4 SIDING_COLOR  = new Color4(0.93f, 0.87f, 0.78f, 1f);
        private static readonly Color4 TRIM_COLOR    = new Color4(0.95f, 0.95f, 0.92f, 1f);
        private static readonly Color4 ROOF_COLOR    = new Color4(0.22f, 0.20f, 0.20f, 1f);
        private static readonly Color4 GARAGE_COLOR  = new Color4(0.93f, 0.87f, 0.78f, 1f);
        private static readonly Color4 DOOR_COLOR    = new Color4(0.18f, 0.24f, 0.38f, 1f);
        private static readonly Color4 WINDOW_COLOR  = new Color4(0.55f, 0.72f, 0.85f, 0.5f);
        private static readonly Color4 FOUNDATION    = new Color4(0.55f, 0.54f, 0.52f, 1f);
        private static readonly Color4 CHIMNEY_COLOR = new Color4(0.60f, 0.35f, 0.28f, 1f);

        // ─────────────────────────────────────────────────────────────────────────
        static void Main(string[] args)
        {
            var credsPath = Path.Combine(AppContext.BaseDirectory, "credentials.json");
            if (!File.Exists(credsPath))
            {
                Console.Error.WriteLine($"[Builder] credentials.json not found at: {credsPath}");
                Console.Error.WriteLine("[Builder] Copy credentials.example.json -> credentials.json and fill in your details.");
                return;
            }
            var creds = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(credsPath));
            string firstName = creds.GetProperty("FirstName").GetString()!;
            string lastName  = creds.GetProperty("LastName").GetString()!;
            string password  = creds.GetProperty("Password").GetString()!;

            client.Settings.LOGIN_SERVER = LOGINURI;
            client.Network.LoginProgress += OnLoginProgress;
            client.Network.SimConnected  += OnSimConnected;

            var loginParams = client.Network.DefaultLoginParams(firstName, lastName, password,
                                                                "HouseBuilder", "1.0");
            Console.WriteLine("[Builder] Logging in…");
            if (!client.Network.Login(loginParams))
            {
                Console.WriteLine("[Builder] Login failed: " + client.Network.LoginMessage);
                return;
            }

            Thread.Sleep(4000);
            origin = client.Self.SimPosition;
            Console.WriteLine($"[Builder] Build origin: {origin}");

            BuildHouse();

            Console.WriteLine("[Builder] Done! Logging out.");
            client.Network.Logout();
        }

        // ═════════════════════════════════════════════════════════════════════════
        //  MAIN BUILD ROUTINE
        // ═════════════════════════════════════════════════════════════════════════
        static void BuildHouse()
        {
            Console.WriteLine("[Builder] === Starting build ===");

            BuildFoundation();
            BuildFirstFloorWalls();
            BuildGarage();
            BuildSecondFloorHalfStory();
            BuildMainRoof();
            BuildGarageRoof();
            BuildChimney();
            BuildFrontPorch();
            BuildWindows();
            BuildDoors();
            BuildInteriorWalls();

            Console.WriteLine("[Builder] === Build complete! ===");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  FOUNDATION  — slightly buried so it always meets the ground
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildFoundation()
        {
            Console.WriteLine("[Builder] Foundation…");

            // Center the slab so it's 75% buried, 25% above ground.
            // Top of slab = +FOUNDATION_H * 0.25 above origin.Z.
            float slabCenterZ = -FOUNDATION_H * 0.25f;

            RezBox(Offset(0,      0,  slabCenterZ), new Vector3(18f, 14f, FOUNDATION_H), FOUNDATION, "Foundation - Main");
            RezBox(Offset(13.5f, -1f, slabCenterZ), new Vector3(9f,  12f, FOUNDATION_H), FOUNDATION, "Foundation - Garage");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  FIRST FLOOR WALLS  — bottom at origin.Z (ground)
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildFirstFloorWalls()
        {
            Console.WriteLine("[Builder] First floor walls…");

            float wallH = FLOOR1_H;
            float wallT = 0.2f;
            float wallZ = wallH / 2f;   // center = half height; bottom = 0 = ground

            RezBox(Offset(0,              -7f + wallT / 2f, wallZ), new Vector3(18f,  wallT, wallH), SIDING_COLOR, "Wall - Front");
            RezBox(Offset(0,               7f - wallT / 2f, wallZ), new Vector3(18f,  wallT, wallH), SIDING_COLOR, "Wall - Rear");
            RezBox(Offset(-9f + wallT / 2f, 0,              wallZ), new Vector3(wallT, 14f,  wallH), SIDING_COLOR, "Wall - Left");
            RezBox(Offset( 9f - wallT / 2f, 0,              wallZ), new Vector3(wallT, 14f,  wallH), SIDING_COLOR, "Wall - Right");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  GARAGE  — bottom at ground
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildGarage()
        {
            Console.WriteLine("[Builder] Garage…");

            float garageH = GARAGE_H;
            float wallT   = 0.2f;
            float wallZ   = garageH / 2f;   // bottom at ground

            // Front wall panels (flanking two door openings)
            RezBox(Offset(10.25f, -6.9f, wallZ), new Vector3(1.5f, wallT, garageH), GARAGE_COLOR, "Garage Front - Left Panel");
            RezBox(Offset(13.5f,  -6.9f, wallZ), new Vector3(0.5f, wallT, garageH), GARAGE_COLOR, "Garage Front - Center Post");
            RezBox(Offset(16.75f, -6.9f, wallZ), new Vector3(1.5f, wallT, garageH), GARAGE_COLOR, "Garage Front - Right Panel");

            // Header
            RezBox(Offset(13.5f, -6.9f, garageH - 0.15f),
                   new Vector3(7f, wallT, 0.3f), TRIM_COLOR, "Garage Door Header");

            // Sectional door panels
            RezBox(Offset(11.75f, -6.85f, garageH * 0.43f),
                   new Vector3(2.8f, 0.05f, garageH * 0.86f), new Color4(0.82f, 0.82f, 0.82f, 1f), "Garage Door Left");
            RezBox(Offset(15.25f, -6.85f, garageH * 0.43f),
                   new Vector3(2.8f, 0.05f, garageH * 0.86f), new Color4(0.82f, 0.82f, 0.82f, 1f), "Garage Door Right");

            // Rear & side walls
            RezBox(Offset(13.5f,  5f - wallT / 2f, wallZ), new Vector3(9f,   wallT, garageH), GARAGE_COLOR, "Garage Rear Wall");
            RezBox(Offset(17.9f, -1f,               wallZ), new Vector3(wallT, 12f,  garageH), GARAGE_COLOR, "Garage Side Wall");
            RezBox(Offset( 9.1f, -1f,               wallZ), new Vector3(wallT, 12f,  garageH), SIDING_COLOR, "Garage Interior Shared Wall");

            // Ceiling / loft floor
            RezBox(Offset(13.5f, -1f, garageH), new Vector3(9f, 12f, 0.2f), FOUNDATION, "Garage Ceiling");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  HALF-STORY
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildSecondFloorHalfStory()
        {
            Console.WriteLine("[Builder] Half-story / upper floor…");

            float wallT = 0.2f;

            // Floor deck sits on top of first-floor walls
            RezBox(Offset(0, 0, FLOOR1_TOP), new Vector3(18f, 14f, FLOOR2_DECK_H), FOUNDATION, "Second Floor Deck");

            // Knee walls
            float kneeZ = FLOOR2_BASE + KNEE_H / 2f;
            RezBox(Offset(0, -7f + wallT / 2f, kneeZ), new Vector3(18f,  wallT, KNEE_H), SIDING_COLOR, "Knee Wall - Front");
            RezBox(Offset(0,  7f - wallT / 2f, kneeZ), new Vector3(18f,  wallT, KNEE_H), SIDING_COLOR, "Knee Wall - Rear");

            // Gable end walls (tapered)
            RezGableWall(Offset(-9f + wallT / 2f, 0, FLOOR2_BASE), new Vector3(wallT, 14f, 3.5f), SIDING_COLOR, "Gable - Left");
            RezGableWall(Offset( 9f - wallT / 2f, 0, FLOOR2_BASE), new Vector3(wallT, 14f, 3.5f), SIDING_COLOR, "Gable - Right");

            // Front dormers
            float dormerZ = FLOOR2_BASE + KNEE_H + 0.5f;
            BuildDormer(Offset(-4f, -6.5f, dormerZ), facing: -1);
            BuildDormer(Offset( 4f, -6.5f, dormerZ), facing: -1);
        }

        static void BuildDormer(Vector3 pos, int facing)
        {
            float dW = 2.4f;
            float dH = 1.6f;
            float dD = 1.4f;

            RezBox(pos, new Vector3(dW, dD, dH), SIDING_COLOR, "Dormer Body");
            RezBox(new Vector3(pos.X, pos.Y - dD * 0.3f,      pos.Z + dH - 0.1f), new Vector3(dW + 0.3f, dD * 0.7f, 0.15f), ROOF_COLOR,   "Dormer Roof Panel");
            RezBox(new Vector3(pos.X, pos.Y - dD / 2f - 0.05f, pos.Z + dH / 2f),  new Vector3(dW * 0.55f, 0.08f, dH * 0.55f), WINDOW_COLOR, "Dormer Window");
            RezBox(new Vector3(pos.X, pos.Y - dD / 2f - 0.08f, pos.Z + dH / 2f),  new Vector3(dW * 0.65f, 0.05f, dH * 0.65f), TRIM_COLOR,   "Dormer Window Trim");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  MAIN ROOF
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildMainRoof()
        {
            Console.WriteLine("[Builder] Main roof…");

            float overhang = 0.6f;

            RezTiltedRoofPanel(
                center:   Offset(0, -(14f / 4f + overhang / 2f), ROOF_BASE + 1.5f),
                size:     new Vector3(18f + overhang * 2f, 8.5f, 0.25f),
                pitchDeg: 22f, forward: true,  label: "Roof Panel - Front");

            RezTiltedRoofPanel(
                center:   Offset(0,  (14f / 4f + overhang / 2f), ROOF_BASE + 1.5f),
                size:     new Vector3(18f + overhang * 2f, 8.5f, 0.25f),
                pitchDeg: 22f, forward: false, label: "Roof Panel - Rear");

            // Ridge cap
            RezBox(Offset(0, 0, ROOF_BASE + 3.1f),
                   new Vector3(18f + overhang * 2f, 0.5f, 0.3f), ROOF_COLOR, "Roof Ridge Cap");
        }

        static void RezTiltedRoofPanel(Vector3 center, Vector3 size, float pitchDeg, bool forward, string label)
        {
            float sign = forward ? 1f : -1f;
            float rad  = pitchDeg * (float)Math.PI / 180f;
            Quaternion rot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, sign * rad);
            RezPrim(center, size, rot, ROOF_COLOR, label);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  GARAGE ROOF
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildGarageRoof()
        {
            Console.WriteLine("[Builder] Garage roof…");

            float overhang = 0.4f;

            RezTiltedRoofPanel(
                center:   Offset(13.5f, -1f, GARAGE_H + 0.6f),
                size:     new Vector3(9f + overhang * 2f, 12f + overhang * 2f, 0.25f),
                pitchDeg: 15f, forward: true, label: "Garage Roof");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  CHIMNEY
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildChimney()
        {
            Console.WriteLine("[Builder] Chimney…");

            float chimH = ROOF_BASE + 4.5f;   // starts at ground, pokes above ridge

            RezBox(Offset(-6f, 3f, chimH / 2f),
                   new Vector3(1.0f, 1.0f, chimH), CHIMNEY_COLOR, "Chimney Stack");
            RezBox(Offset(-6f, 3f, chimH + 0.1f),
                   new Vector3(1.3f, 1.3f, 0.2f), new Color4(0.3f, 0.3f, 0.3f, 1f), "Chimney Cap");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  FRONT PORCH
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildFrontPorch()
        {
            Console.WriteLine("[Builder] Front porch…");

            float deckTop    = 0.3f;                        // porch deck surface above ground
            float columnH    = FLOOR1_H * 0.75f;            // columns 75% of floor height
            float columnZ    = deckTop + columnH / 2f;
            float porchRoofZ = deckTop + columnH + 0.1f;    // just above column tops

            // Deck
            RezBox(Offset(-2.5f, -8.5f, deckTop / 2f), new Vector3(5f, 3f, deckTop), FOUNDATION, "Porch Deck");

            // Steps
            for (int i = 0; i < 3; i++)
            {
                float stepZ = i * 0.1f;
                RezBox(Offset(-2.5f, -9.9f + i * 0.45f, stepZ),
                       new Vector3(4.5f - i * 0.4f, 0.45f, 0.15f), FOUNDATION, $"Porch Step {i + 1}");
            }

            // Porch roof
            RezTiltedRoofPanel(
                center:   Offset(-2.5f, -8.5f, porchRoofZ),
                size:     new Vector3(5.6f, 3.6f, 0.18f),
                pitchDeg: 14f, forward: true, label: "Porch Roof");

            // Columns
            float[] colX = { -4.5f, -3.0f, -2.0f, -0.5f };
            foreach (float cx in colX)
                RezBox(Offset(cx, -7.9f, columnZ), new Vector3(0.2f, 0.2f, columnH), TRIM_COLOR, "Porch Column");

            // Railing
            RezBox(Offset(-2.5f, -7.9f, deckTop + 0.9f), new Vector3(5f, 0.05f, 0.1f), TRIM_COLOR, "Porch Railing");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  WINDOWS
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildWindows()
        {
            Console.WriteLine("[Builder] Windows…");

            float winZ  = FLOOR1_H * 0.5f;   // window center at mid-wall
            float wallT = 0.22f;
            float winH  = 1.6f;

            // Front facade
            RezWindow(    Offset(-5.5f, -7f, winZ), wallT, 2.2f, winH, "Bay Window Left");
            RezWindow(    Offset(-7.0f, -7f, winZ), wallT, 1.0f, winH, "Bay Window Right");
            RezWindow(    Offset( 2.5f, -7f, winZ), wallT, 1.8f, winH, "Front Right Window");

            // Rear facade
            RezWindow(    Offset(-5f,  7f, winZ), wallT, 2.0f, winH, "Rear Left Window");
            RezWindow(    Offset( 0f,  7f, winZ), wallT, 2.0f, winH, "Rear Center Window");
            RezWindow(    Offset( 5f,  7f, winZ), wallT, 2.0f, winH, "Rear Right Window");

            // Left side
            RezWindowSide(Offset(-9f, -3f, winZ), wallT, 1.4f, winH, "Left Side Window 1");
            RezWindowSide(Offset(-9f,  3f, winZ), wallT, 1.4f, winH, "Left Side Window 2");
        }

        static void RezWindow(Vector3 center, float wallT, float w, float h, string label)
        {
            RezBox(new Vector3(center.X, center.Y - wallT / 2f - 0.01f, center.Z), new Vector3(w,       0.05f, h),       WINDOW_COLOR, label + " Glass");
            RezBox(new Vector3(center.X, center.Y - wallT / 2f - 0.03f, center.Z), new Vector3(w + 0.2f, 0.04f, h + 0.2f), TRIM_COLOR, label + " Trim");
        }

        static void RezWindowSide(Vector3 center, float wallT, float w, float h, string label)
        {
            RezBox(new Vector3(center.X - wallT / 2f - 0.01f, center.Y, center.Z), new Vector3(0.05f, w,       h),       WINDOW_COLOR, label + " Glass");
            RezBox(new Vector3(center.X - wallT / 2f - 0.03f, center.Y, center.Z), new Vector3(0.04f, w + 0.2f, h + 0.2f), TRIM_COLOR, label + " Trim");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  DOORS
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildDoors()
        {
            Console.WriteLine("[Builder] Doors…");

            float wallT = 0.22f;
            float doorH = 2.4f;
            float doorZ = doorH / 2f;   // bottom at ground

            // Front door
            RezBox(Offset(-2.5f, -7f,        doorZ), new Vector3(1.0f,        wallT + 0.02f, doorH),       DOOR_COLOR, "Front Door");
            RezBox(Offset(-2.5f, -7f - 0.04f, doorZ), new Vector3(1.3f,       0.04f,         doorH + 0.4f), TRIM_COLOR, "Front Door Frame");

            // Interior garage door
            RezBox(Offset(9f, -1f, doorH / 2f), new Vector3(0.05f, 0.9f, doorH), DOOR_COLOR, "Garage Interior Door");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  INTERIOR WALLS
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildInteriorWalls()
        {
            Console.WriteLine("[Builder] Interior walls…");

            float wallH = FLOOR1_H;
            float wallT = 0.15f;
            float wallZ = wallH / 2f;  // bottom at ground

            RezBox(Offset( 2f,   0,    wallZ), new Vector3(wallT, 14f, wallH), TRIM_COLOR, "Interior - Center Spine Wall");
            RezBox(Offset(-4f,  -2f,   wallZ), new Vector3(5f,  wallT,  wallH), TRIM_COLOR, "Interior - Living-Dining Divider");
            RezBox(Offset( 5f,   2f,   wallZ), new Vector3(7f,  wallT,  wallH), TRIM_COLOR, "Interior - Kitchen Rear");
            RezBox(Offset(-4.5f, 3.5f, wallZ), new Vector3(9f,  wallT,  wallH), TRIM_COLOR, "Interior - Master Divider");
            RezBox(Offset( 5.5f, 0,    wallZ), new Vector3(wallT, 6f,   wallH), TRIM_COLOR, "Interior - Bath-Laundry Wall");

            // Staircase landing
            RezBox(Offset(2.5f, -3f, wallH * 0.3f), new Vector3(2.5f, 3.5f, 0.12f), FOUNDATION, "Staircase Landing");
        }

        // ═════════════════════════════════════════════════════════════════════════
        //  PRIMITIVE REZ HELPERS
        // ═════════════════════════════════════════════════════════════════════════

        static void RezBox(Vector3 pos, Vector3 size, Color4 color, string description)
        {
            RezPrim(pos, size, Quaternion.Identity, color, description);
        }

        static void RezGableWall(Vector3 pos, Vector3 size, Color4 color, string description)
        {
            var prim = BuildPrimData(color);
            prim.PathTaperX = 1.0f;  // tapers top to a point (triangle gable)
            RezAndSetPrim(prim, pos, size, Quaternion.Identity, description);
        }

        static void RezPrim(Vector3 pos, Vector3 size, Quaternion rot, Color4 color, string description)
        {
            var prim = BuildPrimData(color);
            RezAndSetPrim(prim, pos, size, rot, description);
        }

        static Primitive.ConstructionData BuildPrimData(Color4 color)
        {
            var cd = new Primitive.ConstructionData();
            cd.PCode          = PCode.Prim;
            cd.Material       = Material.Wood;
            cd.ProfileCurve   = ProfileCurve.Square;
            cd.PathCurve      = PathCurve.Line;
            cd.ProfileBegin   = 0.0f;
            cd.ProfileEnd     = 1.0f;
            cd.PathBegin      = 0.0f;
            cd.PathEnd        = 1.0f;
            // Must be 1.0 — C# defaults these to 0, which makes everything pyramid-shaped
            cd.PathScaleX     = 1.0f;
            cd.PathScaleY     = 1.0f;
            // Explicit zeroes so gable taper doesn't bleed to other prims
            cd.PathTaperX     = 0.0f;
            cd.PathTaperY     = 0.0f;
            cd.PathShearX     = 0.0f;
            cd.PathShearY     = 0.0f;
            cd.PathTwistBegin = 0;
            cd.PathTwist      = 0;
            cd.ProfileHollow  = 0.0f;
            return cd;
        }

        static void RezAndSetPrim(Primitive.ConstructionData cd, Vector3 pos, Vector3 size, Quaternion rot, string description)
        {
            client.Self.Movement.SendUpdate();
            client.Objects.AddPrim(client.Network.CurrentSim, cd, UUID.Zero, pos, size, rot);
            Thread.Sleep((int)DELAY_MS);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  COORDINATE HELPER
        // ─────────────────────────────────────────────────────────────────────────
        static Vector3 Offset(float x, float y, float z)
            => new Vector3(origin.X + x, origin.Y + y, origin.Z + z);

        // ─────────────────────────────────────────────────────────────────────────
        //  LOGIN CALLBACKS
        // ─────────────────────────────────────────────────────────────────────────
        static void OnLoginProgress(object sender, LoginProgressEventArgs e)
            => Console.WriteLine($"[Login] {e.Status}: {e.Message}");

        static void OnSimConnected(object sender, SimConnectedEventArgs e)
            => Console.WriteLine($"[Network] Connected to sim: {e.Simulator.Name}");
    }
}


// ═══════════════════════════════════════════════════════════════════════════════
//  HOUSE LAYOUT REFERENCE
// ═══════════════════════════════════════════════════════════════════════════════
//
//  ┌─────────────────────────┬──────────┐
//  │ Bedroom 3  │ Bedroom 4  │          │
//  │  (loft)    │  (loft)    │  Garage  │
//  ├────────────┴────────────┤  (2-car) │
//  │  Master Bedroom (rear)  │          │
//  ├─────────────┬───────────┴──────────┘
//  │  Kitchen /  │
//  │  Dining     │
//  ├─────────────┤
//  │  Living Rm  │
//  └──────┬──────┘
//         │ Porch  ▼ Front (south)
