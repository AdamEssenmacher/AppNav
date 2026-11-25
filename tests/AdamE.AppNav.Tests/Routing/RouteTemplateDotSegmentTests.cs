using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTemplateDotSegmentTests
{
    // A short phrase that identifies each kind of rejection, so that tests pin which check reported a problem.
    private const string DotSegmentLiteral = "is a dot segment ('.' or '..' once decoded)";
    private const string DotSegmentValue = "has a value that is a dot segment";
    private const string DotSegmentPart = "has a value with a dot segment part";
    private const string DotSegmentReason = "which a URI parser may remove";
    private const string FollowsCatchAll = "follows the catch-all parameter";
    private const string FollowsOptional = "follows the optional parameter";
    private const string InvalidParameterName = "invalid parameter name";
    private const string NotValidUtf16 = "not valid UTF-16";
    private const string EmptyPart = "has a value with an empty part";
    private const string EmptyValue = "has an empty value";
    private const string UnsatisfiedConstraint = "does not satisfy its constraint";
    private const string Gap = "cannot be written because";

    private static readonly string[] DotSegments = [".", ".."];

    // Text with dots that is not exactly '.' or '..' once decoded, which a URI parser keeps.
    private static readonly string[] NearMisses = ["...", "a..b", ".well-known", "..a", "a.", ".a.", "%2E%2E%2E"];

    [Fact]
    public void AUriWithAnAuthorityRemovesDotSegmentsEvenWhenTheyArePercentEncoded()
    {
        Assert.Equal("/b", new Uri("https://example.test/a/../b").AbsolutePath);
        Assert.Equal("/b", new Uri("https://example.test/a/%2E%2E/b").AbsolutePath);
        Assert.Equal("/a/b", new Uri("https://example.test/a/%2e/b").AbsolutePath);
        Assert.Equal("/b", new Uri("app://host/a/%2e%2E/b").AbsolutePath);
    }

    // Without an authority, System.Uri keeps the dots and decodes them, so Match must refuse them itself.
    [Fact]
    public void AUriWithoutAnAuthorityKeepsDotSegmentsAndMatchRefusesThem()
    {
        string path = new Uri("app:/files/%2E%2E/secret").AbsolutePath;

        Assert.Equal("/files/../secret", path);
        Assert.Null(RouteTemplate.Parse("/files/{*path}").Match(path));
        Assert.Null(RouteTemplate.Parse("/files/{a}/{b}").Match(path));
    }

    // An encoded slash stays inside a parameter's segment, so its value can still read as a relative file path.
    [Fact]
    public void AParameterValueCanContainDotSegmentsBetweenEncodedSlashes()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{name}");

        IReadOnlyDictionary<string, string>? matched = template.Match("/files/..%2F..%2Fsecret");
        Assert.NotNull(matched);
        Assert.Equal("../../secret", matched["name"]);

        string formatted = template.Format(matched);
        Assert.Equal("/files/..%2F..%2Fsecret", formatted);
        Assert.Equal("../../secret", template.Match(formatted)?["name"]);
    }

    [Theory]
    [InlineData("/..", "..")]
    [InlineData("/.", ".")]
    [InlineData("/../a/b", "..")]
    [InlineData("/a/../b", "..")]
    [InlineData("/a/./b", ".")]
    [InlineData("/a/b/..", "..")]
    [InlineData("/a/b/.", ".")]
    [InlineData("/a/%2E%2E/b", "%2E%2E")]
    [InlineData("/a/%2e%2e/b", "%2e%2e")]
    [InlineData("/a/%2e%2E/b", "%2e%2E")]
    [InlineData("/a/%2E%2e/b", "%2E%2e")]
    [InlineData("/a/%2E/b", "%2E")]
    [InlineData("/a/%2e/b", "%2e")]
    [InlineData("/.%2E", ".%2E")]
    [InlineData("/%2e.", "%2e.")]
    [InlineData("//a//..//", "..")]
    [InlineData("/{id}/..", "..")]
    public void ParseRejectsALiteralThatIsADotSegment(string value, string segment)
    {
        ArgumentException exception = AssertRejectsSegment(value, segment, DotSegmentLiteral);

        Assert.Contains(DotSegmentReason, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{*rest}/..", "..")]
    [InlineData("/{*rest}/%2E", "%2E")]
    public void ParseReportsADotSegmentBeforeASegmentThatFollowsACatchAll(string value, string segment)
    {
        AssertRejectsSegment(value, segment, DotSegmentLiteral, FollowsCatchAll);
    }

    [Theory]
    [InlineData("/{id?}/..", "..")]
    [InlineData("/{id?}/.%2e", ".%2e")]
    public void ParseReportsADotSegmentBeforeASegmentThatFollowsAnOptionalParameter(string value, string segment)
    {
        AssertRejectsSegment(value, segment, DotSegmentLiteral, FollowsOptional);
    }

    [Fact]
    public void ParseReportsAnEarlierInvalidSegmentBeforeADotSegment()
    {
        AssertRejectsSegment("/{1a}/..", "{1a}", InvalidParameterName, DotSegmentLiteral);
    }

    [Fact]
    public void ALiteralWithDotsThatIsNotADotSegmentMatchesItself()
    {
        foreach (string literal in NearMisses)
        {
            RouteTemplate template = RouteTemplate.Parse("/a/" + literal + "/b");

            string formatted = template.Format(new Dictionary<string, string>());

            Assert.NotNull(template.Match("/a/" + literal + "/b"));
            Assert.Equal("/a/" + Uri.EscapeDataString(Uri.UnescapeDataString(literal)) + "/b", formatted);
            Assert.NotNull(template.Match(formatted));
        }
    }

    // Each path has the segment count and literals of the other path in its row, so only the dot segment stops it.
    [Theory]
    [InlineData("/files/{name}", "/files/..", "/files/x")]
    [InlineData("/files/{name}", "/files/.", "/files/x")]
    [InlineData("/files/{name}", "/files/%2E%2E", "/files/x")]
    [InlineData("/files/{name}", "/files/%2e%2e", "/files/x")]
    [InlineData("/files/{name}", "/files/%2e%2E", "/files/x")]
    [InlineData("/files/{name}", "/files/%2E%2e", "/files/x")]
    [InlineData("/files/{name}", "/files/%2E", "/files/x")]
    [InlineData("/files/{name}", "/files/%2e", "/files/x")]
    [InlineData("/files/{name}", "/files/.%2E", "/files/x")]
    [InlineData("/files/{name}", "/files/%2e.", "/files/x")]
    [InlineData("/files/{name?}", "/files/..", "/files/x")]
    [InlineData("/files/{name?}", "/files/%2E", "/files/x")]
    [InlineData("/{a?}/{b?}", "/x/..", "/x/x")]
    [InlineData("/{a}/{b}/{c}", "/../y/z", "/x/y/z")]
    [InlineData("/{a}/{b}/{c}", "/x/./z", "/x/x/z")]
    [InlineData("/{a}/{b}/{c}", "/x/y/..", "/x/y/x")]
    [InlineData("/a/{b}/c", "/a/../c", "/a/x/c")]
    public void MatchDoesNotMatchADotSegmentInAParameterPosition(string value, string path, string withoutDots)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.NotNull(template.Match(withoutDots));
        Assert.Null(template.Match(path));
    }

    // These would fail on the literal comparison or the segment count anyway. They document that Match does not
    // resolve a dot segment into the path a URI parser would make of it.
    [Theory]
    [InlineData("/a/b", "/a/..")]
    [InlineData("/b", "/a/../b")]
    [InlineData("/a/b", "/a/./b")]
    public void MatchDoesNotResolveADotSegment(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.Null(template.Match(path));
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("guid")]
    [InlineData("alpha")]
    public void MatchDoesNotMatchADotSegmentInAConstrainedParameterPosition(string constraint)
    {
        RouteTemplate template = RouteTemplate.Parse($"/items/{{v:{constraint}}}");

        Assert.Null(template.Match("/items/."));
        Assert.Null(template.Match("/items/.."));
        Assert.Null(template.Match("/items/%2E%2E"));
    }

    [Theory]
    [InlineData("/files/..")]
    [InlineData("/files/.")]
    [InlineData("/files/../a")]
    [InlineData("/files/a/../b")]
    [InlineData("/files/a/b/..")]
    [InlineData("/files/./a")]
    [InlineData("/files/a/.")]
    [InlineData("/files/%2E%2E/a")]
    [InlineData("/files/a/%2e")]
    [InlineData("/files/a/%2e%2E")]
    [InlineData("/files/%2E%2e%2Fa")]
    [InlineData("/files/a%2F..%2Fb")]
    [InlineData("/files/a%2f.")]
    [InlineData("/files/%2E%2E%2Fa")]
    [InlineData("/files/a%2F%2E%2E")]
    [InlineData("/files/%2F..%2F")]
    public void MatchDoesNotMatchADotSegmentAmongTheSegmentsACatchAllCaptures(string path)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");

        Assert.Null(template.Match(path));
    }

    [Theory]
    [InlineData("/../a")]
    [InlineData("/%2E/a")]
    public void MatchDoesNotMatchADotSegmentBeforeACatchAll(string path)
    {
        RouteTemplate template = RouteTemplate.Parse("/{first}/{*rest}");

        Assert.Null(template.Match(path));
    }

    [Fact]
    public void MatchCapturesTextWithDotsThatIsNotADotSegment()
    {
        RouteTemplate parameter = RouteTemplate.Parse("/files/{name}");
        RouteTemplate catchAll = RouteTemplate.Parse("/files/{*path}");

        foreach (string text in NearMisses)
        {
            string decoded = Uri.UnescapeDataString(text);
            Assert.Equal(decoded, parameter.Match("/files/" + text)?["name"]);
            Assert.Equal(decoded + "/" + decoded, catchAll.Match("/files/" + text + "%2F" + text)?["path"]);
        }
    }

    [Theory]
    [InlineData("/files/{name}", ".")]
    [InlineData("/files/{name}", "..")]
    [InlineData("/files/{name?}", ".")]
    [InlineData("/files/{name?}", "..")]
    [InlineData("/{name}/files", "..")]
    public void FormatRejectsAParameterValueThatIsADotSegment(string value, string dotSegment)
    {
        RouteTemplate template = RouteTemplate.Parse(value);
        var values = new Dictionary<string, string> { ["name"] = dotSegment };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "name", DotSegmentValue);

        Assert.Contains(DotSegmentReason, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../a")]
    [InlineData("a/..")]
    [InlineData("a/../b")]
    [InlineData("./a")]
    [InlineData("a/.")]
    [InlineData("a/./b")]
    public void FormatRejectsACatchAllValueWithAPartThatIsADotSegment(string dotted)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");
        var values = new Dictionary<string, string> { ["path"] = dotted };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "path", DotSegmentPart);

        Assert.Contains(DotSegmentReason, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(DotSegmentValue, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("guid")]
    [InlineData("alpha")]
    public void NoConstraintAcceptsADotSegment(string constraint)
    {
        RouteTemplate template = RouteTemplate.Parse($"/items/{{v:{constraint}}}");

        foreach (string dotSegment in DotSegments)
        {
            var values = new Dictionary<string, string> { ["v"] = dotSegment };

            ArgumentException exception = AssertRejectsValue(
                () => template.Format(values),
                "v",
                UnsatisfiedConstraint);

            Assert.DoesNotContain(DotSegmentValue, exception.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("../")]
    [InlineData("/..")]
    [InlineData("a//..")]
    public void FormatReportsAnEmptyPartBeforeADotSegmentPart(string value)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");
        var values = new Dictionary<string, string> { ["path"] = value };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "path", EmptyPart);

        Assert.DoesNotContain(DotSegmentPart, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatReportsALoneSurrogateBeforeADotSegmentPart()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{*path}");
        var values = new Dictionary<string, string> { ["path"] = "../\uD800" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "path", NotValidUtf16);

        Assert.DoesNotContain(DotSegmentPart, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatReportsADotSegmentBeforeAValueThatFollowsALeftOutParameter()
    {
        RouteTemplate template = RouteTemplate.Parse("/a/{b?}/{c?}");
        var values = new Dictionary<string, string> { ["c"] = ".." };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "c", DotSegmentValue);

        Assert.DoesNotContain(Gap, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatReportsTheFirstParameterInTemplateOrder()
    {
        RouteTemplate template = RouteTemplate.Parse("/{a}/{b}/{*c}");
        var values = new Dictionary<string, string> { ["a"] = "..", ["b"] = "", ["c"] = "x/." };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "a", DotSegmentValue);

        Assert.DoesNotContain(EmptyValue, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(DotSegmentPart, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatWritesTextWithDotsThatIsNotADotSegmentAndAUriKeepsItsSegments()
    {
        RouteTemplate parameter = RouteTemplate.Parse("/files/{name}");
        RouteTemplate catchAll = RouteTemplate.Parse("/files/{*path}");

        foreach (string text in NearMisses)
        {
            var single = new Dictionary<string, string> { ["name"] = text };
            var parts = new Dictionary<string, string> { ["path"] = text + "/" + text };

            string path = parameter.Format(single);
            string joined = catchAll.Format(parts);

            Assert.Equal(text, parameter.Match(path)?["name"]);
            Assert.Equal(text + "/" + text, catchAll.Match(joined)?["path"]);
            Assert.Equal(path, new Uri("https://example.test" + path).AbsolutePath);
            Assert.Equal(joined, new Uri("https://example.test" + joined).AbsolutePath);
        }
    }

    // Asserts that System.Uri, given a formatted path under an authority, removes none of its segments as dot
    // segments. Shared with the generated round trips in the other test classes. Segments are compared decoded,
    // because System.Uri decodes some characters, such as '%7E'. Trailing spaces in the last segment are always
    // ignored, because System.Uri drops a trailing '%20' there when the URI has an encoded non-ASCII character, which
    // has nothing to do with dot segments and is not what this checks.
    internal static void AssertUriKeepsEverySegment(string path)
    {
        string[] written = path.Split('/');
        string[] resolved = new Uri("https://example.test" + path).AbsolutePath.Split('/');

        Assert.Equal(written.Length, resolved.Length);
        for (var i = 0; i < written.Length; i++)
        {
            string expected = Uri.UnescapeDataString(written[i]);
            string actual = Uri.UnescapeDataString(resolved[i]);
            if (i == written.Length - 1)
            {
                expected = expected.TrimEnd(' ');
                actual = actual.TrimEnd(' ');
            }

            Assert.Equal(expected, actual);
        }
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
}
