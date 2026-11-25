using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTemplateCatchAllTests
{
    // A short phrase that identifies each kind of rejection, so that tests pin which check reported a problem.
    private const string MisplacedAsterisk = "only allowed at the start";
    private const string CatchAllSpelling = "'{*name}'";
    private const string OptionalCatchAll = "cannot be optional";
    private const string NothingLeft = "already matches when nothing is left of the path";
    private const string ConstrainedCatchAll = "cannot have a constraint";
    private const string FollowsCatchAll = "follows the catch-all parameter";
    private const string MustBeLast = "must be the last segment";
    private const string FollowsOptional = "follows the optional parameter";
    private const string CatchAllAfterOptional = "a catch-all parameter cannot follow an optional parameter";
    private const string OnlyOptionalCanFollow = "only optional parameters can follow";
    private const string MisplacedQuestionMark = "only allowed at the end";
    private const string InvalidParameterName = "invalid parameter name";
    private const string MoreThanOneColon = "more than one ':'";
    private const string NoConstraint = "no constraint after ':'";
    private const string UnknownConstraint = "unknown constraint";
    private const string RepeatedName = "repeats the name";
    private const string QuestionMarkOrHash = "'?' or '#'";
    private const string ControlCharacter = "control character";

    private const string NoValue = "has no value";
    private const string NullValue = "has a null value";
    private const string EmptyValue = "has an empty value";
    private const string HowToLeaveOut = "to leave out a catch-all parameter, leave it out of the dictionary";
    private const string ExactlyOneSegment = "exactly one path segment";
    private const string NotValidUtf16 = "not valid UTF-16";
    private const string Ambiguous = "ambiguous";
    private const string UnsatisfiedConstraint = "does not satisfy its constraint";
    private const string EmptyPart = "has a value with an empty part";

    [Theory]
    [InlineData("/{*rest}", "/")]
    [InlineData("/{*_}", "/")]
    [InlineData("/{*REST}", "/")]
    [InlineData("/files/{*path}", "/files")]
    [InlineData("//files//{*path}/", "/files")]
    [InlineData("/stores/{store}/files/{*path}", "/stores/north/files")]
    [InlineData("/items/{id:int}/{*rest}", "/items/42")]
    public void ParseAcceptsACatchAllParameterAsTheLastSegment(string value, string pathWithNothingLeft)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.Equal(value, template.Value);
        Assert.NotNull(template.Match(pathWithNothingLeft));
    }

    [Theory]
    [InlineData("/{re*st}", "{re*st}")]
    [InlineData("/{rest*}", "{rest*}")]
    [InlineData("/{**rest}", "{**rest}")]
    [InlineData("/{**}", "{**}")]
    [InlineData("/{*re*st}", "{*re*st}")]
    [InlineData("/{id:*}", "{id:*}")]
    [InlineData("/{*rest:*}", "{*rest:*}")]
    [InlineData("/{id:*int}", "{id:*int}")]
    public void ParseRejectsAnAsteriskThatIsNotFirst(string value, string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, MisplacedAsterisk);

        Assert.Contains(CatchAllSpelling, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(InvalidParameterName, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ConstrainedCatchAll, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{**rest?}", "{**rest?}")]
    [InlineData("/{?*rest}", "{?*rest}")]
    [InlineData("/{*re?st*}", "{*re?st*}")]
    public void ParseReportsAMisplacedAsteriskBeforeAMisplacedQuestionMark(string value, string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, MisplacedAsterisk);

        Assert.DoesNotContain(MisplacedQuestionMark, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(OptionalCatchAll, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{*re?st}", "{*re?st}")]
    [InlineData("/{*rest?:int}", "{*rest?:int}")]
    [InlineData("/{*rest??}", "{*rest??}")]
    [InlineData("/{*?rest}", "{*?rest}")]
    public void ParseReportsAMisplacedQuestionMarkInACatchAllParameterBeforeItsOtherProblems(
        string value,
        string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, MisplacedQuestionMark);

        Assert.DoesNotContain(OptionalCatchAll, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ConstrainedCatchAll, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(InvalidParameterName, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{*}", "{*}")]
    [InlineData("/{*?}", "{*?}")]
    [InlineData("/{*1a}", "{*1a}")]
    [InlineData("/{*a-b}", "{*a-b}")]
    [InlineData("/{* rest}", "{* rest}")]
    [InlineData("/{*:int}", "{*:int}")]
    [InlineData("/{*1a?}", "{*1a?}")]
    [InlineData("/{*1a:int}", "{*1a:int}")]
    public void ParseRejectsAnInvalidCatchAllNameBeforeItsOtherProblems(string value, string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, InvalidParameterName);

        Assert.DoesNotContain(MisplacedAsterisk, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(OptionalCatchAll, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ConstrainedCatchAll, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/files/{*rest?}", "{*rest?}")]
    [InlineData("/{*rest?}", "{*rest?}")]
    [InlineData("/{*rest:int?}", "{*rest:int?}")]
    [InlineData("/{*rest:slug?}", "{*rest:slug?}")]
    public void ParseRejectsAnOptionalCatchAllParameterBeforeItsConstraint(string value, string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, OptionalCatchAll);

        Assert.Contains(NothingLeft, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ConstrainedCatchAll, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(UnknownConstraint, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/files/{*rest:int}", "{*rest:int}")]
    [InlineData("/{*rest:INT}", "{*rest:INT}")]
    [InlineData("/{*rest:alpha}", "{*rest:alpha}")]
    [InlineData("/{*rest:slug}", "{*rest:slug}")]
    [InlineData("/{*rest:}", "{*rest:}")]
    [InlineData("/{*rest:int:long}", "{*rest:int:long}")]
    [InlineData("/{*rest::}", "{*rest::}")]
    public void ParseRejectsACatchAllParameterWithAConstraintBeforeTheConstraintsOwnProblems(
        string value,
        string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, ConstrainedCatchAll);

        Assert.DoesNotContain(OptionalCatchAll, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(UnknownConstraint, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NoConstraint, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MoreThanOneColon, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{*rest}/x", "x", "{*rest}")]
    [InlineData("/files/{*path}/{id}", "{id}", "{*path}")]
    [InlineData("/files/{*path}/{id:int}", "{id:int}", "{*path}")]
    [InlineData("/{*rest}/{tab?}", "{tab?}", "{*rest}")]
    [InlineData("/{*a}/{*b}", "{*b}", "{*a}")]
    [InlineData("/{*REST}/%7Bx%7D", "%7Bx%7D", "{*REST}")]
    [InlineData("/{*rest}/x/{1a}", "x", "{*rest}")]
    public void ParseRejectsASegmentAfterACatchAllParameter(string value, string segment, string catchAll)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, FollowsCatchAll);

        Assert.Contains($"'{catchAll}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(MustBeLast, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{*rest}/{1a}", "{1a}", InvalidParameterName)]
    [InlineData("/{*rest}/a?b", "a?b", QuestionMarkOrHash)]
    [InlineData("/{*rest}/{*x:int}", "{*x:int}", ConstrainedCatchAll)]
    [InlineData("/{*rest}/{x*}", "{x*}", MisplacedAsterisk)]
    [InlineData("/{*rest}/a\tb", "a\\u0009b", ControlCharacter)]
    public void ParseReportsASegmentsOwnProblemBeforeItFollowsACatchAllParameter(
        string value,
        string segment,
        string category)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, category);

        Assert.DoesNotContain(FollowsCatchAll, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{*rest}/{REST}", "{REST}")]
    [InlineData("/{*rest}/{*rest}", "{*rest}")]
    public void ParseReportsASegmentThatFollowsACatchAllParameterBeforeARepeatedName(string value, string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, FollowsCatchAll);

        Assert.DoesNotContain(RepeatedName, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{rest}/{*REST}", "{*REST}", "{rest}")]
    [InlineData("/{id:int}/x/{*Id}", "{*Id}", "{id:int}")]
    public void ParseRejectsACatchAllParameterThatRepeatsAName(string value, string segment, string earlier)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, RepeatedName, FollowsCatchAll);

        Assert.Contains($"'{earlier}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/a/{b?}/{*rest}", "{*rest}", "{b?}")]
    [InlineData("/{b?}/{*rest}", "{*rest}", "{b?}")]
    [InlineData("/{a?}/{b:int?}/{*rest}", "{*rest}", "{a?}")]
    public void ParseRejectsACatchAllParameterAfterAnOptionalParameter(string value, string segment, string optional)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, FollowsOptional);

        Assert.Contains($"'{optional}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(CatchAllAfterOptional, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(OnlyOptionalCanFollow, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FollowsCatchAll, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/a/{b?}/c", "c")]
    [InlineData("/a/{b?}/{c}", "{c}")]
    [InlineData("/a/{b?}/{c:int}", "{c:int}")]
    public void ParseStillSaysOnlyOptionalParametersCanFollowForASegmentThatIsNotACatchAll(
        string value,
        string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, FollowsOptional);

        Assert.Contains(OnlyOptionalCanFollow, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(CatchAllAfterOptional, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/a/{b?}/{*rest?}", "{*rest?}", OptionalCatchAll)]
    [InlineData("/a/{b?}/{*rest:int}", "{*rest:int}", ConstrainedCatchAll)]
    public void ParseReportsACatchAllParametersOwnProblemBeforeItFollowsAnOptionalParameter(
        string value,
        string segment,
        string category)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, category);

        Assert.DoesNotContain(FollowsOptional, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{1a}/{*rest}/x", "{1a}", InvalidParameterName, FollowsCatchAll)]
    [InlineData("/{*rest}/x/{*rest?}", "x", FollowsCatchAll, OptionalCatchAll)]
    [InlineData("/x/{*rest?}/{*rest}", "{*rest?}", OptionalCatchAll, RepeatedName)]
    public void ParseReportsTheFirstInvalidSegmentLeftToRight(
        string value,
        string segment,
        string category,
        string notCategory)
    {
        AssertRejectsSegment(value, segment, category, notCategory);
    }

    [Theory]
    // As the only segment.
    [InlineData("/{*rest}", "", "")]
    [InlineData("/{*rest}", "/", "")]
    [InlineData("/{*rest}", "//", "")]
    [InlineData("/{*rest}", "/a", "rest=a")]
    [InlineData("/{*rest}", "a", "rest=a")]
    [InlineData("/{*rest}", "/a/b", "rest=a/b")]
    [InlineData("/{*rest}", "/a/b/c/d/e", "rest=a/b/c/d/e")]
    // After a literal.
    [InlineData("/files/{*path}", "", null)]
    [InlineData("/files/{*path}", "/", null)]
    [InlineData("/files/{*path}", "/files", "")]
    [InlineData("/files/{*path}", "/files/", "")]
    [InlineData("/files/{*path}", "/files/a", "path=a")]
    [InlineData("/files/{*path}", "/files/a/b/c", "path=a/b/c")]
    [InlineData("/files/{*path}", "/files/files", "path=files")]
    [InlineData("/files/{*path}", "/Files/a", null)]
    [InlineData("/files/{*path}", "/other/files/a", null)]
    // After literals and a required parameter.
    [InlineData("/stores/{store}/files/{*path}", "/stores/north", null)]
    [InlineData("/stores/{store}/files/{*path}", "/stores/north/files", "store=north")]
    [InlineData("/stores/{store}/files/{*path}", "/stores/north/files/a", "store=north;path=a")]
    [InlineData("/stores/{store}/files/{*path}", "/stores/north/files/a/b", "store=north;path=a/b")]
    [InlineData("/stores/{store}/files/{*path}", "/stores/north/items/a/b", null)]
    // After a constrained parameter.
    [InlineData("/items/{id:int}/{*rest}", "/items", null)]
    [InlineData("/items/{id:int}/{*rest}", "/items/42", "id=42")]
    [InlineData("/items/{id:int}/{*rest}", "/items/42/a", "id=42;rest=a")]
    [InlineData("/items/{id:int}/{*rest}", "/items/42/7/x", "id=42;rest=7/x")]
    [InlineData("/items/{id:int}/{*rest}", "/items/x/42", null)]
    // After two required parameters only.
    [InlineData("/{a}/{b}/{*rest}", "/1", null)]
    [InlineData("/{a}/{b}/{*rest}", "/1/2", "a=1;b=2")]
    [InlineData("/{a}/{b}/{*rest}", "/1/2/3/4", "a=1;b=2;rest=3/4")]
    public void MatchesEveryPathWithAtLeastTheSegmentsBeforeTheCatchAll(string value, string path, string? expected)
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
    [InlineData("/files//a///b/", "a/b")]
    [InlineData("//files/a/b//", "a/b")]
    [InlineData("/files/a%20b/c", "a b/c")]
    [InlineData("/files/a%2Fb", "a/b")]
    [InlineData("/files/a%2fb/c", "a/b/c")]
    [InlineData("/files/%FF/x", "%FF/x")]
    [InlineData("/files/./..", "./..")]
    [InlineData("/files/+/%20", "+/ ")]
    public void ACapturedCatchAllValueIsTheDecodedRemainingSegmentsJoinedWithOneSlash(string path, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");

        Assert.Equal(expected, template.Match(path)?["path"]);
    }

    [Fact]
    public void AnEncodedSlashCannotBeToldApartFromASeparator()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");

        Assert.Equal("a/b", template.Match("/files/a/b")?["path"]);
        Assert.Equal("a/b", template.Match("/files/a%2Fb")?["path"]);
    }

    [Theory]
    [InlineData("/{*rest}", "/")]
    [InlineData("/{*rest}", "")]
    [InlineData("/files/{*path}", "/files//")]
    public void AMatchThatCapturesNothingReturnsTheSharedEmptyDictionary(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        IReadOnlyDictionary<string, string>? values = template.Match(path);

        Assert.Same(ReadOnlyDictionary<string, string>.Empty, values);
    }

    [Fact]
    public void ACatchAllParameterHasNoEntryWhenNothingIsLeftOfThePath()
    {
        RouteTemplate template = RouteTemplate.Parse("/stores/{store}/{*path}");

        IReadOnlyDictionary<string, string>? values = template.Match("/stores/north/");

        Assert.NotNull(values);
        Assert.Equal(["store"], values.Keys);
        Assert.False(values.ContainsKey("path"));
        Assert.False(values.TryGetValue("path", out _));
    }

    [Theory]
    [InlineData("path")]
    [InlineData("PATH")]
    [InlineData("Path")]
    public void ACapturedCatchAllValueIsLookedUpWithoutRegardToCase(string key)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{id}/{*Path}");

        IReadOnlyDictionary<string, string>? values = template.Match("/files/1/a/b");

        Assert.NotNull(values);
        Assert.Equal(["Path", "id"], values.Keys.Order(StringComparer.Ordinal));
        Assert.True(values.TryGetValue(key, out string? value));
        Assert.Equal("a/b", value);
    }

    [Fact]
    public void AMatchThatCapturesACatchAllValueReturnsItsOwnDictionary()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");

        IReadOnlyDictionary<string, string>? first = template.Match("/files/a/1");
        IReadOnlyDictionary<string, string>? second = template.Match("/files/a/2");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal("a/1", Assert.Single(first).Value);
        Assert.Equal("a/2", Assert.Single(second).Value);
    }

    [Theory]
    [InlineData("/files/{*path}", "", "/files")]
    [InlineData("/files/{*path}", "other=x", "/files")]
    [InlineData("/{*path}", "", "/")]
    [InlineData("/{*path}", "other=x", "/")]
    [InlineData("/stores/{store}/{*path}", "store=north", "/stores/north")]
    public void FormatWritesNothingForACatchAllParameterWithoutAnEntry(string value, string given, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.Equal(expected, template.Format(ParseValues(given)));
    }

    [Theory]
    [InlineData("a", "/files/a")]
    [InlineData("a/b", "/files/a/b")]
    [InlineData("a/b c", "/files/a/b%20c")]
    [InlineData("a/b/c/d", "/files/a/b/c/d")]
    [InlineData(" / ", "/files/%20/%20")]
    [InlineData("%2F", "/files/%252F")]
    [InlineData("%/?/#", "/files/%25/%3F/%23")]
    [InlineData("./..", "/files/./..")]
    [InlineData("café/\U0001F600", "/files/caf%C3%A9/%F0%9F%98%80")]
    [InlineData("{v}/+", "/files/%7Bv%7D/%2B")]
    public void FormatWritesEachPartOfACatchAllValueAsItsOwnEncodedSegment(string value, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");

        string path = template.Format(new Dictionary<string, string> { ["path"] = value });

        Assert.Equal(expected, path);
        Assert.Equal(value, template.Match(path)?["path"]);
    }

    [Fact]
    public void FormatWritesACatchAllValueAfterTheOtherSegments()
    {
        RouteTemplate template = RouteTemplate.Parse("/stores/{store}/{id:int}/{*path}");
        var values = new Dictionary<string, string> { ["store"] = "north wind", ["id"] = "7", ["path"] = "a/b" };

        Assert.Equal("/stores/north%20wind/7/a/b", template.Format(values));
    }

    [Theory]
    [InlineData(false, "PATH")]
    [InlineData(false, "Path")]
    [InlineData(true, "PATH")]
    public void FormatLooksUpACatchAllParameterWithoutRegardToCase(bool caseInsensitiveDictionary, string key)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");
        var values = new Dictionary<string, string>(
            caseInsensitiveDictionary ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            [key] = "a/b",
        };

        Assert.Equal("/files/a/b", template.Format(values));
    }

    [Fact]
    public void FormatLooksUpACatchAllParameterAsWrittenBeforeOtherSpellings()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["PATH"] = "1", ["path"] = "2" };

        Assert.Equal("/files/2", template.Format(values));
    }

    [Fact]
    public void FormatRejectsACatchAllParameterThatMatchesMoreThanOneKeyIgnoringCase()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["Path"] = "1", ["PATH"] = "2" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "path", Ambiguous);

        Assert.Contains("'Path'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'PATH'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatRejectsANullCatchAllValueAndSaysHowToLeaveItOut()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");
        var values = new Dictionary<string, string> { ["path"] = null! };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "path", NullValue);

        Assert.Contains(HowToLeaveOut, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("PATH")]
    public void FormatRejectsAnEmptyCatchAllValueAndSaysHowToLeaveItOut(string key)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = "" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "path", EmptyValue);

        Assert.Contains(HowToLeaveOut, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ExactlyOneSegment, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(EmptyPart, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("//")]
    [InlineData("/a")]
    [InlineData("a/")]
    [InlineData("/a/")]
    [InlineData("a//b")]
    [InlineData("a/b//c")]
    [InlineData("a///b")]
    [InlineData(" /a/")]
    public void FormatRejectsACatchAllValueWithAnEmptyPart(string value)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");
        var values = new Dictionary<string, string> { ["path"] = value };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "path", EmptyPart);

        Assert.Contains("'//'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a/")]
    [InlineData("/")]
    [InlineData("a//")]
    public void FormatReportsALoneSurrogateInACatchAllValueBeforeAnEmptyPart(string around)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");
        var values = new Dictionary<string, string> { ["path"] = around + (char)0xD800 + around };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "path", NotValidUtf16);

        Assert.DoesNotContain(EmptyPart, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMatchedLoneSurrogateInACatchAllValueCannotBeFormattedBack()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");
        string value = "a/b" + (char)0xD800;

        IReadOnlyDictionary<string, string>? matched = template.Match("/files/" + value);

        Assert.NotNull(matched);
        Assert.Equal(value, matched["path"]);
        AssertRejectsValue(() => template.Format(matched), "path", NotValidUtf16);
    }

    [Theory]
    // At the start, at the end, doubled.
    [InlineData("/files/%2Fa", "a")]
    [InlineData("/files/a%2F", "a")]
    [InlineData("/files/a%2F%2Fb", "a/b")]
    [InlineData("/files/%2Fa%2F", "a")]
    // Mixed with separators between segments.
    [InlineData("/files/a/%2Fb", "a/b")]
    [InlineData("/files/a%2F/b", "a/b")]
    [InlineData("/files/%2F/a/%2F/b%2F%2F/%2F", "a/b")]
    [InlineData("/files/x/a%2Fb%2F/c", "x/a/b/c")]
    public void AnEncodedSlashInACatchAllCreatesNoEmptyPart(string path, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");

        IReadOnlyDictionary<string, string>? matched = template.Match(path);

        Assert.NotNull(matched);
        Assert.Equal(expected, matched["path"]);
        Assert.Equal("/files/" + expected, template.Format(matched));
    }

    [Theory]
    [InlineData("/files/{*path}", "/files/%2F")]
    [InlineData("/files/{*path}", "/files/%2F/%2F")]
    [InlineData("/files/{*path}", "/files/%2F%2F%2f")]
    [InlineData("/{*rest}", "/%2F")]
    public void ACatchAllOfOnlyEncodedSlashesHasNoEntry(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        IReadOnlyDictionary<string, string>? matched = template.Match(path);

        Assert.Same(ReadOnlyDictionary<string, string>.Empty, matched);
    }

    [Fact]
    public void ACatchAllOfOnlyEncodedSlashesHasNoEntryBesideOtherValues()
    {
        RouteTemplate template = RouteTemplate.Parse("/stores/{store}/{*path}");

        IReadOnlyDictionary<string, string>? matched = template.Match("/stores/north/%2F/%2F");

        Assert.NotNull(matched);
        Assert.Equal(["store"], matched.Keys);
        Assert.Equal("/stores/north", template.Format(matched));
    }

    [Fact]
    public void FormatReportsAMissingRequiredParameterBeforeTheCatchAll()
    {
        RouteTemplate template = RouteTemplate.Parse("/{a}/{*rest}");
        var values = new Dictionary<string, string> { ["rest"] = "/" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "a", NoValue);

        Assert.DoesNotContain(EmptyPart, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatReportsAnEarlierUnsatisfiedConstraintBeforeAnEmptyPart()
    {
        RouteTemplate template = RouteTemplate.Parse("/{a:int}/{*rest}");
        var values = new Dictionary<string, string> { ["a"] = "x", ["rest"] = "/" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "a", UnsatisfiedConstraint);

        Assert.DoesNotContain(EmptyPart, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/files/{*path}", "/files")]
    [InlineData("/files/{*path}", "//files//a//b//")]
    [InlineData("/files/{*path}", "/files/a%2Fb")]
    [InlineData("/files/{*path}", "/files/a%20b/%FF/%2B")]
    [InlineData("/files/{*path}", "/files/%2Fa%2F%2F/b%2F")]
    [InlineData("/files/{*path}", "/files/%2F")]
    [InlineData("/{*rest}", "")]
    [InlineData("/{*rest}", "/x/y/./..")]
    [InlineData("/items/{id:int}/{*rest}", "/items/%2B007/a/b")]
    public void FormattingTheValuesOfAMatchWithACatchAllProducesAPathThatMatchesAgain(string value, string path)
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
    public void GeneratedTemplatesWithACatchAllParameterRoundTrip()
    {
        (string? Constraint, string[] Values)[] kinds =
        [
            (null, ["a", " ", "%", "/", "a/b", "%2F", "?", "#", "{v}", "é", "\U0001F600", "\0", "\n", ".", ".."]),
            ("int", ["0", "+007", "-2147483648"]),
            ("bool", ["true", "FALSE"]),
            ("alpha", ["café", "\U0001D49C"]),
        ];
        string[] parts = ["a", " ", "%", "%2F", "?", "#", "{v}", "é", "\U0001F600", "\0", "\n", ".", "..", "+", "~"];
        var random = new Random(20261127);

        for (var i = 0; i < 500; i++)
        {
            var template = new StringBuilder();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            int requiredCount = random.Next(0, 4);
            for (var segment = 0; segment < requiredCount; segment++)
            {
                if (random.Next(3) == 0)
                {
                    template.Append("/lit%20").Append(segment.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                string name = "p" + segment.ToString(CultureInfo.InvariantCulture);
                (string? constraint, string[] choices) = kinds[random.Next(kinds.Length)];
                template.Append("/{").Append(name);
                if (constraint is not null)
                    template.Append(':').Append(constraint);

                template.Append('}');
                string choice = choices[random.Next(choices.Length)];
                expected[name] = choice;
                values[random.Next(2) == 0 ? name : name.ToUpperInvariant()] = choice;
            }

            template.Append("/{*rest}");
            if (random.Next(4) != 0)
            {
                var rest = new StringBuilder();
                int partCount = random.Next(1, 5);
                for (var part = 0; part < partCount; part++)
                {
                    if (part > 0)
                        rest.Append('/');

                    rest.Append(parts[random.Next(parts.Length)]);
                }

                expected["rest"] = rest.ToString();
                values[random.Next(2) == 0 ? "rest" : "REST"] = rest.ToString();
            }

            values["unused" + i.ToString(CultureInfo.InvariantCulture)] = "x";
            RouteTemplate parsed = RouteTemplate.Parse(template.ToString());

            string path = parsed.Format(values);
            IReadOnlyDictionary<string, string>? matched = parsed.Match(path);

            Assert.NotNull(matched);
            Assert.Equal(Sorted(expected), Sorted(matched));
            Assert.Equal(path, parsed.Format(matched));
        }
    }

    [Fact]
    public void GeneratedPathsMatchedByACatchAllTemplateMatchAgainOnceFormatted()
    {
        string[] pieces = ["", "a", "7", " ", "%20", "%2F", "%2f", "%FF", "%", "+", ".", "..", "é", "%C3%A9", "x/y"];
        RouteTemplate[] templates =
        [
            RouteTemplate.Parse("/{*rest}"),
            RouteTemplate.Parse("/a/{*rest}"),
            RouteTemplate.Parse("/{id:int}/{*rest}"),
            RouteTemplate.Parse("/{first}/{*rest}"),
        ];
        var random = new Random(20261128);
        var matchedCount = 0;

        for (var i = 0; i < 500; i++)
        {
            var path = new StringBuilder();
            int pieceCount = random.Next(0, 7);
            for (var piece = 0; piece < pieceCount; piece++)
                path.Append('/').Append(pieces[random.Next(pieces.Length)]);

            RouteTemplate template = templates[random.Next(templates.Length)];
            IReadOnlyDictionary<string, string>? matched = template.Match(path.ToString());
            if (matched is null)
                continue;

            matchedCount++;
            string formatted = template.Format(matched);
            IReadOnlyDictionary<string, string>? rematched = template.Match(formatted);

            Assert.NotNull(rematched);
            Assert.Equal(Sorted(matched), Sorted(rematched));
            Assert.Equal(formatted, template.Format(rematched));
        }

        Assert.True(matchedCount > 100, $"Only {matchedCount} generated paths matched.");
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

    private static ArgumentException AssertRejectsSegment(
        string value,
        string segment,
        string category,
        string? notCategory = null)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => RouteTemplate.Parse(value));

        Assert.Equal("value", exception.ParamName);
        Assert.Contains($"'{segment}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(category, exception.Message, StringComparison.Ordinal);
        if (notCategory is not null)
            Assert.DoesNotContain(notCategory, exception.Message, StringComparison.Ordinal);

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
