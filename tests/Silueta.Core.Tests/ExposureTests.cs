using System.Text.Json;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The ways identified text escapes a de-identifier without anyone redacting anything: through the
/// result object, through the record id, through an exception message. Each of these was a real channel
/// in this library, and each is worth more than a detector, because a leak here is silent.
/// </summary>
public class ExposureTests
{
    private static DeidentificationContext Roster() => new DeidentificationContext("shift-001")
        .AddPerson("patient-1", "Eleanor Vasquez", IdentifierKind.PatientName)
        .AddPerson("staff-1", "Sofía Reyes", IdentifierKind.StaffName);

    [Fact]
    public void The_result_of_a_redaction_carries_no_original_text()
    {
        const string text = "Ellenor Vasques was seen by Sophia at 602-555-0147, jamileth.v@example.com.";

        RedactionResult result = SiluetaEngine.CreateDefault().Redact(text, Roster());

        // Serialised the way a caller would to log or ship a run. Whatever this contains has left the
        // trusted environment.
        string json = JsonSerializer.Serialize(result.Applied);

        Assert.NotEmpty(result.Applied);
        foreach (string original in (string[])["Ellenor", "Vasques", "Sophia", "602-555-0147", "jamileth.v@example.com"])
        {
            Assert.DoesNotContain(original, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_detection_has_no_property_that_returns_the_matched_text()
    {
        // Belt and braces: the test above passes if a property is merely marked JsonIgnore. This one
        // fails unless the value is genuinely not on the public type.
        var detection = new Detection(0, 5, IdentifierKind.PatientName, "known", 1.0);

        Assert.DoesNotContain(
            typeof(Detection).GetProperties(),
            p => p.PropertyType == typeof(string) && p.Name is "Text" or "Value" or "Matched");

        Assert.Equal(5, detection.Length);
    }

    [Fact]
    public void A_runaway_pattern_never_carries_the_transcript_out_with_it()
    {
        // A pattern pack is a JSON file anyone can send. This one backtracks exponentially, and the
        // framework's own timeout exception puts the whole input in its Message and in Input.
        var pack = new[] { new PatternRule { Id = "evil", Kind = "Other", Regex = "^(a+)+$" } };
        var detector = new PatternDetector(pack, TimeSpan.FromMilliseconds(50));
        string transcript = new string('a', 40) + "! Eleanor Vasquez lives here.";

        var thrown = Assert.Throws<PatternPackException>(() => detector.Detect(transcript, Roster()).ToList());

        Assert.Contains("evil", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Eleanor", thrown.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aaaa", thrown.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(thrown.InnerException); // the inner exception is exactly what carries the input
    }

    [Fact]
    public void A_record_needs_an_id_of_its_own()
    {
        // The CLI used to default this to the file name, so a shift note saved as "Ana-Perez.txt"
        // produced a manifest that named Ana Perez. The manifest is the artefact that travels.
        Assert.Throws<ArgumentException>(() => new DeidentificationContext(""));
        Assert.Throws<ArgumentException>(() => new DeidentificationContext("   "));
        Assert.Throws<ArgumentNullException>(() => new DeidentificationContext(null!));
    }
}
