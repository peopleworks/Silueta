using System.Text;

namespace Silueta.Core;

/// <summary>Reads a transcript incrementally, honoring BOM encoding detection and the engine's
/// character ceiling. An oversized file is refused without first allocating the whole input.</summary>
public static class TranscriptReader
{
    public static string Read(string path, RedactionLimits? limits = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        limits ??= RedactionLimits.Default;
        limits.Validate();
        using var reader = new StreamReader(path);
        var text = new StringBuilder();
        var buffer = new char[4_096];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int allowance = (int)Math.Min(buffer.Length, (long)limits.MaxInputCharacters - text.Length + 1);
            int read = reader.Read(buffer, 0, allowance);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0)
            {
                return text.ToString();
            }
            if ((long)text.Length + read > limits.MaxInputCharacters)
            {
                throw new RedactionLimitException("input characters", limits.MaxInputCharacters);
            }
            text.Append(buffer, 0, read);
        }
    }
}
