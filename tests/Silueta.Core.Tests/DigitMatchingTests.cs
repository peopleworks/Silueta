using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// F2.3 — a number is not matched by how it sounds.
/// <para>
/// The phonetic key exists because a speech recogniser mangles the spelling of names, and every rule in it
/// is about letters: silent h, b for v, f for ph. Digits fall through untouched except for one rule that
/// was written for letters and never excluded them — <c>Collapse</c>, which flattens a run of the same
/// character because doubling is a spelling convention rather than a sound. It is, for Ann and An. For
/// digits it means the key of an account number <c>1111</c> is <c>1</c>, and a roster entry for that
/// account matches any stray <c>1</c> in the transcript.
/// </para>
/// <para>
/// There is no ASR damage to absorb here. A recogniser that hears a digit wrong writes a different number,
/// not a similar-sounding one, and a redactor that treats those as the same has removed the wrong figure
/// from a clinical note. Numeric values are compared literally.
/// </para>
/// </summary>
public class DigitMatchingTests
{
    private static List<Detection> Detect(string text, DeidentificationContext context) =>
        [.. new KnownValueDetector().Detect(text, context)];

    [Fact]
    public void A_repeated_digit_account_number_does_not_match_a_single_digit()
    {
        var context = new DeidentificationContext("rec-1")
            .AddValue("1111", IdentifierKind.AccountNumber, "acct-1");

        Assert.Empty(Detect("The patient took 1 tablet with breakfast.", context));
    }

    [Fact]
    public void A_repeated_digit_account_number_still_matches_itself()
    {
        var context = new DeidentificationContext("rec-1")
            .AddValue("1111", IdentifierKind.AccountNumber, "acct-1");

        Detection found = Assert.Single(Detect("The account is 1111 and the balance is nil.", context));
        Assert.Equal(IdentifierKind.AccountNumber, found.Kind);
        Assert.Equal(MatchKind.Exact, found.Match);
    }

    [Fact]
    public void A_long_account_number_does_not_match_the_digits_it_collapses_to()
    {
        // The reachable shape of the audit's defect, and the one the short-key rule from E2 does not cover:
        // that rule asks for the same letters only below the fuzzy floor, and the collapsed key here is five
        // characters long. So the roster's 1122334455 keys to 12345, the transcript's ordinary 12345 keys to
        // 12345, and the two match exactly — a dose, a room, a year quietly redacted as somebody's account.
        var context = new DeidentificationContext("rec-1")
            .AddValue("1122334455", IdentifierKind.AccountNumber, "acct-1");

        Assert.Empty(Detect("Dial extension 12345 for the night desk.", context));
    }

    [Fact]
    public void A_record_number_is_not_matched_by_a_near_miss()
    {
        // 441729 and 441720 are two records. One edit in six characters is inside the fuzzy threshold that
        // exists for damaged spelling, and applying it to a number redacts the wrong patient's file.
        var context = new DeidentificationContext("rec-1")
            .AddValue("441729", IdentifierKind.RecordNumber, "rec-a");

        Assert.Empty(Detect("Chart 441720 was updated this morning.", context));
    }

    [Fact]
    public void The_key_of_a_number_keeps_its_digits()
    {
        // Collapse is a rule about letters. Even where nothing consults this key, a key that says 1111 and 1
        // are the same string is a rule waiting to be believed by the next caller.
        Assert.Equal("1111", PhoneticKey.Compute("1111"));
        Assert.Equal("2024", PhoneticKey.Compute("2024"));
    }

    [Fact]
    public void Letters_still_collapse_the_way_they_always_did()
    {
        Assert.Equal(PhoneticKey.Compute("Ann"), PhoneticKey.Compute("An"));
        Assert.Equal(PhoneticKey.Compute("Ellenor"), PhoneticKey.Compute("Elenor"));
    }

    [Fact]
    public void A_name_with_a_digit_in_it_is_matched_letter_for_letter()
    {
        // "Room 2B" on a roster is not a name to be heard through; it is a label to be found exactly.
        var context = new DeidentificationContext("rec-1")
            .AddValue("2B", IdentifierKind.RecordNumber, "room");

        Assert.Empty(Detect("She was moved to 2D last night.", context));
        Assert.Single(Detect("She was moved to 2B last night.", context));
    }
}
