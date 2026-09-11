using System.Text.Json.Serialization;

namespace Silueta.Core;

/// <summary>A known identifier as it travels in a JSON file, so a caller can hand Silueta a roster
/// without referencing the library's types.</summary>
public sealed class KnownIdentifierDto
{
    public string Value { get; set; } = string.Empty;

    public string Kind { get; set; } = nameof(IdentifierKind.OtherName);

    public string SubjectId { get; set; } = string.Empty;
}

/// <summary>The vault on disk. Deliberately its own file: it is the only artefact that can undo the work.</summary>
public sealed class VaultFile
{
    public string Version { get; set; } = "1";

    public Dictionary<string, string> Subjects { get; set; } = new();
}

/// <summary>
/// Source-generated serialisation. Reflection-based JSON would work today and break the moment this
/// assembly is trimmed for the browser demo, which is exactly where a privacy tool most wants to run.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(PatternRule[]))]
[JsonSerializable(typeof(KnownIdentifierDto[]))]
[JsonSerializable(typeof(RedactionManifest))]
[JsonSerializable(typeof(VaultFile))]
public sealed partial class SiluetaJsonContext : JsonSerializerContext;
