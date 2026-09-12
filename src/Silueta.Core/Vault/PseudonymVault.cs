using System.Security.Cryptography;
using System.Text.Json;

namespace Silueta.Core;

/// <summary>
/// The table that turns a real subject into a stable, meaningless identity — and the only thing that can
/// turn it back.
/// <para>
/// It holds two things per subject, and it holds them together on purpose. The <em>code</em>
/// (<c>SIL-…</c>) is what a structured field refers to. The <em>surrogate</em> ("Cruz Medina") is what
/// the transcript says instead of the name. They used to live apart — the code minted here, the name
/// computed elsewhere from a hash — and a pair that is never written down side by side is a pair that
/// cannot be undone, audited, or even shown to be consistent.
/// </para>
/// <para>
/// Both are random, never derived from the person: 45 CFR § 164.514(c) requires that a re-identification
/// code not be derived from or related to information about the individual, which rules out the hash of a
/// name or a date of birth that so many pipelines reach for. Random also means the vault is the single
/// point of failure, so it stays inside the agency and never travels with the data.
/// </para>
/// </summary>
public sealed class PseudonymVault
{
    private const string FileVersion = "2";

    private readonly Dictionary<string, string> _codes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bySubject = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _surrogates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _takenSurrogates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _takenGiven = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many subjects this vault knows.</summary>
    public int Count => _codes.Count;

    /// <summary>Returns this subject's re-identification code, minting one the first time it is asked for.</summary>
    public string PseudonymFor(string subjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        if (_codes.TryGetValue(subjectId, out string? existing))
        {
            return existing;
        }

        // 128 bits, and checked rather than assumed. Sixty-four bits — what this used to mint — is
        // inside birthday range for a corpus of any size, and two subjects sharing a code is two people
        // becoming one person in every downstream table.
        string code;
        do
        {
            code = "SIL-" + RandomNumberGenerator.GetHexString(32, lowercase: true);
        }
        while (_bySubject.ContainsKey(code));

        _codes[subjectId] = code;
        _bySubject[code] = subjectId;
        return code;
    }

    /// <summary>
    /// Returns the invented name this subject is known by in redacted text, minting one the first time.
    /// </summary>
    /// <param name="avoid">Values that must not be echoed back: the roster of the record being redacted.
    /// A surrogate is rejected if any of its words <em>sounds like</em> any word of any of these, not
    /// merely if it equals one. Equality would not be enough — the matcher that finds names is phonetic,
    /// so a surrogate that merely sounds like a roster name is found and replaced again on the next pass,
    /// and a corpus that changes every time it is reprocessed cannot be reproduced by anyone.</param>
    public string SurrogateFor(string subjectId, IEnumerable<string>? avoid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        if (_surrogates.TryGetValue(subjectId, out string? existing))
        {
            // Stability wins over the avoid list: a subject who already has a name keeps it. Changing it
            // because a later record mentions someone similar would rewrite the corpus behind the caller.
            return existing;
        }

        HashSet<string> forbidden = PhoneticKeysOf(avoid);
        string surrogate = Mint(forbidden);

        _surrogates[subjectId] = surrogate;
        Remember(surrogate);
        PseudonymFor(subjectId); // a subject in the text is a subject in the vault, both halves of it
        return surrogate;
    }

    /// <summary>
    /// Pins a subject to a surrogate the caller chose. For an agency that wants its own invented names,
    /// and for anything that has to be reproducible — a demo, a fixture, a published example.
    /// </summary>
    public PseudonymVault Assign(string subjectId, string surrogate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(surrogate);

        _surrogates[subjectId] = surrogate.Trim();
        Remember(surrogate.Trim());
        PseudonymFor(subjectId);
        return this;
    }

    /// <summary>Goes back to the real subject. Only meaningful inside the trusted environment, and the
    /// caller is expected to log every call: this is the one operation an auditor will ask about.</summary>
    public bool TryReidentify(string pseudonym, out string subjectId) => _bySubject.TryGetValue(pseudonym, out subjectId!);

    public string ToJson()
    {
        var file = new VaultFile { Version = FileVersion };
        foreach ((string subjectId, string code) in _codes)
        {
            file.Subjects[subjectId] = new VaultEntry
            {
                Pseudonym = code,
                Surrogate = _surrogates.GetValueOrDefault(subjectId, string.Empty),
            };
        }

        return JsonSerializer.Serialize(file, SiluetaJsonContext.Default.VaultFile);
    }

    public static PseudonymVault FromJson(string json)
    {
        var vault = new PseudonymVault();
        VaultFile? file = JsonSerializer.Deserialize(json, SiluetaJsonContext.Default.VaultFile);
        if (file is null)
        {
            return vault;
        }

        if (file.Version != FileVersion)
        {
            throw new InvalidOperationException(
                $"This vault is version '{file.Version}'; this build reads version {FileVersion}. Version 1 " +
                "stored only the re-identification code, not the invented name, so the two cannot be " +
                "reconciled automatically — redact the corpus again with a new vault.");
        }

        foreach ((string subjectId, VaultEntry entry) in file.Subjects)
        {
            if (string.IsNullOrWhiteSpace(entry.Pseudonym))
            {
                continue;
            }

            vault._codes[subjectId] = entry.Pseudonym;
            vault._bySubject[entry.Pseudonym] = subjectId;

            if (!string.IsNullOrWhiteSpace(entry.Surrogate))
            {
                vault._surrogates[subjectId] = entry.Surrogate;
                vault.Remember(entry.Surrogate);
            }
        }

        return vault;
    }

    /// <summary>Reads the vault at this path, or starts an empty one if there is nothing there yet.</summary>
    public static PseudonymVault LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new PseudonymVault();
    }

    /// <summary>
    /// Writes the vault, replacing any previous one only once the new file is complete.
    /// <para>
    /// Written to a neighbouring temporary file and moved over the original, because the failure this
    /// prevents is unrecoverable: a process killed halfway through a direct write leaves a truncated
    /// vault, and a truncated vault is a set of people who can no longer be identified by the one party
    /// entitled to identify them. There is no second copy by design.
    /// </para>
    /// </summary>
    public void SaveTo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(directory);

        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporary, ToJson());

        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Draws a name that is not already in use and does not sound like anything on the roster.
    /// <para>
    /// Given names are exhausted before any is reused, because half the mentions in a transcript are a
    /// first name alone: while unused ones remain, two subjects sharing "Alex" would make "Alex said" a
    /// sentence about either of two people. Past <see cref="Surrogates.Given"/>'s length, given names do
    /// repeat and only the full pair stays unique — two people in a corpus can share a first name, as
    /// they do in life. That costs readability, never privacy.
    /// </para>
    /// </summary>
    private string Mint(HashSet<string> forbidden)
    {
        string[] given = Shuffled(Surrogates.Given);
        string[] family = Shuffled(Surrogates.Family);

        // Two sweeps: the first will not reuse a given name, the second is allowed to.
        for (int sweep = 0; sweep < 2; sweep++)
        {
            foreach (string first in given)
            {
                if (sweep == 0 && _takenGiven.Contains(first))
                {
                    continue;
                }

                if (forbidden.Contains(PhoneticKey.Compute(first)))
                {
                    continue;
                }

                foreach (string last in family)
                {
                    if (forbidden.Contains(PhoneticKey.Compute(last)))
                    {
                        continue;
                    }

                    string candidate = $"{first} {last}";
                    if (!_takenSurrogates.Contains(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        throw new InvalidOperationException(
            $"The surrogate pool is exhausted: {_takenSurrogates.Count} names are in use and the roster " +
            "rules out the rest. Widen the name lists before redacting a corpus this large.");
    }

    private void Remember(string surrogate)
    {
        _takenSurrogates.Add(surrogate);

        int space = surrogate.IndexOf(' ');
        _takenGiven.Add(space < 0 ? surrogate : surrogate[..space]);
    }

    /// <summary>Every word of every value the caller says must not come back, as the matcher hears it.</summary>
    private static HashSet<string> PhoneticKeysOf(IEnumerable<string>? values)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (values is null)
        {
            return keys;
        }

        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (Token token in Tokenizer.Tokenize(value))
            {
                string key = PhoneticKey.Compute(token.Text);
                if (key.Length > 0)
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    /// <summary>
    /// A random order, so the pool is not walked alphabetically and "Ale Aguilar" is not every corpus's
    /// first patient. Fisher-Yates over a copy; the source arrays are never touched.
    /// </summary>
    private static string[] Shuffled(string[] source)
    {
        string[] copy = (string[])source.Clone();
        RandomNumberGenerator.Shuffle(copy.AsSpan());
        return copy;
    }
}
