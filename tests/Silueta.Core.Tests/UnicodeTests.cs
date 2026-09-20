using System.Text;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// F2.5 — the same name in two Unicode forms is one name, and a character outside the basic plane is a
/// character.
/// <para>
/// "Sofía" can be written two ways that no reader can tell apart: one code point for í (NFC), or i followed
/// by a combining acute (NFD). A roster typed on a Mac and a transcript from a Windows recogniser can
/// disagree about which, and nothing in this library normalised. Worse than a miss: the tokenizer asked
/// <c>char.IsLetterOrDigit</c>, which is false for a combining mark, so the mark ended the word and NFD
/// "Sofía" tokenised as "Sof" plus "a" — two words, neither of them a name.
/// </para>
/// <para>
/// The input string is not normalised, and must not be. Every offset in this library — a detection, a gold
/// span, the text handed back — indexes the caller's own string, and rewriting it would move all of them
/// and change a transcript nobody asked to have changed. So the tokenizer is made form-agnostic instead: a
/// combining mark belongs to the word it sits on, and the key strips accents from either form anyway.
/// </para>
/// </summary>
public class UnicodeTests
{
    private static string Nfd(string text) => text.Normalize(NormalizationForm.FormD);

    private static bool IsCombining(char c) =>
        System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark;

    [Fact]
    public void A_combining_mark_belongs_to_the_word_it_sits_on()
    {
        string decomposed = Nfd("Sofía");
        Assert.Equal(6, decomposed.Length); // i + combining acute, so this really is the other form

        Token token = Assert.Single(Tokenizer.Tokenize(decomposed));
        Assert.Equal(0, token.Start);
        Assert.Equal(decomposed.Length, token.Length);
    }

    [Fact]
    public void The_two_forms_of_one_name_have_one_key()
    {
        Assert.Equal(PhoneticKey.Compute("Sofía"), PhoneticKey.Compute(Nfd("Sofía")));
        Assert.Equal(PhoneticKey.Compute("Nuñez"), PhoneticKey.Compute(Nfd("Nuñez")));
    }

    [Fact]
    public void A_roster_in_one_form_finds_a_transcript_in_the_other()
    {
        var context = new DeidentificationContext("rec-1")
            .AddPerson("p1", "Sofía Reyes", IdentifierKind.PatientName);

        string transcript = Nfd("Sofía Reyes took the morning shift.");

        // The roster registers the full name and each part of it, so the whole name and the first name are
        // both found; what matters here is that the longest one covers the decomposed spelling exactly.
        Detection found = Assert.IsType<Detection>(new KnownValueDetector().Detect(transcript, context).MaxBy(d => d.Length));

        Assert.Equal(0, found.Start);
        Assert.Equal(Nfd("Sofía Reyes").Length, found.Length);
    }

    [Fact]
    public void The_other_direction_too()
    {
        var context = new DeidentificationContext("rec-1")
            .AddPerson("p1", Nfd("Sofía Reyes"), IdentifierKind.PatientName);

        Assert.NotEmpty(new KnownValueDetector().Detect("Sofía Reyes took the morning shift.", context));
    }

    [Fact]
    public void A_character_outside_the_basic_plane_is_one_word_and_not_none()
    {
        // U+10400 DESERET CAPITAL LETTER LONG I. A surrogate pair: char.IsLetterOrDigit is false for each
        // half on its own, so the tokenizer used to return nothing at all for a word made of them, and a
        // transcript in such a script was a transcript with no words in it to search.
        string deseret = "\U00010400\U00010401\U00010402";

        Token token = Assert.Single(Tokenizer.Tokenize(deseret));
        Assert.Equal(0, token.Start);
        Assert.Equal(deseret.Length, token.Length);
    }

    [Fact]
    public void An_emoji_is_not_a_word_and_does_not_join_the_ones_around_it()
    {
        List<Token> tokens = Tokenizer.Tokenize("Sofia \U0001F600 Reyes");

        Assert.Equal(2, tokens.Count);
        Assert.Equal("Sofia", tokens[0].Text);
        Assert.Equal("Reyes", tokens[1].Text);
    }

    [Fact]
    public void Offsets_still_point_at_the_callers_own_string()
    {
        // The guarantee that stops anyone from being tempted to normalise the input: what a detection says
        // has to be exactly what is there, in the string the caller passed, whatever form it is written in.
        string transcript = Nfd("Call Sofía Reyes now.");
        var context = new DeidentificationContext("rec-1")
            .AddPerson("p1", "Sofía Reyes", IdentifierKind.PatientName);

        List<Detection> found = [.. new KnownValueDetector().Detect(transcript, context)];
        Assert.NotEmpty(found);

        foreach (Detection detection in found)
        {
            Assert.Equal(detection.TextIn(transcript), transcript.Substring(detection.Start, detection.Length));

            // And neither edge falls inside a character: a span that began or ended on a combining mark
            // would leave the accent of a redacted name sitting in the output on its own.
            Assert.False(IsCombining(transcript[detection.Start]));
            Assert.True(detection.End == transcript.Length || !IsCombining(transcript[detection.End]));
        }
    }

    [Fact]
    public void Redacting_a_decomposed_transcript_leaves_everything_else_byte_for_byte()
    {
        string transcript = Nfd("Sofía Reyes visited. Blood pressure 138 over 82.");
        var context = new DeidentificationContext("rec-1")
            .AddPerson("p1", "Sofía Reyes", IdentifierKind.PatientName);

        RedactionResult result = new SiluetaEngine([new KnownValueDetector()]).Redact(transcript, context);

        Assert.DoesNotContain("Sof", result.Text, StringComparison.Ordinal);
        Assert.EndsWith(" visited. Blood pressure 138 over 82.", result.Text, StringComparison.Ordinal);
    }
}
