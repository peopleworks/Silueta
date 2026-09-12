using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// What the pipeline can still find in its own output.
/// <para>
/// Every safety rule in this library is enforced at the moment something is chosen — the surrogate that
/// the roster would not match, the span that was replaced. None of them looks at the finished text. That
/// is the same mistake the leak meter made, one level up: checking the decision instead of the result.
/// The tests here are the cases where a rule enforced at choosing time is not true at emitting time.
/// </para>
/// </summary>
public class ResidueTests
{
    private static readonly string[] Family =
    [
        "Aguilar", "Bravo", "Castro", "Duarte", "Espinal", "Fuentes", "Gaitan", "Herrera",
        "Ibarra", "Jimenez", "Lara", "Medina", "Nieves", "Ochoa", "Prado", "Quintero",
        "Rivas", "Salazar", "Toledo", "Urena", "Vargas", "Zamora",
    ];

    /// <summary>A roster that leaves the vault exactly one family name to choose.</summary>
    private static DeidentificationContext Forcing(string recordId, string onlyFreeSurname)
    {
        var context = new DeidentificationContext(recordId);
        int blocked = 0;
        foreach (string name in Family.Where(n => n != onlyFreeSurname))
        {
            context.AddValue(name, IdentifierKind.OtherName, $"blocker-{blocked++}");
        }

        return context;
    }

    [Fact]
    public void A_clean_redaction_leaves_no_residue()
    {
        var engine = SiluetaEngine.CreateDefault();
        var roster = new DeidentificationContext("residue-clean")
            .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName);

        RedactionResult result = engine.Redact("Ellenor Vasques rested well. Call 602-555-0147.", roster);

        Assert.NotEmpty(result.Applied);
        Assert.Empty(result.Residue);
        Assert.Equal(0, result.Manifest.ResidualSpans);
    }

    [Fact]
    public void A_surrogate_minted_for_one_record_is_checked_again_in_the_next()
    {
        // The vault keeps an assignment on purpose: stability wins, and re-minting would rewrite the
        // corpus behind the caller. But the rule "no invented name this pipeline would find" was only
        // ever enforced at mint time, against whichever record happened to come first. A later record
        // can have a real person whose surname IS the invented one — "Urena" is both in the surrogate
        // pool and an ordinary Hispanic surname — and the engine used to emit it without a word.
        var vault = new PseudonymVault();

        DeidentificationContext first = Forcing("record-a", "Urena")
            .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName);
        var engine = new SiluetaEngine([new KnownValueDetector(), PatternDetector.FromEmbeddedPack()], vault);
        RedactionResult a = engine.Redact("Eleanor Vasquez slept well.", first);

        Assert.Contains("Urena", a.Text, StringComparison.Ordinal);
        Assert.Empty(a.Residue);

        // Second record, same vault, and this time someone real is called Urena.
        var second = new DeidentificationContext("record-b")
            .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName)
            .AddPerson("staff-2", "Rosa Urena", IdentifierKind.StaffName);

        RedactionResult b = engine.Redact("Eleanor Vasquez saw Rosa Urena today.", second);

        Assert.NotEmpty(b.Residue);
        Assert.True(b.Manifest.ResidualSpans > 0);
    }

    [Fact]
    public void A_surrogate_that_forms_a_roster_name_with_the_words_around_it_is_residue()
    {
        // The safety check runs on the candidate in isolation; the matcher runs on windows of the
        // finished transcript. A one-word surrogate can join the word after it and spell someone real.
        var roster = new DeidentificationContext("residue-window");
        roster.AddValue("Sofia", IdentifierKind.PatientName, "s1");
        roster.AddValue("Mar Ochoa", IdentifierKind.StaffName, "s2");

        var vault = new PseudonymVault();
        var engine = new SiluetaEngine([new KnownValueDetector(), PatternDetector.FromEmbeddedPack()], vault);

        // Force the given name "Mar" onto s1 so the scenario is deterministic rather than one draw in
        // twenty-one: every other given name is blocked by being on the roster.
        foreach (string given in (string[])["Ale", "Alex", "Ariel", "Chris", "Cruz", "Dani", "Emery",
            "Guadalupe", "Jordan", "Luca", "Marley", "Noa", "Noel", "Quinn", "Remy", "Rene", "Robin",
            "Sasha", "Sol", "Yael"])
        {
            roster.AddValue(given, IdentifierKind.OtherName, $"block-{given}");
        }

        RedactionResult result = engine.Redact("Sofia Ochoa was here.", roster);

        // Whatever it produced, the promise is the same: the engine reports what it can still find.
        if (result.Text.Contains("Mar Ochoa", StringComparison.Ordinal))
        {
            Assert.NotEmpty(result.Residue);
        }
    }

    [Fact]
    public void Residue_is_what_the_pipeline_finds_not_what_a_reader_would()
    {
        // Worth pinning because it is the honest limit of the check: a name no detector can see is
        // invisible here too. Residue is a self-consistency check, not a leak rate.
        var roster = new DeidentificationContext("residue-limit")
            .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName);

        RedactionResult result = SiluetaEngine.CreateDefault()
            .Redact("Ellenor Vasques rested. Ellie called her neighbour Doris.", roster);

        Assert.Contains("Ellie", result.Text, StringComparison.Ordinal);
        Assert.Empty(result.Residue);
    }
}
