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

/// <summary>What the vault holds about one subject: the code a structured field refers to, and the
/// invented name the transcript says. Stored together because a pair that is not written down side by
/// side cannot be undone or audited.</summary>
public sealed class VaultEntry
{
    public string Pseudonym { get; set; } = string.Empty;

    public string Surrogate { get; set; } = string.Empty;
}

/// <summary>The vault on disk. Deliberately its own file: it is the only artefact that can undo the work.</summary>
public sealed class VaultFile
{
    /// <summary>"1" stored a bare code per subject and no surrogate; "2" stores both.</summary>
    public string Version { get; set; } = "2";

    public Dictionary<string, VaultEntry> Subjects { get; set; } = new();
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
