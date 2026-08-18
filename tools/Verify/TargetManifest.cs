using System.Text.Json.Serialization;

namespace Verify;

/// <summary>
/// Declarative description of the game members a mod patches. Each mod ships one of these
/// as src\&lt;Mod&gt;\patch-targets.json; the loader surface common to every mod lives in
/// tools\core-targets.json.
/// </summary>
public sealed class TargetManifest
{
    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("types")]
    public List<TargetType> Types { get; set; } = new();
}

public sealed class TargetType
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("baseType")]
    public string BaseType { get; set; }

    [JsonPropertyName("interfaces")]
    public List<string> Interfaces { get; set; } = new();

    [JsonPropertyName("properties")]
    public List<TargetMember> Properties { get; set; } = new();

    [JsonPropertyName("fields")]
    public List<TargetMember> Fields { get; set; } = new();

    [JsonPropertyName("methods")]
    public List<TargetMethod> Methods { get; set; } = new();

    [JsonPropertyName("constructors")]
    public List<List<string>> Constructors { get; set; } = new();
}

public sealed class TargetMember
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    /// <summary>Short type name, e.g. Boolean, Single, Int32, Image, Sprite, List`1.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("why")]
    public string Why { get; set; }
}

public sealed class TargetMethod
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    /// <summary>Short parameter type names. Omit to accept any single overload by name.</summary>
    [JsonPropertyName("params")]
    public List<string> Params { get; set; }

    [JsonPropertyName("why")]
    public string Why { get; set; }
}
