namespace Silueta.Core;

/// <summary>
/// The pool of invented names a surrogate is drawn from. Choosing one is the vault's job, not this
/// class's: a surrogate is an assignment that has to be remembered, not a value that can be recomputed.
/// <para>
/// This type used to compute the name from a hash of the subject id. That failed three ways at once. It
/// could return the person's own name — a real Ale became "Ale", marked redacted. It collided, so two
/// people in one corpus became one person. And it was never written down, so the code the vault minted
/// and the name in the text had nothing to do with each other, and nothing could be undone.
/// </para>
/// </summary>
internal static class Surrogates
{
    /// <summary>
    /// Small on purpose: one page of names a reader can check. Becomes a JSON pack when a locale needs
    /// its own list, like the rule packs.
    /// <para>
    /// Every one of these reads as either gender, in Spanish and in English, and that is deliberate.
    /// Picking a replacement by gender would mean inferring the gender of a real person from their name,
    /// which is a guess the library has no business making — and a wrong guess writes "her son Marta"
    /// into a clinical note. Neutral names keep the sentence readable without anyone deciding anything.
    /// </para>
    /// </summary>
    internal static readonly string[] Given =
    [
        "Ale", "Alex", "Ariel", "Chris", "Cruz", "Dani", "Emery", "Guadalupe",
        "Jordan", "Luca", "Mar", "Marley", "Noa", "Noel", "Quinn", "Remy",
        "Rene", "Robin", "Sasha", "Sol", "Yael",
    ];

    internal static readonly string[] Family =
    [
        "Aguilar", "Bravo", "Castro", "Duarte", "Espinal", "Fuentes", "Gaitan", "Herrera",
        "Ibarra", "Jimenez", "Lara", "Medina", "Nieves", "Ochoa", "Prado", "Quintero",
        "Rivas", "Salazar", "Toledo", "Urena", "Vargas", "Zamora",
    ];

    /// <summary>
    /// One word in, one word out: replacing "Sofia" with "Ale Bravo" would rewrite the sentence, and
    /// replacing "Sofia Reyes" with "Ale" would leave a dangling surname behind.
    /// </summary>
    internal static string Fit(string surrogate, int words)
    {
        if (words > 1 || surrogate.Length == 0)
        {
            return surrogate;
        }

        int space = surrogate.IndexOf(' ');
        return space < 0 ? surrogate : surrogate[..space];
    }
}
