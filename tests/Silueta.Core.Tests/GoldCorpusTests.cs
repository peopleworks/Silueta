using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The gold corpus is the yardstick, and a yardstick that loads a bad file quietly measures with a bent
/// rule. Every check here is a way a gold document could be wrong and still produce a number.
/// </summary>
public sealed class GoldCorpusTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("silueta-gold-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void Write(string name, string json) => File.WriteAllText(Path.Combine(_directory, name), json);

    private const string Valid = """
        { "documentId": "g-1", "source": "synthetic", "text": "Eleanor rested.",
          "roster": [ { "value": "Eleanor Vasquez", "kind": "PatientName", "subjectId": "patient-1" } ],
          "spans": [ { "start": 0, "length": 7, "kind": "PatientName", "annotator": "a" } ] }
        """;

    [Fact]
    public void The_committed_corpus_loads_and_is_what_its_README_says_it_is()
    {
        // Structure only — no rates. The corpus was frozen in a commit before it was ever evaluated, and
        // this test holds its shape so a later edit to a gold file shows up as a failure, not as a number.
        GoldCorpus corpus = GoldCorpus.Load(Path.Combine(McpToolDocumentationTests.RepoRoot, "corpus-synthetic"));

        Assert.Equal(31, corpus.Documents.Count);
        Assert.Equal(30, corpus.Documents.Count(d => d.Source.StartsWith("tts-asr/", StringComparison.Ordinal)));
        Assert.Equal(10, corpus.Documents.Count(d => d.Source == "tts-asr/clean"));
        Assert.Equal(10, corpus.Documents.Count(d => d.Source == "tts-asr/phone"));
        Assert.Equal(10, corpus.Documents.Count(d => d.Source == "tts-asr/noisy-phone"));
        Assert.Equal(153, corpus.Documents.Where(d => d.Source.StartsWith("tts-asr/", StringComparison.Ordinal)).Sum(d => d.Spans.Count));
        Assert.All(corpus.Documents, d => Assert.Single(d.Annotators));
    }

    [Fact]
    public void A_valid_document_loads()
    {
        Write("g-1.json", Valid);

        GoldCorpus corpus = GoldCorpus.Load(_directory);

        GoldDocument document = corpus.Documents.Single();
        Assert.Equal("g-1", document.DocumentId);
        Assert.Equal(IdentifierKind.PatientName, document.Spans.Single().Kind);
        Assert.Equal("a", document.Spans.Single().Annotator);
    }

    [Theory]
    [InlineData("""{ "source": "s", "text": "x", "spans": [], "roster": [] }""", "documentId")]
    [InlineData("""{ "documentId": "g", "text": "x", "spans": [], "roster": [] }""", "source")]
    [InlineData("""{ "documentId": "g", "source": "s", "text": "Eleanor", "roster": [], "spans": [ { "start": 3, "length": 9, "kind": "PatientName", "annotator": "a" } ] }""", "outside")]
    [InlineData("""{ "documentId": "g", "source": "s", "text": "Eleanor", "roster": [], "spans": [ { "start": 0, "length": 7, "kind": "Patient", "annotator": "a" } ] }""", "kind")]
    [InlineData("""{ "documentId": "g", "source": "s", "text": "Eleanor", "roster": [], "spans": [ { "start": 0, "length": 7, "kind": "PatientName" } ] }""", "annotator")]
    [InlineData("""{ "documentId": "g", "source": "s", "text": "x", "spans": [], "roster": [ { "value": "Eleanor", "kind": "PatientName" } ] }""", "subjectId")]
    public void A_document_that_would_measure_with_a_bent_rule_is_refused(string json, string because)
    {
        Write("bad.json", json);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => GoldCorpus.Load(_directory));

        Assert.Contains(because, thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_documents_with_one_id_are_refused()
    {
        Write("a.json", Valid);
        Write("b.json", Valid);

        Assert.Throws<InvalidOperationException>(() => GoldCorpus.Load(_directory));
    }

    [Fact]
    public void An_empty_directory_is_not_a_corpus()
    {
        Assert.Throws<InvalidOperationException>(() => GoldCorpus.Load(_directory));
    }

    [Fact]
    public void Agreement_is_not_computable_with_one_annotator_and_is_not_invented()
    {
        Write("g-1.json", Valid);

        Assert.Null(Agreement.Kappa(GoldCorpus.Load(_directory).Documents.Single(), IdentifierKind.PatientName));
    }

    [Fact]
    public void Two_annotators_who_marked_the_same_characters_agree_completely()
    {
        Write("g-2.json", """
            { "documentId": "g-2", "source": "s", "text": "Eleanor rested with Sofia today.", "roster": [],
              "spans": [
                { "start": 0, "length": 7, "kind": "PatientName", "annotator": "a" },
                { "start": 0, "length": 7, "kind": "PatientName", "annotator": "b" } ] }
            """);

        Assert.Equal(1.0, Agreement.Kappa(GoldCorpus.Load(_directory).Documents.Single(), IdentifierKind.PatientName));
    }

    [Fact]
    public void Two_annotators_who_marked_different_names_agree_less_than_chance_allows()
    {
        Write("g-3.json", """
            { "documentId": "g-3", "source": "s", "text": "Eleanor rested with Sofia today.", "roster": [],
              "spans": [
                { "start": 0, "length": 7, "kind": "PatientName", "annotator": "a" },
                { "start": 20, "length": 5, "kind": "PatientName", "annotator": "b" } ] }
            """);

        double? kappa = Agreement.Kappa(GoldCorpus.Load(_directory).Documents.Single(), IdentifierKind.PatientName);

        Assert.NotNull(kappa);
        Assert.True(kappa < 0.0);
    }
}
