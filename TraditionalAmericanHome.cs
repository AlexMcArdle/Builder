// TraditionalAmericanHome.cs
// LibreMetaverse bot script to build a traditional American story-and-a-half home
// with 2-car attached garage and 3-4 bedrooms in Second Life.
//
// HOW TO USE:
//   1. Add OpenMetaverse.dll (LibreMetaverse) as a reference in your project.
//   2. Fill in your SL bot credentials in the credentials file.
//   3. Stand your bot avatar at the desired build origin in-world.
//      (Optional) Set a FriendName in the credentials file and run with `dotnet run -- --at-friend` to build at their location instead of the bot's current position.
//   4. Run. The house will be built relative to the bot's current position.
//
// PRIM BUDGET: ~145 prims
// FOOTPRINT:   ~18m wide x 14m deep (house) + 9m x 12m (garage)
// HEIGHT:      ~11m ridge at peak
//
// Z CONVENTION: All building Z values are measured from origin.Z (avatar feet = ground).
//   Walls/floors start at Z=0. The foundation slab is slightly buried so it
//   always kisses the ground regardless of any SimPosition offset.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenMetaverse;

namespace SLHouseBuilder
{
    partial class TraditionalAmericanHome
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

        // Cumulative Z landmarks (all relative to origin.Z)
        // Foundation sits ON origin.Z: bottom = origin.Z, center = origin.Z + FOUNDATION_H/2, top = origin.Z + FOUNDATION_H
        private const float WALL_BASE   = FOUNDATION_H;                    // walls start at foundation top
        private const float FLOOR1_TOP  = WALL_BASE + FLOOR1_H;
        private const float FLOOR2_BASE = WALL_BASE + FLOOR1_H + FLOOR2_DECK_H;
        private const float ROOF_BASE   = WALL_BASE + FLOOR1_H + FLOOR2_DECK_H + KNEE_H;

        // ── Friend-build settings ─────────────────────────────────────────────────
        private static string friendName = "";   // loaded from credentials.json "FriendName"; empty = build at bot position

        // ── Pending-prim tracking ─────────────────────────────────────────────────
        // Prims are rezzed as fast as the rate-limit allows.  Echo packets are
        // matched back to pending entries by center-position + scale, then names
        // and linking happen in one pass after the build loop finishes.
        private class PendingPrim
        {
            public readonly Vector3 Center;       // world-space center (what server echoes)
            public readonly Vector3 Scale;
            public readonly string  Description;
            public readonly Color4  Color;
            public readonly bool    IsLight;
            public readonly float   Glow;
            public readonly UUID?   TextureUUID;  // if set, applied as face texture instead of blank
            public uint LocalID;                  // 0 until echo arrives
            public PendingPrim(Vector3 c, Vector3 s, string d, Color4 color, bool isLight = false, float glow = 0f, UUID? textureUUID = null)
                { Center = c; Scale = s; Description = d; Color = color; IsLight = isLight; Glow = glow; TextureUUID = textureUUID; }
        }

        private static GridClient         client       = new GridClient();
        private static Vector3            origin;       // set from avatar position at login
        private static float              buildYaw;     // radians — rotates the whole build around Z
        private static List<PendingPrim>  pendingPrims = new();
        private static readonly object    primLock     = new();

        // ── Casing (door/window trim frame) dimensions ────────────────────────────
        private const float CASING_W = 0.10f;   // width of each casing piece (≈4 inches)
        private const float CASING_T = 0.04f;   // thickness protruding from wall face

        // ── Colors ────────────────────────────────────────────────────────────────
        private static readonly Color4 SIDING_COLOR  = new Color4(0.93f, 0.87f, 0.78f, 1f);
        private static readonly Color4 TRIM_COLOR    = new Color4(0.95f, 0.95f, 0.92f, 1f);
        private static readonly Color4 ROOF_COLOR    = new Color4(0.22f, 0.20f, 0.20f, 1f);
        private static readonly Color4 GARAGE_COLOR  = new Color4(0.93f, 0.87f, 0.78f, 1f);


        private static readonly Color4 FOUNDATION    = new Color4(0.55f, 0.54f, 0.52f, 1f);
        private static readonly Color4 CHIMNEY_COLOR = new Color4(0.60f, 0.35f, 0.28f, 1f);

        // ─────────────────────────────────────────────────────────────────────────
        public static async Task RunLegacyAsync(string[] args)
        {
            bool atFriend = Array.Exists(args, a => a.Equals("--at-friend", StringComparison.OrdinalIgnoreCase));

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
            if (creds.TryGetProperty("FriendName", out var fnProp))
                friendName = fnProp.GetString() ?? "";


            client.Settings.LOGIN_SERVER = LOGINURI;
            client.Network.LoginProgress += OnLoginProgress;
            client.Network.SimConnected  += OnSimConnected;

            var loginParams = client.Network.DefaultLoginParams(firstName, lastName, password,
                                                                "HouseBuilder", "1.0");
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
                    Console.WriteLine("[Builder] --at-friend specified but FriendName is not set in credentials.json. Building at bot position.");
                origin = GetSettledPosition();
            }

            Console.WriteLine($"[Builder] Build origin: {origin}  yaw: {buildYaw * 180f / MathF.PI:F1}°");

            client.Objects.ObjectUpdate += OnPrimEcho;
            BuildHouse();
            FinalizeAndLink();   // waits for echoes, names everything, then links

            Console.WriteLine("[Builder] Done! Logging out.");
            client.Network.Logout();
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  ECHO HANDLER  — matches incoming ObjectUpdate packets to pending prims
        // ─────────────────────────────────────────────────────────────────────────
        static void OnPrimEcho(object? s, PrimEventArgs e)
        {
            lock (primLock)
            {
                foreach (var p in pendingPrims)
                {
                    if (p.LocalID == 0 &&
                        Vector3.Distance(e.Prim.Position, p.Center) < 0.15f &&
                        Vector3.Distance(e.Prim.Scale,    p.Scale)  < 0.05f)
                    {
                        p.LocalID = e.Prim.LocalID;
                        break;
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  FINALIZE  — wait for all echoes, rename every prim, then link
        // ─────────────────────────────────────────────────────────────────────────
        static void FinalizeAndLink()
        {
            // Wait until every prim has been matched or we hit the timeout
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                lock (primLock)
                {
                    if (pendingPrims.TrueForAll(p => p.LocalID != 0)) break;
                }
                Thread.Sleep(200);
            }

            client.Objects.ObjectUpdate -= OnPrimEcho;

            List<uint> ids;
            lock (primLock)
            {
                int matched = pendingPrims.FindAll(p => p.LocalID != 0).Count;
                Console.WriteLine($"[Builder] Reconciled {matched}/{pendingPrims.Count} prims.");

                foreach (var p in pendingPrims)
                {
                    if (p.LocalID == 0)
                    {
                        Console.WriteLine($"[Builder] Warning: '{p.Description}' was not matched — skipping.");
                        continue;
                    }
                    client.Objects.SetName(client.Network.CurrentSim, p.LocalID, p.Description);
                    Thread.Sleep(50);
                    client.Objects.SetDescription(client.Network.CurrentSim, p.LocalID, p.Description);
                    Thread.Sleep(50);

                    // Apply color/alpha/glow/fullbright — BuildPrimData doesn't set textures, so we do it here
                    var te = new Primitive.TextureEntry(p.TextureUUID ?? UUID.Zero);
                    te.DefaultTexture.RGBA = p.Color;
                    if (p.Glow > 0f)  te.DefaultTexture.Glow       = p.Glow;
                    if (p.IsLight || p.TextureUUID.HasValue) te.DefaultTexture.Fullbright = true;
                    client.Objects.SetTextures(client.Network.CurrentSim, p.LocalID, te);
                    Thread.Sleep(100);

                    if (p.IsLight)
                    {
                        client.Objects.SetLight(client.Network.CurrentSim, p.LocalID,
                            new Primitive.LightData
                            {
                                Color     = p.Color,
                                Intensity = 1.0f,
                                Radius    = 10.0f,
                                Cutoff    = 0.0f,
                                Falloff   = 0.75f
                            });
                        Thread.Sleep(50);
                    }
                }

                // Foundation is pendingPrims[0] — first in list = root in SL's ObjectLink packet
                ids = pendingPrims.FindAll(p => p.LocalID != 0).ConvertAll(p => p.LocalID);
            }

            if (ids.Count < 2) { Console.WriteLine("[Builder] Not enough matched prims to link."); return; }

            uint rootID = ids[0];   // Foundation — first rezzed, root of the linkset

            // Select all prims (SL requires objects to be selected before linking)
            client.Objects.SelectObjects(client.Network.CurrentSim, ids.ToArray());
            Thread.Sleep(2000);  // give the server time to process the selection

            // Watch for the link confirmation: the server sends ObjectUpdate packets
            // for the newly linked prims, with ParentID set to the root's LocalID.
            // We require at least 3 children confirmed to avoid false positives.
            int linkCount = 0;
            var linkConfirmed = new ManualResetEventSlim(false);
            void onLink(object? s, PrimEventArgs e)
            {
                if (e.Prim.ParentID == rootID)
                {
                    if (Interlocked.Increment(ref linkCount) >= 3)
                        linkConfirmed.Set();
                }
            }

            Console.WriteLine($"[Builder] Linking {ids.Count} prims (root: Foundation)…");
            client.Objects.ObjectUpdate += onLink;
            client.Objects.LinkPrims(client.Network.CurrentSim, ids);
            bool linked = linkConfirmed.Wait(15000);  // wait up to 15s for confirmation
            client.Objects.ObjectUpdate -= onLink;

            if (!linked)
                Console.WriteLine("[Builder] Warning: link confirmation timed out — linkset may not have formed.");

            client.Objects.DeselectObjects(client.Network.CurrentSim, ids.ToArray());
            Thread.Sleep(500);

            Console.WriteLine($"[Builder] Linkset complete ({(linked ? "confirmed" : "unconfirmed")}).");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  FRIEND LOCATE + TELEPORT
        // ─────────────────────────────────────────────────────────────────────────
        static void LocateAndTeleportToFriend()
        {
            // Find friend by name
            UUID friendID = UUID.Zero;
            foreach (var kvp in client.Friends.FriendList)
            {
                if (kvp.Value.Name.Contains(friendName, StringComparison.OrdinalIgnoreCase))
                {
                    friendID = kvp.Key;
                    Console.WriteLine($"[Builder] Found friend: {kvp.Value.Name} ({friendID})");
                    break;
                }
            }

            if (friendID == UUID.Zero)
            {
                Console.WriteLine($"[Builder] Friend '{friendName}' not found in friends list. Building at bot position.");
                origin = GetSettledPosition();
                return;
            }

            // Request map location (requires map rights on friendship)
            var located = new ManualResetEventSlim(false);
            ulong regionHandle = 0;
            Vector3 friendPos  = Vector3.Zero;

            void onFound(object? s, FriendFoundReplyEventArgs e)
            {
                if (e.AgentID != friendID) return;
                regionHandle = e.RegionHandle;
                friendPos    = e.Location;
                located.Set();
            }

            client.Friends.FriendFoundReply += onFound;
            client.Friends.MapFriend(friendID);

            if (!located.Wait(8000))
            {
                Console.WriteLine("[Builder] Timed out waiting for friend location. Building at bot position.");
                client.Friends.FriendFoundReply -= onFound;
                origin = GetSettledPosition();
                return;
            }
            client.Friends.FriendFoundReply -= onFound;

            Console.WriteLine($"[Builder] Friend at region {regionHandle}, local pos {friendPos}");

            // Teleport to friend — retry once on timeout before giving up
            bool teleported = false;
            for (int attempt = 1; attempt <= 2 && !teleported; attempt++)
            {
                if (attempt > 1)
                {
                    Console.WriteLine("[Builder] Retrying teleport (attempt 2)…");
                    Thread.Sleep(4000);
                }
                teleported = client.Self.Teleport(regionHandle, friendPos);
                if (!teleported)
                    Console.WriteLine($"[Builder] Teleport attempt {attempt} failed: {client.Self.TeleportMessage}");
            }

            if (!teleported)
            {
                origin = GetSettledPosition();
                Console.WriteLine($"[Builder] All teleport attempts failed. Building at bot position: {origin}");
                return;
            }
            Thread.Sleep(4000);   // wait for sim objects to stream in after teleport

            // Find the friend's avatar — their in-world position has the real Z,
            // unlike the MapFriend reply which only gives a 2D approximate location.
            Avatar? friendAvatar = null;
            foreach (var av in client.Network.CurrentSim.ObjectsAvatars.Values)
            {
                if (av.ID == friendID) { friendAvatar = av; break; }
            }

            if (friendAvatar != null)
            {
                origin = friendAvatar.Position;   // precise position including correct Z
                friendAvatar.Rotation.GetEulerAngles(out _, out _, out buildYaw);
                Console.WriteLine($"[Builder] Building at friend's position: {origin}  yaw: {buildYaw * 180f / MathF.PI:F1}°");
            }
            else
            {
                // Fallback: use bot's own position
                origin = GetSettledPosition();
                Console.WriteLine($"[Builder] Friend avatar not visible in sim — building at bot position: {origin}");
            }
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
            BuildStaircase();
            BuildCeilingLights();
            BuildSign();

            Console.WriteLine("[Builder] === Build complete! ===");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  FOUNDATION  — slightly buried so it always meets the ground
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildFoundation()
        {
            Console.WriteLine("[Builder] Foundation…");

            // Foundation bottom sits at origin.Z; center is half its height above that.
            float slabCenterZ = FOUNDATION_H / 2f;

            Console.WriteLine($"[Debug] Foundation center Z = {origin.Z + slabCenterZ:F3}  (bottom={origin.Z:F3}, top={origin.Z + FOUNDATION_H:F3})");
            RezBox(Offset(0,      0,  slabCenterZ), new Vector3(18f, 14f, FOUNDATION_H), FOUNDATION, "Foundation - Main");
            RezBox(Offset(13.5f, -1f, slabCenterZ), new Vector3(9f,  12f, FOUNDATION_H), FOUNDATION, "Foundation - Garage");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  FIRST FLOOR WALLS  — segmented around window/door openings
        //  Each wall is split into columns + sill/header pieces so that windows
        //  and doors have genuine open gaps (no solid backing).
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildFirstFloorWalls()
        {
            Console.WriteLine("[Builder] First floor walls…");

            float wallH = FLOOR1_H;   // 4.0 m
            float wallT = 0.2f;

            // Full-height column center Z
            float colZ = WALL_BASE + wallH / 2f;   // 2.5

            // Window opening Z bounds (must match BuildWindows)
            float winCenterZ = WALL_BASE + FLOOR1_H * 0.5f;   // 2.5
            float winH       = 1.6f;
            float winBot     = winCenterZ - winH / 2f;         // 1.7 — opening bottom
            float winTop     = winCenterZ + winH / 2f;         // 3.3 — opening top

            // Sill (wall below window): WALL_BASE → winBot
            float sillH = winBot - WALL_BASE;                  // 1.2
            float sillZ = WALL_BASE + sillH / 2f;              // 1.1

            // Header (wall above window): winTop → ceiling
            float winHdrH = WALL_BASE + wallH - winTop;        // 1.2
            float winHdrZ = winTop + winHdrH / 2f;             // 3.9

            // Door opening header (no sill — opening goes to floor)
            float doorH    = 2.4f;
            float doorTop  = WALL_BASE + doorH;                // 2.9
            float doorHdrH = WALL_BASE + wallH - doorTop;      // 1.6
            float doorHdrZ = doorTop + doorHdrH / 2f;          // 3.7

            // ── FRONT WALL  (Y = -7, runs along X: -9 to +9) ─────────────────
            // Openings: bay window X=[-7.5, -4.4], front door X=[-3.0, -2.0],
            //           front-right window X=[+2.1, +3.9]  (left casing flush with spine wall at X=2)
            float fy = -7f + wallT / 2f;

            // Full-height columns between/around openings
            RezBox(Offset(-8.25f, fy, colZ), new Vector3(1.5f, wallT, wallH), SIDING_COLOR, "Wall - Front Col 1");
            RezBox(Offset(-3.70f, fy, colZ), new Vector3(1.4f, wallT, wallH), SIDING_COLOR, "Wall - Front Col 2");
            RezBox(Offset( 0.05f, fy, colZ), new Vector3(4.1f, wallT, wallH), SIDING_COLOR, "Wall - Front Col 3");
            RezBox(Offset( 6.45f, fy, colZ), new Vector3(5.1f, wallT, wallH), SIDING_COLOR, "Wall - Front Col 4");

            // Bay window (merged opening X=-7.5 to -4.4, center=-5.95, w=3.1)
            RezBox(Offset(-5.95f, fy, sillZ),   new Vector3(3.1f, wallT, sillH),   SIDING_COLOR, "Wall - Front Bay Sill");
            RezBox(Offset(-5.95f, fy, winHdrZ), new Vector3(3.1f, wallT, winHdrH), SIDING_COLOR, "Wall - Front Bay Header");

            // Front door (opening X=-3.0 to -2.0, center=-2.5, w=1.0) — header only
            RezBox(Offset(-2.5f, fy, doorHdrZ), new Vector3(1.0f, wallT, doorHdrH), SIDING_COLOR, "Wall - Front Door Header");

            // Front-right window (X=+2.1 to +3.9, center=+3.0, w=1.8)
            RezBox(Offset(3.0f, fy, sillZ),   new Vector3(1.8f, wallT, sillH),   SIDING_COLOR, "Wall - Front Win Sill");
            RezBox(Offset(3.0f, fy, winHdrZ), new Vector3(1.8f, wallT, winHdrH), SIDING_COLOR, "Wall - Front Win Header");

            // ── REAR WALL  (Y = +7, runs along X: -9 to +9) ──────────────────
            // Openings: rear-left X=[-6, -4], rear-center X=[-1, +1], rear-right X=[+4, +6]
            float ry = 7f - wallT / 2f;

            RezBox(Offset(-7.5f, ry, colZ), new Vector3(3.0f, wallT, wallH), SIDING_COLOR, "Wall - Rear Col 1");
            RezBox(Offset(-2.5f, ry, colZ), new Vector3(3.0f, wallT, wallH), SIDING_COLOR, "Wall - Rear Col 2");
            RezBox(Offset( 2.5f, ry, colZ), new Vector3(3.0f, wallT, wallH), SIDING_COLOR, "Wall - Rear Col 3");
            RezBox(Offset( 7.5f, ry, colZ), new Vector3(3.0f, wallT, wallH), SIDING_COLOR, "Wall - Rear Col 4");

            // Three rear windows (each w=2.0, centers at X=-5, 0, +5): sill + header
            foreach (float wx in new float[] { -5f, 0f, 5f })
            {
                RezBox(Offset(wx, ry, sillZ),   new Vector3(2.0f, wallT, sillH),   SIDING_COLOR, "Wall - Rear Win Sill");
                RezBox(Offset(wx, ry, winHdrZ), new Vector3(2.0f, wallT, winHdrH), SIDING_COLOR, "Wall - Rear Win Header");
            }

            // ── LEFT WALL  (X = -9, runs along Y: -7 to +7) ──────────────────
            // Openings: win-1 Y=[-3.7, -2.3], win-2 Y=[+2.3, +3.7]
            float lx = -9f + wallT / 2f;

            RezBox(Offset(lx, -5.35f, colZ), new Vector3(wallT, 3.3f, wallH), SIDING_COLOR, "Wall - Left Col 1");
            RezBox(Offset(lx,  0f,    colZ), new Vector3(wallT, 4.6f, wallH), SIDING_COLOR, "Wall - Left Col 2");
            RezBox(Offset(lx,  5.35f, colZ), new Vector3(wallT, 3.3f, wallH), SIDING_COLOR, "Wall - Left Col 3");

            // Two side windows (each w=1.4, centers at Y=-3, +3): sill + header
            foreach (float wy in new float[] { -3f, 3f })
            {
                RezBox(Offset(lx, wy, sillZ),   new Vector3(wallT, 1.4f, sillH),   SIDING_COLOR, "Wall - Left Win Sill");
                RezBox(Offset(lx, wy, winHdrZ), new Vector3(wallT, 1.4f, winHdrH), SIDING_COLOR, "Wall - Left Win Header");
            }

            // ── RIGHT WALL  (X = +9, solid — no windows on this side) ─────────
            RezBox(Offset(9f - wallT / 2f, 0, colZ), new Vector3(wallT, 14f, wallH), SIDING_COLOR, "Wall - Right");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  GARAGE  — bottom at ground
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildGarage()
        {
            Console.WriteLine("[Builder] Garage…");

            float garageH = GARAGE_H;
            float wallT   = 0.2f;
            float wallZ   = WALL_BASE + garageH / 2f;   // bottom at foundation top

            // Front wall — layout around center X=13.5 (garage is X=9..18):
            //   Left edge 1.0m | Left opening 3.15m | Center post 0.5m | Right opening 3.15m | Right edge 1.0m
            RezBox(Offset( 9.6f, -6.9f, wallZ), new Vector3(1.0f, wallT, garageH), GARAGE_COLOR, "Garage Front - Left Edge");
            RezBox(Offset(13.5f, -6.9f, wallZ), new Vector3(0.5f, wallT, garageH), GARAGE_COLOR, "Garage Front - Center Post");
            RezBox(Offset(17.4f, -6.9f, wallZ), new Vector3(1.0f, wallT, garageH), GARAGE_COLOR, "Garage Front - Right Edge");

            // Header spanning both door openings
            RezBox(Offset(13.5f, -6.9f, WALL_BASE + garageH - 0.15f),
                   new Vector3(7.8f, wallT, 0.3f), TRIM_COLOR, "Garage Door Header");

            // Sectional door panels — centered in their openings
            // Left opening:  X = 10.1 to 13.25  → center 11.675
            // Right opening: X = 13.75 to 16.9  → center 15.325
            // Panels fill floor-to-header-bottom (3.2 m), slightly wider than each opening (3.25 m)
            float doorH       = garageH - 0.30f;   // fills floor to header bottom (header is 0.3 m tall)
            float doorCenterZ = WALL_BASE + doorH / 2f;
            RezBox(Offset(11.675f, -6.85f, doorCenterZ), new Vector3(3.25f, 0.05f, doorH), new Color4(0.82f, 0.82f, 0.82f, 1f), "Garage Door Left");
            RezBox(Offset(15.325f, -6.85f, doorCenterZ), new Vector3(3.25f, 0.05f, doorH), new Color4(0.82f, 0.82f, 0.82f, 1f), "Garage Door Right");

            // Rear & side walls
            RezBox(Offset(13.5f,  5f - wallT / 2f, wallZ), new Vector3(9f,   wallT, garageH), GARAGE_COLOR, "Garage Rear Wall");
            RezBox(Offset(17.9f, -1f,               wallZ), new Vector3(wallT, 12f,  garageH), GARAGE_COLOR, "Garage Side Wall");
            RezBox(Offset( 9.1f, -1f,               wallZ), new Vector3(wallT, 12f,  garageH), SIDING_COLOR, "Garage Interior Shared Wall");

            // Ceiling — tucked inside the walls so it doesn't poke above the eaves
            RezBox(Offset(13.5f, -1f, WALL_BASE + garageH - 0.15f), new Vector3(9f, 12f, 0.2f), FOUNDATION, "Garage Ceiling");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  HALF-STORY
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildSecondFloorHalfStory()
        {
            Console.WriteLine("[Builder] Half-story / upper floor…");

            float wallT = 0.2f;

            // Floor deck — 5 pieces leaving the L-staircase opening (X=[0.70,2.05], Y=[-7,-2.35])
            // Opening runs from the front wall all the way up to where leg 2 exits at the second floor.
            float deckZ = FLOOR1_TOP + FLOOR2_DECK_H / 2f;
            float dkH   = FLOOR2_DECK_H;
            RezBox(Offset( 0f,     2.325f, deckZ), new Vector3(18f,   9.35f, dkH), FOUNDATION, "Second Floor Deck - Rear");
            RezBox(Offset(-4.15f, -6.1f,   deckZ), new Vector3(9.7f,  1.8f,  dkH), FOUNDATION, "Second Floor Deck - Front Left");
            RezBox(Offset( 5.525f,-6.1f,   deckZ), new Vector3(6.95f, 1.8f,  dkH), FOUNDATION, "Second Floor Deck - Front Right");
            RezBox(Offset(-4.15f, -3.775f, deckZ), new Vector3(9.7f,  2.85f, dkH), FOUNDATION, "Second Floor Deck - Mid Left");
            RezBox(Offset( 5.525f,-3.775f, deckZ), new Vector3(6.95f, 2.85f, dkH), FOUNDATION, "Second Floor Deck - Mid Right");
            // Cap the gap where the deck opening meets the front wall (opening X=[0.70,2.05] at Y=-7)
            RezBox(Offset(1.375f, -6.9f, deckZ), new Vector3(1.35f, 0.2f, dkH), FOUNDATION, "Second Floor Deck - Front Wall Cap");

            // Knee walls
            float kneeZ = FLOOR2_BASE + KNEE_H / 2f;
            RezBox(Offset(0, -7f + wallT / 2f, kneeZ), new Vector3(18f,  wallT, KNEE_H), SIDING_COLOR, "Knee Wall - Front");
            RezBox(Offset(0,  7f - wallT / 2f, kneeZ), new Vector3(18f,  wallT, KNEE_H), SIDING_COLOR, "Knee Wall - Rear");

            // Gable end walls — rectangular base from FLOOR2_BASE to ROOF_BASE, triangle above
            // The rectangular base matches the knee walls (front/rear) so all sides are flush at ROOF_BASE.
            float gableBaseZ = FLOOR2_BASE + KNEE_H / 2f;
            RezBox(Offset(-9f + wallT / 2f, 0, gableBaseZ), new Vector3(wallT, 14f, KNEE_H), SIDING_COLOR, "Gable Base - Left");
            RezBox(Offset( 9f - wallT / 2f, 0, gableBaseZ), new Vector3(wallT, 14f, KNEE_H), SIDING_COLOR, "Gable Base - Right");

            // Triangle starts at ROOF_BASE (= top of knee walls / base of roof panels)
            float gableH = 3.1f;   // matches ridge cap position
            float gableZ = ROOF_BASE + gableH / 2f;
            RezGableWall(Offset(-9f + wallT / 2f, 0, gableZ), new Vector3(wallT, 14f, gableH), SIDING_COLOR, "Gable - Left");
            RezGableWall(Offset( 9f - wallT / 2f, 0, gableZ), new Vector3(wallT, 14f, gableH), SIDING_COLOR, "Gable - Right");

            // Front dormers
            float dormerZ = FLOOR2_BASE + KNEE_H + 0.5f;
            BuildDormer(-4f, -6.5f, dormerZ, facing: -1);
            BuildDormer( 4f, -6.5f, dormerZ, facing: -1);
        }

        // lx/ly/lz: local-space center at eave height; facing=-1 means front of house (toward -Y)
        static void BuildDormer(float lx, float ly, float lz, int facing)
        {
            float dW    = 2.4f;    // width (X)
            float dH    = 1.6f;    // wall height
            float dD    = 1.4f;    // depth (Y)
            float wallT = 0.15f;

            // Front face is toward -Y (facing the street)
            float faceY = ly - dD / 2f;           // local exterior face Y
            float fwCY  = faceY + wallT / 2f;     // local front-wall center Y
            float topZ  = lz + dH;

            // ── Front wall split around window ──────────────────────────────────
            float winW  = 1.0f;
            float winH  = 0.9f;
            float winCZ = lz + dH * 0.54f;
            float winBot = winCZ - winH / 2f;
            float winTop = winCZ + winH / 2f;

            float colW  = (dW - winW) / 2f;   // 0.7 m each side
            float colCZ = lz + dH / 2f;

            RezBox(Offset(lx - dW / 2f + colW / 2f, fwCY, colCZ), new Vector3(colW, wallT, dH),             SIDING_COLOR, "Dormer Front L");
            RezBox(Offset(lx + dW / 2f - colW / 2f, fwCY, colCZ), new Vector3(colW, wallT, dH),             SIDING_COLOR, "Dormer Front R");
            RezBox(Offset(lx,                        fwCY, lz + (winBot - lz) / 2f), new Vector3(winW, wallT, winBot - lz),   SIDING_COLOR, "Dormer Sill");
            RezBox(Offset(lx,                        fwCY, winTop + (topZ - winTop) / 2f), new Vector3(winW, wallT, topZ - winTop), SIDING_COLOR, "Dormer Header");

            // ── Side walls ──────────────────────────────────────────────────────
            RezBox(Offset(lx - dW / 2f + wallT / 2f, ly, colCZ), new Vector3(wallT, dD, dH), SIDING_COLOR, "Dormer Side L");
            RezBox(Offset(lx + dW / 2f - wallT / 2f, ly, colCZ), new Vector3(wallT, dD, dH), SIDING_COLOR, "Dormer Side R");

            // ── 4-piece exterior casing ─────────────────────────────────────────
            float dcy = faceY - CASING_T / 2f;   // local Y: exterior face of dormer casing
            RezBox(Offset(lx,                              dcy, winCZ + winH / 2f + CASING_W / 2f), new Vector3(winW + CASING_W * 2, CASING_T, CASING_W), TRIM_COLOR, "Dormer Window Casing Top");
            RezBox(Offset(lx,                              dcy, winCZ - winH / 2f - CASING_W / 2f), new Vector3(winW + CASING_W * 2, CASING_T, CASING_W), TRIM_COLOR, "Dormer Window Casing Sill");
            RezBox(Offset(lx - winW / 2f - CASING_W / 2f, dcy, winCZ), new Vector3(CASING_W, CASING_T, winH), TRIM_COLOR, "Dormer Window Casing Left");
            RezBox(Offset(lx + winW / 2f + CASING_W / 2f, dcy, winCZ), new Vector3(CASING_W, CASING_T, winH), TRIM_COLOR, "Dormer Window Casing Right");

            // ── Gabled roof — 22° pitch matching main house ─────────────────────
            float pitchDeg = 22f;
            float halfSpan = dD / 2f + 0.15f;   // slight front/rear overhang
            float riseZ    = halfSpan * MathF.Tan(pitchDeg * MathF.PI / 180f);
            float pCtrZ    = topZ + riseZ / 2f;

            RezTiltedRoofPanel(
                center:   Offset(lx, ly - halfSpan / 2f, pCtrZ),
                size:     new Vector3(dW + 0.25f, halfSpan, 0.12f),
                pitchDeg: pitchDeg, forward: true,  label: "Dormer Roof Front");
            RezTiltedRoofPanel(
                center:   Offset(lx, ly + halfSpan / 2f, pCtrZ),
                size:     new Vector3(dW + 0.25f, halfSpan, 0.12f),
                pitchDeg: pitchDeg, forward: false, label: "Dormer Roof Rear");

            // Ridge cap
            RezBox(Offset(lx, ly, topZ + riseZ), new Vector3(dW + 0.3f, 0.2f, 0.15f), ROOF_COLOR, "Dormer Ridge");
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
        //  GARAGE ROOF  — gable matching the main house, ridge along X
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildGarageRoof()
        {
            Console.WriteLine("[Builder] Garage roof…");

            float overhang  = 0.4f;
            float pitchDeg  = 15f;
            float halfSpan  = 6f;    // half of 12m garage depth (ridge at center Y=-1)
            float roofBaseZ = WALL_BASE + GARAGE_H;   // top of garage walls = 4.0m
            float riseZ     = halfSpan * MathF.Tan(pitchDeg * MathF.PI / 180f);   // ~1.61m
            float panelCtrZ = roofBaseZ + riseZ / 2f;

            // Front panel (eave at Y=-7, ridge at Y=-1)
            RezTiltedRoofPanel(
                center:   Offset(13.5f, -1f - halfSpan / 2f, panelCtrZ),
                size:     new Vector3(9f + overhang * 2f, halfSpan + overhang, 0.25f),
                pitchDeg: pitchDeg, forward: true, label: "Garage Roof - Front");

            // Rear panel (ridge at Y=-1, eave at Y=+5)
            RezTiltedRoofPanel(
                center:   Offset(13.5f, -1f + halfSpan / 2f, panelCtrZ),
                size:     new Vector3(9f + overhang * 2f, halfSpan + overhang, 0.25f),
                pitchDeg: pitchDeg, forward: false, label: "Garage Roof - Rear");

            // Ridge cap
            RezBox(Offset(13.5f, -1f, roofBaseZ + riseZ),
                   new Vector3(9f + overhang * 2f, 0.4f, 0.25f), ROOF_COLOR, "Garage Ridge Cap");

            // Gable end walls — triangular fill from roof base to ridge at each X end
            float wallT  = 0.2f;
            float gableH = riseZ;
            float gableZ = roofBaseZ + gableH / 2f;
            // Left end sits on the interior shared wall (X=9); right end on the exterior side wall (X=18)
            RezGableWall(Offset( 9f + wallT / 2f, -1f, gableZ), new Vector3(wallT, 12f, gableH), SIDING_COLOR,  "Garage Gable - Left");
            RezGableWall(Offset(18f - wallT / 2f, -1f, gableZ), new Vector3(wallT, 12f, gableH), GARAGE_COLOR, "Garage Gable - Right");
        }
        // ─────────────────────────────────────────────────────────────────────────
        //  CHIMNEY
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildChimney()
        {
            Console.WriteLine("[Builder] Chimney…");

            float chimH = ROOF_BASE + 4.5f;   // starts at ground, pokes above ridge

            RezBox(Offset(-6f, 4.2f, chimH / 2f),
                   new Vector3(1.0f, 1.0f, chimH), CHIMNEY_COLOR, "Chimney Stack");
            RezBox(Offset(-6f, 4.2f, chimH + 0.1f),
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
                RezBox(Offset(-2.5f, -11.125f + i * 0.45f, stepZ),
                       new Vector3(4.5f - i * 0.4f, 0.45f, 0.15f), FOUNDATION, $"Porch Step {i + 1}");
            }

            // Porch roof
            RezTiltedRoofPanel(
                center:   Offset(-2.5f, -8.5f, porchRoofZ),
                size:     new Vector3(5.6f, 3.6f, 0.18f),
                pitchDeg: 14f, forward: true, label: "Porch Roof");

            // Columns — at outer edge of deck
            float[] colX = { -4.5f, -3.0f, -1.5f, 0.0f };
            foreach (float cx in colX)
                RezBox(Offset(cx, -9.5f, columnZ), new Vector3(0.2f, 0.2f, columnH), TRIM_COLOR, "Porch Column");

            // Railing — two pieces with a gap at center for the staircase (top step is 3.7m wide)
            RezBox(Offset(-4.675f, -9.8f, deckTop + 0.45f), new Vector3(0.65f, 0.05f, 0.9f), TRIM_COLOR, "Porch Railing Left");
            RezBox(Offset(-0.325f, -9.8f, deckTop + 0.45f), new Vector3(0.65f, 0.05f, 0.9f), TRIM_COLOR, "Porch Railing Right");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  WINDOWS
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildWindows()
        {
            Console.WriteLine("[Builder] Windows…");

            float winZ  = WALL_BASE + FLOOR1_H * 0.5f;   // window center at mid-wall
            float winH  = 1.6f;

            // Front facade — suppress the inner casings between the two adjacent bay windows
            // (they would overlap/clutter the gap between the windows)
            RezWindow(-5.5f, -7f, winZ, 2.2f, winH, "Bay Window Left",  suppressLeft: true);
            RezWindow(-7.0f, -7f, winZ, 1.0f, winH, "Bay Window Right", suppressRight: true);
            RezWindow( 3.0f, -7f, winZ, 1.8f, winH, "Front Right Window");

            // Rear facade (extDir +1 = exterior is toward +Y)
            RezWindow(-5f,  7f, winZ, 2.0f, winH, "Rear Left Window",   extDir: +1f);
            RezWindow( 0f,  7f, winZ, 2.0f, winH, "Rear Center Window", extDir: +1f);
            RezWindow( 5f,  7f, winZ, 2.0f, winH, "Rear Right Window",  extDir: +1f);

            // Left side
            RezWindowSide(-9f, -3f, winZ, 1.4f, winH, "Left Side Window 1");
            RezWindowSide(-9f,  3f, winZ, 1.4f, winH, "Left Side Window 2");
        }


        // Rez a front/rear-facing window with 4-piece exterior casing.
        // lx/ly/lz:     local-space center of the window opening
        // extDir:       -1 = exterior toward -Y (front wall), +1 = toward +Y (rear wall)
        // suppressLeft / suppressRight: omit the side casing where two windows are adjacent
        //   (prevents overlapping prims and fixes echo-matching ambiguity between close prims)
        static void RezWindow(float lx, float ly, float lz, float w, float h, string label,
                              float extDir = -1f, bool suppressLeft = false, bool suppressRight = false)
        {
            float ly2 = ly + extDir * CASING_T / 2f;   // local Y, pushed proud of wall exterior face
            RezBox(Offset(lx,                          ly2, lz + h / 2f + CASING_W / 2f), new Vector3(w + CASING_W * 2, CASING_T, CASING_W), TRIM_COLOR, label + " Casing Top");
            RezBox(Offset(lx,                          ly2, lz - h / 2f - CASING_W / 2f), new Vector3(w + CASING_W * 2, CASING_T, CASING_W), TRIM_COLOR, label + " Casing Sill");
            if (!suppressLeft)
                RezBox(Offset(lx - w / 2f - CASING_W / 2f, ly2, lz), new Vector3(CASING_W, CASING_T, h), TRIM_COLOR, label + " Casing Left");
            if (!suppressRight)
                RezBox(Offset(lx + w / 2f + CASING_W / 2f, ly2, lz), new Vector3(CASING_W, CASING_T, h), TRIM_COLOR, label + " Casing Right");
        }

        // Rez a side-facing window (left wall, exterior toward -X) with 4-piece casing.
        // lx/ly/lz: local-space center of the window opening
        static void RezWindowSide(float lx, float ly, float lz, float w, float h, string label)
        {
            float lx2 = lx - CASING_T / 2f;   // local X, pushed proud of left-wall exterior face
            RezBox(Offset(lx2, ly,                          lz + h / 2f + CASING_W / 2f), new Vector3(CASING_T, w + CASING_W * 2, CASING_W), TRIM_COLOR, label + " Casing Top");
            RezBox(Offset(lx2, ly,                          lz - h / 2f - CASING_W / 2f), new Vector3(CASING_T, w + CASING_W * 2, CASING_W), TRIM_COLOR, label + " Casing Sill");
            RezBox(Offset(lx2, ly - w / 2f - CASING_W / 2f, lz), new Vector3(CASING_T, CASING_W, h), TRIM_COLOR, label + " Casing Front");
            RezBox(Offset(lx2, ly + w / 2f + CASING_W / 2f, lz), new Vector3(CASING_T, CASING_W, h), TRIM_COLOR, label + " Casing Rear");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  DOORS
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildDoors()
        {
            Console.WriteLine("[Builder] Doors…");

            float doorH = 2.4f;
            float doorZ = WALL_BASE + doorH / 2f;   // bottom at foundation top

            // ── Front door — 3-piece exterior casing (no floor sill) ──────────────
            float fdc = -7f - CASING_T / 2f;   // local Y of exterior casing center
            RezBox(Offset(-2.5f,            fdc, WALL_BASE + doorH + CASING_W / 2f), new Vector3(1.0f + CASING_W * 2f, CASING_T, CASING_W), TRIM_COLOR, "Front Door Casing Top");
            RezBox(Offset(-3.0f - CASING_W / 2f, fdc, doorZ),                        new Vector3(CASING_W, CASING_T, doorH),                 TRIM_COLOR, "Front Door Casing Left");
            RezBox(Offset(-2.0f + CASING_W / 2f, fdc, doorZ),                        new Vector3(CASING_W, CASING_T, doorH),                 TRIM_COLOR, "Front Door Casing Right");

            // ── Interior garage door — 3-piece casing (house side) ────────────────
            float gcc = 9f + CASING_T / 2f;    // local X of house-side casing center
            RezBox(Offset(gcc, -1f,              WALL_BASE + doorH + CASING_W / 2f), new Vector3(CASING_T, 0.9f + CASING_W * 2f, CASING_W), TRIM_COLOR, "Garage Door Casing Top");
            RezBox(Offset(gcc, -1f - 0.45f - CASING_W / 2f, WALL_BASE + doorH / 2f), new Vector3(CASING_T, CASING_W, doorH),                TRIM_COLOR, "Garage Door Casing Left");
            RezBox(Offset(gcc, -1f + 0.45f + CASING_W / 2f, WALL_BASE + doorH / 2f), new Vector3(CASING_T, CASING_W, doorH),                TRIM_COLOR, "Garage Door Casing Right");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  INTERIOR WALLS
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildInteriorWalls()
        {
            Console.WriteLine("[Builder] Interior walls…");

            float wallH = FLOOR1_H - 0.25f;   // 3.75 m — slightly shorter so tops clear the ceiling deck
            float wallT = 0.15f;
            float wallZ = WALL_BASE + wallH / 2f;  // bottom at foundation top

            // Spine wall — three pieces:
            //   Front section: Y=-6.75 → -1.5  (staircase runs along its left/west face, X<2)
            //   Door opening:  Y=-1.5  → -0.5  (1 m passage from stair landing into upper floor)
            //   Rear section:  Y=-0.5  →  6.75
            RezBox(Offset( 2f,  -4.125f, wallZ), new Vector3(wallT, 5.25f, wallH), TRIM_COLOR, "Interior - Spine Wall Front");
            RezBox(Offset( 2f,   3.125f, wallZ), new Vector3(wallT, 7.25f, wallH), TRIM_COLOR, "Interior - Spine Wall Rear");
            RezBox(Offset(-4f,  -2f,     wallZ), new Vector3(4.6f, wallT,  wallH), TRIM_COLOR, "Interior - Living-Dining Divider");
            RezBox(Offset( 5f,   2f,     wallZ), new Vector3(6.6f, wallT,  wallH), TRIM_COLOR, "Interior - Kitchen Rear");
            RezBox(Offset(-4.5f, 4.2f,   wallZ), new Vector3(8.6f, wallT,  wallH), TRIM_COLOR, "Interior - Master Divider");
            RezBox(Offset( 5.5f, 0,      wallZ), new Vector3(wallT, 5.6f,  wallH), TRIM_COLOR, "Interior - Bath-Laundry Wall");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  STAIRCASE  (L-shaped)
        //  Enter to the RIGHT of the front door (viewed from outside), bottom step
        //  left edge at X=-2.0 (flush with door right edge, no overlap), Y center -6.15.
        //  Leg 1 runs +X (along the front wall area) for 5 steps.
        //  RIGHT turn sends Leg 2 in +Y (along the spine wall, X≈1.40) for 5 steps.
        //  Second-floor deck opening: X=[0.70, 2.05], Y=[-7, -2.35] (full height to front wall).
        //  Treads: 0.55 m deep × 1.3 m wide.
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildStaircase()
        {
            Console.WriteLine("[Builder] Staircase…");

            const int   STEPS    = 10;
            const int   STEPS_L1 = 5;                   // steps in leg 1 (+X, along front wall area)
            const int   STEPS_L2 = STEPS - STEPS_L1;    // steps in leg 2 (+Y, along spine wall)
            const float STEP_H   = FLOOR1_H / STEPS;    // 0.40 m rise per step
            const float STEP_D   = 0.55f;               // tread depth
            const float STEP_W   = 1.3f;                // tread width

            // Leg 1 — bottom step just right of front door, climbs in +X toward spine wall.
            // Last step extends to the spine wall interior face (X=1.925) to fill the L-corner.
            const float STAIR_X0      = -2.0f;    // left edge of first step, flush with door right edge (X=-2.0), no overlap
            const float STAIR_Y_L1   = -6.15f;   // Y center of leg 1 treads (front edge flush with interior wall face at Y=-6.8)
            const float SPINE_WALL_IX = 1.925f;   // interior face of spine wall (X=2.0 - wallT/2)

            for (int i = 0; i < STEPS_L1; i++)
            {
                float stepLeft = STAIR_X0 + i * STEP_D;
                float stepW    = (i == STEPS_L1 - 1) ? (SPINE_WALL_IX - stepLeft) : STEP_D;
                float treadX   = stepLeft + stepW / 2f;
                float boxH     = (i + 1) * STEP_H;
                float boxZ     = WALL_BASE + boxH / 2f;
                RezBox(Offset(treadX, STAIR_Y_L1, boxZ), new Vector3(stepW, STEP_W, boxH), FOUNDATION, $"Stair Step L1-{i + 1}");
            }

            const float X_CORNER    = STAIR_X0 + STEPS_L1 * STEP_D;  // right edge of normal leg 1 steps = 0.75
            const float STAIR_X_L2  = X_CORNER + STEP_W / 2f;         // X center of leg 2 = 1.40
            const float STAIR_Y0_L2 = STAIR_Y_L1 + STEP_W / 2f;       // Y front face of leg 2 = -5.5

            // Leg 2 — right turn at corner near spine wall, climbs in +Y direction
            for (int j = 0; j < STEPS_L2; j++)
            {
                float treadY = STAIR_Y0_L2 + j * STEP_D + STEP_D / 2f;  // stepping toward +Y
                float boxH   = (STEPS_L1 + j + 1) * STEP_H;
                float boxZ   = WALL_BASE + boxH / 2f;
                RezBox(Offset(STAIR_X_L2, treadY, boxZ), new Vector3(STEP_W, STEP_D, boxH), FOUNDATION, $"Stair Step L2-{j + 1}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  CEILING LIGHTS
        //  Small disc fixtures flush with the first-floor ceiling, one per room.
        //  Warm-white color, SL light property enabled, subtle glow.
        // ─────────────────────────────────────────────────────────────────────────
        static void BuildCeilingLights()
        {
            Console.WriteLine("[Builder] Ceiling lights…");

            // Soft warm white
            var lightColor = new Color4(1.0f, 0.97f, 0.88f, 1.0f);

            // Top face of disc sits flush with the first-floor ceiling
            float ceilZ = FLOOR1_TOP;

            // Living room — front-left, bay-window wall
            RezLight(-5.0f, -4.5f, ceilZ, 0.4f, lightColor, "Light - Living Room");

            // Dining / rear-left — between living divider and master divider
            RezLight(-5.0f,  1.5f, ceilZ, 0.4f, lightColor, "Light - Dining Room");

            // Kitchen — right side, north of kitchen rear wall
            RezLight( 5.5f,  4.5f, ceilZ, 0.4f, lightColor, "Light - Kitchen");

            // Front entry / family room — right of spine wall, south area
            RezLight( 3.75f, -4.0f, ceilZ, 0.4f, lightColor, "Light - Entry");

            // Upstairs — disc hanging 0.25 m below the main ridge
            float upCeilZ = ROOF_BASE + 3.1f - 0.25f;
            RezLight( 0f, 0f, upCeilZ, 0.4f, lightColor, "Light - Upstairs");
        }

        // ─────────────────────────────────────────────────────────────────────────
        static void BuildSign()
        {
            Console.WriteLine("[Builder] Sign…");

            var signTex   = new UUID("87fa5aa4-17dd-7e9a-e5e0-6ba9b3f2d26f");
            var signColor = new Color4(1.0f, 1.0f, 1.0f, 1.0f);   // white tint (shows texture as-is)

            // Hang a big banner on the front wall, right section (X≈3.9→9.0, Col 4).
            // Sign face sits just proud of the wall surface (Y = -7.02).
            float signW = 4.5f;   // width
            float signH = 2.0f;   // height
            float signD = 0.04f;  // depth (thin panel)
            float signX = 6.45f;
            float signY = -(7.0f + signD / 2f);
            float signZ = WALL_BASE + FLOOR1_H * 0.55f;   // slightly above mid-wall

            var cd = BuildPrimData(signColor);
            RezAndSetPrim(cd, Offset(signX, signY, signZ), new Vector3(signW, signD, signH),
                          Quaternion.Identity, signColor, "Sign - Vibe Coded House",
                          isLight: true, glow: 0.08f, textureUUID: signTex);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  SETTLED POSITION  — polls SimPosition until Z > 1 m (terrain level)
        //  After login or a failed/timed-out teleport the client can return Z=0
        //  before the first AgentUpdate echo arrives from the sim.  All prims are
        //  rezzed relative to origin.Z, so a stale Z=0 causes every expected
        //  center to be wrong and all echo matches to fail.
        // ─────────────────────────────────────────────────────────────────────────
        static Vector3 GetSettledPosition(int maxWaitMs = 10_000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
            while (DateTime.UtcNow < deadline)
            {
                var pos = client.Self.SimPosition;
                if (pos.Z > 1f) return pos;
                Console.WriteLine($"[Builder] Waiting for valid Z (currently {pos.Z:F2})…");
                Thread.Sleep(500);
            }
            var final = client.Self.SimPosition;
            if (final.Z <= 1f)
                Console.WriteLine($"[Builder] Warning: Z still low ({final.Z:F2}) after waiting — build positions may be wrong.");
            return final;
        }

        // ═════════════════════════════════════════════════════════════════════════
        //  PRIMITIVE REZ HELPERS
        // ═════════════════════════════════════════════════════════════════════════

        static void RezBox(Vector3 pos, Vector3 size, Color4 color, string description)
        {
            RezPrim(pos, size, Quaternion.Identity, color, description);
        }

        // Rez a flat disc (cylinder) ceiling light with the SL light property and glow enabled.
        // lx/ly: local-space center; lz should be the ceiling height (prim top face flush with ceiling).
        static void RezLight(float lx, float ly, float lz, float diameter, Color4 color, string description)
        {
            var cd = BuildPrimData(color);
            cd.ProfileCurve = ProfileCurve.Circle;   // disc shape
            float halfH = 0.05f / 2f;
            RezAndSetPrim(cd, Offset(lx, ly, lz - halfH), new Vector3(diameter, diameter, 0.05f),
                          Quaternion.Identity, color, description, isLight: true, glow: 0.1f);
        }

        static void RezGableWall(Vector3 pos, Vector3 size, Color4 color, string description)
        {
            var prim = BuildPrimData(color);
            prim.PathScaleY = 0.0f;  // collapse Y to 0 at top of path — triangular prism (gable shape)
            RezAndSetPrim(prim, pos, size, Quaternion.Identity, color, description);
        }

        static void RezPrim(Vector3 pos, Vector3 size, Quaternion rot, Color4 color, string description)
        {
            var prim = BuildPrimData(color);
            RezAndSetPrim(prim, pos, size, rot, color, description);
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

        static void RezAndSetPrim(Primitive.ConstructionData cd, Vector3 pos, Vector3 size, Quaternion rot, Color4 color, string description, bool isLight = false, float glow = 0f, UUID? textureUUID = null)
        {
            // Compose build orientation into every prim rotation
            if (buildYaw != 0f)
                rot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, buildYaw) * rot;

            // AddPrim treats pos as the BOTTOM of the prim, not the center.
            // Our coordinate system uses centers, so subtract half the height to compensate.
            Vector3 bottomPos = new(pos.X, pos.Y, pos.Z - size.Z / 2f);

            // Register the expected center + scale so OnPrimEcho can match the server reply.
            lock (primLock) { pendingPrims.Add(new PendingPrim(pos, size, description, color, isLight, glow, textureUUID)); }

            client.Self.Movement.SendUpdate();
            client.Objects.AddPrim(client.Network.CurrentSim, cd, UUID.Zero, bottomPos, size, rot);

            Thread.Sleep((int)DELAY_MS);   // rate-limit only — no waiting for echo
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  COORDINATE HELPER
        // ─────────────────────────────────────────────────────────────────────────
        static Vector3 Offset(float x, float y, float z)
        {
            if (buildYaw == 0f)
                return new Vector3(origin.X + x, origin.Y + y, origin.Z + z);
            float cos = MathF.Cos(buildYaw);
            float sin = MathF.Sin(buildYaw);
            return new Vector3(origin.X + x * cos - y * sin,
                               origin.Y + x * sin + y * cos,
                               origin.Z + z);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  LOGIN CALLBACKS
        // ─────────────────────────────────────────────────────────────────────────
        static void OnLoginProgress(object? sender, LoginProgressEventArgs e)
            => Console.WriteLine($"[Login] {e.Status}: {e.Message}");

        static void OnSimConnected(object? sender, SimConnectedEventArgs e)
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
