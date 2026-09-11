using System.Security.Cryptography;
using System.Text.Json;

namespace Silueta.Core;

/// <summary>
/// The table that turns a real subject into a stable, meaningless id — and the only thing that can turn
/// it back.
/// <para>
/// Ids are random, never derived from the person: 45 CFR § 164.514(c) requires that a re-identification
/// code not be derived from or related to information about the individual, which rules out the hash of a
/// name or a date of birth that so many pipelines reach for. Random also means the vault is the single
/// point of failure, so it stays inside the agency and never travels with the data.
/// </para>
/// </summary>
public sealed class PseudonymVault
{
    private readonly Dictionary<string, string> _forward = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _reverse = new(StringComparer.Ordinal);

    /// <summary>Returns this subject's pseudonym, minting one the first time it is asked for.</summary>
    public string PseudonymFor(string subjectId)
    {
        if (_forward.TryGetValue(subjectId, out string? existing))
        {
            return existing;
        }

        string pseudonym = "SIL-" + RandomNumberGenerator.GetHexString(16, lowercase: true);
        _forward[subjectId] = pseudonym;
        _reverse[pseudonym] = subjectId;
        return pseudonym;
    }

    /// <summary>Goes back to the real subject. Only meaningful inside the trusted environment, and the
    /// caller is expected to log every call: this is the one operation an auditor will ask about.</summary>
    public bool TryReidentify(string pseudonym, out string subjectId) => _reverse.TryGetValue(pseudonym, out subjectId!);

    public int Count => _forward.Count;

    public string ToJson() => JsonSerializer.Serialize(
        new VaultFile { Subjects = new Dictionary<string, string>(_forward) },
        SiluetaJsonContext.Default.VaultFile);

    public static PseudonymVault FromJson(string json)
    {
        var vault = new PseudonymVault();
        VaultFile? file = JsonSerializer.Deserialize(json, SiluetaJsonContext.Default.VaultFile);
        if (file is null)
        {
            return vault;
        }

        foreach ((string subjectId, string pseudonym) in file.Subjects)
        {
            vault._forward[subjectId] = pseudonym;
            vault._reverse[pseudonym] = subjectId;
        }

        return vault;
    }
}
