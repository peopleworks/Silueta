using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Silueta.Core;

/// <summary>
/// How much damage a name may take and still be the same name — in edits, not in proportion.
/// <para>
/// The rule used to be a ratio: the two phonetic keys had to be at least 0.84 similar. The comment beside
/// it said that accepted one edit in a six-letter key. It did not: one edit in six is 0.833, and six
/// letters is the length of an ordinary given name, so the layer that exists to absorb recogniser damage
/// was dead in the middle of its own range. Both audits arrived at the same three pairs —
/// <c>Carmen</c>/<c>Carmin</c>, <c>Jimena</c>/<c>Gimena</c>, <c>Javier</c>/<c>Xavier</c> — each one edit
/// apart and each refused.
/// </para>
/// <para>
/// A proportion is also the wrong shape for the damage. A recogniser writes a wrong letter, or two; it does
/// not write a wrong percentage. Scaling the allowance with length says a longer name may be mangled more,
/// when what a longer name really offers is more evidence that it is the right one. So the allowance is a
/// budget of edits, read from the <b>shorter</b> key: a short key must buy its tolerance with its own
/// length, or a three-letter roster entry would inherit a budget from whatever long word it met.
/// </para>
/// <para>
/// The three numbers are the audit's proposal, kept as proposed. They were not searched for on the
/// committed corpus: picking the setting that scores best there and then publishing the score is choosing
/// the number, which is the thing this project is built to refuse. What the corpus is for is saying what
/// the proposal cost, afterwards.
/// </para>
/// </summary>
public sealed record MatchTolerance
{
    /// <summary>The rule this build matches with, and the one the published leak rate was measured under.</summary>
    public static MatchTolerance Default { get; } = new();

    /// <summary>
    /// Below this key length nothing but an identical key counts. Short keys are where tolerance stops
    /// being tolerance: "Ana" is two edits from "una", and a roster with a short name on it would redact
    /// half the language.
    /// </summary>
    public int ExactBelow { get; init; } = 4;

    /// <summary>From this key length up, two edits. Below it — and at or above <see cref="ExactBelow"/> —
    /// one.</summary>
    public int TwoEditsFrom { get; init; } = 8;

    /// <summary>How many edits are allowed between two keys whose shorter side is this long.</summary>
    public int BudgetFor(int shorterKeyLength) =>
        shorterKeyLength < ExactBelow ? 0 : shorterKeyLength < TwoEditsFrom ? 1 : 2;

    /// <summary>
    /// Whether two phonetic keys are one word heard twice. <paramref name="distance"/> and
    /// <paramref name="budget"/> come back so that a caller explaining the verdict to somebody quotes the
    /// numbers this decided on rather than working them out again — the second copy of a rule is how every
    /// defect in this library has started.
    /// </summary>
    public bool Accepts(string a, string b, out int distance, out int budget)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        budget = BudgetFor(Math.Min(a.Length, b.Length));

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            distance = 0;
            return true;
        }

        // A difference in length is a lower bound on the distance, so a pair that cannot possibly fit the
        // budget is refused without measuring it. On a long transcript this is most of the comparisons.
        if (Math.Abs(a.Length - b.Length) > budget)
        {
            distance = Math.Abs(a.Length - b.Length);
            return false;
        }

        distance = Similarity.Distance(a, b);
        return distance <= budget;
    }

    /// <inheritdoc cref="Accepts(string, string, out int, out int)"/>
    public bool Accepts(string a, string b) => Accepts(a, b, out _, out _);

    /// <summary>
    /// A digest of the rule, for the manifest. Two corpora redacted with different budgets were
    /// indistinguishable: the policy carried a fingerprint and the rule that decides what counts as the
    /// same name did not.
    /// </summary>
    public string Fingerprint
    {
        get
        {
            string canonical = string.Create(CultureInfo.InvariantCulture, $"match-tolerance/1\n{ExactBelow}\n{TwoEditsFrom}\n");
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
        }
    }

    /// <summary>"no edits below 4 characters, one below 8, two from there" — one sentence, one source.</summary>
    public override string ToString() =>
        $"no edits below {ExactBelow} characters, one below {TwoEditsFrom}, two from there";
}
