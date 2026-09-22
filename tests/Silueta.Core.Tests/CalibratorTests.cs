using Silueta.Calibration;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// What the calibration tool writes into the README, held to what it renders today.
/// <para>
/// These lived in <see cref="PublishedLeakRateTests"/> and were moved out when Core began targeting .NET 9
/// as well as .NET 10. The tool is a program and stays on .NET 10; the published measurement is part of
/// the library and has to be checked on both. Keeping them in one file would have taken the embedded
/// number's own tests off .NET 9 with them — which is the runtime where a different number would first
/// show up if the library depended on something the runtime changes.
/// </para>
/// </summary>
public class CalibratorTests
{
    private static readonly string Root = Repo.Root;

    private static EvaluationReport Evaluate() => Evaluation.Run(
        GoldCorpus.Load(Path.Combine(Root, "corpus-synthetic", "tts-asr")),
        EvaluationConfiguration.Silueta,
        EvaluationConfiguration.DenyList);

    [Fact]
    public void The_README_block_is_what_the_tool_that_writes_it_renders_today()
    {
        PublishedLeakRate published = Assert.IsType<PublishedLeakRate>(PublishedLeakRate.Current);
        string readme = File.ReadAllText(Path.Combine(Root, "README.md"));

        string? block = Calibrator.ReadBlock(readme, "leak-rate");
        Assert.NotNull(block);

        // Not "contains each number" — that is the other test, and it passes over a block somebody edited by
        // hand into a different shape. This one says the page is the tool's output, so the generator stays
        // the single place that decides how the published measurement is written.
        Assert.Equal(Calibrator.RenderTable(Evaluate(), published), block);
    }

    [Fact]
    public void Publishing_into_a_page_with_no_markers_refuses_instead_of_appending()
    {
        // The alternative is a tool that writes the number somewhere nobody reads and reports success.
        Assert.Throws<InvalidOperationException>(
            () => Calibrator.ReplaceBlock("# A README with no markers in it\n", "leak-rate", "| x |"));
    }
}
