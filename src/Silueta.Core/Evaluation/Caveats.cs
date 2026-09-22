namespace Silueta.Core;

/// <summary>
/// What has to be said alongside a redacted transcript, every time, in the library rather than in each
/// thing that wraps it.
/// <para>
/// This lived inside the MCP server, which is where it was first needed: a model hands the text on, so the
/// model is told what it is handing on. But the same sentences belong on the web demo, in a report, and in
/// anything else built on this library — and the moment they were written a second time they would start
/// to differ, in the direction of sounding better. That has happened here before: the note used to say
/// "most transcripts still held something identifying", which is the measured number restated from memory.
/// </para>
/// <para>
/// Everything below is derived — from the run, from the roster, from the measurement embedded in the
/// build. Nothing here is a figure typed into a string.
/// </para>
/// </summary>
public static class Caveats
{
    /// <summary>
    /// The notes for one run, most urgent first. Each is a complete sentence and can be shown on its own
    /// line, in a list, or joined into a paragraph.
    /// </summary>
    public static IReadOnlyList<string> For(DeidentificationContext context, RedactionResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        var notes = new List<string>();

        if (result.Residue.Count > 0)
        {
            notes.Add(
                $"DO NOT EXPORT THIS TEXT. After redacting, Silueta read its own output back and still " +
                $"found {result.Residue.Count} identifier(s) in it — an invented name collided with " +
                "someone real in this record, or a replacement joined the words around it to spell one. " +
                "Report this rather than passing the text on.");
        }

        if (result.Manifest.DeparturesFromSafeHarbor.Count > 0)
        {
            // Second, straight after a refusal to export if there is one, because it changes what every other
            // sentence here means: the measured leak rate was taken under Safe Harbor, and this run was not.
            notes.Add(
                $"This run used the policy '{result.Manifest.Policy}' (version {result.Manifest.PolicyVersion}), " +
                $"not Safe Harbor. It departs from it in: {string.Join("; ", result.Manifest.DeparturesFromSafeHarbor)}. " +
                "Under HIPAA, text redacted this way is not de-identified unless a qualified expert determines it " +
                "is (an expert determination, 45 CFR § 164.514(b)(1)); outside HIPAA it is the organisation's own " +
                "risk assessment. The measured leak rate below was taken under Safe Harbor.");
        }

        notes.Add(MeasuredRate);

        if (context.Known.Count == 0)
        {
            // Two sentences, because the rule that finds relatives by their relationship runs without a roster
            // too, and "every name survived" would no longer be true of "my daughter Linda" — while being
            // exactly as true as it ever was of everybody else.
            notes.Add(result.Manifest.RelativesRule == "off"
                ? "No roster was given, so only the pattern rules ran: phone, e-mail, URL, IP, record " +
                  "numbers, dates and ages over 89. EVERY NAME IN THIS TRANSCRIPT SURVIVED."
                : "No roster was given, so only the pattern rules ran — phone, e-mail, URL, IP, record " +
                  "numbers, dates and ages over 89 — and the rule for people named through a relationship " +
                  "(\"my daughter Linda\"). EVERY OTHER NAME IN THIS TRANSCRIPT SURVIVED.");
        }

        if (result.Applied.Count == 0)
        {
            notes.Add("Nothing was replaced. Check that the roster describes the people in this record.");
        }

        if (result.Manifest.UnrosteredPeople > 0)
        {
            notes.Add(
                $"{result.Manifest.UnrosteredPeople} person(s) were named in the transcript only through a " +
                "relationship (\"my daughter Linda\") and were not on the roster. Every mention of them was " +
                "replaced with a label rather than an invented name, because nobody gave them a subject: they are " +
                "de-identified, and they cannot be followed across the corpus. A relative named any other way " +
                "(\"Linda, my daughter\") is not found by this rule.");
        }

        if (result.Manifest.AmbiguousAttributions > 0)
        {
            notes.Add(
                $"{result.Manifest.AmbiguousAttributions} span(s) were replaced with a label rather than an " +
                "invented name, because two names crossed there or two people share one: nothing in the run " +
                "could say whose mention it was. Those spans are de-identified but no longer follow their " +
                "subject across the corpus.");
        }

        notes.Add(
            "No rule finds a street address or a city yet, a postal code is found only after \"ZIP\" or " +
            "\"código postal\", and numbers spoken as words (\"five five five, oh one four seven\") are not " +
            "recognised. The postal-code table is the United States census: a five-digit code from another " +
            "country is widened against it, and three of its digits may stay.");

        return notes;
    }

    /// <inheritdoc cref="For"/>
    public static string Paragraph(DeidentificationContext context, RedactionResult result) =>
        string.Join(" ", For(context, result));

    /// <summary>
    /// What this build knows about how often it fails, in the words a reader needs beside a result — or, when
    /// no measurement is embedded, the admission that there is none. Never a figure written here.
    /// </summary>
    public static string MeasuredRate =>
        PublishedLeakRate.Current is { Shipped: not null } measured
            ? $"Silueta's leak rate is measured only on a small synthetic corpus ({measured.CorpusId}, " +
              $"{measured.Documents} documents, measured {measured.MeasuredOn}): {measured.Shipped!.RateInScope} " +
              "still said something of a kind this build has a way to find, and " +
              $"{measured.Shipped!.Rate} still said something of any kind the annotators marked. This " +
              "output is not verified to be de-identified. Names nobody wrote down — nicknames, a " +
              "neighbour, a doctor named once, a relative named any way but straight after the " +
              "relationship — are invisible to the roster matcher and survive."
            : "This build carries no measured leak rate at all, so nothing here says how often Silueta " +
              "leaves an identifier behind, and this output is not verified to be de-identified. Names " +
              "nobody wrote down — nicknames, a neighbour, a doctor named once, a relative named any way " +
              "but straight after the relationship — are invisible to the roster matcher and survive.";

    /// <summary>
    /// The kinds this build has no way to find at all: no pattern rule, and nothing a roster can hold for
    /// them. Derived from the pack that is actually loaded, so a rule added tomorrow leaves this list on its
    /// own. Safe Harbor names these and the library does not deliver them yet, which is a thing to read
    /// before trusting a result, not after.
    /// </summary>
    public static IReadOnlyList<IdentifierKind> NotCoveredByAnyRule(SiluetaLineage? lineage = null)
    {
        IReadOnlyCollection<IdentifierKind> covered = (lineage ?? SiluetaLineage.Default).CreatePatternDetector().Kinds;

        return
        [
            .. Enum.GetValues<IdentifierKind>()
                .Where(kind => !covered.Contains(kind) && !kind.IsPersonName())
                // A company and a product are found from the roster, and "Other" is the kind a caller
                // reaches for when none of the others fit — none of the three is a gap in the rules.
                .Where(kind => kind is not (IdentifierKind.Organization or IdentifierKind.Product or IdentifierKind.Other))
                .Order(),
        ];
    }
}
