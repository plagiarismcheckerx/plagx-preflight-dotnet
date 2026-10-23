using System.Text;
using System.Text.RegularExpressions;

namespace Plagx.Preflight;

/// <summary>
/// JavaScript semantics the port needs: regex building blocks (JavaScript's \s, ASCII \b,
/// Unicode properties including characters outside the BMP) and full case mapping.
/// </summary>
internal static class Js
{
    /// <summary>JavaScript's \s as character-class content (.NET's \s is different).</summary>
    public const string WsClass = "\\t\\n\\u000B\\f\\r \\u00A0\\u1680\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000\\uFEFF";
    public const string Ws = "[" + WsClass + "]";
    public const string NotWs = "[^" + WsClass + "]";

    /// <summary>JavaScript's \b without the u flag: an ASCII word boundary.</summary>
    public const string B = "(?:(?<=[A-Za-z0-9_])(?![A-Za-z0-9_])|(?<![A-Za-z0-9_])(?=[A-Za-z0-9_]))";

    /// <summary>JavaScript's "." without the s flag.</summary>
    public const string Dot = "[^\\n\\r\\u2028\\u2029]";

    /// <summary>One character of \p{L} (or of \p{L} plus the extra class content).</summary>
    public static string L(string extra = "") => $"(?:[{UnicodeData.LBmp}{extra}]|{UnicodeData.LAstral})";

    /// <summary>One character of \p{L}, \p{M} or the extra class content.</summary>
    public static string LM(string extra = "") => $"(?:[{UnicodeData.LBmp}{UnicodeData.MBmp}{extra}]|{UnicodeData.LAstral}|{UnicodeData.MAstral})";

    public static string Lu(string extra = "") => $"(?:[{UnicodeData.LuBmp}{extra}]|{UnicodeData.LuAstral})";

    public static string Ll() => $"(?:[{UnicodeData.LlBmp}]|{UnicodeData.LlAstral})";

    public static Regex Re(string pattern, bool ignoreCase = false) =>
        new(pattern, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));

    private static readonly Regex TrimRe = Re($"^{Ws}+|{Ws}+\\z");

    /// <summary>String.prototype.trim().</summary>
    public static string Trim(string s) => TrimRe.Replace(s, "");

    private static bool IsCased(int cp) => UnicodeData.Upper.ContainsKey(cp) || UnicodeData.Lower.ContainsKey(cp);

    /// <summary>Code point at i (a lone surrogate counts as itself, as in JavaScript).</summary>
    private static int CpAt(string s, int i) => char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s, i) : s[i];

    /// <summary>Code point that ends just before i.</summary>
    private static int CpBefore(string s, int i) =>
        i > 1 && char.IsSurrogatePair(s, i - 2) ? char.ConvertToUtf32(s, i - 2) : s[i - 1];

    private static string Str(int cp) => cp > 0xFFFF ? char.ConvertFromUtf32(cp) : ((char)cp).ToString();

    /// <summary>String.prototype.toLowerCase(), including the final-sigma rule.</summary>
    public static string Lower(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1)
        {
            var cp = CpAt(s, i);
            if (cp == 0x3A3)
            {
                // Final sigma: after a cased letter and not before one.
                var before = i > 0 && IsCased(CpBefore(s, i));
                var after = i + 1 < s.Length && IsCased(CpAt(s, i + 1));
                sb.Append(before && !after ? 'ς' : 'σ');
                continue;
            }
            sb.Append(UnicodeData.Lower.TryGetValue(cp, out var m) ? m : Str(cp));
        }
        return sb.ToString();
    }

    /// <summary>String.prototype.toUpperCase().</summary>
    public static string Upper(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1)
        {
            var cp = CpAt(s, i);
            sb.Append(UnicodeData.Upper.TryGetValue(cp, out var m) ? m : Str(cp));
        }
        return sb.ToString();
    }

    /// <summary>s.charAt(0).toUpperCase() + s.slice(1): only the first UTF-16 unit changes.</summary>
    public static string UpperFirst(string s) =>
        s.Length == 0 || char.IsSurrogate(s[0]) ? s : Upper(s[..1]) + s[1..];

    /// <summary>Number of code points.</summary>
    public static int CodePoints(string s)
    {
        var n = 0;
        for (var i = 0; i < s.Length; i += 1)
        {
            if (!(char.IsLowSurrogate(s[i]) && i > 0 && char.IsHighSurrogate(s[i - 1]))) n += 1;
        }
        return n;
    }
}
