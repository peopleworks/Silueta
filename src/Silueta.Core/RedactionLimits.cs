namespace Silueta.Core;

/// <summary>Resource ceilings, not detection rules. Exceeding one fails the whole run; text and
/// candidates are never silently truncated. Character counts are UTF-16 units, like detection offsets.</summary>
public sealed record RedactionLimits
{
    public static RedactionLimits Default { get; } = new();

    public int MaxInputCharacters { get; init; } = 1_000_000;

    /// <summary>All candidates, including overlaps and candidates the policy will keep, per pass.</summary>
    public int MaxDetections { get; init; } = 100_000;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInputCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDetections);
    }

    internal void Add(List<Detection> detections, Detection detection)
    {
        if (detections.Count >= MaxDetections)
        {
            throw new RedactionLimitException("candidate detections", MaxDetections);
        }
        detections.Add(detection);
    }
}

/// <summary>A resource ceiling was exceeded. Contains no text, path, subject or inner exception.</summary>
public sealed class RedactionLimitException : Exception
{
    public RedactionLimitException(string resource, int limit)
        : base($"Redaction exceeded its {resource} limit ({limit}). No complete result is available.")
    {
        Resource = resource;
        Limit = limit;
    }

    public string Resource { get; }
    public int Limit { get; }
}
