// Program.cs — entry point, dispatches to legacy or JSON build mode.
//
// Usage:
//   dotnet run                                  — legacy hardcoded build
//   dotnet run -- --at-friend                   — legacy, build at friend's position
//   dotnet run -- json                          — JSON build (TraditionalAmericanHome.jsonc)
//   dotnet run -- json MyBuilding.json          — JSON build from specific file
//   dotnet run -- json MyBuilding.json --at-friend

using System;
using System.Linq;
using SLHouseBuilder;

int jsonIdx = Array.FindIndex(args, a =>
    a.Equals("json",   StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--json", StringComparison.OrdinalIgnoreCase));

if (jsonIdx >= 0)
{
    string path = (jsonIdx + 1 < args.Length && !args[jsonIdx + 1].StartsWith('-'))
        ? args[jsonIdx + 1]
        : "TraditionalAmericanHome.jsonc";

    string[] rest = args.Where((_, i) => i != jsonIdx && i != jsonIdx + 1).ToArray();
    await TraditionalAmericanHome.RunJsonAsync(rest, path);
}
else
{
    await TraditionalAmericanHome.RunLegacyAsync(args);
}
