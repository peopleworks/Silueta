namespace Silueta.Core;

/// <summary>
/// Invented names, chosen the same way every time for the same subject.
/// <para>
/// The choice is a hash of the subject id and the policy seed — not of the real name. Hashing the name
/// would make the surrogate a function of the thing we are hiding, which is the same mistake
/// 45 CFR § 164.514(c) forbids for re-identification codes.
/// </para>
/// </summary>
internal static class Surrogates
{
    // Small on purpose: one page of names a reader can check. Becomes a JSON pack when a locale needs
    // its own list, like the rule packs.
    //
    // Every one of these reads as either gender, in Spanish and in English, and that is deliberate.
    // Picking a replacement by gender would mean inferring the gender of a real person from their name,
    // which is a guess the library has no business making — and a wrong guess writes "her son Marta"
    // into a clinical note. Neutral names keep the sentence readable without anyone deciding anything.
    private static readonly string[] Given =
    [
        "Ale", "Alex", "Ariel", "Chris", "Cruz", "Dani", "Emery", "Guadalupe",
        "Jordan", "Luca", "Mar", "Marley", "Noa", "Noel", "Quinn", "Remy",
        "Rene", "Robin", "Sasha", "Sol", "Yael",
    ];

    private static readonly string[] Family =
    [
        "Aguilar", "Bravo", "Castro", "Duarte", "Espinal", "Fuentes", "Gaitan", "Herrera",
        "Ibarra", "Jimenez", "Lara", "Medina", "Nieves", "Ochoa", "Prado", "Quintero",
        "Rivas", "Salazar", "Toledo", "Urena", "Vargas", "Zamora",
    ];

    public static string ForSubject(int seed, string subjectId, int words)
    {
        uint hash = Fnv1a($"{seed}|{subjectId}");
        string given = Given[hash % Given.Length];
        string family = Family[(hash / (uint)Given.Length) % Family.Length];

        // One word in, one word out: replacing "Sofia" with "Alba Bravo" would rewrite the sentence.
        return words <= 1 ? given : $"{given} {family}";
    }

    private static uint Fnv1a(string s)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        uint hash = offset;
        foreach (char c in s)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }
}
