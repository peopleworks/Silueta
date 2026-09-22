using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// Punto 2, slice D1 — a street address is found and removed.
/// <para>
/// Pedro, 22 September 2026: "incluso una dirección exacta es una fuga". Safe Harbor names the street address
/// first among the geographic subdivisions smaller than a state (45 CFR § 164.514(b)(2)(i)(B)), and until now no
/// rule looked for one: an address survived unless a caller put it on the roster.
/// </para>
/// <para>
/// The rules are built from published address standards and nothing else. For the United States, USPS
/// Publication 28: a house number, an optional directional, a name and a street suffix from Appendix C1, an
/// optional unit from Appendix C2. For Mexico, INEGI's Norma Técnica sobre Domicilios Geográficos: a type of
/// road — calle, avenida, privada — then its name, then an optional number. Without a house number only a suffix
/// that names nothing but a road counts ("Maple Street", never "Supreme Court" or "St. Mary's"). The frozen
/// corpus's street marks were seen before these rules were written — at the close of the slice before — which is
/// why the rules come from the standards and why what they move on that corpus is reported as optimistic.
/// </para>
/// <para>
/// Like the city rule, these need capitals: "a 5 minute drive" and "a 3 block walk" are a number, a word and a
/// street suffix too, and only the capitals tell them from "5 Minute Drive". A recogniser that writes everything
/// in lower case gives these rules nothing.
/// </para>
/// </summary>
public class AddressTests
{
    private static string Run(string text) =>
        SiluetaEngine.FromLineage(SiluetaLineage.Default, new PseudonymVault())
            .Redact(text, new DeidentificationContext("r-1")).Text;

    [Theory]
    [InlineData("She lives at 1600 Pennsylvania Avenue now.", "She lives at [ADDRESS] now.")]
    [InlineData("Mail it to 221 N Main St, Apt 4B, Mesa, AZ 85201.", "Mail it to [ADDRESS], [CITY], AZ 852XX.")]
    [InlineData("Her house is at 7 W 23rd St.", "Her house is at [ADDRESS].")]
    [InlineData("They moved to 3050 East Camelback Road, Suite 200 last year.", "They moved to [ADDRESS] last year.")]
    [InlineData("We walked down Maple Street to the pharmacy.", "We walked down [ADDRESS] to the pharmacy.")]
    [InlineData("Send it to PO Box 1142.", "Send it to [ADDRESS].")]
    public void An_address_written_the_way_the_postal_service_writes_one_is_removed(string text, string expected)
    {
        Assert.Equal(expected, Run(text));
    }

    [Theory]
    [InlineData("Vive en la calle Hidalgo 27, cerca del mercado.", "Vive en la [ADDRESS], cerca del mercado.")]
    [InlineData("Su domicilio es Avenida Revolución número 1450.", "Su domicilio es [ADDRESS].")]
    [InlineData("Está en Privada de los Pinos #8.", "Está en [ADDRESS].")]
    [InlineData("Escríbale al apartado postal 88.", "Escríbale al [ADDRESS].")]
    public void Una_direccion_escrita_como_la_escribe_el_inegi_se_quita(string text, string expected)
    {
        Assert.Equal(expected, Run(text));
    }

    [Theory]
    [InlineData("It was a 5 minute drive from the clinic.")]
    [InlineData("Take 2 tablets by mouth every 8 hours.")]
    [InlineData("we took the back road home")]
    [InlineData("Dr. Patel saw her at St. Mary's this morning.")]
    [InlineData("The Supreme Court ruled on it.")]
    [InlineData("They took the 10 Freeway west.")]
    [InlineData("Caminamos por la calle principal.")]
    [InlineData("Está en la cama 12 del piso 3.")]
    public void A_number_or_a_word_that_only_looks_like_part_of_an_address_is_left_alone(string text)
    {
        Assert.Equal(text, Run(text));
    }

    [Fact]
    public void An_address_in_a_transcript_with_no_capitals_is_not_found()
    {
        // Pinned, like the city: the day this changes, it changes on purpose.
        Assert.Equal("she lives at 1600 pennsylvania avenue", Run("she lives at 1600 pennsylvania avenue"));
    }

    [Fact]
    public void Every_rule_in_the_built_in_pack_loads()
    {
        // A rule naming a list the file does not have is skipped, and written down — but in the pack that
        // ships, a skipped rule is a rule this build believes it has and does not.
        PatternDetector pack = PatternDetector.FromEmbeddedPack();

        Assert.Empty(pack.RulesSkipped);
        Assert.Contains(IdentifierKind.Address, pack.Kinds);
    }
}
