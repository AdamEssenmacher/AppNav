using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTemplateTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/settings")]
    [InlineData("//settings//profile/")]
    [InlineData("/files/a%20b")]
    public void ValueIsTheTemplateTextAsGiven(string value)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.Equal(value, template.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("//")]
    public void RootTemplateMatchesAPathWithNoSegments(string path)
    {
        RouteTemplate template = RouteTemplate.Parse("/");

        Assert.NotNull(template.Match(path));
    }

    [Theory]
    [InlineData("/settings")]
    [InlineData("/ ")]
    [InlineData("/%20")]
    public void RootTemplateDoesNotMatchAPathWithASegment(string path)
    {
        RouteTemplate template = RouteTemplate.Parse("/");

        Assert.Null(template.Match(path));
    }

    [Theory]
    [InlineData("/settings")]
    [InlineData("/settings/profile")]
    [InlineData("/settings/profile/photo")]
    public void MatchesAPathWithTheSameLiteralSegments(string value)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.NotNull(template.Match(value));
    }

    [Theory]
    [InlineData("/settings/account")]
    [InlineData("/account/profile")]
    [InlineData("/settings/profile/photo")]
    [InlineData("/settings")]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("/settings/profil")]
    public void DoesNotMatchAPathWithADifferentExtraOrMissingSegment(string path)
    {
        RouteTemplate template = RouteTemplate.Parse("/settings/profile");

        Assert.Null(template.Match(path));
    }

    [Theory]
    [InlineData("/Settings/profile")]
    [InlineData("/settings/PROFILE")]
    public void SegmentsAreComparedCaseSensitively(string path)
    {
        RouteTemplate template = RouteTemplate.Parse("/settings/profile");

        Assert.Null(template.Match(path));
    }

    [Theory]
    [InlineData("/settings/profile/")]
    [InlineData("//settings/profile")]
    [InlineData("/settings//profile")]
    [InlineData("///settings///profile///")]
    public void LeadingTrailingAndRepeatedSlashesInATemplateDoNotCreateSegments(string value)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.NotNull(template.Match("/settings/profile"));
        Assert.Null(template.Match("/settings"));
    }

    [Theory]
    [InlineData("/settings/profile/")]
    [InlineData("//settings/profile")]
    [InlineData("/settings//profile")]
    [InlineData("///settings///profile///")]
    public void LeadingTrailingAndRepeatedSlashesInAPathDoNotCreateSegments(string path)
    {
        RouteTemplate template = RouteTemplate.Parse("/settings/profile");

        Assert.NotNull(template.Match(path));
    }

    [Fact]
    public void APathDoesNotNeedALeadingSlash()
    {
        RouteTemplate template = RouteTemplate.Parse("/settings/profile");

        Assert.NotNull(template.Match("settings/profile"));
    }

    [Theory]
    [InlineData("/settings ")]
    [InlineData("/ settings")]
    [InlineData("/settings/ ")]
    [InlineData("/settings%20")]
    public void WhiteSpaceInAPathIsSignificant(string path)
    {
        RouteTemplate template = RouteTemplate.Parse("/settings");

        Assert.Null(template.Match(path));
    }

    [Theory]
    [InlineData("/my files", "/my files")]
    [InlineData("/my files", "/my%20files")]
    [InlineData("/my%20files", "/my files")]
    public void InnerWhiteSpaceInATemplateSegmentIsLiteral(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.NotNull(template.Match(path));
    }

    [Theory]
    [InlineData("/files/a%20b", "/files/a b")]
    [InlineData("/files/a%20b", "/files/a%20b")]
    [InlineData("/stores/northwind", "/stores/north%77ind")]
    [InlineData("/stores/north%77ind", "/stores/northwind")]
    [InlineData("/caf%C3%A9", "/café")]
    [InlineData("/café", "/caf%C3%A9")]
    public void PercentEncodedSegmentsAreDecodedBeforeComparison(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.NotNull(template.Match(path));
    }

    [Fact]
    public void EncodedSlashInATemplateIsPartOfItsSegment()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/a%2Fb");

        Assert.NotNull(template.Match("/files/a%2Fb"));
        Assert.Null(template.Match("/files/a/b"));
    }

    [Fact]
    public void EncodedSlashInAPathIsPartOfItsSegment()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/a/b");

        Assert.NotNull(template.Match("/files/a/b"));
        Assert.Null(template.Match("/files/a%2Fb"));
    }

    [Fact]
    public void HexDigitsInValidPercentSequencesAreCaseInsensitive()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/a%2fb");

        Assert.NotNull(template.Match("/files/a%2Fb"));
        Assert.NotNull(template.Match("/files/a%2fb"));
    }

    [Fact]
    public void SegmentsAreDecodedOnlyOnce()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/a%252Fb");

        Assert.NotNull(template.Match("/files/a%252Fb"));
        Assert.Null(template.Match("/files/a%2Fb"));
    }

    [Theory]
    [InlineData("/discount/100%")]
    [InlineData("/discount/%zz")]
    [InlineData("/discount/%2")]
    [InlineData("/discount/%FF")]
    public void InvalidPercentSequencesAreKeptAsWritten(string value)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.NotNull(template.Match(value));
        Assert.Null(template.Match("/discount/�"));
    }

    [Fact]
    public void InvalidPercentSequencesAreComparedAsWritten()
    {
        RouteTemplate template = RouteTemplate.Parse("/discount/%FF");

        Assert.Null(template.Match("/discount/%ff"));
    }

    [Fact]
    public void PlusInAPathIsNotASpace()
    {
        Assert.NotNull(RouteTemplate.Parse("/a+b").Match("/a+b"));
        Assert.Null(RouteTemplate.Parse("/a+b").Match("/a b"));
        Assert.Null(RouteTemplate.Parse("/a%20b").Match("/a+b"));
    }

    [Theory]
    [InlineData("/a/./b")]
    [InlineData("/a/../b")]
    public void DotSegmentsAreLiteral(string value)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.NotNull(template.Match(value));
        Assert.Null(template.Match("/a/b"));
        Assert.Null(template.Match("/b"));
    }

    [Fact]
    public void QueryAndFragmentCharactersInAPathAreLiteral()
    {
        RouteTemplate template = RouteTemplate.Parse("/settings");

        Assert.Null(template.Match("/settings?tab=profile"));
        Assert.Null(template.Match("/settings#profile"));
    }

    [Theory]
    [InlineData("/a%7Bb%7D", "/a{b}")]
    [InlineData("/what%3F", "/what?")]
    [InlineData("/what%3F", "/what%3F")]
    [InlineData("/item%23", "/item#")]
    [InlineData("/item%23", "/item%23")]
    [InlineData("/%20padded%20", "/ padded ")]
    [InlineData("/%20padded%20", "/%20padded%20")]
    public void EncodedReservedCharactersInATemplateMatchTheDecodedText(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.NotNull(template.Match(path));
    }

    [Theory]
    [InlineData("/a%00b", "/a\0b")]
    [InlineData("/a%00b", "/a%00b")]
    [InlineData("/line%0Afeed", "/line\nfeed")]
    [InlineData("/%09tab", "/\ttab")]
    [InlineData("/%09tab", "/%09tab")]
    [InlineData("/delete%7F", "/delete\u007F")]
    [InlineData("/next%C2%85line", "/next\u0085line")]
    public void EncodedControlCharactersInATemplateMatchTheDecodedText(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.NotNull(template.Match(path));
    }

    [Fact]
    public void ASuccessfulMatchReturnsAnEmptyReadOnlyDictionary()
    {
        RouteTemplate template = RouteTemplate.Parse("/settings/profile");

        IReadOnlyDictionary<string, string>? values = template.Match("/settings/profile");

        Assert.NotNull(values);
        Assert.Empty(values);
        IDictionary<string, string> mutable = Assert.IsAssignableFrom<IDictionary<string, string>>(values);
        Assert.True(mutable.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutable.Add("key", "value"));
    }

    [Fact]
    public void ParseRejectsANullValue()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => RouteTemplate.Parse(null!));

        Assert.Equal("value", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void ParseRejectsAnEmptyOrWhiteSpaceValue(string value)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => RouteTemplate.Parse(value));

        Assert.Equal("value", exception.ParamName);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData(" /settings")]
    [InlineData("\\settings")]
    [InlineData("%2Fsettings")]
    public void ParseRejectsAValueThatDoesNotStartWithASlash(string value)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => RouteTemplate.Parse(value));

        Assert.Equal("value", exception.ParamName);
    }

    [Theory]
    [InlineData("/{id}", "{id}")]
    [InlineData("/products/{*path}", "{*path}")]
    [InlineData("/{}", "{}")]
    [InlineData("/a{b", "a{b")]
    [InlineData("/a}b", "a}b")]
    [InlineData("/products/item{id}/details", "item{id}")]
    [InlineData("/a%20{b", "a%20{b")]
    public void ParseRejectsASegmentContainingABrace(string value, string segment)
    {
        AssertRejectsSegment(value, segment);
    }

    [Theory]
    [InlineData("/settings?tab=profile", "settings?tab=profile")]
    [InlineData("/what?", "what?")]
    [InlineData("/settings#profile", "settings#profile")]
    [InlineData("/products/#", "#")]
    [InlineData("/a%20?", "a%20?")]
    public void ParseRejectsASegmentContainingAQuestionMarkOrHash(string value, string segment)
    {
        AssertRejectsSegment(value, segment);
    }

    [Theory]
    [InlineData("/ settings", " settings")]
    [InlineData("/settings ", "settings ")]
    [InlineData("/settings/\u00A0profile", "\u00A0profile")]
    [InlineData("/ /", " ")]
    [InlineData("/settings/ /profile", " ")]
    [InlineData("/%20a ", "%20a ")]
    public void ParseRejectsASegmentThatStartsOrEndsWithWhiteSpace(string value, string segment)
    {
        AssertRejectsSegment(value, segment);
    }

    [Theory]
    [InlineData("/\0settings", "\\u0000settings")]
    [InlineData("/a\0b", "a\\u0000b")]
    [InlineData("/settings\0", "settings\\u0000")]
    [InlineData("/\nsettings", "\\u000Asettings")]
    [InlineData("/set\rtings", "set\\u000Dtings")]
    [InlineData("/settings\u001B", "settings\\u001B")]
    [InlineData("/settings/a\u007Fb", "a\\u007Fb")]
    [InlineData("/a\u0001\u0002b", "a\\u0001\\u0002b")]
    [InlineData("/a%20\u0001", "a%20\\u0001")]
    public void ParseRejectsASegmentContainingAControlCharacter(string value, string segment)
    {
        AssertRejectsControlCharacterSegment(value, segment);
    }

    [Theory]
    [InlineData("/\t", "\\u0009")]
    [InlineData("/settings/\tprofile", "\\u0009profile")]
    [InlineData("/settings\n", "settings\\u000A")]
    [InlineData("/a\tb", "a\\u0009b")]
    public void ParseRejectsATabOrLineFeedAsAControlCharacterEvenAtAnEdge(string value, string segment)
    {
        AssertRejectsControlCharacterSegment(value, segment);
    }

    [Theory]
    [InlineData("/\u0085next", "\\u0085next")]
    [InlineData("/next\u0085line", "next\\u0085line")]
    [InlineData("/next\u0085", "next\\u0085")]
    public void ParseRejectsTheC1NextLineCharacterAsAControlCharacterThoughItIsAlsoWhiteSpace(
        string value,
        string segment)
    {
        Assert.True(char.IsControl('\u0085'));
        Assert.True(char.IsWhiteSpace('\u0085'));

        AssertRejectsControlCharacterSegment(value, segment);
    }

    [Theory]
    [InlineData("/{\n}", "{\\u000A}")]
    [InlineData("/what?\0", "what?\\u0000")]
    public void ParseReportsAControlCharacterBeforeTheSegmentsOtherProblems(string value, string segment)
    {
        AssertRejectsControlCharacterSegment(value, segment);
    }

    [Fact]
    public void MatchRejectsANullPath()
    {
        RouteTemplate template = RouteTemplate.Parse("/");

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => template.Match(null!));

        Assert.Equal("path", exception.ParamName);
    }

    private static void AssertRejectsSegment(string value, string segment)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => RouteTemplate.Parse(value));

        Assert.Equal("value", exception.ParamName);
        Assert.Contains($"'{segment}'", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertRejectsControlCharacterSegment(string value, string segment)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => RouteTemplate.Parse(value));

        Assert.Equal("value", exception.ParamName);
        Assert.Contains($"'{segment}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("control character", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(exception.Message, char.IsControl);
    }
}
