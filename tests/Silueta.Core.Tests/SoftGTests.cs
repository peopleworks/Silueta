using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// F2.2 — Spanish jota and soft g are one sound, and the key used to write them as two.
/// <para>
/// <c>j</c> mapped to <c>y</c> and <c>g</c> before <c>e</c> or <c>i</c> mapped to <c>h</c>. In Spanish
/// those are the same sound: <i>Jimena</i> and <i>Gimena</i>, <i>Jerardo</i> and <i>Gerardo</i> are one
/// name written two ways, and a recogniser picks between the spellings by guesswork. Writing them as
/// different symbols spent an edit of the budget on a difference that is not one, so a name that had also
/// taken real damage had nothing left to pay with.
/// </para>
/// <para>
/// This does not buy the pairs that F2.1 already bought. Jimena and Gimena were one edit apart and the
/// budget covers one edit, so they matched from the moment the budget existed. What this buys is the
/// second edit: the name that was misheard <em>and</em> spelled with the other letter.
/// </para>
/// </summary>
public class SoftGTests
{
    private static bool Matches(string roster, string heard) =>
        new KnownValueDetector()
            .Detect(heard, new DeidentificationContext("rec-1").AddValue(roster, IdentifierKind.PatientName, "s1"))
            .Any();

    [Theory]
    [InlineData("Jimena", "Gimena")]
    [InlineData("Jerardo", "Gerardo")]
    [InlineData("Jines", "Gines")]
    public void One_sound_is_one_key(string withJ, string withG)
    {
        Assert.Equal(PhoneticKey.Compute(withJ), PhoneticKey.Compute(withG));
    }

    [Fact]
    public void The_budget_is_no_longer_spent_on_a_difference_that_is_not_one()
    {
        // "Jimena" heard as "Gimeno": the other spelling of the same sound, plus one real vowel error.
        // Two edits in a six-character key is over budget, so the whole name was lost to a letter that
        // never made a sound.
        Assert.True(Matches("Jimena", "Gimeno"));
    }

    [Fact]
    public void A_soft_g_is_still_not_a_hard_one()
    {
        // The rule is about g before e and i. "Gomez" and "Jomez" are not one name and must not become one.
        Assert.NotEqual(PhoneticKey.Compute("Gomez"), PhoneticKey.Compute("Jomez"));
        Assert.NotEqual(PhoneticKey.Compute("Gato"), PhoneticKey.Compute("Jato"));
    }

    [Fact]
    public void An_h_is_still_silent_and_that_is_a_different_rule()
    {
        // Merging the jota with soft g does not merge either with h. Spanish h is silent, so "Hose" keys
        // to "ose" and "Jose" to "yose": a real difference in what the letters do, left alone.
        Assert.NotEqual(PhoneticKey.Compute("Jose"), PhoneticKey.Compute("Hose"));
    }

    [Fact]
    public void Names_that_were_one_key_before_are_one_key_still()
    {
        // The guard on a change that touches every word with a g or a j in it.
        Assert.Equal(PhoneticKey.Compute("Sophia"), PhoneticKey.Compute("Sofía"));
        Assert.Equal(PhoneticKey.Compute("Jamileth"), PhoneticKey.Compute("Yamilet"));
        Assert.Equal(PhoneticKey.Compute("Vasquez"), PhoneticKey.Compute("Vasques"));
        Assert.Equal(PhoneticKey.Compute("Guillermo"), PhoneticKey.Compute("Guilermo"));
    }

    [Fact]
    public void And_names_that_were_different_stay_different()
    {
        Assert.NotEqual(PhoneticKey.Compute("Parkinson"), PhoneticKey.Compute("Patterson"));
        Assert.False(Matches("Reyes", "Rays"));
        Assert.False(Matches("Eleanor", "Ellie"));
    }
}
