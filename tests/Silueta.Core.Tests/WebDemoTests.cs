using System.Text.RegularExpressions;
using Silueta.Core;

namespace Silueta.Core.Tests;

/// <summary>
/// The browser demo makes two promises on its own front page, and both are the kind that rot quietly.
/// <para>
/// The first is that nothing a visitor types leaves the machine. That is true because the whole library is
/// compiled into the WebAssembly the browser downloaded and there is nothing to send anything with — a
/// property of the project file and the host, not of the sentence claiming it. One <c>HttpClient</c> added
/// later for a "small" feature, or one font pulled from a CDN, and the page is lying while still saying so.
/// </para>
/// <para>
/// The second is that every number on it is measured rather than typed. This repository has spent days
/// hunting second copies of its own figures; a page that hard-codes "76.7%" is the next one, and it would
/// go stale at the next slice of the matcher while looking perfectly convincing.
/// </para>
/// </summary>
public class WebDemoTests
{
    private static readonly string Web = Path.Combine(Repo.Root, "src", "Silueta.Web");

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Web, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal)
                     || f.EndsWith(".cs", StringComparison.Ordinal)
                     || f.EndsWith(".html", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void The_demo_exists_where_this_guard_is_looking()
    {
        // Without this the rest pass vacuously the day somebody renames the project.
        Assert.True(Directory.Exists(Web), $"No web project at {Web}, so every check below proves nothing.");
        Assert.Contains(SourceFiles(), f => f.EndsWith("Demo.razor", StringComparison.Ordinal));
    }

    [Fact]
    public void No_figure_on_the_page_is_typed_into_the_page()
    {
        // A percentage or an interval written by hand. Every number the demo shows has to come from the
        // run in front of it or from the measurement embedded in the build.
        // Not preceded by a quote, so a format string is not mistaken for a claim on the page.
        var typedNumber = new Regex(@"(?<![""'])\d{1,3}\.\d\s?%|recall\s+0\.\d{3}", RegexOptions.CultureInvariant);

        foreach (string file in SourceFiles())
        {
            string text = File.ReadAllText(file);
            Match match = typedNumber.Match(text);
            Assert.False(
                match.Success,
                $"{Path.GetFileName(file)} contains the figure \"{match.Value}\". Read it from PublishedLeakRate " +
                "or from the result instead: a number typed onto the page is the copy that goes stale.");
        }
    }

    [Fact]
    public void Nothing_on_the_page_can_send_anything_anywhere()
    {
        // Real use, not the word: Program.cs says in a comment that there is deliberately no HttpClient
        // here, and a guard that banned the word would ban the explanation along with the thing.
        string[] ways = ["new HttpClient", "AddHttpClient", "IHttpClientFactory", "System.Net.Http", "fetch("];

        foreach (string file in SourceFiles())
        {
            string text = File.ReadAllText(file);
            foreach (string way in ways)
            {
                Assert.False(
                    text.Contains(way, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} uses {way}. The page promises that nothing a visitor types " +
                    "leaves the machine, and that promise is kept by there being nothing here to send it with.");
            }
        }

        string project = File.ReadAllText(Path.Combine(Web, "Silueta.Web.csproj"));
        Assert.DoesNotContain("Http", project, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_loads_nothing_from_another_origin()
    {
        // A font or a stylesheet from a CDN is a request carrying a Referer, made by a page whose entire
        // argument is that it makes none.
        string index = File.ReadAllText(Path.Combine(Web, "wwwroot", "index.html"));

        Assert.DoesNotMatch(new Regex(@"(src|href)=""https?://", RegexOptions.IgnoreCase), index);
    }

    [Fact]
    public void The_page_says_the_thing_the_other_tests_are_protecting()
    {
        // The claim and its guards have to stay together: if the sentence goes, these tests are enforcing
        // a promise nobody is making, and if the sentence stays it had better be true.
        string page = File.ReadAllText(Path.Combine(Web, "Pages", "Demo.razor"));

        Assert.Contains("leaves this page", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_samples_carry_their_own_rosters_and_none_of_them_is_the_gold_corpus()
    {
        // The corpus is thirty annotated documents whose purpose is to be evaluated. Bundling it into a
        // web page would be a sentence a reviewer is right to misread, quite apart from what it would do
        // to the download.
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Web, "*.*", SearchOption.AllDirectories),
            f => f.Contains("corpus", StringComparison.OrdinalIgnoreCase));
    }
}
