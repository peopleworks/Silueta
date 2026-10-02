using Silueta.Core;

namespace Silueta.Core.Tests;

public class ProcessingLimitsTests
{
    private static DeidentificationContext Empty => new("limits-1");

    [Fact]
    public void Oversized_input_is_refused_before_a_detector_or_vault_is_touched()
    {
        var detector = new CountingDetector();
        var engine = new SiluetaEngine([detector]) { Limits = new() { MaxInputCharacters = 8 } };
        const string text = "Invented Person came in.";

        var error = Assert.Throws<RedactionLimitException>(() => engine.Redact(text, Empty));
        Assert.Equal(8, error.Limit);
        Assert.Equal(0, detector.Calls);
        Assert.Equal(0, engine.Vault.Count);
        Assert.DoesNotContain(text, error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Exactly_the_input_limit_is_allowed()
    {
        var engine = new SiluetaEngine([]) { Limits = new() { MaxInputCharacters = 8 } };
        Assert.Equal("abcdefgh", engine.Redact("abcdefgh", Empty).Text);
    }

    [Fact]
    public void Too_many_candidates_fail_instead_of_silently_dropping_a_detection()
    {
        var detector = new CountingDetector(5);
        var engine = new SiluetaEngine([detector]) { Limits = new() { MaxDetections = 2 } };

        Assert.Throws<RedactionLimitException>(() => engine.Redact("abcde", Empty));
        Assert.Equal(3, detector.Yielded);
    }

    [Fact]
    public void The_read_back_pass_is_limited_independently()
    {
        var detector = new CountingDetector(5, onlyOnCall: 3);
        var engine = new SiluetaEngine([detector]) { Limits = new() { MaxDetections = 2 } };
        // Detection, opaque record-id check, then read-back. No subject ids or surrogates here.
        Assert.Throws<RedactionLimitException>(() => engine.Redact("abcde", Empty));
    }

    [Fact]
    public void A_pre_cancelled_run_does_not_call_detectors_or_mint_names()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var detector = new CountingDetector();
        var engine = new SiluetaEngine([detector]);

        var error = Assert.Throws<OperationCanceledException>(() =>
            engine.Redact("a", Empty, policy: null, cancellationToken: source.Token));
        Assert.Equal(source.Token, error.CancellationToken);
        Assert.Equal(0, detector.Calls);
        Assert.Equal(0, engine.Vault.Count);
    }

    [Fact]
    public void Known_value_candidates_are_limited_before_minting()
    {
        var engine = new SiluetaEngine([new KnownValueDetector()])
        {
            Limits = new() { MaxDetections = 2 }, FindRelativesNamedInText = false
        };
        var roster = Empty.AddValue("Ada", IdentifierKind.PatientName, "s-1");
        Assert.Throws<RedactionLimitException>(() => engine.Redact("Ada Ada Ada", roster));
        Assert.Equal(0, engine.Vault.Count);
    }

    [Theory]
    [InlineData("born in 1930. born in 1931.")]
    [InlineData("call five five five zero one four seven. call six zero two five five five zero one four seven.")]
    public void Rules_outside_the_detector_pack_share_the_candidate_limit(string text)
    {
        var engine = new SiluetaEngine([]) { Limits = new() { MaxDetections = 1 } };
        var context = new DeidentificationContext("limits-1") { RecordedOn = new DateOnly(2026, 10, 2) };
        Assert.Throws<RedactionLimitException>(() => engine.Redact(text, context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Built_in_detectors_observe_cancellation_between_candidates(bool patterns)
    {
        using var source = new CancellationTokenSource();
        IDetector detector = patterns
            ? new PatternDetector([new() { Id = "letters", Kind = "Other", Regex = @"\ba\b" }])
            : new KnownValueDetector();
        var context = Empty.AddValue("a", IdentifierKind.PatientName, "s-1");
        using IEnumerator<Detection> candidates = detector.Detect("a a a", context, source.Token).GetEnumerator();
        Assert.True(candidates.MoveNext());
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => candidates.MoveNext());
    }

    [Fact]
    public void Exactly_the_candidate_limit_is_allowed()
    {
        var engine = new SiluetaEngine([new PatternDetector([new() { Id = "letters", Kind = "Other", Regex = @"\ba\b" }])])
        {
            Limits = new() { MaxDetections = 2 }
        };
        Assert.Equal(2, engine.Redact("a a", Empty).Applied.Count);
    }

    [Fact]
    public void Cancellation_during_a_legacy_detector_stops_at_the_next_candidate()
    {
        using var source = new CancellationTokenSource();
        var detector = new CountingDetector(100, cancel: source);
        var engine = new SiluetaEngine([detector]);

        Assert.Throws<OperationCanceledException>(() => engine.Redact("abcdef", Empty, policy: null, cancellationToken: source.Token));
        Assert.Equal(1, detector.Yielded);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void Invalid_limits_fail_before_processing(int characters, int candidates)
    {
        var detector = new CountingDetector();
        var engine = new SiluetaEngine([detector])
        {
            Limits = new() { MaxInputCharacters = characters, MaxDetections = candidates }
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Redact("a", Empty));
        Assert.Equal(0, detector.Calls);
    }

    private sealed class CountingDetector(int count = 0, int onlyOnCall = 0, CancellationTokenSource? cancel = null) : IDetector
    {
        public string Id => "counting";
        public int Calls { get; private set; }
        public int Yielded { get; private set; }

        public IEnumerable<Detection> Detect(string text, DeidentificationContext context)
        {
            Calls++;
            if (onlyOnCall != 0 && Calls != onlyOnCall)
            {
                yield break;
            }
            for (int i = 0; i < count; i++)
            {
                Yielded++;
                cancel?.Cancel();
                yield return new Detection(0, 1, IdentifierKind.Other, Id, 1.0);
            }
        }
    }
}
