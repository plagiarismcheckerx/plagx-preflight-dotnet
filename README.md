# Plagx Preflight for .NET

*A free academic writing tool by Plagiarism Checker X.*

**Plagx Preflight for .NET is a free, open-source draft checker for C# and .NET apps.** Use it as an essay checker before students submit: it checks the word count and structure, matches in-text citations against the reference list, and gives punctuation and basic grammar suggestions plus readability stats.

- No dependencies, works offline: text never leaves your app
- .NET 8+, nullable annotations and XML docs included
- APA and MLA citation checks
- Results identical to the [JavaScript version](https://github.com/plagiarismcheckerx/plagx-preflight), offsets included

**[Try the live demo →](https://plagiarismcheckerx.github.io/plagx-preflight/)**

Developer downloads: [NuGet](https://www.nuget.org/packages/Plagx.Preflight) · [npm](https://www.npmjs.com/package/plagx-preflight) · [PyPI](https://pypi.org/project/plagx-preflight/) · [Packagist](https://packagist.org/packages/plagiarismcheckerx/plagx-preflight) · [GitHub](https://github.com/plagiarismcheckerx/plagx-preflight)

> Plagx Preflight is a free academic writing utility by Plagiarism Checker X. It checks length, structure, references, punctuation and basic grammar, and every result is a suggestion. Plagiarism checking and AI-generated text detection are available in Plagiarism Checker X, linked at the end of this page.

## Install

```bash
dotnet add package Plagx.Preflight
```

## Quick start

```csharp
using Plagx.Preflight;

var result = Preflight.CheckDraft(text, new PreflightOptions
{
    MinWords = 1500,
    MaxWords = 2000,
    Style = "apa",
    RequiredHeadings = new[] { "Introduction", "Conclusion" },
});

Console.WriteLine($"{result.Stats.Words} words, Flesch {result.Stats.FleschReadingEase}");

foreach (var issue in result.Issues)
{
    Console.WriteLine($"{issue.Line}:{issue.Column} {issue.Level} {issue.Message}");
}
// 5:14 warning Suggestion: "the the" looks like a repeated word.
```

## API

`Preflight.CheckDraft(string text, PreflightOptions? options = null)` returns a `DraftResult` with `Stats`, `Errors`, `Warnings`, `Infos` and `Issues`. Each `DraftIssue` has `Level`, `Code`, `Message`, `Start`, `End`, `Line`, `Column`, `Text` and `Suggestion`. Issues are sorted by position. The records serialise with `System.Text.Json` to the same JSON as the other Plagx Preflight libraries.

`Start`, `End` and `Column` are UTF-16 offsets, so they index .NET strings directly.

| Option | Default | |
|---|---|---|
| `MinWords`, `MaxWords` | `null` | Word limits (reference list excluded) |
| `Style` | `"apa"` | `"apa"` or `"mla"` |
| `Checks` | all except `"passive-voice"` | `length`, `references`, `punctuation`, `grammar`, `passive-voice` |
| `RequiredHeadings` | empty | e.g. `new[] { "Introduction", "Conclusion" }` |

Helpers: `Preflight.CountWords(text)`, `Preflight.SplitSentences(text)`, `Preflight.FindCitations(text, style)`, `Preflight.Version`, `Preflight.AllChecks`. Invalid options throw `ArgumentException`.

## Checks

| Group | Codes | Level |
|---|---|---|
| Length & structure | `word-count-low`, `word-count-high`, `long-sentence` (over 40 words), `long-paragraph` (over 250 words), `single-sentence-paragraph`, `missing-heading` | warning / info |
| References | `citation-no-reference`, `missing-reference-section`, `reference-not-cited`, `et-al-format` | error / warning |
| Punctuation | `unmatched-quote`, `unmatched-bracket`, `double-space`, `space-before-punctuation`, `missing-space-after-punctuation`, `repeated-punctuation`, `mixed-quote-styles` | warning / info |
| Grammar | `repeated-word`, `a-an`, `sentence-capital`, `lowercase-i`, `common-confusion`, `passive-voice` (off by default) | warning / info |

## Good to know

- Every result is a suggestion. The grammar rules are deliberately conservative, so they miss some mistakes rather than flag correct text.
- The reference list starts at a heading such as References, Bibliography or Works Cited. It is excluded from the word count and from the punctuation and grammar checks.

## Development

```bash
dotnet test
```

`tests/Plagx.Preflight.Tests/parity/expected.json` and the fixtures come from the JavaScript reference (`npm run parity` in plagx-preflight). Every case must match exactly. `src/Plagx.Preflight/UnicodeData.cs` holds Unicode letter classes and case mappings generated from the JavaScript engine (`node scripts/build-unicode.mjs`).

## Plagx Preflight - Free Draft & Grammar Checker

An open-source writing utility for students, researchers and developers. Check length, structure, citations, punctuation and basic grammar before you submit.

Developed by Plagiarism Checker X.

Plagx Preflight gives writing suggestions; it does not detect plagiarism or AI-generated text. For full document checks, explore [Plagiarism Checker X](https://plagiarismcheckerx.com/?utm_source=plagx-preflight-dotnet) and its [AI Content Detector](https://plagiarismcheckerx.com/ai-content-detector?utm_source=plagx-preflight-dotnet).

## License

MIT © Plagiarism Checker X
