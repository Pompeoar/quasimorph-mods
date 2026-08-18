using System.Reflection;
using System.Text.Json;
using Verify;
using Verify.Checks;

// Verifies every mod in this repo against the SHIPPED Assembly-CSharp.dll - not against a
// decompiler dump, which may be stale. Run it after every game update.
//
//   dotnet run --project tools\Verify [-- <Managed folder> [ModName]]

var repoRoot = FindRepoRoot();
if (repoRoot is null)
{
    Console.Error.WriteLine("FAIL: could not locate the repo root (no Directory.Build.props found).");
    return 1;
}

var managed = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0]
    : @"C:\Program Files (x86)\Steam\steamapps\common\Quasimorph\Quasimorph_Data\Managed";

var only = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : null;

if (!Directory.Exists(managed))
{
    Console.Error.WriteLine($"FAIL: Managed folder not found: {managed}");
    return 1;
}

var gamePath = Path.Combine(managed, "Assembly-CSharp.dll");
if (!File.Exists(gamePath))
{
    Console.Error.WriteLine($"FAIL: Assembly-CSharp.dll not found in {managed}");
    return 1;
}

var resolver = new PathAssemblyResolver(Directory.GetFiles(managed, "*.dll"));
using var mlc = new MetadataLoadContext(resolver, "netstandard");
var game = mlc.LoadFromAssemblyPath(gamePath);

var reporter = new Reporter();
var checker = new MetadataChecker(game, reporter);

var jsonOptions = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip };

// The mod-loader surface every mod in this repo depends on.
var coreTargets = Path.Combine(repoRoot, "tools", "core-targets.json");
if (File.Exists(coreTargets) && only is null)
{
    var manifest = JsonSerializer.Deserialize<TargetManifest>(File.ReadAllText(coreTargets), jsonOptions);
    checker.Check(manifest, "core");
}

// Per-mod declared patch targets.
var srcDir = Path.Combine(repoRoot, "src");
var mods = Directory.Exists(srcDir)
    ? Directory.GetDirectories(srcDir).OrderBy(d => d).ToList()
    : new List<string>();

var verifiedMods = new List<string>();

foreach (var modDir in mods)
{
    var modName = Path.GetFileName(modDir);
    if (only is not null && !string.Equals(only, modName, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    var targetsPath = Path.Combine(modDir, "patch-targets.json");
    if (!File.Exists(targetsPath))
    {
        // A mod that patches nothing is possible, but silently skipping one that should
        // have a manifest is how a gap goes unnoticed. Say so.
        Console.WriteLine($"  note: {modName} has no patch-targets.json, skipping metadata checks");
        continue;
    }

    var manifest = JsonSerializer.Deserialize<TargetManifest>(File.ReadAllText(targetsPath), jsonOptions);
    checker.Check(manifest, modName);
    verifiedMods.Add(modName);
}

// Behavioural checks: the logic a JSON manifest cannot express.
var allChecks = new List<IModChecks>
{
    new PerkCooldownHudChecks(),
};

foreach (var modChecks in allChecks)
{
    if (only is not null && !string.Equals(only, modChecks.ModName, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    modChecks.Run(reporter);
}

// ---- Report ----------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine($"Game assembly : {gamePath}");
Console.WriteLine($"Mods verified : {(verifiedMods.Count > 0 ? string.Join(", ", verifiedMods) : "(none)")}");
Console.WriteLine($"Checks run    : {reporter.Checks}");

if (reporter.FailureCount > 0)
{
    Console.Error.WriteLine();
    foreach (var f in reporter.Failures)
    {
        Console.Error.WriteLine("  FAIL: " + f);
    }

    Console.Error.WriteLine($"\n{reporter.FailureCount} problem(s). Affected mods would silently no-op or throw at patch time.");
    return 1;
}

if (reporter.Checks == 0)
{
    Console.Error.WriteLine("FAIL: nothing was actually checked.");
    return 1;
}

Console.WriteLine("OK - every patch target is present with the expected signature.");
return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return null;
}
