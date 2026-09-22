using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// F2.7, first step — a person the roster never listed, named in the transcript through a relationship.
/// <para>
/// The case is Xari's, from a test with their own demo data on 22 September 2026: "My daughter Linda brought
/// pie" came back with Linda in it. The roster held the patient and the nurse; nobody had listed the
/// daughter, and a roster matcher cannot find someone it was never told about. That is the residue F2.7
/// described on 11 September, before the corpus existed, with its own order of attack: relationship rules
/// first, measure, and only then decide whether a model is needed.
/// </para>
/// <para>
/// The rule is built from three inputs and no others: that text, the kinship vocabulary embedded for the
/// linkage report (committed before the corpus was frozen), and Xari's sentence. The corpus was not read to
/// shape it; it is measured afterwards.
/// </para>
/// <para>
/// What the rule does is add the name to the roster of that one run, with no subject. Two consequences, both
/// deliberate. Every mention of the name is found, not only the one after the relationship word — "Linda said
/// she would call" two sentences later is the same leak. And nobody is invented: an unlisted relative has no
/// subject the agency gave, and minting one from the text would put a derivative of a real name into the
/// vault's keys. So the name becomes a label, coreference is lost for that person, and the manifest says how
/// many people it happened to.
/// </para>
/// </summary>
public class RelativesInTextTests
{
    private static RedactionResult Redact(string text, DeidentificationContext context, PseudonymVault? vault = null) =>
        SiluetaEngine.FromLineage(SiluetaLineage.Default, vault ?? new PseudonymVault()).Redact(text, context);

    private static DeidentificationContext XariRoster() => new DeidentificationContext("visit-7f3a")
        .AddPerson("obj-19c2", "Walter Pryor", IdentifierKind.PatientName)
        .AddPerson("u-4be1", "Dana Coleman", IdentifierKind.StaffName);

    [Fact]
    public void The_daughter_nobody_listed_does_not_survive()
    {
        RedactionResult result = Redact("I know, I had a hard time with the diet. My daughter Linda brought pie.", XariRoster());

        Assert.DoesNotContain("Linda", result.Text, StringComparison.Ordinal);
        Assert.Contains("[FAMILY]", result.Text, StringComparison.Ordinal);
        Assert.Empty(result.Residue);
    }

    [Fact]
    public void Every_later_mention_of_her_goes_too_not_only_the_one_after_the_relationship()
    {
        // The reason this is not a detector that fires on the word after "daughter": the second sentence
        // names her with no relationship in front of it, and a document that still says Linda once still leaks.
        RedactionResult result = Redact(
            "My daughter Linda brought pie. Linda said she would call on Sunday, and Lynda was right.", XariRoster());

        Assert.DoesNotContain("Linda", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Lynda", result.Text, StringComparison.Ordinal); // heard through, like any name
    }

    [Fact]
    public void Spanish_relationships_are_heard_too()
    {
        RedactionResult result = Redact("Su hija Lucía llamó ayer y dijo que Lucía vendría el jueves.", XariRoster());

        Assert.DoesNotContain("Lucía", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_name_after_the_relationship_is_taken_whole()
    {
        RedactionResult result = Redact("Her son Michael Brandt visits on weekends.", XariRoster());

        Assert.DoesNotContain("Michael", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Brandt", result.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("My daughter called this morning.")]           // not a name
    [InlineData("My son I think was worried.")]                // "I" is capitalised and is not a name
    [InlineData("Her sister Doctor Reyes came by.")]           // a title is not a name; titles are F2.7's third item
    [InlineData("My son 2B said so.")]                          // a digit is never a name (F2.3)
    [InlineData("It was my daughter. Linda called later.")]    // the relationship ends with its sentence
    [InlineData("Daughter Linda called.")]                      // a capitalised relationship with no possessive
    public void Nothing_is_taken_that_the_rule_cannot_stand_behind(string text)
    {
        RedactionResult result = Redact(text, XariRoster());

        Assert.Equal(0, result.Manifest.UnrosteredPeople);
    }

    [Fact]
    public void A_relative_already_on_the_roster_keeps_her_subject_and_her_invented_name()
    {
        // The roster wins. A relationship word in front of a listed relative is no reason to strip her of
        // the subject the agency gave her — and a conflict between the two would have been counted as an
        // ambiguous attribution by the overlap rule, which is a worse lie than the leak it replaced.
        DeidentificationContext roster = XariRoster().AddPerson("family-1", "Linda Pryor", IdentifierKind.FamilyName);

        RedactionResult result = Redact("My daughter Linda brought pie.", roster);

        Detection linda = Assert.Single(result.Applied);
        Assert.Equal("family-1", linda.SubjectId);
        Assert.Equal(0, result.Manifest.UnrosteredPeople);
        Assert.Equal(0, result.Manifest.AmbiguousAttributions);
        Assert.DoesNotContain("[FAMILY]", result.Text, StringComparison.Ordinal); // she got an invented name
    }

    [Fact]
    public void A_relative_who_shares_the_patients_surname_does_not_take_the_patients_attribution_with_her()
    {
        // "Linda Pryor" is unlisted; "Pryor" alone is the patient. Registering the unlisted surname as well
        // would have put a second, subjectless opinion on every "Mr. Pryor" in the file.
        RedactionResult result = Redact("My daughter Linda Pryor called. Mr. Pryor was tired after.", XariRoster());

        Assert.DoesNotContain("Linda", result.Text, StringComparison.Ordinal);
        Assert.Equal(0, result.Manifest.AmbiguousAttributions);
        Assert.Contains(result.Applied, d => d.SubjectId == "obj-19c2" && result.Text.Length > 0);
    }

    [Fact]
    public void No_invented_identity_is_minted_for_someone_the_agency_never_listed()
    {
        var vault = new PseudonymVault();
        RedactionResult result = Redact("My daughter Linda brought pie.", XariRoster(), vault);

        Assert.All(result.Applied, d => Assert.DoesNotContain("Linda", d.SubjectId, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, vault.Count); // the patient and the nurse were not mentioned; Linda has no subject
    }

    [Fact]
    public void Running_the_output_through_again_changes_nothing()
    {
        // The idempotence the library promises. The output says "My daughter [FAMILY]", and a label is not a
        // name to be found — which is also why the run reads its own output back with no false alarm.
        var vault = new PseudonymVault();
        RedactionResult first = Redact("My daughter Linda brought pie. Linda said so.", XariRoster(), vault);
        RedactionResult second = Redact(first.Text, XariRoster(), vault);

        Assert.Empty(first.Residue);
        Assert.Equal(first.Text, second.Text);
        Assert.Empty(second.Applied);
    }

    [Fact]
    public void The_manifest_says_how_many_people_were_labelled_because_nobody_had_listed_them()
    {
        RedactionResult result = Redact("My daughter Linda brought pie, and her brother Tom came too.", XariRoster());

        Assert.Equal(2, result.Manifest.UnrosteredPeople);
        Assert.Contains(Caveats.For(XariRoster(), result), note => note.Contains("relationship", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_rule_that_ran_is_written_into_the_manifest_and_can_be_turned_off()
    {
        var off = new SiluetaEngine([new KnownValueDetector(), PatternDetector.FromEmbeddedPack()], new PseudonymVault())
        {
            FindRelativesNamedInText = false,
        };

        RedactionResult result = off.Redact("My daughter Linda brought pie.", XariRoster());

        Assert.Contains("Linda", result.Text, StringComparison.Ordinal);
        Assert.Equal("off", result.Manifest.RelativesRule);
        Assert.NotEqual("off", Redact("My daughter Linda brought pie.", XariRoster()).Manifest.RelativesRule);
    }

    [Fact]
    public void Where_the_relationship_is_not_in_front_of_the_name_it_is_not_heard_and_that_is_said()
    {
        // Out of scope for this slice, pinned so that it is a decision and not an accident: an appositive after
        // the name, and a relationship that runs through "de".
        Assert.Contains("Linda", Redact("Linda, my daughter, brought pie.", XariRoster()).Text, StringComparison.Ordinal);
        Assert.Contains("Pérez", Redact("La hija de la señora Pérez llamó.", XariRoster()).Text, StringComparison.Ordinal);
    }
}
