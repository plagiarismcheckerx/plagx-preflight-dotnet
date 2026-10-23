using System.Text.Json.Serialization;

namespace Plagx.Preflight;

/// <summary>Options for <see cref="Preflight.CheckDraft"/>.</summary>
public sealed record PreflightOptions
{
    /// <summary>Minimum word count (reference list excluded).</summary>
    public int? MinWords { get; init; }

    /// <summary>Maximum word count (reference list excluded).</summary>
    public int? MaxWords { get; init; }

    /// <summary>Citation style to look for: <c>"apa"</c> (default) or <c>"mla"</c>.</summary>
    public string Style { get; init; } = "apa";

    /// <summary>Check groups to run. Default: all except <c>"passive-voice"</c>.</summary>
    public IReadOnlyList<string>? Checks { get; init; }

    /// <summary>Headings the draft should contain, e.g. <c>["Introduction", "Conclusion"]</c>.</summary>
    public IReadOnlyList<string> RequiredHeadings { get; init; } = Array.Empty<string>();
}

/// <summary>Word count and readability of a draft.</summary>
public sealed record DraftStats(
    [property: JsonPropertyName("words")] int Words,
    [property: JsonPropertyName("sentences")] int Sentences,
    [property: JsonPropertyName("paragraphs")] int Paragraphs,
    [property: JsonPropertyName("characters")] int Characters,
    [property: JsonPropertyName("readingTimeMinutes")] int ReadingTimeMinutes,
    [property: JsonPropertyName("fleschReadingEase")] double FleschReadingEase);

/// <summary>One suggestion.</summary>
/// <param name="Level"><c>"error"</c>, <c>"warning"</c> or <c>"info"</c>.</param>
/// <param name="Code">Check code, such as <c>repeated-word</c>.</param>
/// <param name="Message">Readable suggestion, starting with "Suggestion:".</param>
/// <param name="Start">UTF-16 offset where the issue starts (0 for document-level issues).</param>
/// <param name="End">UTF-16 offset where the issue ends.</param>
/// <param name="Line">1-based line.</param>
/// <param name="Column">1-based column in UTF-16 units.</param>
/// <param name="Text">The flagged text.</param>
/// <param name="Suggestion">Replacement text, or null.</param>
public sealed record DraftIssue(
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("start")] int Start,
    [property: JsonPropertyName("end")] int End,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("column")] int Column,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("suggestion")] string? Suggestion);

/// <summary>Result of <see cref="Preflight.CheckDraft"/>.</summary>
public sealed record DraftResult(
    [property: JsonPropertyName("stats")] DraftStats Stats,
    [property: JsonPropertyName("errors")] int Errors,
    [property: JsonPropertyName("warnings")] int Warnings,
    [property: JsonPropertyName("infos")] int Infos,
    [property: JsonPropertyName("issues")] IReadOnlyList<DraftIssue> Issues);

/// <summary>A sentence with UTF-16 offsets.</summary>
public sealed record Sentence(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("start")] int Start,
    [property: JsonPropertyName("end")] int End);

/// <summary>An in-text citation. <paramref name="Key"/> is the first word of the author text in lowercase; <paramref name="Year"/> is null for MLA.</summary>
public sealed record Citation(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("start")] int Start,
    [property: JsonPropertyName("end")] int End,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("year")] string? Year);
