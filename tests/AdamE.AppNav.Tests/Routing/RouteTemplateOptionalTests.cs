using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTemplateOptionalTests
{
    // A short phrase that identifies each kind of rejection, so that tests pin which check reported a problem.
    private const string MisplacedQuestionMark = "only allowed at the end";
    private const string ValidSpellings = "'{name?}' or '{name:constraint?}'";
    private const string Asterisk = "'*'";
    private const string InvalidParameterName = "invalid parameter name";
    private const string MoreThanOneColon = "more than one ':'";
    private const string NoConstraint = "no constraint after ':'";
    private const string UnknownConstraint = "unknown constraint";
    private const string RepeatedName = "repeats the name";
    private const string FollowsOptional = "follows the optional parameter";
    private const string QuestionMarkOrHash = "'?' or '#'";
    private const string ControlCharacter = "control character";

    private const string NoValue = "has no value";
    private const string NullValue = "has a null value";
    private const string EmptyValue = "has an empty value";
    private const string HowToLeaveOut = "leave it out of the dictionary";
    private const string NotValidUtf16 = "not valid UTF-16";
    private const string Ambiguous = "ambiguous";
    private const string UnsatisfiedConstraint = "does not satisfy its constraint";
    private const string Gap = "cannot be written because";

    private static readonly Dictionary<string, string> NoValues = [];

    [Theory]
    [InlineData("/{id?}", "/")]
    [InlineData("/products/{id?}", "/products")]
    [InlineData("/products/{id:int?}", "/products")]
    [InlineData("/products/{id:INT?}", "/products")]
    [InlineData("/{_?}", "")]
    [InlineData("/a/{b?}/{c?}", "/a")]
    [InlineData("/{a?}/{b:int?}/{c?}", "/")]
    [InlineData("/stores/{store}/items/{id:int?}", "/stores/north/items")]
    [InlineData("//products//{id?}/", "/products")]
    public void ParseAcceptsATrailingOptionalParameter(string value, string pathWithoutOptionalSegments)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.Equal(value, template.Value);
        Assert.NotNull(template.Match(pathWithoutOptionalSegments));
    }

    [Theory]
    [InlineData("/{id?:int}", "{id?:int}")]
    [InlineData("/{id??}", "{id??}")]
    [InlineData("/{i?d}", "{i?d}")]
    [InlineData("/{?id}", "{?id}")]
    [InlineData("/{id:in?t}", "{id:in?t}")]
    [InlineData("/{id:?int}", "{id:?int}")]
    [InlineData("/{id:int??}", "{id:int??}")]
    [InlineData("/{?:int}", "{?:int}")]
    [InlineData("/{??}", "{??}")]
    [InlineData("/{1?a?}", "{1?a?}")]
    [InlineData("/{id?:slug}", "{id?:slug}")]
    [InlineData("/{id?:int:long}", "{id?:int:long}")]
    public void ParseRejectsAQuestionMarkThatIsNotLast(string value, string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, MisplacedQuestionMark);

        Assert.Contains(ValidSpellings, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Asterisk, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(InvalidParameterName, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(UnknownConstraint, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MoreThanOneColon, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{i?d*}", "{i?d*}")]
    [InlineData("/{?*id}", "{?*id}")]
    [InlineData("/{id?:in*t}", "{id?:in*t}")]
    public void ParseReportsAnAsteriskBeforeAMisplacedQuestionMark(string value, string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, Asterisk);

        Assert.DoesNotContain(MisplacedQuestionMark, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{?}", "{?}", InvalidParameterName)]
    [InlineData("/{1?}", "{1?}", InvalidParameterName)]
    [InlineData("/{a-b?}", "{a-b?}", InvalidParameterName)]
    [InlineData("/{:int?}", "{:int?}", InvalidParameterName)]
    [InlineData("/{id:int:long?}", "{id:int:long?}", MoreThanOneColon)]
    [InlineData("/{id::?}", "{id::?}", MoreThanOneColon)]
    [InlineData("/{id:?}", "{id:?}", NoConstraint)]
    [InlineData("/{id:slug?}", "{id:slug?}", UnknownConstraint)]
    public void ParseChecksAnOptionalParameterWithoutItsQuestionMark(string value, string segment, string category)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, category);

        Assert.DoesNotContain(MisplacedQuestionMark, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{id:slug?}", "slug")]
    [InlineData("/{id: int?}", " int")]
    [InlineData("/{id:Int32?}", "Int32")]
    public void ParseNamesAnUnknownConstraintWithoutTheQuestionMark(string value, string constraint)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => RouteTemplate.Parse(value));

        Assert.Contains($"unknown constraint '{constraint}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{id}/{ID?}", "{ID?}", "{id}")]
    [InlineData("/{id?}/{id?}", "{id?}", "{id?}")]
    [InlineData("/{id:int?}/{Id?}", "{Id?}", "{id:int?}")]
    [InlineData("/{id}/x/{ID:int?}", "{ID:int?}", "{id}")]
    public void ParseRejectsARepeatedNameIgnoringTheQuestionMark(string value, string segment, string earlier)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, RepeatedName);

        Assert.Contains($"'{earlier}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{id?}/details", "details", "{id?}")]
    [InlineData("/products/{id?}/{name}", "{name}", "{id?}")]
    [InlineData("/{a?}/{b?}/c", "c", "{a?}")]
    [InlineData("/{a?}/{b:int}", "{b:int}", "{a?}")]
    [InlineData("/x/{id:int?}/y", "y", "{id:int?}")]
    [InlineData("/x/{id:int?}/%7Bid%7D", "%7Bid%7D", "{id:int?}")]
    public void ParseRejectsASegmentAfterAnOptionalParameterThatIsNotOptional(
        string value,
        string segment,
        string optional)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, FollowsOptional);

        Assert.Contains($"'{optional}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{id?}/{1a}", "{1a}", InvalidParameterName)]
    [InlineData("/{id?}/a?b", "a?b", QuestionMarkOrHash)]
    [InlineData("/{id?}/{x?:int}", "{x?:int}", MisplacedQuestionMark)]
    [InlineData("/{id?}/{x:slug}", "{x:slug}", UnknownConstraint)]
    [InlineData("/{id?}/a\tb", "a\\u0009b", ControlCharacter)]
    public void ParseReportsASegmentsOwnProblemBeforeItFollowsAnOptionalParameter(
        string value,
        string segment,
        string category)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, category);

        Assert.DoesNotContain(FollowsOptional, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{id?}/{ID}", "{ID}")]
    [InlineData("/{id?}/{id:int}", "{id:int}")]
    public void ParseReportsASegmentThatFollowsAnOptionalParameterBeforeARepeatedName(string value, string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, FollowsOptional);

        Assert.DoesNotContain(RepeatedName, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{id?}/details/{1a}", "details", FollowsOptional)]
    [InlineData("/{1a}/{id?}/details", "{1a}", InvalidParameterName)]
    [InlineData("/{a?}/b/{a}", "b", FollowsOptional)]
    public void ParseReportsTheFirstInvalidSegmentLeftToRight(string value, string segment, string category)
    {
        AssertRejectsSegment(value, segment, category);
    }

    [Theory]
    // No optional parameter.
    [InlineData("/products/{id}", "/products", null)]
    [InlineData("/products/{id}", "/products/42", "id=42")]
    [InlineData("/products/{id}", "/products/42/x", null)]
    // One optional parameter.
    [InlineData("/{id?}", "", "")]
    [InlineData("/{id?}", "/", "")]
    [InlineData("/{id?}", "//", "")]
    [InlineData("/{id?}", "/42", "id=42")]
    [InlineData("/{id?}", "/42/x", null)]
    [InlineData("/products/{id?}", "", null)]
    [InlineData("/products/{id?}", "/products", "")]
    [InlineData("/products/{id?}", "/products/", "")]
    [InlineData("/products/{id?}", "//products//", "")]
    [InlineData("/products/{id?}", "/products/42", "id=42")]
    [InlineData("/products/{id?}", "/products/42/", "id=42")]
    [InlineData("/products/{id?}", "/products/42/x", null)]
    [InlineData("/products/{id?}", "/items", null)]
    [InlineData("/products/{id?}", "/items/42", null)]
    [InlineData("/products/{id:int?}", "/products", "")]
    [InlineData("/products/{id:int?}", "/products/42", "id=42")]
    [InlineData("/products/{id:int?}", "/products/x", null)]
    [InlineData("/products/{id:int?}", "/products/%2042", null)]
    // Two optional parameters after a literal, a required parameter and another literal.
    [InlineData("/stores/{store}/items/{id?}/{tab?}", "", null)]
    [InlineData("/stores/{store}/items/{id?}/{tab?}", "/stores", null)]
    [InlineData("/stores/{store}/items/{id?}/{tab?}", "/stores/north", null)]
    [InlineData("/stores/{store}/items/{id?}/{tab?}", "/stores/north/items", "store=north")]
    [InlineData("/stores/{store}/items/{id?}/{tab?}", "/stores/north/items/42", "store=north;id=42")]
    [InlineData("/stores/{store}/items/{id?}/{tab?}", "/stores/north/items/42/info", "store=north;id=42;tab=info")]
    [InlineData("/stores/{store}/items/{id?}/{tab?}", "/stores/north/items/42/info/x", null)]
    [InlineData("/stores/{store}/items/{id?}/{tab?}", "/stores/north/item/42", null)]
    [InlineData("/stores/{store}/items/{id?}/{tab?}", "/stores/north/items//info", "store=north;id=info")]
    // Two optional parameters with constraints.
    [InlineData("/items/{id:int?}/{tab:alpha?}", "/items", "")]
    [InlineData("/items/{id:int?}/{tab:alpha?}", "/items/42", "id=42")]
    [InlineData("/items/{id:int?}/{tab:alpha?}", "/items/42/info", "id=42;tab=info")]
    [InlineData("/items/{id:int?}/{tab:alpha?}", "/items/info", null)]
    [InlineData("/items/{id:int?}/{tab:alpha?}", "/items/42/7", null)]
    [InlineData("/items/{id:int?}/{tab:alpha?}", "/items/x/info", null)]
    [InlineData("/items/{id:int?}/{tab:alpha?}", "/items/42/info/x", null)]
    // Only optional parameters.
    [InlineData("/{a?}/{b:bool?}", "/", "")]
    [InlineData("/{a?}/{b:bool?}", "/x", "a=x")]
    [InlineData("/{a?}/{b:bool?}", "/x/TRUE", "a=x;b=TRUE")]
    [InlineData("/{a?}/{b:bool?}", "/x/yes", null)]
    public void MatchesEachSegmentCountFromTheRequiredSegmentsToAll(string value, string path, string? expected)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        IReadOnlyDictionary<string, string>? values = template.Match(path);

        if (expected is null)
        {
            Assert.Null(values);
            return;
        }

        Assert.NotNull(values);
        Assert.Equal(Sorted(ParseValues(expected)), Sorted(values));
    }

    [Theory]
    [InlineData("/products/{id?}", "/products")]
    [InlineData("/{id?}", "/")]
    [InlineData("/{a?}/{b?}", "")]
    public void AMatchThatCapturesNothingReturnsTheSharedEmptyDictionary(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        IReadOnlyDictionary<string, string>? values = template.Match(path);

        Assert.Same(ReadOnlyDictionary<string, string>.Empty, values);
    }

    [Fact]
    public void AnOmittedOptionalParameterHasNoEntry()
    {
        RouteTemplate template = RouteTemplate.Parse("/stores/{store}/{id?}");

        IReadOnlyDictionary<string, string>? values = template.Match("/stores/north");

        Assert.NotNull(values);
        Assert.Equal(["store"], values.Keys);
        Assert.False(values.ContainsKey("id"));
        Assert.False(values.TryGetValue("id", out _));
    }

    [Theory]
    [InlineData("tab")]
    [InlineData("TAB")]
    [InlineData("Tab")]
    public void ACapturedOptionalValueIsLookedUpWithoutRegardToCase(string key)
    {
        RouteTemplate template = RouteTemplate.Parse("/items/{id?}/{Tab?}");

        IReadOnlyDictionary<string, string>? values = template.Match("/items/42/info");

        Assert.NotNull(values);
        Assert.Equal(["Tab", "id"], values.Keys.Order(StringComparer.Ordinal));
        Assert.True(values.TryGetValue(key, out string? value));
        Assert.Equal("info", value);
    }

    [Fact]
    public void AMatchThatCapturesAnOptionalValueReturnsItsOwnDictionary()
    {
        RouteTemplate template = RouteTemplate.Parse("/items/{id?}");

        IReadOnlyDictionary<string, string>? first = template.Match("/items/1");
        IReadOnlyDictionary<string, string>? second = template.Match("/items/2");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal("1", Assert.Single(first).Value);
        Assert.Equal("2", Assert.Single(second).Value);
    }

    [Theory]
    [InlineData("/files/a%2Fb", "a/b")]
    [InlineData("/files/%20", " ")]
    [InlineData("/files/%FF", "%FF")]
    public void ACapturedOptionalValueIsTheDecodedSegment(string path, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{name?}");

        Assert.Equal(expected, template.Match(path)?["name"]);
    }

    [Theory]
    [InlineData("/products/{id?}", "", "/products")]
    [InlineData("/products/{id?}", "other=x", "/products")]
    [InlineData("/products/{id:int?}", "other=x", "/products")]
    [InlineData("/{id?}", "", "/")]
    [InlineData("/{id?}", "other=x", "/")]
    [InlineData("/{a?}/{b?}", "other=x", "/")]
    [InlineData("/x/{a}/{b?}/{c?}", "a=1;other=x", "/x/1")]
    public void FormatLeavesOutAnOptionalParameterWithoutAnEntry(string value, string given, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.Equal(expected, template.Format(ParseValues(given)));
    }

    [Theory]
    [InlineData("", "/a")]
    [InlineData("b=1", "/a/1")]
    [InlineData("b=1;c=2", "/a/1/2")]
    [InlineData("b=1;c=2;x=3", "/a/1/2")]
    [InlineData("x=3", "/a")]
    public void FormatWritesTheOptionalParametersThatAreGiven(string given, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse("/a/{b?}/{c?}");

        Assert.Equal(expected, template.Format(ParseValues(given)));
    }

    [Fact]
    public void FormatIgnoresEntriesTheTemplateDoesNotUseAfterAnOptionalParameterIsLeftOut()
    {
        RouteTemplate template = RouteTemplate.Parse("/a/{b?}");

        string path = template.Format(new Dictionary<string, string> { ["c"] = "1", [""] = "", ["x"] = null! });

        Assert.Equal("/a", path);
    }

    [Theory]
    [InlineData(false, "ID")]
    [InlineData(false, "Id")]
    [InlineData(true, "ID")]
    public void FormatLooksUpAnOptionalParameterWithoutRegardToCase(bool caseInsensitiveDictionary, string key)
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id?}");
        var values = new Dictionary<string, string>(
            caseInsensitiveDictionary ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            [key] = "42",
        };

        Assert.Equal("/products/42", template.Format(values));
    }

    [Theory]
    [InlineData(false, "ID")]
    [InlineData(true, "ID")]
    [InlineData(true, "id")]
    public void FormatLooksUpAnOptionalParameterThroughAReadOnlyWrapper(bool caseInsensitiveDictionary, string key)
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id?}/{tab?}");
        var inner = new Dictionary<string, string>(
            caseInsensitiveDictionary ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            [key] = "42",
        };

        Assert.Equal("/products/42", template.Format(new ReadOnlyDictionary<string, string>(inner)));
    }

    [Fact]
    public void FormatRejectsAnAmbiguousOptionalParameterThroughAReadOnlyWrapper()
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id?}");
        var inner = new Dictionary<string, string>(StringComparer.Ordinal) { ["Id"] = "1", ["ID"] = "2" };

        AssertRejectsValue(
            () => template.Format(new ReadOnlyDictionary<string, string>(inner)),
            "id",
            Ambiguous);
    }

    [Fact]
    public void FormatFindsADifferentlyCasedKeyInADictionaryWithAnotherIgnoreCaseComparer()
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id?}");
        var values = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase) { ["ID"] = "42" };

        Assert.Equal("/products/42", template.Format(values));
    }

    [Fact]
    public void FormatLooksUpAnOptionalParameterAsWrittenBeforeOtherSpellings()
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id?}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["ID"] = "1", ["id"] = "2" };

        Assert.Equal("/products/2", template.Format(values));
    }

    [Fact]
    public void FormatRejectsAnOptionalParameterThatMatchesMoreThanOneKeyIgnoringCase()
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id?}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["Id"] = "1", ["ID"] = "2" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "id", Ambiguous);

        Assert.Contains("'Id'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'ID'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatRejectsANullValueForAnOptionalParameter()
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id?}");
        var values = new Dictionary<string, string> { ["id"] = null! };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "id", NullValue);

        Assert.Contains(HowToLeaveOut, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("ID")]
    public void FormatRejectsAnEmptyValueForAnOptionalParameterAndSaysHowToLeaveItOut(string key)
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id?}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = "" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "id", EmptyValue);

        Assert.Contains(HowToLeaveOut, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NoValue, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatRejectsALoneSurrogateInAnOptionalValue()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{name?}");
        var values = new Dictionary<string, string> { ["name"] = "a\uD800" };

        AssertRejectsValue(() => template.Format(values), "name", NotValidUtf16);
    }

    [Theory]
    [InlineData("x")]
    [InlineData(" 42")]
    [InlineData("4.2")]
    public void FormatRejectsAnOptionalValueThatDoesNotSatisfyItsConstraint(string value)
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id:int?}");
        var values = new Dictionary<string, string> { ["id"] = value };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "id", UnsatisfiedConstraint);

        Assert.Contains("'int'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatStillRejectsAMissingRequiredParameterBeforeAnOptionalOne()
    {
        RouteTemplate template = RouteTemplate.Parse("/{a}/{b?}");

        ArgumentException exception = AssertRejectsValue(
            () => template.Format(new Dictionary<string, string> { ["b"] = "1" }),
            "a",
            NoValue);

        Assert.DoesNotContain(Gap, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/a/{b?}/{c?}", "c", "b")]
    [InlineData("/a/{b?}/{c:int?}", "c", "b")]
    [InlineData("/{b?}/{x?}/{c?}", "c", "b")]
    public void FormatRejectsAnOptionalValueAfterAnOptionalParameterThatIsLeftOut(
        string value,
        string later,
        string earlier)
    {
        RouteTemplate template = RouteTemplate.Parse(value);
        var values = new Dictionary<string, string> { [later] = "1" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), later, Gap);

        Assert.Contains($"'{earlier}' is left out", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NoValue, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatReportsTheFirstOptionalValueAfterAGap()
    {
        RouteTemplate template = RouteTemplate.Parse("/{b?}/{c?}/{d?}");
        var values = new Dictionary<string, string> { ["c"] = "1", ["d"] = "2" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "c", Gap);

        Assert.DoesNotContain("'d'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", EmptyValue)]
    [InlineData("x", UnsatisfiedConstraint)]
    public void FormatReportsAValueProblemBeforeAGap(string value, string category)
    {
        RouteTemplate template = RouteTemplate.Parse("/a/{b?}/{c:int?}");
        var values = new Dictionary<string, string> { ["c"] = value };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "c", category);

        Assert.DoesNotContain(Gap, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatReportsALoneSurrogateBeforeAGap()
    {
        RouteTemplate template = RouteTemplate.Parse("/a/{b?}/{c?}");
        var values = new Dictionary<string, string> { ["c"] = "4" + (char)0xD800 };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "c", NotValidUtf16);

        Assert.DoesNotContain(Gap, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatReportsAnAmbiguousKeyBeforeAGap()
    {
        RouteTemplate template = RouteTemplate.Parse("/a/{b?}/{tab?}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["TAB"] = "1", ["Tab"] = "2" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "tab", Ambiguous);

        Assert.DoesNotContain(Gap, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/products/{id?}", "/products/")]
    [InlineData("/products/{id?}", "//products//42/")]
    [InlineData("/products/{id:int?}", "/products/%2B007")]
    [InlineData("/a/{b?}/{c?}", "/a/x%2Fy")]
    [InlineData("/a/{b?}/{c?}", "a//1//2//")]
    [InlineData("/{a?}", "")]
    public void FormattingTheValuesOfAMatchWithOptionalParametersProducesAPathThatMatchesAgain(
        string value,
        string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);
        IReadOnlyDictionary<string, string>? matched = template.Match(path);
        Assert.NotNull(matched);

        string formatted = template.Format(matched);
        IReadOnlyDictionary<string, string>? rematched = template.Match(formatted);

        Assert.NotNull(rematched);
        Assert.Equal(Sorted(matched), Sorted(rematched));
        Assert.Equal(formatted, template.Format(rematched));
    }

    [Fact]
    public void GeneratedTemplatesWithOptionalParametersRoundTrip()
    {
        (string? Constraint, string[] Values)[] kinds =
        [
            (null, ["a", " ", "%", "/", "a/b", "%2F", "?", "#", "{v}", "é", "\U0001F600", "\0", "\n", ".", ".."]),
            ("int", ["0", "+007", "-2147483648"]),
            ("decimal", [".5", "-1.25"]),
            ("bool", ["true", "FALSE"]),
            ("guid", ["6f9619ff-8b86-d011-b42d-00cf4fc964ff"]),
            ("alpha", ["café", "\U0001D49C"]),
        ];
        var random = new Random(20261126);

        for (var i = 0; i < 500; i++)
        {
            var template = new StringBuilder();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            int requiredCount = random.Next(0, 4);
            int optionalCount = random.Next(0, 4);
            int givenCount = random.Next(0, optionalCount + 1);
            for (var segment = 0; segment < requiredCount + optionalCount; segment++)
            {
                bool optional = segment >= requiredCount;
                if (!optional && random.Next(3) == 0)
                {
                    template.Append("/lit%20").Append(segment.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                string name = "p" + segment.ToString(CultureInfo.InvariantCulture);
                (string? constraint, string[] choices) = kinds[random.Next(kinds.Length)];
                template.Append("/{").Append(name);
                if (constraint is not null)
                    template.Append(':').Append(constraint);

                template.Append(optional ? "?}" : "}");
                if (optional && segment - requiredCount >= givenCount)
                    continue;

                string choice = choices[random.Next(choices.Length)];
                expected[name] = choice;
                values[random.Next(2) == 0 ? name : name.ToUpperInvariant()] = choice;
            }

            values["unused" + i.ToString(CultureInfo.InvariantCulture)] = "x";
            RouteTemplate parsed = RouteTemplate.Parse(template.Length == 0 ? "/" : template.ToString());

            string path = parsed.Format(values);
            IReadOnlyDictionary<string, string>? matched = parsed.Match(path);

            Assert.NotNull(matched);
            Assert.Equal(Sorted(expected), Sorted(matched));
            Assert.Equal(path, parsed.Format(matched));
        }
    }

    // Reads entries written 'key=value;key=value'; an empty string is an empty dictionary.
    private static Dictionary<string, string> ParseValues(string entries)
    {
        var values = new Dictionary<string, string>();
        foreach (string entry in entries.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = entry.Split('=');
            values.Add(pair[0], pair[1]);
        }

        return values;
    }

    private static ArgumentException AssertRejectsSegment(string value, string segment, string category)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => RouteTemplate.Parse(value));

        Assert.Equal("value", exception.ParamName);
        Assert.Contains($"'{segment}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(category, exception.Message, StringComparison.Ordinal);
        return exception;
    }

    private static ArgumentException AssertRejectsValue(Func<string> format, string parameterName, string category)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(format);

        Assert.Equal("pathValues", exception.ParamName);
        Assert.Contains($"'{parameterName}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(category, exception.Message, StringComparison.Ordinal);
        return exception;
    }

    private static List<KeyValuePair<string, string>> Sorted(IReadOnlyDictionary<string, string> values)
    {
        var list = new List<KeyValuePair<string, string>>(values);
        list.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        return list;
    }
}
