using System.Text.Json;
using System.Text.Json.Serialization;

namespace Silueta.Core;

/// <summary>A known identifier as it travels in a JSON file, so a caller can hand Silueta a roster
/// without referencing the library's types.</summary>
public sealed class KnownIdentifierDto
{
    public string Value { get; set; } = string.Empty;

    /// <summary>Empty by default, and required. It defaulted to "OtherName", so a roster entry with no
    /// kind was a person — and after the company kinds, a company left without one went to the pool of
    /// people's names. The third time this project has met a required field with a harmless default.</summary>
    public string Kind { get; set; } = string.Empty;

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

    /// <summary>
    /// Pool name to word list. Read as raw JSON rather than as lists, on purpose: a key starting with "_"
    /// is a comment — the convention the built-in file teaches — and a list-typed map would refuse the
    /// whole file over one. The loader matches the names regardless of case, because a dictionary's keys
    /// are case-sensitive even where the serializer's property names are not, and "Given" loaded fine
    /// before this was a map.
    /// </summary>
    public Dictionary<string, JsonElement>? Pools { get; set; }

    public Dictionary<string, string>? Labels { get; set; }

    public Dictionary<string, string>? Generalizations { get; set; }

    /// <summary>Present means these rules REPLACE the pack compiled into the build.</summary>
    public List<PatternRule>? Patterns { get; set; }
}

/// <summary>The quasi-identifier vocabulary as it sits in its embedded file.</summary>
public sealed class QuasiIdentifierVocabularyFile
{
    public string Vocabulary { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public List<string>? Kinship { get; set; }

    public List<string>? AgeBrackets { get; set; }
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
[JsonSerializable(typeof(QuasiIdentifierVocabularyFile))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
public sealed partial class SiluetaJsonContext : JsonSerializerContext;
