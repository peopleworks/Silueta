using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// F2.6 — two names that overlap in the text leave nothing of either behind, and who a mention is
/// attributed to does not depend on the order the roster was written in.
/// <para>
/// Resolution used to be: longest span wins, the rest are discarded whole. That is right when one candidate
/// contains another, which is the common case ("Sofía Reyes" over "Sofía"). It is wrong when two candidates
/// merely overlap — roster <c>Ana Maria</c> and <c>Maria Perez</c>, text <c>Ana Maria Perez</c> — because
/// discarding the loser discards the characters only it covered, and a word of somebody's name stays in the
/// transcript.
/// </para>
/// <para>
/// And where two candidates cover exactly the same span, the winner was whichever the sort happened to
/// reach first, which is the order of the roster. Two people with the same name is not a strange case in
/// a household with relatives on one file; reordering the roster must not change whose life the sentence
/// is about.
/// </para>
/// </summary>
public class OverlapTests
{
    private static RedactionResult Redact(string text, DeidentificationContext context) =>
        new SiluetaEngine([new KnownValueDetector()], new PseudonymVault()).Redact(text, context);

    [Fact]
    public void Two_overlapping_names_leave_no_word_of_either_behind()
    {
        // The audit's case, reproduced: the roster holds two people whose registered names share a word.
        var context = new DeidentificationContext("rec-1")
            .AddValue("Ana Maria", IdentifierKind.PatientName, "p1")
            .AddValue("Maria Perez", IdentifierKind.FamilyName, "p2");

        RedactionResult result = Redact("Ana Maria Perez came in this morning.", context);

        Assert.DoesNotContain("Ana", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Maria", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Perez", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Residue);
    }

    [Fact]
    public void The_order_of_the_roster_does_not_decide_whose_mention_it_is()
    {
        // Same text, same two subjects, roster written the other way round. Whatever the run decides, it
        // has to decide the same thing both times.
        static string Run(bool reversed)
        {
            var context = new DeidentificationContext("rec-1");
            if (reversed)
            {
                context.AddValue("Maria Perez", IdentifierKind.FamilyName, "p2");
                context.AddValue("Ana Maria", IdentifierKind.PatientName, "p1");
            }
            else
            {
                context.AddValue("Ana Maria", IdentifierKind.PatientName, "p1");
                context.AddValue("Maria Perez", IdentifierKind.FamilyName, "p2");
            }

            return string.Join("|", Redact("Ana Maria Perez came in this morning.", context).Applied
                .OrderBy(d => d.Start)
                .Select(d => $"{d.Start},{d.Length},{d.Kind},{d.SubjectId}"));
        }

        Assert.Equal(Run(reversed: false), Run(reversed: true));
    }

    [Fact]
    public void An_exact_tie_between_two_subjects_is_not_broken_by_who_was_registered_first()
    {
        // The sharper version: two people who are actually called the same thing, so the two candidates
        // cover the very same characters with the very same confidence.
        static string Run(bool reversed)
        {
            var context = new DeidentificationContext("rec-1");
            foreach (string subject in reversed ? (string[])["p2", "p1"] : ["p1", "p2"])
            {
                context.AddValue("Maria Perez", IdentifierKind.PatientName, subject);
            }

            return string.Join("|", Redact("Maria Perez called.", context).Applied
                .Select(d => $"{d.Start},{d.Length},{d.SubjectId}"));
        }

        Assert.Equal(Run(reversed: false), Run(reversed: true));
    }

    [Fact]
    public void A_span_nobody_can_claim_is_labelled_and_counted()
    {
        var context = new DeidentificationContext("rec-1")
            .AddValue("Ana Maria", IdentifierKind.PatientName, "p1")
            .AddValue("Maria Perez", IdentifierKind.FamilyName, "p2");

        RedactionResult result = Redact("Ana Maria Perez came in this morning.", context);

        // Not an invented name: an invented name here would say the whole mention belongs to one of the
        // two, which is the thing nothing in the run knows.
        Assert.Equal(1, result.Manifest.AmbiguousAttributions);
        Assert.Empty(Assert.Single(result.Applied).SubjectId);
        Assert.StartsWith("[", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_surname_shared_by_two_relatives_still_keeps_its_person()
    {
        // The case the union must not break, and the reason attribution asks only what covers the whole
        // span: mother and daughter on one file share a surname, so the daughter's surname-only entry sits
        // inside the mother's full name. That is containment, not a crossing, and the mention is hers.
        var context = new DeidentificationContext("rec-1")
            .AddPerson("mother", "Maria Perez", IdentifierKind.PatientName)
            .AddPerson("daughter", "Lucia Perez", IdentifierKind.FamilyName);

        RedactionResult result = Redact("Maria Perez said she would call.", context);

        Detection only = Assert.Single(result.Applied);
        Assert.Equal("mother", only.SubjectId);
        Assert.Equal(0, result.Manifest.AmbiguousAttributions);
    }

    [Fact]
    public void A_span_inside_a_longer_one_is_still_swallowed_by_it()
    {
        // What must not change: the ordinary case, where the longer candidate contains the shorter.
        var context = new DeidentificationContext("rec-1")
            .AddPerson("p1", "Sofía Reyes", IdentifierKind.PatientName);

        RedactionResult result = Redact("Sofía Reyes took the shift.", context);

        Detection only = Assert.Single(result.Applied);
        Assert.Equal(0, only.Start);
        Assert.Equal("Sofía Reyes".Length, only.Length);
    }

    [Fact]
    public void Everything_a_detector_found_is_covered_by_what_the_run_applied()
    {
        // The invariant the union exists to keep: no candidate that passed the policy may be left with
        // characters that nothing in the final set covers.
        var context = new DeidentificationContext("rec-1")
            .AddValue("Ana Maria", IdentifierKind.PatientName, "p1")
            .AddValue("Maria Perez", IdentifierKind.FamilyName, "p2")
            .AddValue("Perez Gomez", IdentifierKind.StaffName, "p3");

        const string text = "Ana Maria Perez Gomez signed the note.";
        RedactionResult result = Redact(text, context);

        var covered = new bool[text.Length];
        foreach (Detection applied in result.Applied)
        {
            for (int i = applied.Start; i < applied.End; i++)
            {
                covered[i] = true;
            }
        }

        foreach (Detection candidate in new KnownValueDetector().Detect(text, context))
        {
            for (int i = candidate.Start; i < candidate.End; i++)
            {
                Assert.True(covered[i], $"Character {i} was found by a detector and covered by nothing.");
            }
        }
    }
}
