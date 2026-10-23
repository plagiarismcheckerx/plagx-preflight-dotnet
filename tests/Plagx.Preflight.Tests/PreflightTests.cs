using System.Text.Json;

namespace Plagx.Preflight.Tests;

public class PreflightTests
{
    private static List<string> Codes(string text, PreflightOptions? options = null) =>
        Preflight.CheckDraft(text, options).Issues.Select(i => i.Code).ToList();

    [Fact]
    public void ResultShape()
    {
        var r = Preflight.CheckDraft("The the cat sat.");
        var json = JsonDocument.Parse(JsonSerializer.Serialize(r)).RootElement;
        Assert.Equal(new[] { "stats", "errors", "warnings", "infos", "issues" }, json.EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "words", "sentences", "paragraphs", "characters", "readingTimeMinutes", "fleschReadingEase" },
            json.GetProperty("stats").EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "level", "code", "message", "start", "end", "line", "column", "text", "suggestion" },
            json.GetProperty("issues")[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal("Suggestion: \"The the\" looks like a repeated word.", r.Issues[0].Message);
    }

    [Fact]
    public void Utf16Offsets()
    {
        var i = Preflight.CheckDraft("\U0001F600 the the end.").Issues.First(x => x.Code == "repeated-word");
        Assert.Equal((3, 10, 4), (i.Start, i.End, i.Column));
        Assert.Equal(3, Preflight.CheckDraft("a\U0001F600b").Stats.Characters);
    }

    [Fact]
    public void MatchesJavaScriptOnNonBmpLettersAndSpecialCasing()
    {
        // Expected values produced by the JavaScript, Python and PHP versions.
        var text = "\U0001D400lpha is fine. ßig news: an \U0001D401eta test. The the \U0001D4B3yz word. ΟΔΟΣ and ΣΟΦΙΑ. a apple, i think.";
        var got = Preflight.CheckDraft(text).Issues.Select(i => $"{i.Code}@{i.Start}-{i.End}={i.Suggestion}");
        Assert.Equal(new[]
        {
            "sentence-capital@16-19=SSig", "repeated-word@41-48=The", "a-an@76-77=an", "sentence-capital@76-77=A", "lowercase-i@85-86=I",
        }, got);
    }

    [Fact]
    public void Helpers()
    {
        Assert.Equal(6, Preflight.CountWords("It's a well-known fact, 42 times."));
        Assert.Equal(new[] { "One.", "Two!" }, Preflight.SplitSentences("One. Two!").Select(s => s.Text));
        var c = Preflight.FindCitations("é (Moore, 2011; Price, 2016, 2018)");
        Assert.Equal(new[] { "moore:2011", "price:2016", "price:2018" }, c.Select(x => $"{x.Key}:{x.Year}"));
        Assert.Equal(3, c[0].Start);
        Assert.Equal("greenblatt", Preflight.FindCitations("(Greenblatt 45)", "mla").Single().Key);
    }

    [Fact]
    public void Options()
    {
        Assert.Equal(new[] { "missing-heading", "missing-heading", "word-count-low" },
            Codes("One two. Three four.", new() { MinWords = 10, RequiredHeadings = new[] { "Introduction", "Conclusion" }, Checks = new[] { "length" } }));
        Assert.Empty(Codes("the the ,,", new() { Checks = Array.Empty<string>() }));
        Assert.Contains("passive-voice", Codes("The data were collected.", new() { Checks = new[] { "passive-voice" } }));
    }

    [Fact]
    public void InvalidOptions()
    {
        Assert.Throws<ArgumentException>(() => Preflight.CheckDraft("x", new() { Style = "chicago" }));
        Assert.Throws<ArgumentException>(() => Preflight.CheckDraft("x", new() { Checks = new[] { "magic" } }));
        Assert.Throws<ArgumentException>(() => Preflight.CheckDraft("x", new() { MinWords = -1 }));
        Assert.Throws<ArgumentNullException>(() => Preflight.CheckDraft(null!));
    }
}
