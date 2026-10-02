using Silueta.Core;

namespace Silueta.Core.Tests;

public class MatchingCostTests
{
    [Fact]
    public void Repeated_replacements_do_not_materialize_words_for_every_mention()
    {
        const int mentions = 10_000;
        const string phrase = "Carmen Alvarez. ";
        string source = string.Concat(Enumerable.Repeat(phrase, mentions));
        Detection[] matches = Enumerable.Range(0, mentions)
            .Select(i => new Detection(i * phrase.Length, 14, IdentifierKind.PatientName,
                "fixture", 1, "subject-1", MatchKind.Exact)).ToArray();
        var vault = new PseudonymVault().Assign("subject-1", "Ale Bravo");
        var engine = new SiluetaEngine([new FixtureDetector(source, matches)], vault);
        var context = new DeidentificationContext("allocation-fixture");
        engine.Redact(source, context);

        long before = GC.GetAllocatedBytesForCurrentThread();
        RedactionResult result = engine.Redact(source, context);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(mentions, result.Applied.Count);
        Assert.Empty(result.Residue);
        Assert.Equal(string.Concat(Enumerable.Repeat("Ale Bravo. ", mentions)), result.Text);
        Assert.True(allocated < 9_500_000, $"Replacing {mentions:N0} mentions allocated {allocated:N0} bytes.");
    }

    private sealed class FixtureDetector(string source, Detection[] matches) : IDetector
    {
        public string Id => "fixture";
        public IEnumerable<Detection> Detect(string text, DeidentificationContext context) =>
            ReferenceEquals(text, source) ? matches : [];
    }

    [Theory]
    [InlineData("Sofi\u0301a")]
    [InlineData("\U00010400\U00010428")]
    [InlineData("Na'vi")]
    [InlineData("Ana‐Maria")]
    [InlineData("O’Neil")]
    [InlineData("Ab-7")]
    [InlineData("Ana--Maria")]
    [InlineData("Ana\U0001F600Maria")]
    [InlineData("Ana\ud800Maria")]
    [InlineData("\u0301")]
    [InlineData(" - ' ")]
    [InlineData("  Ana Maria  ")]
    public void Replacement_word_shape_uses_the_tokenizers_unicode_and_joiner_rules(string mention)
    {
        AssertReplacementShape(mention);
    }

    [Fact]
    public void Replacement_word_shape_agrees_with_tokenization_for_mixed_unicode_sequences()
    {
        string[] pieces = ["Ana", "7", "\u0301", "\U00010400", "\U0001F600", "\ud800", " ",
            "'", "’", "-", "‐", ".", "\n", "ماريا"];
        var random = new Random(73);
        for (int i = 0; i < 500; i++)
        {
            string mention = string.Concat(Enumerable.Range(0, random.Next(1, 20))
                .Select(_ => pieces[random.Next(pieces.Length)]));
            AssertReplacementShape(mention);
        }
    }

    private static void AssertReplacementShape(string mention)
    {
        var pools = new SurrogatePools(["María José"], ["De la Cruz"]);
        var vault = new PseudonymVault(pools).Assign("subject-1", "María José De la Cruz");
        Detection[] matches = [new(0, mention.Length, IdentifierKind.PatientName,
            "fixture", 1, "subject-1", MatchKind.Exact)];
        var engine = new SiluetaEngine([new FixtureDetector(mention, matches)], vault);

        RedactionResult result = engine.Redact(mention, new DeidentificationContext("word-shape"));

        Assert.Equal(pools.Fit("María José De la Cruz", Tokenizer.Tokenize(mention).Count), result.Text);
        Assert.Empty(result.Residue);
    }

    [Fact]
    public void The_fast_verdict_agrees_with_full_distance_across_edit_budgets()
    {
        MatchTolerance[] tolerances = [MatchTolerance.Default,
            new() { ExactBelow = int.MaxValue },
            new() { ExactBelow = 0, TwoEditsFrom = int.MaxValue },
            new() { ExactBelow = 0, TwoEditsFrom = 0 }];
        string[] keys = ["", .. Enumerable.Range(1, 6).SelectMany(length =>
            Enumerable.Range(0, 1 << length).Select(value =>
                new string(Enumerable.Range(0, length).Select(bit => (value & (1 << bit)) == 0 ? 'a' : 'b').ToArray())))];

        foreach (string a in keys)
        {
            foreach (string b in keys)
            {
                int distance = Similarity.Distance(a, b);
                foreach (MatchTolerance tolerance in tolerances)
                {
                    Assert.Equal(distance <= tolerance.BudgetFor(Math.Min(a.Length, b.Length)), tolerance.Accepts(a, b));
                }
            }
        }

        var random = new Random(42);
        for (int i = 0; i < 1_000; i++)
        {
            string a = new(Enumerable.Range(0, random.Next(8, 80)).Select(_ => (char)random.Next('a', 'f')).ToArray());
            string b = i % 2 == 0 ? a[..^1] + "x" : new string(a.Reverse().ToArray());
            Assert.Equal(Similarity.Distance(a, b) <= MatchTolerance.Default.BudgetFor(Math.Min(a.Length, b.Length)),
                MatchTolerance.Default.Accepts(a, b));
        }
    }

    [Fact]
    public void Comparing_keys_does_not_allocate_rows_for_each_word_on_the_roster()
    {
        MatchTolerance tolerance = MatchTolerance.Default;
        const string near = "abcdefgh";
        const string accepted = "abcdefxy";
        const string rejected = "zyxwvuts";
        for (int i = 0; i < 100; i++)
        {
            tolerance.Accepts(near, accepted);
            tolerance.Accepts(near, rejected);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int found = 0;
        for (int i = 0; i < 10_000; i++)
        {
            found += tolerance.Accepts(near, accepted) ? 1 : 0;
            found += tolerance.Accepts(near, rejected) ? 1 : 0;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(10_000, found);
        Assert.True(allocated < 1_024, $"Key comparisons allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Repeated_words_compute_their_key_once_per_call()
    {
        Token[] tokens = Enumerable.Range(0, 10_000).Select(i => new Token(i * 8, 7, "patient")).ToArray();
        PhoneticKey.ComputeAll(tokens);

        long before = GC.GetAllocatedBytesForCurrentThread();
        string[] keys = PhoneticKey.ComputeAll(tokens);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        string expected = PhoneticKey.Compute("patient");
        Assert.All(keys, key => Assert.Equal(expected, key));
        // Enough room for the returned references and bookkeeping, but not 10,000 string pipelines.
        Assert.True(allocated < 300_000, $"Repeated keys allocated {allocated:N0} bytes.");
    }

    [Theory]
    [InlineData("Carmen", "Carmin")]
    [InlineData("Javier", "Xavier")]
    [InlineData("Rodriguez", "Rodrigues")]
    public void Accepted_matches_keep_the_full_distance_confidence(string roster, string heard)
    {
        var context = new DeidentificationContext("perf-test").AddValue(roster, IdentifierKind.PatientName, "s-1");
        Detection match = Assert.Single(new KnownValueDetector().Detect(heard, context));
        Assert.Equal(Similarity.Ratio(PhoneticKey.Compute(roster), PhoneticKey.Compute(heard)), match.Confidence);
    }

    [Fact]
    public void Key_reuse_preserves_default_tokens_and_enumerates_input_only_once()
    {
        Token[] tokens = [default, new(0, 0, ""), new(0, 5, "Sofía"),
            new(0, 6, "Sofi\u0301a"), new(0, 5, "SOFÍA"), new(0, 5, "Sofía")];
        int enumerations = 0;
        IEnumerable<Token> Once()
        {
            Assert.Equal(1, ++enumerations);
            foreach (Token token in tokens)
            {
                yield return token;
            }
        }

        Assert.Equal(tokens.Select(token => PhoneticKey.Compute(token.Text)), PhoneticKey.ComputeAll(Once()));
        Assert.Equal(1, enumerations);
    }

    [Fact]
    public void Explanations_still_get_the_full_distance_for_equal_length_rejections()
    {
        Assert.False(MatchTolerance.Default.Accepts("abcdefgh", "zyxwvuts", out int distance, out int budget));
        Assert.Equal(8, distance);
        Assert.Equal(2, budget);
    }
}
