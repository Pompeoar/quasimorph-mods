// Verifies that every member this mod patches actually exists, with the expected shape,
// in the SHIPPED Assembly-CSharp.dll - not in a decompiler dump that may be stale.
//
// Harmony resolves its targets by name at runtime, so a silent rename in a game update
// would otherwise surface as "mod does nothing" with no error. This is the gate.
//
// Usage: dotnet run --project tools\VerifyPatchTargets -- "<path to Managed folder>"

using System.Reflection;

var managed = args.Length > 0
    ? args[0]
    : @"C:\Program Files (x86)\Steam\steamapps\common\Quasimorph\Quasimorph_Data\Managed";

if (!Directory.Exists(managed))
{
    Console.Error.WriteLine($"FAIL: Managed folder not found: {managed}");
    return 1;
}

var assemblies = Directory.GetFiles(managed, "*.dll").ToList();
var resolver = new PathAssemblyResolver(assemblies);
using var mlc = new MetadataLoadContext(resolver, "netstandard");

var game = mlc.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));

var failures = new List<string>();
var checks = 0;

Type Need(string fullName)
{
    var t = game.GetType(fullName);
    checks++;
    if (t is null)
    {
        failures.Add($"missing type {fullName}");
    }

    return t;
}

void NeedProperty(Type t, string name, string expectedReturn)
{
    if (t is null) return;

    checks++;
    var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (p is null)
    {
        failures.Add($"missing property {t.Name}.{name}");
        return;
    }

    if (p.PropertyType.Name != expectedReturn)
    {
        failures.Add($"{t.Name}.{name} is {p.PropertyType.Name}, expected {expectedReturn}");
    }

    if (p.GetMethod is null)
    {
        failures.Add($"{t.Name}.{name} has no getter to patch");
    }
}

void NeedMethod(Type t, string name, params string[] paramTypeNames)
{
    if (t is null) return;

    checks++;
    var candidates = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .Where(m => m.Name == name)
        .ToList();

    if (candidates.Count == 0)
    {
        failures.Add($"missing method {t.Name}.{name}");
        return;
    }

    var match = candidates.FirstOrDefault(m =>
        m.GetParameters().Select(p => p.ParameterType.Name).SequenceEqual(paramTypeNames));

    if (match is null)
    {
        var seen = string.Join(" | ", candidates.Select(c =>
            $"({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name))})"));
        failures.Add($"{t.Name}.{name} has no overload ({string.Join(", ", paramTypeNames)}); found {seen}");
    }
}

// ---- PerkTrigger: the four getters we postfix, plus the state they read -----------------
var perkTrigger = Need("MGSC.PerkTrigger");
NeedProperty(perkTrigger, "Show", "Boolean");
NeedProperty(perkTrigger, "ViewValue", "Single");
NeedProperty(perkTrigger, "BlinkOnChange", "Boolean");
NeedProperty(perkTrigger, "IsRedView", "Boolean");
NeedProperty(perkTrigger, "IsInActivePhase", "Boolean");
NeedProperty(perkTrigger, "ActivePhaseDuration", "Int32");

// Duration lives on BaseEffect and is what we report as the cooldown remaining.
var baseEffect = Need("MGSC.BaseEffect");
NeedProperty(baseEffect, "Duration", "Int32");

if (perkTrigger is not null && baseEffect is not null)
{
    checks++;
    if (!baseEffect.IsAssignableFrom(perkTrigger))
    {
        failures.Add("PerkTrigger no longer derives from BaseEffect");
    }
}

var effectWithView = Need("MGSC.IEffectWithView");
if (perkTrigger is not null && effectWithView is not null)
{
    checks++;
    if (!perkTrigger.GetInterfaces().Any(i => i.FullName == "MGSC.IEffectWithView"))
    {
        failures.Add("PerkTrigger no longer implements IEffectWithView");
    }
}

// ---- CommonEffectPanel: the two methods we postfix to drive the alpha -------------------
var panel = Need("MGSC.CommonEffectPanel");
NeedMethod(panel, "Initialize", "Creatures", "IEffectWithView", "Sprite");
NeedMethod(panel, "RefreshValue", "List`1");

// ---- Mod loader surface ----------------------------------------------------------------
var hook = Need("MGSC.Hook");
Need("MGSC.ModHookType");
var modContext = Need("MGSC.IModContext");

if (hook is not null)
{
    checks++;
    var ctor = hook.GetConstructors().FirstOrDefault(c =>
        c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType.Name == "ModHookType");
    if (ctor is null)
    {
        failures.Add("MGSC.Hook has no (ModHookType) constructor");
    }
}

if (modContext is not null)
{
    checks++;
    if (modContext.GetProperty("ModContentPath") is null)
    {
        failures.Add("IModContext.ModContentPath missing");
    }
}

// ---- Independent replay of the countdown the player will actually see ------------------
// Reimplemented from PerkTrigger's own arithmetic so an off-by-one in the patch shows up
// here rather than in game. Uses an unsaturated case (active 3, cooldown 5) where every
// number is distinct.
static (List<string> active, List<string> cooling) Replay(int activeTurns, int cooldownTurns, int cdRecoveryPerTurn = 0)
{
    var activePhaseDuration = activeTurns;
    var duration = activeTurns + cooldownTurns;
    var originalDuration = duration;

    var shownActive = new List<string>();
    var shownCooling = new List<string>();

    // The effect is removed - and the perk becomes usable again - when Duration hits 0.
    while (duration > 0)
    {
        var isInActivePhase = originalDuration - duration <= activePhaseDuration - 1;

        // Vanilla ViewValue, then our patch's override.
        var view = (float)(activePhaseDuration - Math.Abs(duration - originalDuration));
        if (!isInActivePhase)
        {
            view = duration;
        }

        if (isInActivePhase)
        {
            shownActive.Add(view.ToString());
        }
        else
        {
            shownCooling.Add(view.ToString());
        }

        duration -= 1 + (isInActivePhase ? 0 : cdRecoveryPerTurn);
    }

    return (shownActive, shownCooling);
}

void Expect(string label, string actual, string expected)
{
    checks++;
    if (actual != expected)
    {
        failures.Add($"{label}: got [{actual}], expected [{expected}]");
    }
}

var (active, cooling) = Replay(activeTurns: 3, cooldownTurns: 5);
Expect("active phase readout", string.Join(",", active), "3,2,1");
Expect("cooldown readout", string.Join(",", cooling), "5,4,3,2,1");

checks++;
if (cooling.Count != 5)
{
    failures.Add($"cooldown shown for {cooling.Count} turns, config says 5");
}

// No value may ever render negative - that is the bug the ViewValue patch exists to fix.
checks++;
if (active.Concat(cooling).Any(v => v.StartsWith("-") || v == "0"))
{
    failures.Add("a non-positive value would be displayed");
}

// ICDRecovery makes cooldowns tick down faster; the readout must stay truthful.
var (_, fastCooling) = Replay(activeTurns: 2, cooldownTurns: 6, cdRecoveryPerTurn: 1);
Expect("cooldown readout with ICDRecovery=1", string.Join(",", fastCooling), "6,4,2");

// ---- Report ----------------------------------------------------------------------------
Console.WriteLine($"Checked {checks} patch target(s) against {Path.Combine(managed, "Assembly-CSharp.dll")}");

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    foreach (var f in failures)
    {
        Console.Error.WriteLine("  FAIL: " + f);
    }

    Console.Error.WriteLine($"\n{failures.Count} problem(s). The mod would silently no-op.");
    return 1;
}

Console.WriteLine("OK - every patch target is present with the expected signature.");
return 0;
