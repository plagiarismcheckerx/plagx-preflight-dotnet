using System.Text.RegularExpressions;

namespace Plagx.Preflight;

/// <summary>State shared by the checks for one <see cref="Preflight.CheckDraft"/> call.</summary>
internal sealed class Context
{
    // Codes inside URLs, email addresses and `code` are skipped.
    private static readonly Regex Masked = Js.Re(
        "^(?:unmatched-|double-space|space-before|missing-space|repeated-|mixed-quote|a-an|sentence-capital|lowercase-i|common-confusion|passive-voice)");
    private static readonly Regex MaskRe = Js.Re(
        $"{Js.B}https?://[^{Js.WsClass}<>()]+|{Js.B}www\\.[^{Js.WsClass}<>()]+|[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\\.[A-Za-z0-9-]+)+|`[^`\\n]*`");

    private readonly List<(int, int)> masks;
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    public readonly List<(string Level, string Code, string Message, int Start, int End, string? Suggestion)> Issues = new();

    public Context(string text, Options opts, Document doc, DraftStats stats)
    {
        Text = text;
        Opts = opts;
        Doc = doc;
        Stats = stats;
        BodyText = text[..doc.BodyEnd];
        masks = MaskRe.Matches(text).Select(m => (m.Index, m.Index + m.Length)).ToList();
    }

    public string Text { get; }
    public Options Opts { get; }
    public Document Doc { get; }
    public DraftStats Stats { get; }
    public string BodyText { get; }
    public IReadOnlyList<Paragraph> Body => Doc.Body;

    public void Add(string level, string code, int start, int end, string message, string? suggestion = null)
    {
        if (Masked.IsMatch(code) && masks.Any(m => start < m.Item2 && end > m.Item1)) return;
        if (!seen.Add($"{code}:{start}:{end}:{message}")) return;
        Issues.Add((level, code, message, start, end, suggestion));
    }

    public void AddDoc(string level, string code, string message) => Add(level, code, 0, 0, message);
}

/// <summary>Normalised options.</summary>
internal sealed record Options(int? MinWords, int? MaxWords, string Style, IReadOnlyList<string> Checks, IReadOnlyList<string> RequiredHeadings);

/// <summary>The individual check groups.</summary>
internal static class Checks
{
    public const int LongSentenceWords = 40;
    public const int LongParagraphWords = 250;

    private static readonly Regex UpperStart = Js.Re($"^{Js.Lu()}");

    private static string MatchCase(string fix, string original) => UpperStart.IsMatch(original) ? Js.UpperFirst(fix) : fix;

    private static bool StartsWithAny(string w, string[] prefixes) => prefixes.Any(p => w.StartsWith(p, StringComparison.Ordinal));

    // ---------------------------------------------------------------- length

    private static readonly Regex ListItem = Js.Re($"^(?:[-*+\\u2022]|[0-9]+[.)]){Js.Ws}");

    public static void Length(Context ctx)
    {
        var words = ctx.Stats.Words;
        var o = ctx.Opts;
        if (o.MinWords is int min && words < min)
            ctx.AddDoc("warning", "word-count-low", $"Suggestion: the draft has {words} words; the minimum is {min}.");
        if (o.MaxWords is int max && words > max)
            ctx.AddDoc("warning", "word-count-high", $"Suggestion: the draft has {words} words; the maximum is {max}.");
        foreach (var p in ctx.Body)
        {
            if (p.Heading) continue;
            var sentences = Text.ParagraphSentences(p);
            foreach (var s in sentences)
            {
                var n = Text.CountWords(s.Text);
                if (n > LongSentenceWords)
                    ctx.Add("warning", "long-sentence", s.Start, s.End, $"Suggestion: this sentence has {n} words; consider splitting it (over {LongSentenceWords}).");
            }
            var pn = Text.CountWords(p.Text);
            if (pn > LongParagraphWords)
                ctx.Add("info", "long-paragraph", p.Start, p.End, $"Suggestion: this paragraph has {pn} words; consider splitting it (over {LongParagraphWords}).");
            if (sentences.Count == 1 && !ListItem.IsMatch(p.Text))
                ctx.Add("info", "single-sentence-paragraph", p.Start, p.End, "Suggestion: this paragraph has only one sentence.");
        }
        var headings = ctx.Doc.Paragraphs.Where(p => p.Heading).Select(p => Text.NormalizeHeading(p.Text)).ToHashSet(StringComparer.Ordinal);
        foreach (var h in o.RequiredHeadings)
        {
            if (!headings.Contains(Text.NormalizeHeading(h))) ctx.AddDoc("warning", "missing-heading", $"Suggestion: add a \"{h}\" heading.");
        }
    }

    // ---------------------------------------------------------------- references

    private static readonly Regex EtAl = Js.Re($"{Js.B}et\\.?{Js.Ws}*al{Js.B}\\.*", ignoreCase: true);

    public static void References(Context ctx)
    {
        var doc = ctx.Doc;
        var cites = Citations.Find(ctx.BodyText, ctx.Opts.Style);
        if (cites.Count > 0 && doc.ReferenceHeading is null)
            ctx.AddDoc("error", "missing-reference-section", "Suggestion: the draft has citations but no reference list heading (References or Works Cited).");
        var entries = doc.Entries.Select(Citations.ParseEntry).ToList();
        if (doc.ReferenceHeading is not null)
        {
            foreach (var c in cites)
            {
                if (!entries.Any(e => Citations.Matches(c, e)))
                    ctx.Add("error", "citation-no-reference", c.Start, c.End, $"Suggestion: \"{c.Text}\" has no matching entry in the reference list.");
            }
            foreach (var e in entries)
            {
                if (!cites.Any(c => Citations.Matches(c, e)))
                    ctx.Add("warning", "reference-not-cited", e.Start, e.End, "Suggestion: this reference is never cited in the text.");
            }
        }
        foreach (Match m in EtAl.Matches(ctx.BodyText))
        {
            if (Js.Lower(m.Value) != "et al.")
                ctx.Add("warning", "et-al-format", m.Index, m.Index + m.Length, "Suggestion: write \"et al.\" with one period.", "et al.");
        }
    }

    // ---------------------------------------------------------------- punctuation

    private static readonly Regex ListMarkerBefore = Js.Re($"(?:^|{Js.Ws})[A-Za-z0-9]{{1,3}}\\z");
    private static readonly Regex DoubleSpace = Js.Re($"(?<={Js.NotWs}) {{2,}}(?={Js.NotWs})");
    private static readonly Regex SpaceBefore = Js.Re($"(?<={Js.L("0-9)\\]\"\\u201D")}) +([,;:!?.])(?={Js.Ws}|\\z)");
    private static readonly Regex MissingSpace = Js.Re("[,;:!?](?=[A-Za-z])|(?<=[a-z]{2})\\.(?=[A-Z][a-z])");
    private static readonly Regex Repeated = Js.Re("([!?,;:])\\1+|(?<!\\.)\\.\\.(?!\\.)");
    private const string Unmatched = "Suggestion: this quotation mark has no matching pair.";

    public static void Punctuation(Context ctx)
    {
        int straight = -1, curly = -1;
        foreach (var p in ctx.Body)
        {
            var t = p.Text;
            var at = p.Start;

            // Quotation marks
            var quotes = new List<int>();
            for (var i = 0; i < t.Length; i += 1) if (t[i] == '"') quotes.Add(i);
            if (quotes.Count > 0 && straight < 0) straight = at + quotes[0];
            if (quotes.Count % 2 == 1)
            {
                var i = quotes[^1];
                ctx.Add("warning", "unmatched-quote", at + i, at + i + 1, Unmatched);
            }
            var open = new List<int>();
            for (var i = 0; i < t.Length; i += 1)
            {
                if ((t[i] == '“' || t[i] == '”') && curly < 0) curly = at + i;
                if (t[i] == '“') open.Add(i);
                else if (t[i] == '”')
                {
                    if (open.Count > 0) open.RemoveAt(open.Count - 1);
                    else ctx.Add("warning", "unmatched-quote", at + i, at + i + 1, Unmatched);
                }
            }
            foreach (var i in open) ctx.Add("warning", "unmatched-quote", at + i, at + i + 1, Unmatched);

            // Brackets
            var stack = new List<int>();
            for (var i = 0; i < t.Length; i += 1)
            {
                var c = t[i];
                if (c is '(' or '[' or '{') stack.Add(i);
                else if (c is ')' or ']' or '}')
                {
                    var pair = c == ')' ? '(' : c == ']' ? '[' : '{';
                    if (stack.Count > 0 && t[stack[^1]] == pair)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }
                    else
                    {
                        // List markers such as "a)", "ii)" or "1)".
                        if (c == ')' && ListMarkerBefore.IsMatch(t[..i])) continue;
                        ctx.Add("warning", "unmatched-bracket", at + i, at + i + 1, $"Suggestion: \"{c}\" has no matching bracket.");
                    }
                }
            }
            foreach (var i in stack) ctx.Add("warning", "unmatched-bracket", at + i, at + i + 1, $"Suggestion: \"{t[i]}\" has no matching bracket.");

            foreach (Match m in DoubleSpace.Matches(t))
                ctx.Add("info", "double-space", at + m.Index, at + m.Index + m.Length, "Suggestion: use a single space.", " ");
            foreach (Match m in SpaceBefore.Matches(t))
                ctx.Add("warning", "space-before-punctuation", at + m.Index, at + m.Index + m.Length,
                    $"Suggestion: remove the space before \"{m.Groups[1].Value}\".", m.Groups[1].Value);
            foreach (Match m in MissingSpace.Matches(t))
                ctx.Add("warning", "missing-space-after-punctuation", at + m.Index, at + m.Index + 1,
                    $"Suggestion: add a space after \"{m.Value}\".", $"{m.Value} ");
            foreach (Match m in Repeated.Matches(t))
                ctx.Add("warning", "repeated-punctuation", at + m.Index, at + m.Index + m.Length,
                    $"Suggestion: \"{m.Value}\" repeats punctuation.", m.Value[..1]);
        }
        if (straight >= 0 && curly >= 0)
        {
            var i = Math.Max(straight, curly);
            ctx.Add("info", "mixed-quote-styles", i, i + 1, "Suggestion: the draft mixes straight and curly quotation marks.");
        }
    }

    // ---------------------------------------------------------------- grammar

    private static readonly string[] SilentH = { "hour", "honest", "honor", "honour", "heir" };
    private static readonly string[] ConsonantSound = { "uni", "use", "usa", "usu", "uti", "ure", "uro", "urani", "eu", "ewe", "one", "once", "ubiq", "unanim" };
    private static readonly string[] Uncertain = { "herb", "histor", "hotel", "unin", "unim", "unid", "oner" };

    private static Regex Ci(string pattern) => Js.Re(pattern.Replace("\\b", Js.B), ignoreCase: true);

    private static readonly (Regex Re, string Fix)[] Confusions =
    {
        (Ci("\\b(could|would|should|must|might) of\\b"), "{0} have"),
        (Ci("\\bits (a|an|been|not|going)\\b"), "it's {0}"),
        (Ci("\\bit['\\u2019]s own\\b"), "its own"),
        (Ci("\\b(more|less|better|worse|rather|greater|fewer|larger|smaller|higher|lower|faster|slower|easier|harder) then\\b"), "{0} than"),
        (Ci("\\btheir (is|are|was|were)\\b"), "there {0}"),
        (Ci("\\bthere own\\b"), "their own"),
        (Ci("\\b(the|an) affect\\b"), "{0} effect"),
        (Ci("\\b(may|might|will|can|could|would|should|negatively|positively|greatly|significantly) effect\\b"), "{0} affect"),
        (Ci("\\bloose (weight|track|control)\\b"), "lose {0}"),
        (Ci("\\balot\\b"), "a lot"),
    };

    private static readonly Regex Passive = Ci(
        $"\\b(?:am|is|are|was|were|be|been|being){Js.Ws}+(?:[a-z]+ly{Js.Ws}+)?(?:[a-z]{{3,}}ed|written|done|made|given|taken|shown|seen|known|found|held|chosen|built|drawn|taught|thought|brought|said|told|sent|kept|paid|begun|broken|driven|eaten|forgotten|hidden|spoken|stolen|worn)\\b");
    private static readonly Regex IForm = Js.Re("^i(['\\u2019](?:m|ve|ll|d))?\\z");
    private static readonly Regex Gap = Js.Re($"^{Js.Ws}+\\z");
    private static readonly Regex HasLetter = Js.Re(Js.L());
    private static readonly Regex LowerStart = Js.Re($"^{Js.Ll()}");
    private static readonly Regex HasUpper = Js.Re(Js.Lu());
    private static readonly Regex AsciiDigit = Js.Re("[0-9]");
    private static readonly Regex AsciiLowerStart = Js.Re("^[a-z]");
    private static readonly Regex VowelStart = Js.Re("^[aeiou]");

    public static void Grammar(Context ctx)
    {
        foreach (var p in ctx.Body)
        {
            var t = p.Text;
            var toks = Text.Words(t);
            for (var i = 0; i < toks.Count; i += 1)
            {
                var tok = toks[i];
                var low = Js.Lower(tok.Text);
                Part? next = i + 1 < toks.Count && Gap.IsMatch(t[tok.End..toks[i + 1].Start]) ? toks[i + 1] : null;

                if (next is not null && Js.Lower(next.Text) == low && HasLetter.IsMatch(low) && low != "had" && low != "that")
                    ctx.Add("warning", "repeated-word", p.Start + tok.Start, p.Start + next.End,
                        $"Suggestion: \"{tok.Text} {next.Text}\" looks like a repeated word.", tok.Text);

                if (next is not null && (low == "a" || low == "an"))
                {
                    var nw = next.Text.Split('-')[0];
                    var nl = Js.Lower(nw);
                    var acronym = nw.Length > 1 && nw == Js.Upper(nw);
                    if (nw.Length > 1 && !AsciiDigit.IsMatch(nw) && !acronym && AsciiLowerStart.IsMatch(nl) && !StartsWithAny(nl, Uncertain))
                    {
                        var vowel = StartsWithAny(nl, SilentH) || (VowelStart.IsMatch(nl) && !StartsWithAny(nl, ConsonantSound));
                        var expected = vowel ? "an" : "a";
                        if (low != expected)
                            ctx.Add("warning", "a-an", p.Start + tok.Start, p.Start + tok.End,
                                $"Suggestion: use \"{expected}\" before \"{next.Text}\".", MatchCase(expected, tok.Text));
                    }
                }

                var im = IForm.Match(tok.Text);
                if (im.Success)
                {
                    var before = tok.Start > 0 ? t[tok.Start - 1] : '\0';
                    var after = tok.End < t.Length ? t[tok.End] : '\0';
                    if (im.Groups[1].Success || (after != '.' && after != ')' && before != '.'))
                    {
                        var fix = Js.UpperFirst(tok.Text);
                        ctx.Add("warning", "lowercase-i", p.Start + tok.Start, p.Start + tok.End, $"Suggestion: use \"{fix}\" instead of \"{tok.Text}\".", fix);
                    }
                }
            }

            if (!p.Heading)
            {
                foreach (var s in Text.ParagraphSentences(p))
                {
                    var first = Text.Words(s.Text).FirstOrDefault();
                    if (first is not null && first.Start == 0 && LowerStart.IsMatch(first.Text) && !HasUpper.IsMatch(first.Text)
                        && !IForm.IsMatch(first.Text) && (first.End >= s.Text.Length || s.Text[first.End] != ')'))
                        ctx.Add("warning", "sentence-capital", s.Start, s.Start + first.End, "Suggestion: start the sentence with a capital letter.", Js.UpperFirst(first.Text));
                }
            }

            foreach (var (re, fix) in Confusions)
            {
                foreach (Match m in re.Matches(t))
                {
                    var suggestion = MatchCase(string.Format(fix, m.Groups.Count > 1 ? m.Groups[1].Value : ""), m.Value);
                    ctx.Add("warning", "common-confusion", p.Start + m.Index, p.Start + m.Index + m.Length,
                        $"Suggestion: \"{m.Value}\" may be \"{suggestion}\".", suggestion);
                }
            }
        }
    }

    public static void PassiveVoice(Context ctx)
    {
        foreach (var p in ctx.Body)
        {
            foreach (Match m in Passive.Matches(p.Text))
                ctx.Add("info", "passive-voice", p.Start + m.Index, p.Start + m.Index + m.Length, $"Suggestion: \"{m.Value}\" may be passive voice.");
        }
    }
}
