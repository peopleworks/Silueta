using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// F2.1 — how much damage a name may take and still be the same name, counted in edits rather than as a
/// share of its length.
/// <para>
/// The rule was a ratio: accept when the similarity of the two keys is at least 0.84. The comment beside it
/// said that accepts one edit in a six-letter key. It does not — one edit in six is 0.833 — and the
/// difference is not academic, because six letters is the length of an ordinary given name. Both audits
/// found the same three pairs dead there: Carmen/Carmin, Jimena/Gimena, Javier/Xavier. A ratio also says
/// that a longer name may be damaged more, which is backwards: the recogniser makes a wrong letter or two,
/// not a wrong fraction.
/// </para>
/// <para>
/// So the tolerance is a budget of edits, by the length of the shorter key: none below four characters,
/// one from four to seven, two from eight. The numbers are the audit's proposal, not a search for the
/// setting that scores best on the committed corpus — that would be choosing the number and then
/// publishing it.
/// </para>
/// </summary>
public class MatchToleranceTests
{
    private static bool Matches(string roster, string heard, IdentifierKind kind = IdentifierKind.PatientName) =>
        new KnownValueDetector()
            .Detect(heard, new DeidentificationContext("rec-1").AddValue(roster, kind, "s1"))
            .Any();

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(20, 2)]
    public void The_budget_grows_with_the_shorter_key_and_then_stops(int length, int expected)
    {
        Assert.Equal(expected, MatchTolerance.Default.BudgetFor(length));
    }

    [Fact]
    public void The_budget_is_read_from_the_shorter_key_not_the_longer()
    {
        // Otherwise a three-letter roster entry would buy a budget from whatever long word it was compared
        // against, and short keys are exactly where fuzzy matching starts eating ordinary words.
        Assert.Equal(0, MatchTolerance.Default.BudgetFor(Math.Min(3, 9)));
    }

    [Theory]
    [InlineData("Carmen", "Carmin")]
    [InlineData("Javier", "Xavier")]
    [InlineData("Marisol", "Marysol")]
    public void One_edit_in_an_ordinary_given_name_is_now_the_same_name(string roster, string heard)
    {
        Assert.True(Matches(roster, heard), $"{roster} / {heard} should be one name under a budget of one edit.");
    }

    [Fact]
    public void A_ratio_would_still_have_refused_those()
    {
        // The measurement behind the change, kept as a test so the reason cannot be lost: the pair is one
        // edit apart and scores below the 0.84 the matcher used to demand, which is why it was refused.
        double ratio = Similarity.Ratio(PhoneticKey.Compute("Carmen"), PhoneticKey.Compute("Carmin"));

        Assert.Equal(1, Similarity.Distance(PhoneticKey.Compute("Carmen"), PhoneticKey.Compute("Carmin")));
        Assert.True(ratio < 0.84, $"Carmen/Carmin scored {ratio:0.000}, so the old rule refused it and there is nothing here to fix.");
    }

    [Fact]
    public void A_short_name_is_still_matched_exactly_and_only_exactly()
    {
        Assert.False(Matches("Ana", "Ame"));
        Assert.True(Matches("Ana", "Anna")); // one key, no edits: the same name however it is spelled
    }

    [Fact]
    public void Two_edits_need_a_long_name_to_pay_for_them()
    {
        // "Rodriguez" and "Rodrigues" is one edit; "Rodriguez" and "Rodrigo" is more than the budget.
        Assert.True(Matches("Rodriguez", "Rodrigues"));
        Assert.False(Matches("Rodriguez", "Rodrigo"));
    }

    [Fact]
    public void What_the_recogniser_did_to_Reyes_is_still_out_of_reach()
    {
        // The demo's honest failure, and it stays one: the keys are "reyes" and "rais", three edits apart
        // on a five-character key. A budget that reached this far would reach most surnames.
        Assert.Equal(3, Similarity.Distance(PhoneticKey.Compute("Reyes"), PhoneticKey.Compute("Rays")));
        Assert.False(Matches("Reyes", "Rays"));
    }

    [Fact]
    public void A_nickname_is_still_not_a_spelling_of_the_name()
    {
        Assert.False(Matches("Eleanor", "Ellie"));
    }

    [Fact]
    public void A_stricter_tolerance_can_be_asked_for_and_is_obeyed()
    {
        var exactOnly = new MatchTolerance { ExactBelow = int.MaxValue };
        var detector = new KnownValueDetector(exactOnly);

        Assert.Empty(detector.Detect("Carmin", new DeidentificationContext("r").AddValue("Carmen", IdentifierKind.PatientName, "s1")));
        Assert.Single(detector.Detect("Karmen", new DeidentificationContext("r").AddValue("Carmen", IdentifierKind.PatientName, "s1")));
    }

    [Fact]
    public void The_tolerance_that_ran_is_written_into_the_manifest()
    {
        // Two corpora redacted with different budgets used to be indistinguishable: the policy was
        // fingerprinted and the rule that decides what counts as the same name was not.
        var strict = new SiluetaEngine([new KnownValueDetector(new MatchTolerance { ExactBelow = 99 })]);
        var loose = new SiluetaEngine([new KnownValueDetector()]);

        var context = new DeidentificationContext("rec-1").AddValue("Carmen", IdentifierKind.PatientName, "s1");

        Assert.NotEqual(
            strict.Redact("Carmen came in.", context).Manifest.DetectorFingerprints["known-value"],
            loose.Redact("Carmen came in.", context).Manifest.DetectorFingerprints["known-value"]);
    }
}
