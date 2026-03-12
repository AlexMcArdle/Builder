# SL House Builder

A C# bot that automatically builds a traditional American story-and-a-half home in Second Life using [LibreMetaverse](https://github.com/cinderblocks/libremetaverse).

## What it builds

- **House** — ~18m × 14m footprint, ~11m ridge height
- **Attached 2-car garage** — 9m × 12m
- 3–4 bedroom layout with interior walls, staircase, and second-floor half-story
- Front bay window, dormers, front porch, chimney
- Window and door casings (trim)
- Ceiling lights with SL light property and glow
- Exterior sign

**Prim budget:** ~160 prims

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A Second Life bot account (a free alt works)
- Land with build permissions and enough prim allowance

## Setup

1. Copy `credentials.example.json` to `credentials.json` and fill in your bot's details:

```json
{
  "FirstName": "YourBotFirstName",
  "LastName": "Resident",
  "Password": "your-password-here",
  "FriendName": ""
}
```

2. Build the project:

```sh
dotnet build
```

## Usage

Log your bot into Second Life and stand it at the desired build origin, then run:

```sh
dotnet run
```

The house is built relative to the bot's current position and orientation.

### JSON build mode

Building definitions can be stored in `.jsonc` files (JSON with comments). To build from a file:

```sh
dotnet run -- json                          # loads TraditionalAmericanHome.jsonc
dotnet run -- json MyBuilding.jsonc         # loads a specific file
```

The JSONC format supports named constants, derived expressions (e.g. `"WALL_BASE + FLOOR1_H / 2"`), a color palette, and one or more linksets. See `TraditionalAmericanHome.jsonc` for a full example.

### Build at a friend's location

Set `FriendName` in `credentials.json` to the display name of an online friend, then pass `--at-friend`:

```sh
dotnet run -- --at-friend                   # legacy build at friend
dotnet run -- json --at-friend              # JSON build at friend
```

The bot will teleport to the friend, orient toward them, and build at that location.

## How it works

The bot logs in, rezzes each prim one at a time with a short delay between calls to avoid flooding the simulator, then waits for the server to echo back each prim's local ID. Once all prims are confirmed, it sets names, textures, colors, glow, and light properties in a single finalization pass, then links everything into one linked set.

## Notes

- `credentials.json` is excluded from version control — never commit it.
- The delay between prim rez calls (`DELAY_MS`) can be tuned in the source if the sim is slow or fast.
- All measurements are in meters. Z values are relative to the bot's foot position at runtime.
