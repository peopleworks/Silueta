using System.Text.Json;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// What a reader of the redacted corpus can put together across documents.
/// <para>
/// Every measure this library had was per document, and the attack is not. The vault guarantees that a
/// subject keeps one invented name across the whole corpus — README feature, and the reason dashboards
/// work — so the invented name is a join key the corpus carries: every visit, every kept year, every
/// "90 or older", every "her daughter", filed under one string. Whether a reader can name that person is a
/// test no code can run. How many subjects share their pattern of surviving quasi-identifiers is
/// computable, and was not computed.
/// </para>
/// </summary>
public class LinkageReportTests
{
    private static PseudonymVault Vault() => new PseudonymVault()
        .Assign("patient-1", "Ale Espinal")
        .Assign("patient-2", "Noa Bravo")
        .Assign("patient-3", "Remy Toledo");

    private static (string, string) Doc(string id, string text) => (id, text);

    [Fact]
    public void It_counts_how_many_documents_carry_each_invented_name()
    {
        LinkageReport report = Linkage.Analyze(
            [
                Doc("d1", "Ale Espinal rested well in 2026."),
                Doc("d2", "Ale Espinal refused lunch."),
                Doc("d3", "Noa Bravo walked with her daughter."),
                Doc("d4", "Ale Espinal slept."),
            ],
            Vault())!;

        Assert.Equal(3, report.BySubject.Single(s => s.Surrogate == "Ale Espinal").Documents);
        Assert.Equal(1, report.BySubject.Single(s => s.Surrogate == "Noa Bravo").Documents);
        Assert.DoesNotContain(report.BySubject, s => s.Surrogate == "Remy Toledo");
    }

    [Fact]
    public void A_subject_whose_surviving_details_nobody_else_shares_is_a_class_of_one()
    {
        LinkageReport report = Linkage.Analyze(
            [
                Doc("d1", "Ale Espinal was seen in 2024. She is 90 or older."),
                Doc("d2", "Noa Bravo was seen in 2026 with her son."),
            ],
            Vault())!;

        Assert.Equal(1, report.SmallestClass);
        Assert.Equal(2, report.UniqueSubjects);
    }

    [Fact]
    public void Subjects_with_the_same_surviving_details_share_a_class()
    {
        LinkageReport report = Linkage.Analyze(
            [
                Doc("d1", "Ale Espinal was seen in 2026 with her daughter."),
                Doc("d2", "Noa Bravo was seen in 2026 with her daughter."),
            ],
            Vault())!;

        Assert.Equal(2, report.SmallestClass);
        Assert.Equal(0, report.UniqueSubjects);
    }

    [Fact]
    public void A_mention_by_first_name_alone_is_the_same_subject_when_only_one_subject_has_that_name()
    {
        LinkageReport report = Linkage.Analyze(
            [
                Doc("d1", "Ale Espinal rested."),
                Doc("d2", "Ale said the pain was 4 out of 10."),
            ],
            Vault())!;

        Assert.Equal(2, report.BySubject.Single(s => s.Surrogate == "Ale Espinal").Documents);
    }

    [Fact]
    public void Spanish_kinship_and_age_wording_is_seen_too()
    {
        LinkageReport report = Linkage.Analyze(
            [Doc("d1", "Ale Espinal llegó con su hija. Tiene 90 o más.")],
            Vault())!;

        SubjectExposure ale = report.BySubject.Single();
        Assert.Contains("hija", ale.Kinship);
        Assert.True(ale.AgeBracket);
    }

    [Fact]
    public void The_report_names_no_subject_and_carries_no_code()
    {
        // It is keyed by the invented name, which the corpus already carries. The subject id and the SIL-
        // code are the vault's, and a report that included them would be the way back, filed next to the
        // corpus.
        LinkageReport report = Linkage.Analyze([Doc("d1", "Ale Espinal rested in 2026.")], Vault())!;

        string json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("patient-1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SIL-", json, StringComparison.Ordinal);
    }

    [Fact]
    public void It_says_what_it_cannot_see()
    {
        LinkageReport report = Linkage.Analyze([Doc("d1", "Ale Espinal rested.")], Vault())!;

        Assert.Contains(report.Blind, line => line.Contains("place", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Blind, line => line.Contains("co-occurrence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_empty_corpus_has_no_report_rather_than_a_perfect_one()
    {
        Assert.Null(Linkage.Analyze([], Vault()));
    }
}
