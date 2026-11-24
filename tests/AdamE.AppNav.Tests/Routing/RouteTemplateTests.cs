using System.Text;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTemplateTests
{
    // A short phrase that identifies each kind of rejection, so that tests pin which check reported a segment.
    private const string ControlCharacter = "control character";
    private const string InvalidParameterName = "invalid parameter name";
    private const string ReservedInAParameterName = "not allowed in a parameter name";
    private const string BraceOutsideAParameter = "outside a parameter";
    private const string EdgeWhiteSpace = "white space";
    private const string QuestionMarkOrHash = "'?' or '#'";
    private const string RepeatedName = "repeats the name";
    private const string LoneSurrogate = "lone surrogate";

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
    [InlineData("/%7Bid%7D", "/{id}")]
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
    public void ALiteralTemplateMatchReturnsAnEmptyReadOnlyDictionary()
    {
        RouteTemplate template = RouteTemplate.Parse("/settings/profile");

        IReadOnlyDictionary<string, string>? values = template.Match("/settings/profile");

        Assert.NotNull(values);
        Assert.Empty(values);
        IDictionary<string, string> mutable = Assert.IsAssignableFrom<IDictionary<string, string>>(values);
        Assert.True(mutable.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutable.Add("key", "value"));
    }

    [Theory]
    [InlineData("/{id}")]
    [InlineData("/{_id}")]
    [InlineData("/{_}")]
    [InlineData("/{a1}")]
    [InlineData("/{Store_Id_2}")]
    public void AWholeSegmentInBracesIsAParameter(string value)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        IReadOnlyDictionary<string, string>? values = template.Match("/anything");

        Assert.NotNull(values);
        Assert.Equal([value[2..^1]], values.Keys);
        Assert.Equal("anything", Assert.Single(values.Values));
    }

    [Fact]
    public void EncodedBracesAreALiteralNotAParameter()
    {
        RouteTemplate template = RouteTemplate.Parse("/%7Bid%7D");

        IReadOnlyDictionary<string, string>? values = template.Match("/{id}");

        Assert.NotNull(values);
        Assert.Empty(values);
        Assert.Null(template.Match("/42"));
    }

    [Fact]
    public void CapturedValuesAreKeyedByTheNamesAsWritten()
    {
        RouteTemplate template = RouteTemplate.Parse("/{zone}/stores/{StoreId}/{aisle_2}/{B}");

        IReadOnlyDictionary<string, string>? values = template.Match("/north/stores/42/7/x");

        Assert.NotNull(values);
        Assert.Equal(["B", "StoreId", "aisle_2", "zone"], values.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("/{id}", "/42")]
    [InlineData("/{id}/details", "/42/details")]
    [InlineData("/products/{id}/details", "/products/42/details")]
    [InlineData("/products/{id}", "/products/42")]
    public void AParameterCapturesOneSegmentAtAnyPosition(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        IReadOnlyDictionary<string, string>? values = template.Match(path);

        Assert.NotNull(values);
        Assert.Equal(new Dictionary<string, string> { ["id"] = "42" }, values);
    }

    [Fact]
    public void SeveralParametersMixedWithLiteralsAreCaptured()
    {
        RouteTemplate template = RouteTemplate.Parse("/stores/{storeId}/products/{productId}/{tab}");

        IReadOnlyDictionary<string, string>? values = template.Match("/stores/northwind/products/42/reviews");

        Assert.NotNull(values);
        Assert.Equal(
            new Dictionary<string, string> { ["storeId"] = "northwind", ["productId"] = "42", ["tab"] = "reviews" },
            values);
    }

    [Theory]
    [InlineData("/products")]
    [InlineData("/products/")]
    [InlineData("/products//")]
    [InlineData("/products/42/details")]
    [InlineData("/")]
    [InlineData("")]
    public void AParameterDoesNotMatchAMissingOrExtraSegment(string path)
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id}");

        Assert.Null(template.Match(path));
    }

    [Theory]
    [InlineData("/products/42/Details")]
    [InlineData("/items/42/details")]
    public void LiteralsAroundAParameterMustStillMatch(string path)
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id}/details");

        Assert.Null(template.Match(path));
    }

    [Theory]
    [InlineData("/files/a%20b", "a b")]
    [InlineData("/files/caf%C3%A9", "café")]
    [InlineData("/files/a%2Fb", "a/b")]
    [InlineData("/files/a%252Fb", "a%2Fb")]
    [InlineData("/files/%20", " ")]
    [InlineData("/files/ a ", " a ")]
    [InlineData("/files/a+b", "a+b")]
    [InlineData("/files/%zz", "%zz")]
    [InlineData("/files/%FF", "%FF")]
    [InlineData("/files/100%", "100%")]
    [InlineData("/files/..", "..")]
    [InlineData("/files/a?b=c", "a?b=c")]
    [InlineData("/files/%0A", "\n")]
    [InlineData("/files/a%00b", "a\0b")]
    public void ACapturedValueIsTheDecodedSegment(string path, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{name}");

        IReadOnlyDictionary<string, string>? values = template.Match(path);

        Assert.NotNull(values);
        Assert.Equal(expected, values["name"]);
    }

    [Theory]
    [InlineData("storeId")]
    [InlineData("STOREID")]
    [InlineData("storeid")]
    public void CapturedValuesAreLookedUpWithoutRegardToCase(string key)
    {
        RouteTemplate template = RouteTemplate.Parse("/stores/{storeId}");

        IReadOnlyDictionary<string, string>? values = template.Match("/stores/northwind");

        Assert.NotNull(values);
        Assert.True(values.TryGetValue(key, out string? value));
        Assert.Equal("northwind", value);
    }

    [Fact]
    public void EachMatchReturnsItsOwnValues()
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id}");

        IReadOnlyDictionary<string, string>? first = template.Match("/products/1");
        IReadOnlyDictionary<string, string>? second = template.Match("/products/2");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal("1", first["id"]);
        Assert.Equal("2", second["id"]);
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
    [InlineData("/a{b", "a{b")]
    [InlineData("/a}b", "a}b")]
    [InlineData("/{", "{")]
    [InlineData("/}", "}")]
    [InlineData("/products/item{id}/details", "item{id}")]
    [InlineData("/{id}x", "{id}x")]
    [InlineData("/{a}{b}", "{a}{b}")]
    [InlineData("/{id}}", "{id}}")]
    [InlineData("/{{id}", "{{id}")]
    [InlineData("/a%20{b", "a%20{b")]
    public void ParseRejectsABraceOutsideAWholeSegmentParameter(string value, string segment)
    {
        AssertRejectsSegment(value, segment, BraceOutsideAParameter, notCategory: InvalidParameterName);
    }

    [Theory]
    [InlineData("/{}", "{}", BraceOutsideAParameter)]
    [InlineData("/{ }", "{ }", EdgeWhiteSpace)]
    [InlineData("/{ id }", "{ id }", EdgeWhiteSpace)]
    [InlineData("/{a b}", "{a b}", BraceOutsideAParameter)]
    [InlineData("/{1a}", "{1a}", ReservedInAParameterName)]
    [InlineData("/{a-b}", "{a-b}", ReservedInAParameterName)]
    [InlineData("/{a.b}", "{a.b}", ReservedInAParameterName)]
    [InlineData("/{é}", "{é}", ReservedInAParameterName)]
    [InlineData("/{café}", "{café}", ReservedInAParameterName)]
    [InlineData("/{%41}", "{%41}", ReservedInAParameterName)]
    public void ParseRejectsAnInvalidParameterName(string value, string segment, string notCategory)
    {
        AssertRejectsSegment(value, segment, InvalidParameterName, notCategory);
    }

    [Theory]
    [InlineData("/products/{id:int}", "{id:int}")]
    [InlineData("/products/{id?}", "{id?}")]
    [InlineData("/files/{*rest}", "{*rest}")]
    [InlineData("/{:}", "{:}")]
    [InlineData("/{1:a}", "{1:a}")]
    public void ParseRejectsAColonQuestionMarkOrAsteriskInAParameterName(string value, string segment)
    {
        ArgumentException exception =
            AssertRejectsSegment(value, segment, ReservedInAParameterName, notCategory: InvalidParameterName);

        Assert.DoesNotContain(QuestionMarkOrHash, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{id}/{id}", "{id}", "{id}")]
    [InlineData("/{id}/{ID}", "{ID}", "{id}")]
    [InlineData("/{id}/products/{ID}", "{ID}", "{id}")]
    [InlineData("/{storeId}/{a}/{StoreID}", "{StoreID}", "{storeId}")]
    public void ParseRejectsAParameterNameUsedTwiceIgnoringCase(string value, string segment, string earlierSegment)
    {
        ArgumentException exception =
            AssertRejectsSegment(value, segment, RepeatedName, notCategory: InvalidParameterName);

        Assert.Contains($"'{earlierSegment}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/settings?tab=profile", "settings?tab=profile")]
    [InlineData("/what?", "what?")]
    [InlineData("/settings#profile", "settings#profile")]
    [InlineData("/products/#", "#")]
    [InlineData("/a%20?", "a%20?")]
    public void ParseRejectsASegmentContainingAQuestionMarkOrHash(string value, string segment)
    {
        AssertRejectsSegment(value, segment, QuestionMarkOrHash, notCategory: ReservedInAParameterName);
    }

    [Theory]
    [InlineData("/ settings", " settings", QuestionMarkOrHash)]
    [InlineData("/settings ", "settings ", QuestionMarkOrHash)]
    [InlineData("/settings/\u00A0profile", "\u00A0profile", QuestionMarkOrHash)]
    [InlineData("/ /", " ", QuestionMarkOrHash)]
    [InlineData("/settings/ /profile", " ", QuestionMarkOrHash)]
    [InlineData("/%20a ", "%20a ", QuestionMarkOrHash)]
    [InlineData("/ what?", " what?", QuestionMarkOrHash)]
    [InlineData("/ {id}", " {id}", BraceOutsideAParameter)]
    [InlineData("/{id} ", "{id} ", BraceOutsideAParameter)]
    [InlineData("/products/ {id}/details", " {id}", BraceOutsideAParameter)]
    [InlineData("/{id}\u00A0", "{id}\u00A0", BraceOutsideAParameter)]
    public void ParseRejectsASegmentThatStartsOrEndsWithWhiteSpace(string value, string segment, string notCategory)
    {
        AssertRejectsSegment(value, segment, EdgeWhiteSpace, notCategory);
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
        AssertRejectsControlCharacterSegment(value, segment, notCategory: EdgeWhiteSpace);
    }

    [Theory]
    [InlineData("/\t", "\\u0009")]
    [InlineData("/settings/\tprofile", "\\u0009profile")]
    [InlineData("/settings\n", "settings\\u000A")]
    [InlineData("/a\tb", "a\\u0009b")]
    public void ParseRejectsATabOrLineFeedAsAControlCharacterEvenAtAnEdge(string value, string segment)
    {
        AssertRejectsControlCharacterSegment(value, segment, notCategory: EdgeWhiteSpace);
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

        AssertRejectsControlCharacterSegment(value, segment, notCategory: EdgeWhiteSpace);
    }

    [Theory]
    [InlineData("/{\n}", "{\\u000A}", InvalidParameterName)]
    [InlineData("/{a\tb}", "{a\\u0009b}", InvalidParameterName)]
    [InlineData("/{id}/{\u0001}", "{\\u0001}", InvalidParameterName)]
    [InlineData("/{id}/{ID\u0001}", "{ID\\u0001}", RepeatedName)]
    [InlineData("/a{\0", "a{\\u0000", BraceOutsideAParameter)]
    [InlineData("/what?\0", "what?\\u0000", QuestionMarkOrHash)]
    public void ParseReportsAControlCharacterBeforeTheSegmentsOtherProblems(
        string value,
        string segment,
        string notCategory)
    {
        AssertRejectsControlCharacterSegment(value, segment, notCategory);
    }

    [Theory]
    [InlineData("a", 0xD800, "b", "a\\uD800b", EdgeWhiteSpace)]
    [InlineData("", 0xDFFF, "", "\\uDFFF", EdgeWhiteSpace)]
    [InlineData("{id", 0xDC00, "}", "{id\\uDC00}", InvalidParameterName)]
    [InlineData("{", 0xD800, "}", "{\\uD800}", InvalidParameterName)]
    [InlineData("{a", 0xDFFF, "b}", "{a\\uDFFFb}", InvalidParameterName)]
    [InlineData("{", 0xDBFF, "", "{\\uDBFF", BraceOutsideAParameter)]
    [InlineData("\U0001F600", 0xD83D, "", "\U0001F600\\uD83D", ControlCharacter)]
    [InlineData("x", 0xDE00, "\U0001F600", "x\\uDE00\U0001F600", ControlCharacter)]
    public void ParseRejectsASegmentContainingALoneSurrogate(
        string before,
        int surrogate,
        string after,
        string quoted,
        string notCategory)
    {
        string value = "/" + before + (char)surrogate + after;

        ArgumentException exception = AssertRejectsSegment(value, quoted, LoneSurrogate, notCategory);

        Assert.True(IsValidUtf16(exception.Message));
    }

    [Theory]
    [InlineData("\0", 0xD800, "", "\\u0000\\uD800")]
    [InlineData("", 0xDC00, "\n", "\\uDC00\\u000A")]
    [InlineData("{a\u0001", 0xDBFF, "}", "{a\\u0001\\uDBFF}")]
    public void ParseReportsAControlCharacterBeforeALoneSurrogate(
        string before,
        int surrogate,
        string after,
        string quoted)
    {
        string value = "/" + before + (char)surrogate + after;

        ArgumentException exception = AssertRejectsSegment(value, quoted, ControlCharacter, LoneSurrogate);

        Assert.True(IsValidUtf16(exception.Message));
    }

    [Fact]
    public void ASurrogatePairInATemplateIsValidText()
    {
        RouteTemplate template = RouteTemplate.Parse("/smile\U0001F600/{id}");

        Assert.NotNull(template.Match("/smile\U0001F600/1"));
        Assert.NotNull(template.Match("/smile%F0%9F%98%80/1"));
    }

    [Fact]
    public void EncodedSurrogateBytesAreKeptAsWritten()
    {
        Assert.Equal("%ED%A0%80", Uri.UnescapeDataString("%ED%A0%80"));

        RouteTemplate template = RouteTemplate.Parse("/%ED%A0%80");

        Assert.NotNull(template.Match("/%ED%A0%80"));
        Assert.Null(template.Match("/" + (char)0xD800));
    }

    [Fact]
    public void MatchRejectsANullPath()
    {
        RouteTemplate template = RouteTemplate.Parse("/");

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => template.Match(null!));

        Assert.Equal("path", exception.ParamName);
    }

    private static ArgumentException AssertRejectsSegment(
        string value,
        string segment,
        string category,
        string notCategory)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => RouteTemplate.Parse(value));

        Assert.Equal("value", exception.ParamName);
        Assert.Contains($"'{segment}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(category, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(notCategory, exception.Message, StringComparison.Ordinal);
        return exception;
    }

    private static void AssertRejectsControlCharacterSegment(string value, string segment, string notCategory)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, ControlCharacter, notCategory);

        Assert.DoesNotContain(exception.Message, char.IsControl);
    }

    private static bool IsValidUtf16(string text)
    {
        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
