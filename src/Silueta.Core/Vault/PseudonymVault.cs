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
    private readonly Dictionary<string, List<string>> _retired = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bySurrogate = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="pools">The word lists this vault mints from. The vault keeps them because the vault
    /// is what mints: asking anyone else what the head of a surrogate is means two answers to one
    /// question the first time a lineage has a two-word entry in it.</param>
    public PseudonymVault(SurrogatePools? pools = null) => Pools = pools ?? SiluetaLineage.Default.Pools;

    /// <summary>The word lists this vault mints from.</summary>
    public SurrogatePools Pools { get; }

    /// <summary>How many subjects this vault knows.</summary>
    public int Count => _codes.Count;

    /// <summary>Every invented name this vault has handed out, retired ones included — the strings a
    /// redacted corpus can contain. No subject ids and no codes: this is what a reader of the corpus could
    /// already collect, and it is what a linkage report is keyed by.</summary>
    public IReadOnlyCollection<string> Surrogates => _bySurrogate.Keys;

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
    /// <para>
    /// With no record in hand there is nothing to check the name against, so this only avoids names the
    /// vault itself has already given out. A caller that is about to redact a record has a record: use
    /// the overload taking a predicate, which is what the engine does.
    /// </para>
    /// </summary>
    public string SurrogateFor(string subjectId) => SurrogateFor(subjectId, static _ => false);

    /// <summary>
    /// Returns the invented name this subject is known by in redacted text, minting one the first time,
    /// rejecting any whose words sound like a word of <paramref name="avoid"/>.
    /// </summary>
    /// <param name="avoid">Values that must not be echoed back: the roster of the record being redacted.</param>
    [Obsolete(
        "This is the cheap test and it is not enough: sounding alike here means an identical phonetic " +
        "key, while the matcher forgives a budget of edits on top. Real surnames one edit from the pool " +
        "(Aguiar/Aguilar, Quinteros/Quintero, Fuente/Fuentes) pass this check and are then found by the " +
        "very pipeline that wrote them. Pass the detectors themselves: SurrogateFor(id, wouldBeFound).")]
    public string SurrogateFor(string subjectId, IEnumerable<string>? avoid)
    {
        HashSet<string> forbidden = PhoneticKeysOf(avoid);
        return SurrogateFor(subjectId, candidate => Tokenizer.Tokenize(candidate)
            .Any(token => forbidden.Contains(PhoneticKey.Compute(token.Text))));
    }

    /// <summary>
    /// Returns the invented name this subject is known by in redacted text, minting one the first time.
    /// </summary>
    /// <param name="wouldBeFound">Asked of every candidate: would this name be detected in this record?
    /// <para>
    /// The engine answers by running the detectors it is about to run anyway, which is the only test
    /// that cannot drift. Comparing phonetic keys here instead would mean writing the matcher's
    /// threshold down in a second place, and the two copies disagreeing is precisely the bug: a
    /// surrogate that merely sounds like a roster name is found and replaced again on the next pass,
    /// and a corpus that changes every time it is reprocessed cannot be reproduced by anyone.
    /// </para></param>
    public string SurrogateFor(string subjectId, Func<string, bool> wouldBeFound) =>
        SurrogateFor(subjectId, IdentifierKind.OtherName, wouldBeFound);

    /// <summary>
    /// Returns the invented name this subject is known by in redacted text, minting one of the right shape
    /// for its kind the first time: a person's name for a person, a company's for an organisation.
    /// </summary>
    /// <param name="kind">Decides which of the lineage's pools the name is drawn from. It does not
    /// decide anything for a subject that already has a name — see the note inside.</param>
    /// <param name="wouldBeFound">As in the overload without a kind: the engine's own detectors.</param>
    /// <exception cref="InvalidOperationException">The lineage has no pool for this kind. The engine
    /// checks <see cref="SurrogatePools.Has"/> first and labels instead; this is for any other caller,
    /// who must not silently receive a name drawn from another kind's pool.</exception>
    public string SurrogateFor(string subjectId, IdentifierKind kind, Func<string, bool> wouldBeFound)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentNullException.ThrowIfNull(wouldBeFound);

        if (_surrogates.TryGetValue(subjectId, out string? existing))
        {
            // Stability wins: a subject who already has a name keeps it. Changing it because a later
            // record mentions someone similar would rewrite the corpus behind the caller. That includes a
            // later record that calls the subject a different kind: a roster that listed a company as
            // OtherName on the first run pinned a person's name to it, and the way out is Remint with the
            // right kind — which records what it replaced — not a quiet rename here.
            return existing;
        }

        string surrogate = Mint(kind, wouldBeFound);

        _surrogates[subjectId] = surrogate;
        Remember(surrogate, subjectId);
        PseudonymFor(subjectId); // a subject in the text is a subject in the vault, both halves of it
        return surrogate;
    }

    /// <summary>
    /// Pins a subject to a surrogate the caller chose. For an agency that wants its own invented names,
    /// and for anything that has to be reproducible — a demo, a fixture, a published example.
    /// <para>
    /// It enforces the half of <c>Mint</c>'s invariants that do not need a record: one subject per name,
    /// one name per subject, and never a name already retired. It is the second door into the same table
    /// and a door with no lock at all is the shape of every defect in this file's history — without the
    /// check below, <c>Assign("patient-1", "Ale Espinal").Assign("family-1", "Ale Espinal")</c> was
    /// accepted in silence: a mother and her daughter became one person in the corpus, and because the
    /// vault then held two subjects pointing at one name, neither could be traced back.
    /// </para>
    /// <para>
    /// What it cannot enforce is <c>Mint</c>'s other half: that no detector would find this name in the
    /// record. The caller chose the name and the caller holds the roster, so the check the engine makes
    /// is not available here. A name assigned by hand is checked when it is used — the engine reads its
    /// own output back and reports it as residue — and not before.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">The name is already in use by another subject.</exception>
    public PseudonymVault Assign(string subjectId, string surrogate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(surrogate);

        string chosen = surrogate.Trim();

        if (_surrogates.TryGetValue(subjectId, out string? already) && !string.Equals(already, chosen, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Subject '{subjectId}' is already known as something else in this vault. Changing it would " +
                "rewrite every transcript already redacted under the old name.", nameof(subjectId));
        }

        if (_takenSurrogates.Contains(chosen) && !_surrogates.TryGetValue(subjectId, out _))
        {
            throw new ArgumentException(
                "That invented name is already in use by another subject in this vault. Two people sharing " +
                "one invented name merges them in the corpus, and neither can be traced back.", nameof(surrogate));
        }

        _surrogates[subjectId] = chosen;
        Remember(chosen, subjectId);
        PseudonymFor(subjectId);
        return this;
    }

    /// <summary>
    /// This subject's invented name, without minting one. The <c>SurrogateFor</c> overloads mint on a
    /// miss, which makes them useless as a question: asking mutated the vault.
    /// </summary>
    public bool TryGetSurrogate(string subjectId, out string surrogate) =>
        _surrogates.TryGetValue(subjectId, out surrogate!);

    /// <summary>
    /// The subject behind an invented name, including one that has been retired.
    /// <para>
    /// This is the way in that the holder of a redacted corpus actually has. A redacted transcript
    /// contains invented names and never a <c>SIL-</c> code, so <see cref="TryReidentify"/> — which takes
    /// the code — could only be entered from the one side nobody holds.
    /// </para>
    /// </summary>
    public bool TryFindSubjectBySurrogate(string surrogate, out string subjectId)
    {
        subjectId = null!;
        return !string.IsNullOrWhiteSpace(surrogate) && _bySurrogate.TryGetValue(surrogate.Trim(), out subjectId!);
    }

    /// <summary>Invented names this subject used before, oldest first. Empty for almost every subject.</summary>
    public IReadOnlyList<string> RetiredSurrogatesFor(string subjectId) =>
        _retired.TryGetValue(subjectId, out List<string>? names) ? names : [];

    /// <summary>
    /// Gives a subject a new invented name, keeping the old one claimed and still traceable.
    /// <para>
    /// This is the escape from a record that can never be exported. A surrogate is cleared against the
    /// roster of the record it was minted for; the vault then keeps it, deliberately, so a later record
    /// whose roster holds that same name leaves residue no rerun can clear. Without this the only way out
    /// was to hand-edit the vault, which every document in this repository forbids.
    /// </para>
    /// <para>
    /// The retired name is never handed to anyone else and still leads back to this subject, because a
    /// corpus redacted before the remint still says it. Orphaning those documents would be a worse
    /// failure than the one being fixed.
    /// </para>
    /// </summary>
    public string Remint(string subjectId, Func<string, bool> wouldBeFound) =>
        Remint(subjectId, IdentifierKind.OtherName, wouldBeFound);

    /// <summary>
    /// Gives a subject a new invented name of the shape its kind needs. Takes the kind because this is
    /// the one path that exists to fix a bad name: without it, reminting a company would hand it a
    /// person's name, which is the defect being fixed.
    /// </summary>
    public string Remint(string subjectId, IdentifierKind kind, Func<string, bool> wouldBeFound)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentNullException.ThrowIfNull(wouldBeFound);

        if (!_surrogates.TryGetValue(subjectId, out string? old))
        {
            return SurrogateFor(subjectId, kind, wouldBeFound);
        }

        string replacement = Mint(kind, wouldBeFound);

        if (!_retired.TryGetValue(subjectId, out List<string>? history))
        {
            _retired[subjectId] = history = [];
        }

        history.Add(old);
        _surrogates[subjectId] = replacement;
        Remember(replacement, subjectId);
        return replacement;
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
                Retired = _retired.TryGetValue(subjectId, out List<string>? history) ? [.. history] : [],
            };
        }

        return JsonSerializer.Serialize(file, SiluetaJsonContext.Default.VaultFile);
    }

    public static PseudonymVault FromJson(string json, SurrogatePools? pools = null)
    {
        var vault = new PseudonymVault(pools);
        VaultFile? file = JsonSerializer.Deserialize(json, SiluetaJsonContext.Default.VaultFile);
        if (file is null)
        {
            return vault;
        }

        if (file.Version != FileVersion)
        {
            // An absent version used to default to "2", so any JSON object — {}, a roster, somebody's
            // config — became a valid, empty vault. Paired with an atomic writer that then replaced the
            // file, one mistyped path re-minted every surrogate and destroyed whatever the file was.
            throw new InvalidOperationException(
                file.Version.Length == 0
                    ? "This is not a vault: it carries no version. Refusing, rather than reading it as an " +
                      "empty one, because the next thing that happens is a vault being written over it."
                    : $"This vault is version '{file.Version}'; this build reads version {FileVersion}. " +
                      "Version 1 stored only the re-identification code, not the invented name, so the two " +
                      "cannot be reconciled automatically — redact the corpus again with a new vault.");
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
                // Trimmed, exactly as Assign trims. A file written by hand or by another tool can hold
                // " Cruz Medina"; stored raw, Remember's space-slice put "" into the taken-given set and
                // a value into the taken-surrogates set that Mint's exact comparison could never match,
                // so the vault would cheerfully hand the same name to a second subject.
                string stored = entry.Surrogate.Trim();
                vault._surrogates[subjectId] = stored;
                vault.Remember(stored, subjectId);
            }

            foreach (string retired in entry.Retired.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                string stored = retired.Trim();
                if (!vault._retired.TryGetValue(subjectId, out List<string>? history))
                {
                    vault._retired[subjectId] = history = [];
                }

                history.Add(stored);
                vault.Remember(stored, subjectId);
            }
        }

        return vault;
    }

    /// <summary>Reads the vault at this path, or starts an empty one if there is nothing there yet.</summary>
    /// <param name="pools">What to mint from when this vault has to invent a name. A vault read back
    /// from disk still needs them: the names already in it are kept, and the next subject is new.</param>
    public static PseudonymVault LoadOrCreate(string path, SurrogatePools? pools = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path) ? FromJson(File.ReadAllText(path), pools) : new PseudonymVault(pools);
    }

    /// <summary>Whether this text is a vault this build wrote. Asked before overwriting a file.</summary>
    private static bool IsAVault(string text)
    {
        try
        {
            return JsonSerializer.Deserialize(text, SiluetaJsonContext.Default.VaultFile)?.Version == FileVersion;
        }
        catch (JsonException)
        {
            return false;
        }
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

        // Refuse to replace something that is not a vault. The atomic write below is careful about
        // interruption and says nothing about aim: pointed at the wrong file, it destroyed it perfectly.
        if (File.Exists(path) && !IsAVault(File.ReadAllText(path)))
        {
            throw new InvalidOperationException(
                $"'{path}' exists and is not a vault this build wrote. Refusing to replace it — a vault has " +
                "no second copy, and neither, probably, does that file.");
        }

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
    /// Draws a name of the right shape for the kind, not already in use, and not something this pipeline
    /// would find in the record.
    /// <para>
    /// Heads are exhausted before any is reused, because half the mentions in a transcript are a first
    /// name alone: while unused ones remain, two subjects sharing "Alex" would make "Alex said" a sentence
    /// about either of two people. Past the head pool's length, heads do repeat and only the full pair
    /// stays unique — two people in a corpus can share a first name, as they do in life. That costs
    /// readability, never privacy.
    /// </para>
    /// <para>
    /// A company or product with no suffix pool is drawn whole from its heads: each entry is the name.
    /// One namespace for every kind — a company and a person never share an invented name, because the
    /// way back from a redacted transcript starts from a string, and a string does not say its kind.
    /// </para>
    /// </summary>
    private string Mint(IdentifierKind kind, Func<string, bool> wouldBeFound)
    {
        (IReadOnlyList<string> heads, IReadOnlyList<string> tails, string headPool) = Pools.For(kind);

        string[] given = Shuffled(heads);
        string[] family = Shuffled(tails);

        // The head is tested on its own as well as inside the pair, because a one-word mention is
        // replaced by the head alone: a surrogate that is safe as "Remy Aguilar" is not safe if "Remy" by
        // itself would be found.
        var rejectedGiven = new HashSet<string>(StringComparer.Ordinal);

        // Two sweeps: the first will not reuse a head, the second is allowed to.
        for (int sweep = 0; sweep < 2; sweep++)
        {
            foreach (string first in given)
            {
                if (sweep == 0 && _takenGiven.Contains(first))
                {
                    continue;
                }

                if (rejectedGiven.Contains(first))
                {
                    continue;
                }

                if (wouldBeFound(first))
                {
                    rejectedGiven.Add(first);
                    continue;
                }

                if (family.Length == 0)
                {
                    // Whole names: the entry is the surrogate, and the head test above was the whole test.
                    if (!_takenSurrogates.Contains(first))
                    {
                        return first;
                    }

                    continue;
                }

                foreach (string last in family)
                {
                    string candidate = $"{first} {last}";
                    if (!_takenSurrogates.Contains(candidate) && !wouldBeFound(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        throw new InvalidOperationException(
            $"The '{headPool}' pool is exhausted for {kind}: {_takenSurrogates.Count} invented names are in " +
            "use across this vault and this record's roster rules out the rest. Add entries to that pool " +
            "before redacting a corpus this large.");
    }

    private void Remember(string surrogate, string subjectId)
    {
        _takenSurrogates.Add(surrogate);
        _bySurrogate[surrogate] = subjectId;

        // The pools decide where the head ends, not the first space in the string. With one-word pool
        // entries the two agree; with "María José De la Cruz" they do not, and the vault would then
        // reserve "María" — a name it never minted and cannot look up.
        _takenGiven.Add(Pools.HeadOf(surrogate));
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
    private static string[] Shuffled(IReadOnlyList<string> source)
    {
        string[] copy = [.. source];
        RandomNumberGenerator.Shuffle(copy.AsSpan());
        return copy;
    }
}
