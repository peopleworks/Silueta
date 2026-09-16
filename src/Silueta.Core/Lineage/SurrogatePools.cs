using System.Collections.Frozen;

namespace Silueta.Core;

/// <summary>
/// The word lists an invented name is built from. Each kind of subject draws from a pair — a pool of
/// heads and a pool of tails — and a surrogate is "&lt;head&gt; &lt;tail&gt;": a given name and a family name
/// for a person, a company name and an optional suffix for an organisation, a product name and an
/// optional suffix for a product. Choosing the pair is the vault's job; this type says what the words
/// are, which pair a kind uses, and how a name made of them comes apart again.
/// <para>
/// An entry is not a word. It used to be, implicitly: the built-in lists held exactly one word each, so
/// "the head of a surrogate" and "everything before the first space" were the same string. The first
/// lineage written in Spanish breaks that — <c>María José</c> and <c>De la Cruz</c> are one entry each —
/// and a company breaks it for good. So the pools are asked.
/// </para>
/// <para>
/// Which pair a kind uses is compiled in, not declared by the lineage. A lineage names pools; it does not
/// route kinds to them. A route written in a JSON file is a route a typo can change in silence, which is
/// the same reason <see cref="IdentifierKind"/> is a closed enum.
/// </para>
/// </summary>
public sealed class SurrogatePools
{
    public const string GivenPool = "given";
    public const string FamilyPool = "family";
    public const string CompanyPool = "company";
    public const string CompanySuffixPool = "companySuffix";
    public const string ProductPool = "product";
    public const string ProductSuffixPool = "productSuffix";

    /// <summary>The pool names this build understands. A lineage naming any other is recorded, not
    /// obeyed.</summary>
    public static readonly FrozenSet<string> Recognised = new[]
    {
        GivenPool, FamilyPool, CompanyPool, CompanySuffixPool, ProductPool, ProductSuffixPool,
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The pools that hold heads, and so decide where a surrogate's head ends.</summary>
    private static readonly string[] HeadPools = [GivenPool, CompanyPool, ProductPool];

    private readonly FrozenDictionary<string, IReadOnlyList<string>> _pools;
    private readonly FrozenSet<string> _heads;

    /// <summary>People only: the shape every lineage had before organisations existed.</summary>
    public SurrogatePools(IEnumerable<string> given, IEnumerable<string> family)
        : this(new Dictionary<string, IReadOnlyList<string>>
        {
            [GivenPool] = [.. given],
            [FamilyPool] = [.. family],
        })
    {
    }

    public SurrogatePools(IReadOnlyDictionary<string, IReadOnlyList<string>> pools)
    {
        _pools = pools
            .Where(p => Recognised.Contains(p.Key))
            .ToFrozenDictionary(p => p.Key, p => (IReadOnlyList<string>)[.. p.Value], StringComparer.OrdinalIgnoreCase);

        _heads = HeadPools
            .SelectMany(Pool)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The heads a person's invented name starts with.</summary>
    public IReadOnlyList<string> Given => Pool(GivenPool);

    /// <summary>The tails a person's invented name ends with.</summary>
    public IReadOnlyList<string> Family => Pool(FamilyPool);

    /// <summary>Every pool this instance holds, by name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> All => _pools;

    /// <summary>A pool by name, or an empty list when this lineage did not declare it.</summary>
    public IReadOnlyList<string> Pool(string name) =>
        _pools.TryGetValue(name, out IReadOnlyList<string>? pool) ? pool : [];

    /// <summary>
    /// The names of the head pool and the tail pool a kind draws from, or null for a kind that is never
    /// given an invented name — a phone number, a date. People share one pair; that is what
    /// <see cref="IdentifierKindExtensions.IsPersonName"/> is for.
    /// </summary>
    public static (string Head, string Tail)? PoolNamesFor(IdentifierKind kind) =>
        kind.IsPersonName() ? (GivenPool, FamilyPool)
        : kind switch
        {
            IdentifierKind.Organization => (CompanyPool, CompanySuffixPool),
            IdentifierKind.Product => (ProductPool, ProductSuffixPool),
            _ => null,
        };

    /// <summary>
    /// Whether an invented name can be drawn for this kind at all. A person needs both a head and a tail;
    /// a company or a product needs heads, and uses a suffix only when the lineage brings one — so
    /// <c>"company": ["Aurora Servicios"]</c> and <c>"company": ["Meridiano"], "companySuffix":
    /// ["Logística"]</c> both work.
    /// </summary>
    public bool Has(IdentifierKind kind)
    {
        if (PoolNamesFor(kind) is not var (head, tail))
        {
            return false;
        }

        return Pool(head).Count > 0 && (!kind.IsPersonName() || Pool(tail).Count > 0);
    }

    /// <summary>The heads and tails for a kind. Throws for a kind <see cref="Has"/> says cannot be served:
    /// falling back to the people's pools is the defect this type was widened to remove.</summary>
    public (IReadOnlyList<string> Heads, IReadOnlyList<string> Tails, string HeadName) For(IdentifierKind kind)
    {
        if (!Has(kind) || PoolNamesFor(kind) is not var (head, tail))
        {
            throw new InvalidOperationException(
                $"This lineage has no pool to invent a name for {kind} from. A {kind} must be labelled " +
                "rather than given a name drawn from another kind's pool.");
        }

        return (Pool(head), Pool(tail), head);
    }

    /// <summary>
    /// The head of a surrogate, as the pools define it rather than as the spaces suggest. Falls back to
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
    /// replacing "Sofia Reyes" with "Ale" would leave a dangling surname behind. A company called
    /// "Aurora Servicios" is one head, so a one-word mention of it gets both words — which is what a
    /// company is called by.
    /// </summary>
    public string Fit(string surrogate, int words) =>
        words > 1 || surrogate.Length == 0 ? surrogate : HeadOf(surrogate);
}
