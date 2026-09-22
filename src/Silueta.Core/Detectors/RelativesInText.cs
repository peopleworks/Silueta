using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Silueta.Core;

/// <summary>A person named in the text through a relationship, before anything is decided about them.</summary>
/// <param name="Name">The name as said, one or two words: "Linda", "Michael Brandt".</param>
/// <param name="GivenName">Its first word.</param>
/// <param name="Surname">Its second word, when there was one.</param>
public readonly record struct NamedRelative(string Name, string GivenName, string? Surname, IdentifierKind Kind);

/// <summary>
/// People a transcript names through a relationship — "my daughter Linda", "su hija Lucía" — whom nobody put
/// on the roster.
/// <para>
/// A roster matcher cannot find someone it was never told about, and the relatives of a patient are exactly
/// who an agency does not list. Xari met it with their own demo data on 22 September 2026: "My daughter Linda
/// brought pie" came back with Linda in it. F2.7 named this residue on 11 September, before any corpus
/// existed, and named its order of attack: relationship rules first, measure, and only then decide whether a
/// model is needed. This is the first of those rules.
/// </para>
/// <para>
/// It is built from three inputs and no others — that text, the kinship words embedded for the linkage report
/// (committed before the corpus was frozen), and Xari's sentence — and it is deliberately narrow. The shape
/// is a relationship word and then a name: one or two title-cased words, nothing between them but a space
/// or a comma. It refuses what it cannot stand behind: a word with no lowercase letter ("I", a label, an
/// acronym), a word with a digit, a title ("Doctor Reyes" is F2.7's third item, not this one), a relationship
/// that ends with its sentence, and a capitalised relationship word with no possessive in front of it
/// ("Daughter Linda called" is too weak to act on). An appositive after the name ("Linda, my daughter") and a
/// relationship through "de" ("la hija de la señora Pérez") are not heard, and a test says so.
/// </para>
/// <para>
/// It finds candidates and nothing else. Whether a candidate is somebody the roster already knows, and what
/// becomes of the rest, is decided by the engine — see <see cref="SiluetaEngine.FindRelativesNamedInText"/>.
/// </para>
/// </summary>
public static class RelativesInText
{
    /// <summary>Bumped when the shape of the rule changes, so two corpora can say which rule found their relatives.</summary>
    public const string RuleVersion = "kinship/1";

    private static readonly HashSet<string> Possessives = new(StringComparer.Ordinal)
    {
        "my", "her", "his", "their", "our", "your",
        "mi", "mis", "su", "sus", "tu", "tus", "nuestra", "nuestro", "nuestras", "nuestros",
    };

    // Titles are not names. "Her sister Doctor Reyes" is left alone rather than half-taken: titles have their
    // own place in F2.7, and a rule that took "Doctor" as the name would be wrong in a way nobody could see.
    // "Don" is not here on purpose: it is an English given name ("my brother Don"), and the Spanish title is
    // written in lower case ("don Andrés"), which the title-case check already refuses.
    private static readonly HashSet<string> Titles = new(StringComparer.Ordinal)
    {
        "mr", "mrs", "ms", "miss", "mx", "dr", "doctor", "nurse",
        "sr", "sra", "srta", "senor", "senora", "senorita", "doctora", "enfermera", "enfermero",
    };

    // A relationship that is not family: the name is still a person, but not a relative.
    private static readonly HashSet<string> NotFamily = new(StringComparer.Ordinal)
    {
        "caregiver", "cuidadora", "cuidador",
    };

    /// <summary>
    /// A digest of the rule and the word list it read, for the manifest: two corpora whose relatives were
    /// found with different words are two different methods, and should say so.
    /// </summary>
    public static string Fingerprint(QuasiIdentifierVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);

        string canonical = RuleVersion + "\n" + string.Join('\n', vocabulary.Kinship.Select(Fold).Order(StringComparer.Ordinal));
        return $"{RuleVersion} {Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16]}";
    }

    /// <summary>The people this text names through a relationship, in reading order. Candidates only.</summary>
    public static IReadOnlyList<NamedRelative> NamedIn(string text, QuasiIdentifierVocabulary? vocabulary = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        vocabulary ??= QuasiIdentifierVocabulary.Default;

        var kinship = new HashSet<string>(vocabulary.Kinship.Select(Fold), StringComparer.Ordinal);
        List<Token> tokens = Tokenizer.Tokenize(text);
        var found = new List<NamedRelative>();

        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            string relation = Fold(tokens[i].Text);
            if (!kinship.Contains(relation))
            {
                continue;
            }

            // "Daughter Linda called" — a capitalised relationship word is too often the start of a sentence or
            // a place name to act on alone. With a possessive in front of it ("My daughter"), it is a relationship.
            bool possessive = i > 0 && Possessives.Contains(Fold(tokens[i - 1].Text)) && OnlySpaceBetween(text, tokens[i - 1], tokens[i]);
            if (StartsUpper(tokens[i].Text) && !possessive)
            {
                continue;
            }

            Token given = tokens[i + 1];
            if (!RelationReachesName(text, tokens[i], given) || !LooksLikeAName(given.Text, kinship))
            {
                continue;
            }

            string? surname = null;
            int end = given.End;
            if (i + 2 < tokens.Count
                && OnlySpaceBetween(text, given, tokens[i + 2])
                && LooksLikeAName(tokens[i + 2].Text, kinship))
            {
                surname = tokens[i + 2].Text;
                end = tokens[i + 2].End;
            }

            found.Add(new NamedRelative(
                text[given.Start..end],
                given.Text,
                surname,
                NotFamily.Contains(relation) ? IdentifierKind.OtherName : IdentifierKind.FamilyName));
        }

        return found;
    }

    /// <summary>
    /// Title case, which is how a recogniser writes a name: an upper-case first letter and at least one lower-case
    /// letter after it, letters only. "Linda", "O'Brien", "McDonald" and "Lucía" pass; "I", "FAMILY", "ASR" and
    /// "2B" do not — the second of those is what this pipeline writes in place of a relative, which is why the
    /// output can be read back without the rule mistaking its own label for a name.
    /// </summary>
    private static bool LooksLikeAName(string word, HashSet<string> kinship)
    {
        if (!StartsUpper(word) || Titles.Contains(Fold(word)) || kinship.Contains(Fold(word)))
        {
            return false;
        }

        bool lower = false;
        foreach (Rune rune in word.EnumerateRunes())
        {
            if (Rune.IsDigit(rune))
            {
                return false;
            }

            lower |= Rune.IsLower(rune);
        }

        return lower;
    }

    private static bool StartsUpper(string word) =>
        word.Length > 0 && Rune.TryGetRuneAt(word, 0, out Rune first) && Rune.IsUpper(first);

    /// <summary>A space, or a comma and a space — "my daughter, Linda," — and no sentence end or blank line.</summary>
    private static bool RelationReachesName(string text, Token relation, Token name)
    {
        ReadOnlySpan<char> gap = text.AsSpan(relation.End, name.Start - relation.End);
        int commas = 0;
        int breaks = 0;
        foreach (char c in gap)
        {
            if (c == ',')
            {
                commas++;
            }
            else if (c == '\n')
            {
                breaks++;
            }
            else if (!char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return commas <= 1 && breaks <= 1;
    }

    private static bool OnlySpaceBetween(string text, Token left, Token right)
    {
        foreach (char c in text.AsSpan(left.End, right.Start - left.End))
        {
            if (!char.IsWhiteSpace(c) || c == '\n')
            {
                return false;
            }
        }

        return true;
    }

    private static string Fold(string word) =>
        Folding.StripAccents(word).ToLower(CultureInfo.InvariantCulture);
}
