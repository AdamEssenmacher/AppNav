using System.Globalization;
using System.Text;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTemplateFormatTests
{
    // A short phrase that identifies each kind of rejection.
    private const string NoValue = "has no value";
    private const string NullValue = "has a null value";
    private const string EmptyValue = "has an empty value";
    private const string NotValidUtf16 = "not valid UTF-16";
    private const string Ambiguous = "ambiguous";
    private const string UnsatisfiedConstraint = "does not satisfy its constraint";

    private static readonly Dictionary<string, string> NoValues = [];

    private static readonly string[] AwkwardValues =
    [
        "a", "%", "%2F", "%252F", "%zz", "%FF", "a%20b", "+", " ", " a ", "/", "//", "a/b/c", "?", "#", "?a=b#c",
        "{", "}", "{v}", "é", "€", "\U0001F600", "\0", "\t", "\n", "\r\n", "\u0085", "\u00A0", "~", "&=;", "...",
        ".a", "a.", "a..b", "%2E",
    ];

    [Theory]
    [InlineData("/", "/")]
    [InlineData("//", "/")]
    [InlineData("/settings", "/settings")]
    [InlineData("/settings/profile", "/settings/profile")]
    [InlineData("//settings//profile/", "/settings/profile")]
    [InlineData("/~a-b_c.d", "/~a-b_c.d")]
    public void FormatsALiteralTemplateWithSingleSlashesAndNoTrailingSlash(string value, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.Equal(expected, template.Format(NoValues));
    }

    [Theory]
    [InlineData("/my files", "/my%20files")]
    [InlineData("/my%20files", "/my%20files")]
    [InlineData("/north%77ind", "/northwind")]
    [InlineData("/a+b", "/a%2Bb")]
    [InlineData("/a%2fb", "/a%2Fb")]
    [InlineData("/a%252Fb", "/a%252Fb")]
    [InlineData("/100%", "/100%25")]
    [InlineData("/%FF", "/%25FF")]
    [InlineData("/café", "/caf%C3%A9")]
    [InlineData("/caf%C3%A9", "/caf%C3%A9")]
    [InlineData("/a%7Bb%7D", "/a%7Bb%7D")]
    [InlineData("/what%3F", "/what%3F")]
    [InlineData("/item%23", "/item%23")]
    [InlineData("/a%09b", "/a%09b")]
    public void FormatsEachLiteralAsItsDecodedTextPercentEncoded(string value, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        string path = template.Format(NoValues);

        Assert.Equal(expected, path);
        Assert.NotNull(template.Match(path));
    }

    [Theory]
    [InlineData("/{id}", "/42")]
    [InlineData("/{id}/details", "/42/details")]
    [InlineData("/products/{id}/details", "/products/42/details")]
    [InlineData("/products/{id}", "/products/42")]
    public void FormatsAParameterAtAnyPosition(string value, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.Equal(expected, template.Format(new Dictionary<string, string> { ["id"] = "42" }));
    }

    [Fact]
    public void FormatsEachParameterInItsOwnPosition()
    {
        RouteTemplate template = RouteTemplate.Parse("/stores/{storeId}/products/{productId}/{tab}");

        string path = template.Format(
            new Dictionary<string, string> { ["tab"] = "reviews", ["productId"] = "42", ["storeId"] = "northwind" });

        Assert.Equal("/stores/northwind/products/42/reviews", path);
    }

    [Theory]
    [InlineData("a b", "a%20b")]
    [InlineData(" ", "%20")]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("%", "%25")]
    [InlineData("%2F", "%252F")]
    [InlineData("+", "%2B")]
    [InlineData("?", "%3F")]
    [InlineData("#", "%23")]
    [InlineData("{id}", "%7Bid%7D")]
    [InlineData("é", "%C3%A9")]
    [InlineData("\U0001F600", "%F0%9F%98%80")]
    [InlineData("\n", "%0A")]
    [InlineData("~a-b_c.d", "~a-b_c.d")]
    [InlineData("...", "...")]
    public void FormatsAValuePercentEncoded(string value, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{name}");

        Assert.Equal("/files/" + expected, template.Format(new Dictionary<string, string> { ["name"] = value }));
    }

    [Fact]
    public void IgnoresValuesForNamesTheTemplateDoesNotUse()
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id}");

        string path = template.Format(
            new Dictionary<string, string> { ["id"] = "42", ["tab"] = "reviews", [""] = "", ["x"] = null! });

        Assert.Equal("/products/42", path);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/settings")]
    public void ALiteralTemplateIgnoresEveryValue(string value)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        string path = template.Format(new Dictionary<string, string> { ["id"] = "1", ["x"] = "" });

        Assert.Equal(value, path);
    }

    [Theory]
    [InlineData(false, "storeId")]
    [InlineData(false, "STOREID")]
    [InlineData(false, "StoreId")]
    [InlineData(true, "storeId")]
    [InlineData(true, "STOREID")]
    public void LooksUpANameWithoutRegardToCase(bool caseInsensitiveDictionary, string key)
    {
        RouteTemplate template = RouteTemplate.Parse("/stores/{storeId}");
        var values = new Dictionary<string, string>(
            caseInsensitiveDictionary ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            [key] = "north",
            ["other"] = "x",
        };

        Assert.Equal("/stores/north", template.Format(values));
    }

    [Theory]
    [InlineData("STOREID", "StoreId")]
    [InlineData("StoreId", "STOREID")]
    public void LooksUpTheNameAsWrittenBeforeOtherSpellings(string before, string after)
    {
        RouteTemplate template = RouteTemplate.Parse("/stores/{storeId}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [before] = "other",
            ["storeId"] = "exact",
            [after] = "another",
        };

        Assert.Equal("/stores/exact", template.Format(values));
    }

    [Theory]
    [InlineData("StoreId", "STOREID")]
    [InlineData("storeid", "STOREID")]
    [InlineData("STOREID", "storeid")]
    public void FormatRejectsANameThatMatchesMoreThanOneKeyIgnoringCase(string first, string second)
    {
        RouteTemplate template = RouteTemplate.Parse("/stores/{storeId}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { [first] = "a", [second] = "b" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "storeId", Ambiguous);

        Assert.Contains($"'{first}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{second}'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NoValue, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/{v}")]
    [InlineData("/files/{v}")]
    [InlineData("/{v}/details")]
    [InlineData("/my files/{v}/%7Bx%7D")]
    public void AFormattedValueIsMatchedBackUnchanged(string value)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        foreach (string awkward in AwkwardValues)
            AssertRoundTrips(template, new Dictionary<string, string> { ["v"] = awkward });
    }

    [Fact]
    public void GeneratedTemplatesAndValuesRoundTrip()
    {
        string[] pieces =
        [
            "a", "Z", "0", " ", "-", ".", "_", "~", "%", "/", "?", "#", "{", "}", "+", "&", "=", ":", "@", "\\", "\"",
            "<", "^", "`", "|", "\t", "\n", "\0", "\u0085", "\u00A0", "é", "€", "\U0001F600", "%\U0001F600",
            "\U0001F600%", "%2F", "%F0%9F%98%80", "..", "%2E",
        ];
        var random = new Random(20261124);

        for (var i = 0; i < 300; i++)
        {
            var template = new StringBuilder();
            var literals = new List<string>();
            var values = new Dictionary<string, string>();
            int segmentCount = random.Next(1, 5);
            for (var segment = 0; segment < segmentCount; segment++)
            {
                if (random.Next(2) == 0)
                {
                    string name = "p" + segment.ToString(CultureInfo.InvariantCulture);
                    template.Append("/{").Append(name).Append('}');
                    values[name] = Generate();
                }
                else
                {
                    string literal = Generate();
                    template.Append('/').Append(Uri.EscapeDataString(literal));
                    literals.Add(literal);
                }
            }

            RouteTemplate parsed = RouteTemplate.Parse(template.ToString());
            string path = AssertRoundTrips(parsed, values);
            foreach (string literal in literals)
                Assert.Contains("/" + Uri.EscapeDataString(literal), path, StringComparison.Ordinal);

            RouteTemplateDotSegmentTests.AssertUriKeepsEverySegment(path);
        }

        // A dot segment cannot be a literal or a value, so it is generated again; text with other dots is kept.
        string Generate()
        {
            var builder = new StringBuilder();
            while (builder.Length == 0 || builder.ToString() is "." or "..")
            {
                builder.Clear();
                int count = random.Next(1, 8);
                for (var i = 0; i < count; i++)
                    builder.Append(pieces[random.Next(pieces.Length)]);
            }

            return builder.ToString();
        }
    }

    [Theory]
    [InlineData("int", "42", "42")]
    [InlineData("int", "+007", "%2B007")]
    [InlineData("int", "-2147483648", "-2147483648")]
    [InlineData("long", "9223372036854775807", "9223372036854775807")]
    [InlineData("decimal", "-.5", "-.5")]
    [InlineData("decimal", "+19.95", "%2B19.95")]
    [InlineData("bool", "TRUE", "TRUE")]
    [InlineData("guid", "6f9619ff-8b86-d011-b42d-00cf4fc964ff", "6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("guid", "6F9619FF8B86D011B42D00CF4FC964FF", "6F9619FF8B86D011B42D00CF4FC964FF")]
    [InlineData("alpha", "caf\u00E9", "caf%C3%A9")]
    [InlineData("alpha", "\U0001D49C", "%F0%9D%92%9C")]
    [InlineData("INT", "42", "42")]
    public void FormatsAValueThatSatisfiesItsConstraintUnchanged(string constraint, string value, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse($"/items/{{v:{constraint}}}");
        var values = new Dictionary<string, string> { ["v"] = value };

        Assert.Equal("/items/" + expected, AssertRoundTrips(template, values));
    }

    [Theory]
    [InlineData("int", "abc")]
    [InlineData("int", " 42")]
    [InlineData("int", "42 ")]
    [InlineData("int", "42\u0000")]
    [InlineData("int", "1,000")]
    [InlineData("int", "2147483648")]
    [InlineData("int", "\u0663")]
    [InlineData("int", "%34%32")]
    [InlineData("long", "9223372036854775808")]
    [InlineData("long", "4.2")]
    [InlineData("decimal", "1,000.5")]
    [InlineData("decimal", "19.95-")]
    [InlineData("decimal", "1e3")]
    [InlineData("decimal", " 1")]
    [InlineData("decimal", "79228162514264337593543950336")]
    [InlineData("bool", " true")]
    [InlineData("bool", "true\u0000")]
    [InlineData("bool", "yes")]
    [InlineData("guid", "{6f9619ff-8b86-d011-b42d-00cf4fc964ff}")]
    [InlineData("guid", "(6f9619ff-8b86-d011-b42d-00cf4fc964ff)")]
    [InlineData("guid", " 6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("alpha", "a1")]
    [InlineData("alpha", "a b")]
    [InlineData("alpha", "e\u0301")]
    [InlineData("ALPHA", "a1")]
    public void FormatRejectsAValueThatDoesNotSatisfyItsConstraint(string constraint, string value)
    {
        RouteTemplate template = RouteTemplate.Parse($"/items/{{v:{constraint}}}");
        var values = new Dictionary<string, string> { ["v"] = value };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "v", UnsatisfiedConstraint);

        Assert.Contains($"'{constraint.ToLowerInvariant()}'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NoValue, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatReportsAnEmptyOrInvalidValueBeforeItsConstraint()
    {
        RouteTemplate template = RouteTemplate.Parse("/items/{v:alpha}");

        ArgumentException empty = AssertRejectsValue(
            () => template.Format(new Dictionary<string, string> { ["v"] = "" }),
            "v",
            EmptyValue);
        ArgumentException surrogate = AssertRejectsValue(
            () => template.Format(new Dictionary<string, string> { ["v"] = "a\uD800" }),
            "v",
            NotValidUtf16);

        Assert.DoesNotContain(UnsatisfiedConstraint, empty.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(UnsatisfiedConstraint, surrogate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatReportsTheFirstParameterThatDoesNotSatisfyItsConstraint()
    {
        RouteTemplate template = RouteTemplate.Parse("/{first:int}/x/{second:bool}");

        ArgumentException exception = AssertRejectsValue(
            () => template.Format(new Dictionary<string, string> { ["first"] = "one", ["second"] = "maybe" }),
            "first",
            UnsatisfiedConstraint);

        Assert.DoesNotContain("'second'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedConstrainedTemplatesAndValuesRoundTrip()
    {
        (string Constraint, string[] Values)[] constrained =
        [
            ("int", ["0", "-0", "+007", "2147483647", "-2147483648"]),
            ("long", ["9223372036854775807", "-9223372036854775808", "+00"]),
            ("decimal", ["19.95", ".5", "5.", "-.5", "+0.0", "79228162514264337593543950335.4"]),
            ("bool", ["true", "FALSE", "tRuE"]),
            ("guid", ["6f9619ff-8b86-d011-b42d-00cf4fc964ff", "6F9619FF8B86D011B42D00CF4FC964FF"]),
            ("alpha", ["a", "caf\u00E9", "\U0001D49C", "\u65E5\u672C", "\u01C5"]),
        ];
        var random = new Random(20261125);

        for (var i = 0; i < 300; i++)
        {
            var template = new StringBuilder();
            var values = new Dictionary<string, string>();
            int segmentCount = random.Next(1, 5);
            for (var segment = 0; segment < segmentCount; segment++)
            {
                string name = "p" + segment.ToString(CultureInfo.InvariantCulture);
                switch (random.Next(3))
                {
                    case 0:
                        (string constraint, string[] choices) = constrained[random.Next(constrained.Length)];
                        string spelling = random.Next(2) == 0 ? constraint : constraint.ToUpperInvariant();
                        template.Append("/{").Append(name).Append(':').Append(spelling).Append('}');
                        values[name] = choices[random.Next(choices.Length)];
                        break;
                    case 1:
                        template.Append("/{").Append(name).Append('}');
                        values[name] = AwkwardValues[random.Next(AwkwardValues.Length)];
                        break;
                    default:
                        template.Append("/lit%20").Append(name);
                        break;
                }
            }

            AssertRoundTrips(RouteTemplate.Parse(template.ToString()), values);
        }
    }

    [Theory]
    [InlineData("/products/{id:int}", "/products/%2B007")]
    [InlineData("/products/{id:int}", "/products/4%32")]
    [InlineData("/products/{id:decimal}", "/products/-.5")]
    [InlineData("/products/{id:guid}", "/products/6F9619FF8b86d011b42d00cf4fc964ff")]
    [InlineData("/products/{id:bool}", "/products/True")]
    [InlineData("/{store:alpha}/x/{id:long}", "//caf%C3%A9//x/0042/")]
    public void FormattingTheValuesOfAConstrainedMatchProducesAPathThatMatchesAgain(string value, string path)
    {
        FormattingTheValuesOfAMatchProducesAPathThatMatchesAgain(value, path);
    }

    [Theory]
    [InlineData("/files/{name}", "/files/a%20b")]
    [InlineData("/files/{name}", "/files/a b")]
    [InlineData("/files/{name}", "/files/a+b")]
    [InlineData("/files/{name}", "/files/%FF")]
    [InlineData("/files/{name}", "/files/100%")]
    [InlineData("/files/{name}", "/files/a%2Fb")]
    [InlineData("/files/{name}", "/files/%E2%82%AC")]
    [InlineData("/files/{name}", "/files/%0A")]
    [InlineData("/files/{name}", "/files/...")]
    [InlineData("/{a}/x/{b}", "//one//x/two/")]
    [InlineData("/my files/{n}", "/my%20files/%41")]
    [InlineData("/", "")]
    public void FormattingTheValuesOfAMatchProducesAPathThatMatchesAgain(string value, string path)
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
    public void AReformattedPathIsCanonicalRatherThanTheTextThatWasMatched()
    {
        RouteTemplate template = RouteTemplate.Parse("/my files/{n}");
        IReadOnlyDictionary<string, string>? matched = template.Match("//my files//%41/");
        Assert.NotNull(matched);

        Assert.Equal("/my%20files/A", template.Format(matched));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/settings")]
    [InlineData("/products/{id}")]
    public void FormatRejectsANullDictionary(string value)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => template.Format(null!));

        Assert.Equal("pathValues", exception.ParamName);
    }

    [Theory]
    [InlineData(false, "other")]
    [InlineData(false, "i")]
    [InlineData(false, "idx")]
    [InlineData(true, "other")]
    public void FormatRejectsAMissingValue(bool caseInsensitiveDictionary, string key)
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id}");
        var values = new Dictionary<string, string>(
            caseInsensitiveDictionary ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            [key] = "1",
        };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "id", NoValue);

        Assert.DoesNotContain(NullValue, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatRejectsAnEmptyDictionaryForATemplateWithParameters()
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id}");

        AssertRejectsValue(() => template.Format(NoValues), "id", NoValue);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("ID")]
    public void FormatRejectsANullValue(string key)
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = null! };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "id", NullValue);

        Assert.DoesNotContain(NoValue, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/products/{id}", "id")]
    [InlineData("/products/{id}", "ID")]
    [InlineData("/{a}/products/{id}", "id")]
    public void FormatRejectsAnEmptyValue(string value, string key)
    {
        RouteTemplate template = RouteTemplate.Parse(value);
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1", [key] = "" };

        ArgumentException exception = AssertRejectsValue(() => template.Format(values), "id", EmptyValue);

        Assert.DoesNotContain(NoValue, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0xD800, "")]
    [InlineData(0xDBFF, "a")]
    [InlineData(0xDC00, "b")]
    [InlineData(0xDFFF, "\U0001F600")]
    public void FormatRejectsAValueWithALoneSurrogate(int surrogate, string around)
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{name}");
        string value = around + (char)surrogate + around;

        AssertRejectsValue(
            () => template.Format(new Dictionary<string, string> { ["name"] = value }),
            "name",
            NotValidUtf16);
    }

    [Fact]
    public void FormatRejectsALowSurrogateBeforeAHighSurrogate()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{name}");
        string value = "\uDE00\uD83D";

        AssertRejectsValue(
            () => template.Format(new Dictionary<string, string> { ["name"] = value }),
            "name",
            NotValidUtf16);
    }

    [Fact]
    public void AMatchedLoneSurrogateCannotBeFormattedBack()
    {
        RouteTemplate template = RouteTemplate.Parse("/files/{name}");
        string value = "a" + (char)0xD800;

        IReadOnlyDictionary<string, string>? matched = template.Match("/files/" + value);

        Assert.NotNull(matched);
        Assert.Equal(value, matched["name"]);
        AssertRejectsValue(() => template.Format(matched), "name", NotValidUtf16);
    }

    [Fact]
    public void FormatReportsTheFirstParameterWithoutAValue()
    {
        RouteTemplate template = RouteTemplate.Parse("/{first}/x/{second}");

        ArgumentException exception = AssertRejectsValue(
            () => template.Format(new Dictionary<string, string> { ["second"] = "" }),
            "first",
            NoValue);

        Assert.DoesNotContain("'second'", exception.Message, StringComparison.Ordinal);
    }

    // Formats the values, matches the path back, and checks that the same values come back and that formatting them
    // again gives the same path.
    private static string AssertRoundTrips(RouteTemplate template, Dictionary<string, string> values)
    {
        string path = template.Format(values);

        IReadOnlyDictionary<string, string>? matched = template.Match(path);

        Assert.NotNull(matched);
        Assert.Equal(Sorted(values), Sorted(matched));
        Assert.Equal(path, template.Format(matched));
        return path;
    }

    private static List<KeyValuePair<string, string>> Sorted(IReadOnlyDictionary<string, string> values)
    {
        var list = new List<KeyValuePair<string, string>>(values);
        list.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        return list;
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
