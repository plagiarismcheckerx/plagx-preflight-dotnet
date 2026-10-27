namespace Plagx.Preflight;

/// <summary>
/// Plagx Preflight: a draft checker with basic grammar suggestions. A free academic writing
/// utility by Plagiarism Checker X. Runs fully offline; results match the JavaScript reference exactly.
/// </summary>
public static class Preflight
{
    /// <summary>Library version.</summary>
    public const string Version = "0.1.3";

    /// <summary>Check groups. <c>"passive-voice"</c> is off by default.</summary>
    public static readonly IReadOnlyList<string> AllChecks = new[] { "length", "references", "punctuation", "grammar", "passive-voice" };

    /// <summary>Checks run when <see cref="PreflightOptions.Checks"/> is null.</summary>
    public static readonly IReadOnlyList<string> DefaultChecks = new[] { "length", "references", "punctuation", "grammar" };

    /// <summary>Citation styles.</summary>
    public static readonly IReadOnlyList<string> Styles = Citations.Styles;

    private static readonly Dictionary<string, Action<Context>> Runners = new()
    {
        ["length"] = Checks.Length,
        ["references"] = Checks.References,
        ["punctuation"] = Checks.Punctuation,
        ["grammar"] = Checks.Grammar,
        ["passive-voice"] = Checks.PassiveVoice,
    };

    /// <summary>Checks a draft and returns stats plus a sorted list of suggestions.</summary>
    /// <exception cref="ArgumentException">An option is invalid.</exception>
    public static DraftResult CheckDraft(string text, PreflightOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var opts = Normalize(options ?? new PreflightOptions());
        var doc = Text.Structure(text);
        var ctx = new Context(text, opts, doc, Stats(text, doc));
        foreach (var c in opts.Checks) Runners[c](ctx);

        var starts = Text.LineStarts(text);
        var issues = ctx.Issues
            .Select(x =>
            {
                var line = Text.LineIndex(starts, x.Start);
                return new DraftIssue(x.Level, x.Code, x.Message, x.Start, x.End, line + 1, x.Start - starts[line] + 1, text[x.Start..x.End], x.Suggestion);
            })
            .OrderBy(i => i.Start).ThenBy(i => i.Code, StringComparer.Ordinal).ThenBy(i => i.End)
            .ToList();
        return new DraftResult(ctx.Stats, issues.Count(i => i.Level == "error"), issues.Count(i => i.Level == "warning"), issues.Count(i => i.Level == "info"), issues);
    }

    /// <summary>Number of words in the text.</summary>
    public static int CountWords(string text) => Text.CountWords(text);

    /// <summary>Sentences with UTF-16 offsets. Headings count as one sentence each.</summary>
    public static IReadOnlyList<Sentence> SplitSentences(string text) =>
        Text.SplitSentences(text).Select(s => new Sentence(s.Text, s.Start, s.End)).ToList();

    /// <summary>In-text citations with UTF-16 offsets.</summary>
    /// <param name="text">Text to search.</param>
    /// <param name="style"><c>"apa"</c> (default) or <c>"mla"</c>.</param>
    public static IReadOnlyList<Citation> FindCitations(string text, string style = "apa") => Citations.Find(text, style);

    private static Options Normalize(PreflightOptions o)
    {
        if (!Styles.Contains(o.Style)) throw new ArgumentException($"Unknown style \"{o.Style}\". Use: {string.Join(", ", Styles)}", nameof(o.Style));
        var list = o.Checks ?? DefaultChecks;
        foreach (var c in list)
        {
            if (!AllChecks.Contains(c)) throw new ArgumentException($"Unknown check \"{c}\". Use: {string.Join(", ", AllChecks)}", nameof(o.Checks));
        }
        if (o.MinWords < 0) throw new ArgumentException("MinWords must be a whole number", nameof(o.MinWords));
        if (o.MaxWords < 0) throw new ArgumentException("MaxWords must be a whole number", nameof(o.MaxWords));
        return new Options(o.MinWords, o.MaxWords, o.Style, AllChecks.Where(list.Contains).ToList(), o.RequiredHeadings.ToList());
    }

    /// <summary>JavaScript Math.round: halves round up, towards +infinity.</summary>
    private static double JsRound(double v)
    {
        var r = Math.Floor(v);
        return v - r >= 0.5 ? r + 1 : r;
    }

    private static DraftStats Stats(string text, Document doc)
    {
        var bodyWords = Text.CountWords(text[..doc.BodyEnd]);
        int sentences = 0, sentenceWords = 0, syl = 0, paras = 0;
        foreach (var p in doc.Body)
        {
            if (p.Heading) continue;
            paras += 1;
            sentences += Text.ParagraphSentences(p).Count;
            foreach (var w in Text.Words(p.Text))
            {
                sentenceWords += 1;
                syl += Text.Syllables(w.Text);
            }
        }
        var flesch = 0.0;
        if (sentences > 0 && sentenceWords > 0)
        {
            flesch = JsRound((206.835 - 1.015 * ((double)sentenceWords / sentences) - 84.6 * ((double)syl / sentenceWords)) * 10) / 10;
            if (flesch == 0) flesch = 0; // no negative zero
        }
        return new DraftStats(bodyWords, sentences, paras, Js.CodePoints(text), (int)Math.Ceiling(bodyWords / 200.0), flesch);
    }
}
