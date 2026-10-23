using System.Text.RegularExpressions;

namespace Plagx.Preflight;

/// <summary>In-text citations (APA author-date, MLA author-page) and reference list entries.</summary>
internal static class Citations
{
    public static readonly string[] Styles = { "apa", "mla" };

    private const string Year = "(?:[0-9]{4}[a-z]?|n\\.d\\.)";
    private static readonly string Name = $"{Js.Lu()}{Js.L("'\\u2019-")}+";
    private static readonly HashSet<string> NotAuthors = new(StringComparer.Ordinal)
    {
        "appendix", "chapter", "chapters", "fig", "figure", "figures", "line", "lines", "p", "page", "pages", "part",
        "phase", "pp", "section", "see", "step", "table", "tables", "version", "vol", "volume",
    };

    private static readonly Regex WordRe = Js.Re($"{Js.L()}{Js.L("'\\u2019-")}*");
    private static readonly Regex Lead = Js.Re($"^{Js.Ws}+");
    private static readonly Regex ApaParen = Js.Re($"\\(([^()]*?{Year}[^()]*)\\)");
    private static readonly Regex ApaPrefix = Js.Re($"^(?:e\\.g\\.|see also|see|cf\\.),?{Js.Ws}+", ignoreCase: true);
    private static readonly Regex ApaAuthor = Js.Re($"^({Js.Dot}*?{Js.L()}{Js.Dot}*?),?{Js.Ws}+({Year}(?:,{Js.Ws}*{Year})*)(?=\\z|,)");
    private static readonly Regex YearSplit = Js.Re($",{Js.Ws}*");
    private static readonly Regex UpperStart = Js.Re($"^{Js.Lu()}");
    private static readonly Regex Narrative = Js.Re(
        $"((?:{Name}{Js.Ws}+){{0,4}}{Name}(?:{Js.Ws}+(?:and|&){Js.Ws}+{Name}|{Js.Ws}+et\\.?{Js.Ws}*al\\.*)?){Js.Ws}+\\(({Year})(?:,[^()]*)?\\)");
    private static readonly Regex MlaParen = Js.Re("\\(([^()]+)\\)");
    private static readonly Regex MlaPart = Js.Re(
        $"^({Js.Lu()}{Js.L("'\\u2019.\\- &,")}*?),?{Js.Ws}+([0-9]+(?:{Js.Ws}*[-\\u2013]{Js.Ws}*[0-9]+)?)\\z");
    private static readonly Regex FourDigits = Js.Re("^[0-9]{4}\\z");
    private static readonly Regex EntryMarker = Js.Re($"^(?:\\[[0-9]+\\]|[0-9]+\\.|[-*\\u2022]){Js.Ws}+");
    private static readonly Regex EntryYear = Js.Re($"\\(({Year})");

    private static string LowerKey(string s) => Js.Lower(s).Replace('’', '\'');

    private static string FirstWord(string s)
    {
        var m = WordRe.Match(s);
        return m.Success ? LowerKey(m.Value) : "";
    }

    /// <summary>Splits the inside of a parenthesis on ";" into trimmed parts with their start offsets.</summary>
    private static IEnumerable<(string Trimmed, int Start)> Parts(string inner, int offset)
    {
        foreach (var part in inner.Split(';'))
        {
            var lead = part.Length - Lead.Replace(part, "", 1).Length;
            yield return (Js.Trim(part), offset + lead);
            offset += part.Length + 1;
        }
    }

    private static List<Citation> Apa(string text)
    {
        var output = new List<Citation>();
        // Parenthetical: (Smith, 2020), (Smith & Lee, 2020, p. 4; Brown, 2018a)
        foreach (Match m in ApaParen.Matches(text))
        {
            foreach (var (trimmed, pStart) in Parts(m.Groups[1].Value, m.Groups[1].Index))
            {
                var px = ApaPrefix.Match(trimmed);
                var skip = px.Success ? px.Length : 0;
                var rest = trimmed[skip..];
                var am = ApaAuthor.Match(rest);
                if (!am.Success) continue;
                var author = am.Groups[1].Value;
                if (!UpperStart.IsMatch(author) || NotAuthors.Contains(FirstWord(author))) continue;
                foreach (var year in YearSplit.Split(am.Groups[2].Value))
                {
                    output.Add(new Citation(rest, pStart + skip, pStart + trimmed.Length, author, FirstWord(author), year.ToLowerInvariant()));
                }
            }
        }
        // Narrative: Smith (2020), Smith and Lee (2020, p. 4), Smith et al. (2020)
        foreach (Match m in Narrative.Matches(text))
        {
            output.Add(new Citation(m.Value, m.Index, m.Index + m.Length, m.Groups[1].Value, FirstWord(m.Groups[1].Value), m.Groups[2].Value.ToLowerInvariant()));
        }
        return output;
    }

    private static List<Citation> Mla(string text)
    {
        var output = new List<Citation>();
        // (Smith 50), (Smith and Lee 50-52), (Smith et al. 12; Brown 7)
        foreach (Match m in MlaParen.Matches(text))
        {
            foreach (var (trimmed, pStart) in Parts(m.Groups[1].Value, m.Groups[1].Index))
            {
                var pm = MlaPart.Match(trimmed);
                if (!pm.Success || NotAuthors.Contains(FirstWord(pm.Groups[1].Value)) || FourDigits.IsMatch(pm.Groups[2].Value)) continue;
                output.Add(new Citation(trimmed, pStart, pStart + trimmed.Length, pm.Groups[1].Value, FirstWord(pm.Groups[1].Value), null));
            }
        }
        return output;
    }

    public static List<Citation> Find(string text, string style)
    {
        if (!Styles.Contains(style)) throw new ArgumentException($"Unknown style \"{style}\". Use: {string.Join(", ", Styles)}", nameof(style));
        var found = style == "apa" ? Apa(text) : Mla(text);
        return found.OrderBy(c => c.Start).ThenBy(c => c.End).ToList();
    }

    /// <summary>Whether a citation points at a reference entry.</summary>
    public static bool Matches(Citation c, Entry e)
    {
        if (c.Year is not null && c.Year != e.Year) return false;
        return WordRe.Matches(c.Author).Any(w => LowerKey(w.Value) == e.Key);
    }

    public static Entry ParseEntry(Part part)
    {
        var body = EntryMarker.Replace(part.Text, "", 1);
        var y = EntryYear.Match(body);
        return new Entry(part.Text, part.Start, part.End, FirstWord(body), y.Success ? y.Groups[1].Value.ToLowerInvariant() : null);
    }
}

/// <summary>A reference list entry with its key (first word, lowercase) and year.</summary>
internal sealed record Entry(string Text, int Start, int End, string Key, string? Year);
