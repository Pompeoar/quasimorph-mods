using System.Reflection;

namespace Verify;

/// <summary>
/// Checks a TargetManifest against the shipped game assembly.
///
/// Harmony resolves patch targets by name at runtime, so a rename in a game update would
/// otherwise surface as "the mod silently does nothing" (for a method that no longer
/// matches) or as a TypeInitializationException deep in patching (for a field reached via
/// FieldRefAccess). This turns both into a build-time failure with a readable message.
/// </summary>
public sealed class MetadataChecker
{
    private readonly Assembly _game;
    private readonly Reporter _reporter;

    public MetadataChecker(Assembly game, Reporter reporter)
    {
        _game = game;
        _reporter = reporter;
    }

    public void Check(TargetManifest manifest, string source)
    {
        foreach (var target in manifest.Types)
        {
            var type = _game.GetType(target.Name);

            _reporter.Assert(type is not null, $"[{source}] missing type {target.Name}");
            if (type is null)
            {
                continue;
            }

            CheckRelationships(target, type, source);

            foreach (var p in target.Properties)
            {
                CheckProperty(type, p, source);
            }

            foreach (var f in target.Fields)
            {
                CheckField(type, f, source);
            }

            foreach (var m in target.Methods)
            {
                CheckMethod(type, m, source);
            }

            foreach (var ctor in target.Constructors)
            {
                CheckConstructor(type, ctor, source);
            }
        }
    }

    private void CheckRelationships(TargetType target, Type type, string source)
    {
        if (!string.IsNullOrEmpty(target.BaseType))
        {
            var expected = _game.GetType(target.BaseType);
            _reporter.Assert(
                expected is not null && expected.IsAssignableFrom(type),
                $"[{source}] {type.Name} no longer derives from {target.BaseType}");
        }

        foreach (var iface in target.Interfaces)
        {
            _reporter.Assert(
                type.GetInterfaces().Any(i => i.FullName == iface),
                $"[{source}] {type.Name} no longer implements {iface}");
        }
    }

    private void CheckProperty(Type type, TargetMember member, string source)
    {
        var p = type.GetProperty(member.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (p is null)
        {
            _reporter.Fail($"[{source}] missing property {type.Name}.{member.Name}{Because(member.Why)}");
            return;
        }

        _reporter.Assert(
            p.PropertyType.Name == member.Type,
            $"[{source}] {type.Name}.{member.Name} is {p.PropertyType.Name}, expected {member.Type}");

        _reporter.Assert(
            p.GetMethod is not null,
            $"[{source}] {type.Name}.{member.Name} has no getter to patch");
    }

    private void CheckField(Type type, TargetMember member, string source)
    {
        var f = type.GetField(member.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (f is null)
        {
            _reporter.Fail($"[{source}] missing field {type.Name}.{member.Name}{Because(member.Why)}");
            return;
        }

        _reporter.Assert(
            f.FieldType.Name == member.Type,
            $"[{source}] {type.Name}.{member.Name} is {f.FieldType.Name}, expected {member.Type}");
    }

    private void CheckMethod(Type type, TargetMethod method, string source)
    {
        var candidates = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name == method.Name)
            .ToList();

        if (candidates.Count == 0)
        {
            _reporter.Fail($"[{source}] missing method {type.Name}.{method.Name}{Because(method.Why)}");
            return;
        }

        if (method.Params is null)
        {
            // No signature given: only unambiguous if there is exactly one overload, since
            // Harmony would not know which to take either.
            _reporter.Assert(
                candidates.Count == 1,
                $"[{source}] {type.Name}.{method.Name} now has {candidates.Count} overloads; the manifest must name the parameter types");
            return;
        }

        var match = candidates.FirstOrDefault(m =>
            m.GetParameters().Select(p => p.ParameterType.Name).SequenceEqual(method.Params));

        if (match is null)
        {
            var seen = string.Join(" | ", candidates.Select(c =>
                $"({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name))})"));
            _reporter.Fail(
                $"[{source}] {type.Name}.{method.Name} has no overload ({string.Join(", ", method.Params)}); found {seen}");
            return;
        }

        _reporter.Assert(true, string.Empty);
    }

    private void CheckConstructor(Type type, List<string> paramTypeNames, string source)
    {
        var match = type.GetConstructors().FirstOrDefault(c =>
            c.GetParameters().Select(p => p.ParameterType.Name).SequenceEqual(paramTypeNames));

        _reporter.Assert(
            match is not null,
            $"[{source}] {type.Name} has no constructor ({string.Join(", ", paramTypeNames)})");
    }

    private static string Because(string why) => string.IsNullOrEmpty(why) ? string.Empty : $" - {why}";
}
