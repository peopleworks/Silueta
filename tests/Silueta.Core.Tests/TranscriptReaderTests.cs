using System.Text;
using Silueta.Core;

namespace Silueta.Core.Tests;

public sealed class TranscriptReaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("silueta-read-").FullName;
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_ceiling_counts_decoded_characters_with_BOM_detection(bool utf16)
    {
        string path = Path.Combine(_directory, "note.txt");
        const string text = "ñ😀";
        File.WriteAllText(path, text, utf16 ? Encoding.Unicode : new UTF8Encoding(true));

        Assert.Equal(text, TranscriptReader.Read(path, new() { MaxInputCharacters = text.Length }));
        var error = Assert.Throws<RedactionLimitException>(() =>
            TranscriptReader.Read(path, new() { MaxInputCharacters = text.Length - 1 }));
        Assert.DoesNotContain(text, error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Chunked_reads_preserve_text_exactly_at_the_limit()
    {
        string path = Path.Combine(_directory, "note.txt");
        string text = new string('a', 4_095) + "😀\r\n" + new string('b', 5_000);
        File.WriteAllText(path, text);
        Assert.Equal(text, TranscriptReader.Read(path, new() { MaxInputCharacters = text.Length }));
    }

    [Fact]
    public void Pre_cancelled_reads_do_not_open_the_path()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => TranscriptReader.Read(
            Path.Combine(_directory, "does-not-exist.txt"), cancellationToken: source.Token));
    }
}
