namespace Silueta.Core;

/// <summary>
/// One way of finding identifiers in a transcript. Detectors are deliberately small and stackable: the
/// deterministic ones (known values, patterns) carry the load, and a model-backed one can be added later
/// without the pipeline, the policy or the measurement changing shape.
/// </summary>
public interface IDetector
{
    /// <summary>Stable id, recorded on every detection so the manifest can say who found what.</summary>
    string Id { get; }

    IEnumerable<Detection> Detect(string text, DeidentificationContext context);
}
