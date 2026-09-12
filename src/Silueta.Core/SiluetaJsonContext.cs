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

    /// <summary>Invented names this subject used before. A corpus redacted earlier still says one of
    /// these, so they stay claimed forever and still lead back here.</summary>
    public List<string> Retired { get; set; } = new();
}

/// <summary>The word lists of a lineage as they sit in its file.</summary>
public sealed class LineagePools
{
    public List<string> Given { get; set; } = new();

    public List<string> Family { get; set; } = new();
}

/// <summary>
/// A lineage on disk: what an organisation brings of its own. Both <see cref="Lineage"/> and
/// <see cref="Version"/> are empty by default and both are required, so that no other JSON object in the
/// world reads as a valid, empty lineage — the mistake the vault format made and paid for.
/// </summary>
public sealed class LineageFile
{
    public string Lineage { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>Recorded and fingerprinted; it does not select phonetic rules. See SiluetaLineage.</summary>
    public string Language { get; set; } = string.Empty;

    public LineagePools? Pools { get; set; }

    public Dictionary<string, string>? Labels { get; set; }

    public Dictionary<string, string>? Generalizations { get; set; }

    /// <summary>Present means these rules REPLACE the pack compiled into the build.</summary>
    public List<PatternRule>? Patterns { get; set; }
}

/// <summary>The vault on disk. Deliberately its own file: it is the only artefact that can undo the work.</summary>
public sealed class VaultFile
{
    /// <summary>"1" stored a bare code per subject and no surrogate; "2" stores both.
    /// <para>
    /// Empty by default on purpose. It used to default to "2", so any JSON object at all — <c>{}</c>, a
    /// roster, somebody's config — passed the version check and produced a valid, empty vault. Paired
    /// with an atomic writer that then replaced the file, one mistyped path re-minted every surrogate
    /// and destroyed whatever the file actually was.
    /// </para></summary>
    public string Version { get; set; } = string.Empty;

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
[JsonSerializable(typeof(LineageFile))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
public sealed partial class SiluetaJsonContext : JsonSerializerContext;
