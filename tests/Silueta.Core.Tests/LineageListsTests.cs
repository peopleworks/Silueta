using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// Slice F — a lineage brings word lists of its own, and its rules name them.
/// <para>
/// Pedro's reminder, 22 September 2026: Silueta is not Navi's. It has to be configurable for any project that
/// comes next. The rules that ship are built from American and Mexican standards — the states, the street
/// suffixes of USPS Publication 28, INEGI's types of road — and those lists were compiled in, so an agency in
/// Bogotá or Lisbon could write rules of its own but had to spell every word of its country inside each regular
/// expression, in each of them, with no way to say "the same list as the other rule".
/// </para>
/// <para>
/// So a lineage may declare <c>lists</c>, and its rules name them exactly as the built-in rules name theirs. A
/// name the build already has is refused rather than quietly replaced: two lists under one name is the second
/// copy of a rule, and here it would be one that decides what gets found. The built-in lists stay available, so
/// a lineage written for Colombia can still name <c>{{month-es}}</c> instead of writing the months again.
/// </para>
/// </summary>
public class LineageListsTests
{
    private static SiluetaLineage Lineage(string body) => SiluetaLineage.FromJson($$"""
        {
          "lineage": "clinica-bogota", "version": "1", "language": "es-CO",
          "pools": { "given": ["Ale", "Noa"], "family": ["Bravo", "Toledo"] },
          "labels": { "City": "[CIUDAD]", "Date": "[FECHA]", "Address": "[DIRECCION]" },
          {{body}}
        }
        """);

    private static string Run(SiluetaLineage lineage, string text) =>
        SiluetaEngine.FromLineage(lineage, new PseudonymVault()).Redact(text, new DeidentificationContext("r-1")).Text;

    private const string ColombianRules = """
        "lists": {
          "departamento-co": ["Antioquia", "Cundinamarca", "Valle del Cauca", "Atlántico"],
          "via-co": ["Calle", "Carrera", "Diagonal", "Transversal", "Avenida"]
        },
        "patterns": [
          { "id": "ciudad-co", "kind": "City", "regex": "(?-i:\\b\\p{Lu}\\p{Ll}+)(?=,\\s+{{departamento-co}}\\b)", "confidence": 0.9 },
          { "id": "via-co", "kind": "Address", "regex": "\\b{{via-co}}\\s+\\d{1,3}\\s*#\\s*\\d{1,3}-\\d{1,3}\\b", "confidence": 0.9 }
        ]
        """;

    [Fact]
    public void An_organisation_brings_the_word_lists_of_its_own_country()
    {
        SiluetaLineage lineage = Lineage(ColombianRules);

        Assert.Equal("Vive en [CIUDAD], Antioquia.", Run(lineage, "Vive en Medellín, Antioquia."));
        Assert.Equal("La dirección es [DIRECCION].", Run(lineage, "La dirección es Carrera 43 # 12-34."));
    }

    [Fact]
    public void A_lineage_rule_may_name_a_list_this_build_already_has()
    {
        // The point of naming a list rather than spelling it out: a lineage that replaces the pack does not have
        // to rewrite the months to keep finding a date.
        SiluetaLineage lineage = Lineage("""
            "patterns": [
              { "id": "fecha-co", "kind": "Date", "regex": "\\b\\d{1,2}\\s+de\\s+{{month-es}}\\b", "confidence": 0.9 }
            ]
            """);

        Assert.Equal("La cita es el [FECHA].", Run(lineage, "La cita es el 3 de marzo."));
    }

    [Fact]
    public void A_list_under_a_name_this_build_already_uses_is_refused()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Lineage("""
            "lists": { "month-es": ["enero", "febrero"] }
            """));

        Assert.Contains("month-es", refused.Message, StringComparison.Ordinal);
        Assert.Contains("already", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_name_a_rule_could_not_write_is_refused_too()
    {
        // {{...}} reads lower-case letters, digits and hyphens. A list nothing can name is a list that does
        // nothing, and saying so at load beats a rule that silently never fires.
        Assert.Throws<InvalidOperationException>(() => Lineage("""
            "lists": { "Departamento CO": ["Antioquia"] }
            """));
    }

    [Fact]
    public void A_rule_naming_a_list_nobody_declared_is_skipped_and_written_down()
    {
        SiluetaLineage lineage = Lineage("""
            "patterns": [
              { "id": "vereda", "kind": "Address", "regex": "\\b{{vereda-co}}\\b", "confidence": 0.9 }
            ]
            """);

        PatternDetector detector = lineage.CreatePatternDetector();

        Assert.Contains("vereda", detector.RulesSkipped);
        Assert.Equal(0, detector.RulesLoaded);
    }

    [Fact]
    public void The_lists_are_part_of_what_the_lineage_fingerprint_describes()
    {
        Assert.NotEqual(
            Lineage("\"lists\": { \"via-co\": [\"Calle\"] }").Fingerprint,
            Lineage("\"lists\": { \"via-co\": [\"Carrera\"] }").Fingerprint);
    }

    [Fact]
    public void The_built_in_lineage_declares_none_and_digests_as_it_did()
    {
        // A lineage without lists must digest exactly as before this existed, or every manifest ever written
        // stops matching the lineage it names.
        Assert.Empty(SiluetaLineage.Default.Lists);
        Assert.Equal("0d2728ee31c404eb", SiluetaLineage.Default.Fingerprint);
    }
}
