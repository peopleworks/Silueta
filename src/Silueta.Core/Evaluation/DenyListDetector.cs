namespace Silueta.Core;

/// <summary>
/// A baseline, not a detector anyone should ship: the roster, matched literally.
/// <para>
/// It is what anyone can build in ten minutes with a general PII tool and the list they already hold, and
/// the distance between it and Silueta is the value of the phonetic thesis — measured, not asserted. So it
/// has to be exactly that dumb and no dumber. It folds case and accents (a literal match that missed
/// "SOFIA" for "Sofía" would be a straw man, and beating a straw man proves nothing), it matches whole
/// words (so "Ana" is not found inside "banana"), and it registers first names the way the roster does.
/// It hears nothing: "Ellenor Vasques" is not "Eleanor Vasquez" to it, which is the whole point.
/// </para>
/// </summary>
public sealed class DenyListDetector : IDetector
{
    public string Id => "deny-list";

    public IEnumerable<Detection> Detect(string text, DeidentificationContext context)
    {
        var results = new List<Detection>();
        if (string.IsNullOrEmpty(text) || context.Known.Count == 0)
        {
            return results;
        }

        List<Token> tokens = Tokenizer.Tokenize(text);

        foreach (KnownIdentifier known in context.Known)
        {
            string[] words = [.. Tokenizer.Tokenize(known.Value).Select(t => t.Text)];
            if (words.Length == 0)
            {
                continue;
            }

            for (int i = 0; i + words.Length <= tokens.Count; i++)
            {
                bool all = true;
                for (int k = 0; k < words.Length && all; k++)
                {
                    all = Folding.SameLetters(tokens[i + k].Text, words[k]);
                }

                if (all)
                {
                    int start = tokens[i].Start;
                    int end = tokens[i + words.Length - 1].End;
                    results.Add(new Detection(start, end - start, known.Kind, Id, 1.0, known.SubjectId, MatchKind.Exact));
                }
            }
        }

        return results;
    }
}
