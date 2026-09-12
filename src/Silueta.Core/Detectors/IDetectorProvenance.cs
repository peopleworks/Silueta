namespace Silueta.Core;

/// <summary>
/// What a detector can say about itself for the manifest. Optional, and separate from
/// <see cref="IDetector"/> so a third-party detector is not forced to implement it.
/// <para>
/// The policy was fingerprinted and the rules that do the finding were not, so two corpora could carry
/// the same policy fingerprint and have been searched with different rules. And a rule the build could
/// not load was skipped in silence, which made "that rule found nothing" and "that rule never ran"
/// indistinguishable in the one file meant to record the method.
/// </para>
/// </summary>
public interface IDetectorProvenance
{
    /// <summary>A digest of the rules this detector is carrying. Must not contain any rule's input.</summary>
    string Fingerprint { get; }

    /// <summary>How many rules loaded.</summary>
    int RulesLoaded { get; }

    /// <summary>The ids of rules this build could not load. Ids only — never a pattern's input.</summary>
    IReadOnlyList<string> RulesSkipped { get; }
}
