using System.Collections.Frozen;

namespace Silueta.Core;

/// <summary>
/// The word lists a surrogate is built from: a pool of heads and a pool of tails, combined into
/// "&lt;head&gt; &lt;tail&gt;". Choosing the pair is the vault's job — a surrogate is an assignment that has to
/// be remembered, not a value that can be recomputed — and this type only says what the words are and
/// how a name made of them comes apart again.
/// <para>
/// An entry is not a word. It used to be, implicitly: the built-in lists held exactly one word each, so
/// "the head of a surrogate" and "everything before the first space" were the same string, and both
/// <see cref="Fit"/> and the vault's bookkeeping took the second one. The first lineage written in
/// Spanish breaks that: <c>María José</c> and <c>De la Cruz</c> are one entry each, and a one-word
/// mention replaced by "everything before the first space" is replaced by <c>María</c> — a name the
/// vault never minted, never checked against the roster and cannot look up. So the pools are asked.
/// </para>
/// </summary>
public sealed class SurrogatePools
{
    private readonly FrozenSet<string> _heads;

    public SurrogatePools(IEnumerable<string> given, IEnumerable<string> family)
    {
        Given = [.. given];
        Family = [.. family];
        _heads = Given.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The heads: what a one-word mention is replaced by, on its own.</summary>
    public IReadOnlyList<string> Given { get; }

    /// <summary>The tails.</summary>
    public IReadOnlyList<string> Family { get; }

    /// <summary>
    /// The head of a surrogate, as the pool defines it rather than as the spaces suggest. Falls back to
    /// the first word for a surrogate no pool produced — one pinned by hand through
    /// <see cref="PseudonymVault.Assign"/>, or minted by an older lineage whose pools are gone.
    /// </summary>
    public string HeadOf(string surrogate)
    {
        if (string.IsNullOrEmpty(surrogate))
        {
            return surrogate;
        }

        // Longest first: a pool holding both "María" and "María José" must not cut the longer one short.
        string? head = null;
        foreach (string candidate in _heads)
        {
            if (!surrogate.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A prefix that stops mid-word is not a head: "Ana" does not head "Anabel Rivas".
            if (surrogate.Length > candidate.Length && surrogate[candidate.Length] != ' ')
            {
                continue;
            }

            if (head is null || candidate.Length > head.Length)
            {
                head = candidate;
            }
        }

        if (head is not null)
        {
            return head;
        }

        int space = surrogate.IndexOf(' ');
        return space < 0 ? surrogate : surrogate[..space];
    }

    /// <summary>
    /// One mention, one name: replacing "Sofia" with "Ale Bravo" would rewrite the sentence, and
    /// replacing "Sofia Reyes" with "Ale" would leave a dangling surname behind.
    /// </summary>
    public string Fit(string surrogate, int words) =>
        words > 1 || surrogate.Length == 0 ? surrogate : HeadOf(surrogate);
}
