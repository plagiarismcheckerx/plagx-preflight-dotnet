using System.Text.RegularExpressions;

namespace Plagx.Preflight;

/// <summary>A piece of text with UTF-16 offsets into the checked text.</summary>
internal sealed record Part(string Text, int Start, int End);

/// <summary>A paragraph: a run of non-blank lines.</summary>
internal sealed record Paragraph(string Text, int Start, int End, bool Heading);

/// <summary>A document split into body and reference list.</summary>
internal sealed record Document(
    IReadOnlyList<Paragraph> Paragraphs, IReadOnlyList<Paragraph> Body, int BodyEnd, Paragraph? ReferenceHeading, IReadOnlyList<Part> Entries);

/// <summary>Text structure helpers: words, lines, paragraphs, headings, sentences, syllables.</summary>
internal static class Text
{
    private static readonly Regex WordRe = Js.Re($"{Js.LM("0-9")}+(?:['\\u2019-]{Js.LM("0-9")}+)*");

    private static readonly HashSet<string> Abbreviations = new(StringComparer.Ordinal)
    {
        "al", "approx", "cf", "co", "dept", "dr", "ed", "eds", "eg", "et", "etc", "fig", "figs", "ie",
        "inc", "jr", "ltd", "mr", "mrs", "ms", "no", "nos", "p", "pp", "prof", "sr", "st", "vol", "vols", "vs",
    };

    public static readonly HashSet<string> ReferenceHeadings = new(StringComparer.Ordinal)
    {
        "references", "reference list", "bibliography", "works cited", "work cited", "sources", "literature cited",
    };

    private static readonly Regex Hashes = Js.Re($"^#+{Js.Ws}*");
    private static readonly Regex Numbering = Js.Re($"^(?:[0-9]+(?:\\.[0-9]+)*\\.?|[ivxlcIVXLC]+\\.){Js.Ws}+");
    private static readonly Regex Colon = Js.Re($"{Js.Ws}*:\\z");
    private static readonly Regex Spaces = Js.Re($"{Js.Ws}+");
    private static readonly Regex HeadingEnd = Js.Re("[.!?,;\"'\\u201D\\u2019)\\]]\\z");
    private static readonly Regex HeadingStart = Js.Re($"^{Js.Lu("0-9")}");
    private static readonly Regex BlankLine = Js.Re("^[ \\t\\r\\f\\u000B\\u00A0]*\\z");
    private static readonly Regex Terminator = Js.Re($"[.!?]+[\"'\\u201D\\u2019)\\]]*(?={Js.Ws}|\\z)");
    private static readonly Regex Closers = Js.Re("[\"'\\u201D\\u2019)\\]]+\\z");
    private static readonly Regex Before = Js.Re($"(?:^|[{Js.WsClass}(])((?:{Js.L(".")})+)\\z");
    private static readonly Regex OneLetter = Js.Re($"^{Js.L()}\\z");

    public static List<Part> Words(string text) =>
        WordRe.Matches(text).Select(m => new Part(m.Value, m.Index, m.Index + m.Length)).ToList();

    public static int CountWords(string text) => WordRe.Count(text);

    public static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i += 1)
        {
            if (text[i] == '\n') starts.Add(i + 1);
        }
        return starts;
    }

    public static int LineIndex(List<int> starts, int offset)
    {
        int lo = 0, hi = starts.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (starts[mid] <= offset) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\r' or '\n' or '\f' or '\v' or ' ';

    private static (int, int) TrimRange(string text, int s, int e)
    {
        while (s < e && IsSpace(text[s])) s += 1;
        while (e > s && IsSpace(text[e - 1])) e -= 1;
        return (s, e);
    }

    public static string NormalizeHeading(string s)
    {
        s = Js.Trim(s);
        s = Hashes.Replace(s, "", 1);
        s = Numbering.Replace(s, "", 1);
        s = Colon.Replace(s, "", 1);
        s = Spaces.Replace(s, " ");
        return Js.Lower(s);
    }

    private static bool IsHeading(string t)
    {
        if (t.Contains('\n')) return false;
        if (t.StartsWith('#')) return true;
        if (ReferenceHeadings.Contains(NormalizeHeading(t))) return true;
        var n = CountWords(t);
        if (n < 1 || n > 10) return false;
        if (HeadingEnd.IsMatch(t)) return false;
        return HeadingStart.IsMatch(t);
    }

    public static List<Paragraph> Paragraphs(string text)
    {
        var output = new List<Paragraph>();
        var starts = LineStarts(text);
        int blockStart = -1, blockEnd = -1;
        void Flush()
        {
            if (blockStart < 0) return;
            var (s, e) = TrimRange(text, blockStart, blockEnd);
            var t = text[s..e];
            output.Add(new Paragraph(t, s, e, IsHeading(t)));
            blockStart = -1;
        }
        for (var i = 0; i < starts.Count; i += 1)
        {
            var ls = starts[i];
            var le = i + 1 < starts.Count ? starts[i + 1] - 1 : text.Length;
            if (BlankLine.IsMatch(text[ls..le]))
            {
                Flush();
            }
            else
            {
                if (blockStart < 0) blockStart = ls;
                blockEnd = le;
            }
        }
        Flush();
        return output;
    }

    public static Document Structure(string text)
    {
        var paras = Paragraphs(text);
        var idx = paras.FindIndex(p => p.Heading && ReferenceHeadings.Contains(NormalizeHeading(p.Text)));
        var body = idx < 0 ? paras : paras.GetRange(0, idx);
        var heading = idx < 0 ? null : paras[idx];
        var entries = new List<Part>();
        if (heading is not null)
        {
            var starts = LineStarts(text);
            for (var i = 0; i < starts.Count; i += 1)
            {
                var ls = starts[i];
                if (ls <= heading.End) continue;
                var le = i + 1 < starts.Count ? starts[i + 1] - 1 : text.Length;
                var (s, e) = TrimRange(text, ls, le);
                if (e > s) entries.Add(new Part(text[s..e], s, e));
            }
        }
        return new Document(paras, body, heading is null ? text.Length : heading.Start, heading, entries);
    }

    private static List<Part> SentencesIn(string s, int baseOffset)
    {
        var output = new List<Part>();
        var start = 0;
        void Push(int end)
        {
            var (a, b) = TrimRange(s, start, end);
            if (b > a) output.Add(new Part(s[a..b], baseOffset + a, baseOffset + b));
            start = end;
        }
        foreach (Match m in Terminator.Matches(s))
        {
            var marks = Closers.Replace(m.Value, "", 1);
            if (marks.StartsWith("..", StringComparison.Ordinal)) continue; // an ellipsis
            if (marks == ".")
            {
                // A lone period after an abbreviation, an initial or a dotted form (U.S.) does not end a sentence.
                var b = Before.Match(s[..m.Index]);
                if (b.Success)
                {
                    var w = Js.Lower(b.Groups[1].Value);
                    if (Abbreviations.Contains(w) || OneLetter.IsMatch(w) || w.Contains('.')) continue;
                }
            }
            Push(m.Index + m.Length);
        }
        Push(s.Length);
        return output;
    }

    public static List<Part> SplitSentences(string text)
    {
        var output = new List<Part>();
        foreach (var p in Paragraphs(text))
        {
            if (p.Heading) output.Add(new Part(p.Text, p.Start, p.End));
            else output.AddRange(SentencesIn(p.Text, p.Start));
        }
        return output;
    }

    public static List<Part> ParagraphSentences(Paragraph p) => SentencesIn(p.Text, p.Start);

    private static readonly Regex NotAz = Js.Re("[^a-z]");
    private static readonly Regex Ending = Js.Re("(?:[^laeiouy]es|ed|[^laeiouy]e)\\z");
    private static readonly Regex Vowels = Js.Re("[aeiouy]+");

    public static int Syllables(string word)
    {
        var w = NotAz.Replace(Js.Lower(word), "");
        if (w.Length == 0) return 0;
        if (w.Length <= 3) return 1;
        w = Ending.Replace(w, "", 1);
        if (w.StartsWith('y')) w = w[1..];
        var n = Vowels.Count(w);
        return n > 0 ? n : 1;
    }
}
