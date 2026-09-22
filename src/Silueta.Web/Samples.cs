using Silueta.Core;

namespace Silueta.Web;

/// <summary>One transcript to try, with the roster that goes with it.</summary>
/// <param name="Title">What the sample is for, in the words of the thing it demonstrates.</param>
/// <param name="Why">One line under the button: what to look at once it has run.</param>
public sealed record Sample(string Title, string Why, string Text, IReadOnlyList<RosterRow> Roster);

/// <summary>A roster entry as the page edits it: three strings, before any of them is a kind.</summary>
public sealed class RosterRow
{
    public string Value { get; set; } = string.Empty;

    public string Kind { get; set; } = nameof(IdentifierKind.PatientName);

    public string SubjectId { get; set; } = string.Empty;
}

/// <summary>
/// The transcripts the page offers to try.
/// <para>
/// All three are invented, and the page says so. That is not a disclaimer, it is the rule this repository
/// runs on: nothing about a real person enters it, not in a test, a corpus, a roster or a demo. The gold
/// corpus is not bundled here either — it is thirty annotated documents whose whole purpose is to be
/// evaluated, and putting it in a web page is a sentence a reviewer would rightly misread.
/// </para>
/// <para>
/// Each one exists to show something the library gets right and something it does not, because a demo that
/// only shows the wins is an advertisement.
/// </para>
/// </summary>
public static class Samples
{
    public static IReadOnlyList<Sample> All { get; } =
    [
        new Sample(
            "A shift report, as a recogniser wrote it",
            "Every name is misspelled the way speech recognition misspells names. Watch which ones are found anyway — and watch \"Rays\" and \"Ellie\", which are not.",
            """
            Shift report. Sophia Rays was with Mrs. Ellenor Vasques this morning.
            Her daughter Jamileth called at 602-555-0147 about the 3/14/2026 appointment,
            and Ellie said she would email jamileth.v@example.com. The patient is 94 years old.
            Blood pressure 138 over 82, pain 4 out of 10, and she took the warfarin with breakfast.
            """,
            [
                new RosterRow { Value = "Eleanor Vasquez", Kind = nameof(IdentifierKind.PatientName), SubjectId = "patient-1" },
                new RosterRow { Value = "Yamilet Vasquez", Kind = nameof(IdentifierKind.FamilyName), SubjectId = "family-1" },
                new RosterRow { Value = "Sofía Reyes", Kind = nameof(IdentifierKind.StaffName), SubjectId = "staff-1" },
            ]),

        new Sample(
            "Una visita, en español",
            "La clave fonética es bilingüe: «Gimena» y «Jimena» son un solo nombre, y «Vasques» encuentra a «Vásquez». El lugar sobrevive entero, porque ninguna regla busca lugares todavía.",
            """
            Visita de la tarde. Gimena Vásques atendió a don Andrés Beltrán en su casa de Santo Domingo Este.
            La hija llamó al 809-555-0182 y pidió que le escribieran a andres.b@example.com.
            Don Andrés tiene 91 años y toma la pastilla de la presión con el desayuno.
            """,
            [
                new RosterRow { Value = "Jimena Vásquez", Kind = nameof(IdentifierKind.StaffName), SubjectId = "staff-1" },
                new RosterRow { Value = "Andrés Beltrán", Kind = nameof(IdentifierKind.PatientName), SubjectId = "patient-1" },
            ]),

        new Sample(
            "Two names that cross",
            "Both people are on the roster and their names share a word. Nothing in the run can say whose mention it is, so it is labelled instead of given an invented name — and the manifest counts it.",
            "Ana Maria Perez came in this morning. Call 602-555-0147 about the visit.",
            [
                new RosterRow { Value = "Ana Maria", Kind = nameof(IdentifierKind.PatientName), SubjectId = "patient-1" },
                new RosterRow { Value = "Maria Perez", Kind = nameof(IdentifierKind.FamilyName), SubjectId = "family-1" },
            ]),

        new Sample(
            "No roster at all",
            "The honest worst case. Only the shape rules fire — phone, e-mail, date, age over 89 — and one more: a person named right after a relationship, so \"her sister Rose\" is caught with nobody on the roster. Every other name walks straight through.",
            """
            Evening shift. Kevin Brooks with Mae Thompson. Her sister Rose visited.
            Call 602-555-0147 or write to kevin.brooks@example.com about the 3/14/2026 review.
            The patient is 94 years old and lives in Flagstaff.
            """,
            []),
    ];
}
