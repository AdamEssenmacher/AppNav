using System.Globalization;
using System.Text;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTemplateConstraintTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("42")]
    [InlineData("-42")]
    [InlineData("+42")]
    [InlineData("-0")]
    [InlineData("007")]
    [InlineData("+007")]
    [InlineData("2147483647")]
    [InlineData("-2147483648")]
    [InlineData("+2147483647")]
    [InlineData("0000000000000000000000000000002147483647")]
    [InlineData("%2B1")]
    [InlineData("4%32")]
    public void IntAcceptsASignedAsciiIntegerInRange(string segment)
    {
        AssertAccepts("int", segment);
    }

    [Theory]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("9223372036854775807")]
    [InlineData("%2042")]
    [InlineData("42%20")]
    [InlineData(" 42")]
    [InlineData("42%09")]
    [InlineData("42%00")]
    [InlineData("1,000")]
    [InlineData("1%2C000")]
    [InlineData("1_000")]
    [InlineData("4.2")]
    [InlineData("42.")]
    [InlineData("1e3")]
    [InlineData("0x1F")]
    [InlineData("\u0663")]
    [InlineData("%D9%A3")]
    [InlineData("\uFF14\uFF12")]
    [InlineData("\u221242")]
    [InlineData("42-")]
    [InlineData("--1")]
    [InlineData("+-1")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("4%2")]
    [InlineData("abc")]
    public void IntRejectsAnythingElse(string segment)
    {
        AssertRejects("int", segment);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-42")]
    [InlineData("+007")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775808")]
    [InlineData("00000000000000000000000000000000000000009223372036854775807")]
    [InlineData("-00000000000000000000000000000000000000009223372036854775808")]
    public void LongAcceptsASignedAsciiIntegerInRange(string segment)
    {
        AssertAccepts("long", segment);
    }

    [Theory]
    [InlineData("9223372036854775808")]
    [InlineData("-9223372036854775809")]
    [InlineData("%2042")]
    [InlineData("42%20")]
    [InlineData("42%00")]
    [InlineData("1,000")]
    [InlineData("1.0")]
    [InlineData("\u0663")]
    [InlineData("\u221242")]
    [InlineData("42-")]
    [InlineData("-")]
    public void LongRejectsAnythingElse(string segment)
    {
        AssertRejects("long", segment);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("42")]
    [InlineData("19.95")]
    [InlineData("-19.95")]
    [InlineData("+19.95")]
    [InlineData("007.50")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("-.5")]
    [InlineData("+.5")]
    [InlineData("-5.")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335")]
    [InlineData("79228162514264337593543950335.4")]
    [InlineData("-79228162514264337593543950335.4")]
    [InlineData("0.00000000000000000000000000000000001")]
    [InlineData("19%2E95")]
    public void DecimalAcceptsASignedAsciiDecimalInRange(string segment)
    {
        AssertAccepts("decimal", segment);
    }

    [Theory]
    [InlineData("79228162514264337593543950336")]
    [InlineData("-79228162514264337593543950336")]
    [InlineData("79228162514264337593543950335.5")]
    [InlineData("-79228162514264337593543950335.5")]
    [InlineData("-.")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("1.2.3")]
    [InlineData("..5")]
    [InlineData("%2019.95")]
    [InlineData("19.95%20")]
    [InlineData("1.5%00")]
    [InlineData("1,000")]
    [InlineData("1,000.5")]
    [InlineData("19,95")]
    [InlineData("19.95-")]
    [InlineData("19.95+")]
    [InlineData("1e3")]
    [InlineData("1E-3")]
    [InlineData("$5")]
    [InlineData("\u00A45")]
    [InlineData("(5)")]
    [InlineData("\u0663.5")]
    [InlineData("\u22125")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void DecimalRejectsAnythingElse(string segment)
    {
        AssertRejects("decimal", segment);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("TRUE")]
    [InlineData("False")]
    [InlineData("tRuE")]
    [InlineData("%74rue")]
    public void BoolAcceptsTrueOrFalseInAnyAsciiCase(string segment)
    {
        AssertAccepts("bool", segment);
    }

    [Theory]
    [InlineData("%20true")]
    [InlineData("true%20")]
    [InlineData("true%00")]
    [InlineData("%09false")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData("t")]
    [InlineData("truee")]
    [InlineData("fals")]
    [InlineData("fal\u017Fe")]
    [InlineData("\uFF54rue")]
    public void BoolRejectsAnythingElse(string segment)
    {
        AssertRejects("bool", segment);
    }

    [Theory]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("6F9619FF-8B86-D011-B42D-00CF4FC964FF")]
    [InlineData("6f9619FF-8b86-D011-b42d-00CF4fc964ff")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("aaaaaaaa-bbbb-cccc-dddd-eeeeeeffffff")]
    [InlineData("6f9619ff%2D8b86-d011-b42d-00cf4fc964ff")]
    public void GuidAcceptsTheHyphenatedFormInAnyCase(string segment)
    {
        AssertAccepts("guid", segment);
    }

    [Theory]
    [InlineData("{6f9619ff-8b86-d011-b42d-00cf4fc964ff}")]
    [InlineData("%7B6f9619ff-8b86-d011-b42d-00cf4fc964ff%7D")]
    [InlineData("(6f9619ff-8b86-d011-b42d-00cf4fc964ff)")]
    [InlineData("{0x6f9619ff,0x8b86,0xd011,{0xb4,0x2d,0x00,0xcf,0x4f,0xc9,0x64,0xff}}")]
    [InlineData("%206f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964ff%20")]
    [InlineData("6f9619ff8b86d011b42d00cf4fc964ff")]
    [InlineData("6F9619FF8B86D011B42D00CF4FC964FF")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("6f9619ff8b86d011b42d00cf4fc964ff%00")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964ff%00")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964ff%0A")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964f%20")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc96-4f")]
    [InlineData("6f9619ff--8b86-d011-b42d-00cf4fc964f")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964ff0")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964gf")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964\uFF10f")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964\u0663f")]
    [InlineData("6f9619ff\u20118b86-d011-b42d-00cf4fc964ff")]
    [InlineData("6f9619ff\u2013-8b86-d011-b42d-00cf4fc964f")]
    [InlineData("6f9619ff\u22128b86-d011-b42d-00cf4fc964ff")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964f\u00E9")]
    [InlineData("6f9619ff-+b86-d011-b42d-00cf4fc964ff")]
    [InlineData("+f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("%2Bf9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("0x9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("0X9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("6f9619ff-8b86-d011-b42d-0x0f4fc964ff")]
    [InlineData("6f9619ff8-b86-d011-b42d-00cf4fc964ff")]
    [InlineData("6f9619ff-8b86-d011-b42d00cf4fc964ff-")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964fg")]
    [InlineData("6f9619ff8b86d011b42d00cf4fc964fg")]
    [InlineData("6f9619ff8b86d011b42d00cf4fc964f")]
    [InlineData("6f9619ff8b86d011b42d00cf4fc964ff0")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964f")]
    [InlineData("6f9619ff-8b86d011-b42d-00cf4fc964ff")]
    [InlineData("\uFF16f9619ff8b86d011b42d00cf4fc964ff")]
    [InlineData("-6f9619ff8b86d011b42d00cf4fc964f")]
    public void GuidRejectsAnythingElse(string segment)
    {
        AssertRejects("guid", segment);
    }

    // Checked on the constraint itself, because a path never captures an empty segment, and so that a raw newline
    // is tested as well as the encoded one above.
    [Theory]
    [InlineData("")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964ff\n")]
    [InlineData("6f9619ff-8b86-d011-b42d-00cf4fc964f\n")]
    public void GuidRejectsTheEmptyStringAndATrailingNewline(string value)
    {
        Assert.False(RouteConstraint.Find("guid")!.IsSatisfiedBy(value));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Northwind")]
    [InlineData("caf\u00E9")]
    [InlineData("caf%C3%A9")]
    [InlineData("\u03A9")]
    [InlineData("\u65E5\u672C")]
    [InlineData("\u01C5")]
    [InlineData("\u02B0")]
    [InlineData("\U0001D49C")]
    [InlineData("%F0%9D%92%9C")]
    [InlineData("\U00010437x")]
    [InlineData("\U00020000")]
    public void AlphaAcceptsOneOrMoreUnicodeLetters(string segment)
    {
        AssertAccepts("alpha", segment);
    }

    [Theory]
    [InlineData("a1")]
    [InlineData("1")]
    [InlineData("a b")]
    [InlineData("a%20b")]
    [InlineData("%20a")]
    [InlineData("a%20")]
    [InlineData("a-b")]
    [InlineData("a_b")]
    [InlineData("a.b")]
    [InlineData("e\u0301")]
    [InlineData("e%CC%81")]
    [InlineData("\u0301")]
    [InlineData("\u0928\u092E\u0938\u094D\u0924\u0947")]
    [InlineData("%E0%A4%A8%E0%A4%AE%E0%A4%B8%E0%A5%8D%E0%A4%A4%E0%A5%87")]
    [InlineData("\u0663")]
    [InlineData("\U0001D7D9")]
    [InlineData("\U0001F600")]
    [InlineData("a%00")]
    public void AlphaRejectsAnythingThatIsNotALetter(string segment)
    {
        AssertRejects("alpha", segment);
    }

    [Theory]
    [InlineData("a", 0xD835, "")]
    [InlineData("", 0xDC9C, "a")]
    [InlineData("", 0xD835, "")]
    public void AlphaRejectsALoneSurrogate(string before, int surrogate, string after)
    {
        AssertRejects("alpha", before + (char)surrogate + after);
    }

    [Theory]
    [InlineData("int", "42", "x")]
    [InlineData("INT", "42", "x")]
    [InlineData("Int", "42", "x")]
    [InlineData("LONG", "42", "x")]
    [InlineData("DeCiMaL", "4.2", "x")]
    [InlineData("BOOL", "true", "x")]
    [InlineData("Guid", "6f9619ff-8b86-d011-b42d-00cf4fc964ff", "6f9619ff8b86d011b42d00cf4fc964ff")]
    [InlineData("ALPHA", "x", "42")]
    public void ConstraintNamesAreComparedWithoutRegardToCase(string constraint, string satisfying, string other)
    {
        RouteTemplate template = RouteTemplate.Parse($"/{{v:{constraint}}}");

        Assert.Equal(satisfying, template.Match("/" + satisfying)?["v"]);
        Assert.Null(template.Match("/" + other));
    }

    [Theory]
    [InlineData("/stores/{storeId}/products/{productId:int}/{tab:alpha}", "/stores/north wind/products/42/reviews")]
    [InlineData("/{productId:int}/stores/{storeId}/{tab:alpha}", "/42/stores/north wind/reviews")]
    public void AConstrainedParameterMatchesAmongLiteralsAndOtherParameters(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        IReadOnlyDictionary<string, string>? values = template.Match(path);

        Assert.NotNull(values);
        Assert.Equal("north wind", values["storeId"]);
        Assert.Equal("42", values["productId"]);
        Assert.Equal("reviews", values["tab"]);
    }

    [Theory]
    [InlineData("/stores/north/products/4x/reviews")]
    [InlineData("/stores/north/products/42/reviews1")]
    [InlineData("/stores/north/products/4x/reviews1")]
    [InlineData("/stores/north/items/42/reviews")]
    [InlineData("/stores/north/items/4x/reviews")]
    public void AnyUnsatisfiedConstraintOrUnequalLiteralFailsTheMatch(string path)
    {
        RouteTemplate template = RouteTemplate.Parse("/stores/{storeId}/products/{productId:int}/{tab:alpha}");

        Assert.Null(template.Match(path));
    }

    [Theory]
    [InlineData("int", "%2B007", "+007")]
    [InlineData("int", "%2B1", "+1")]
    [InlineData("decimal", "-%2E5", "-.5")]
    [InlineData("alpha", "caf%C3%A9", "caf\u00E9")]
    public void TheCapturedValueIsTheDecodedTextUnchanged(string constraint, string segment, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse($"/items/{{v:{constraint}}}");

        IReadOnlyDictionary<string, string>? values = template.Match("/items/" + segment);

        Assert.NotNull(values);
        Assert.Equal(expected, values["v"]);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("decimal")]
    public void AVeryLongNumberIsOutOfRange(string constraint)
    {
        string segment = "1" + new string('0', 1000);

        Assert.Null(RouteTemplate.Parse($"/{{v:{constraint}}}").Match("/" + segment));
        Assert.NotNull(RouteTemplate.Parse("/{v}").Match("/" + segment));
    }

    [Theory]
    [InlineData("0.", 1000, "1")]
    [InlineData("1.", 1000, "")]
    [InlineData("", 1000, "1")]
    [InlineData("0.", 100, "1")]
    [InlineData("-0.", 100, "1")]
    public void ALongDecimalWithinRangeIsAcceptedAndCapturedAsWritten(string before, int zeros, string after)
    {
        string segment = before + new string('0', zeros) + after;

        Assert.Equal(segment, RouteTemplate.Parse("/{v:decimal}").Match("/" + segment)?["v"]);
    }

    [Fact]
    public void ALongRunOfLettersSatisfiesAlpha()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 5000; i++)
            builder.Append(i % 2 == 0 ? "a" : "\U0001D49C");

        string segment = builder.ToString();

        Assert.Equal(segment, RouteTemplate.Parse("/{v:alpha}").Match("/" + segment)?["v"]);
        Assert.Null(RouteTemplate.Parse("/{v:alpha}").Match("/" + segment + "1"));
    }

    // CultureInfo.CurrentCulture is stored in an AsyncLocal, so setting it affects only this test's execution flow,
    // not tests running in parallel; it is restored before the test returns so that nothing after it in the same
    // flow sees it either.
    [Theory]
    [InlineData("sv-SE")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("de-DE")]
    [InlineData("fa-IR")]
    public void ConstraintsBehaveTheSameInEveryCulture(string culture)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Equal("-42", RouteTemplate.Parse("/{v:int}").Match("/-42")?["v"]);
            Assert.Equal("+42", RouteTemplate.Parse("/{v:int}").Match("/+42")?["v"]);
            Assert.Equal("-42", RouteTemplate.Parse("/{v:long}").Match("/-42")?["v"]);
            Assert.Equal("-4.2", RouteTemplate.Parse("/{v:decimal}").Match("/-4.2")?["v"]);
            Assert.Equal("TRUE", RouteTemplate.Parse("/{v:bool}").Match("/TRUE")?["v"]);
            Assert.NotNull(RouteTemplate.Parse("/{v:guid}").Match("/6F9619FF-8B86-D011-B42D-00CF4FC964FF"));
            Assert.NotNull(RouteTemplate.Parse("/{v:guid}").Match("/6f9619ff-8b86-d011-b42d-00cf4fc964ff"));
            Assert.Null(RouteTemplate.Parse("/{v:guid}").Match("/6F9619FF8B86D011B42D00CF4FC964FF"));
            Assert.Null(RouteTemplate.Parse("/{v:guid}").Match("/6F9619FF\u20118B86-D011-B42D-00CF4FC964FF"));
            Assert.NotNull(RouteTemplate.Parse("/{id:INT}").Match("/42"));
            Assert.NotNull(RouteTemplate.Parse("/{id:ALPHA}").Match("/I\u0131"));

            Assert.Null(RouteTemplate.Parse("/{v:decimal}").Match("/4,2"));
            Assert.Null(RouteTemplate.Parse("/{v:int}").Match("/\u221242"));
            Assert.Null(RouteTemplate.Parse("/{v:decimal}").Match("/\u22124.2"));

            RouteTemplate formatted = RouteTemplate.Parse("/{a:int}/{b:decimal}");
            var values = new Dictionary<string, string> { ["a"] = "-42", ["b"] = "-4.2" };
            Assert.Equal("/-42/-4.2", formatted.Format(values));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void AnUnsatisfiedConstraintDoesNotAffectLaterMatches()
    {
        RouteTemplate template = RouteTemplate.Parse("/products/{id:int}");

        Assert.Null(template.Match("/products/abc"));
        Assert.Equal("42", template.Match("/products/42")?["id"]);
        Assert.Null(template.Match("/products/4.2"));
        Assert.Equal("7", template.Match("/products/7")?["id"]);
    }

    [Fact]
    public void APathThatFailsAConstraintCanMatchAnotherTemplate()
    {
        RouteTemplate byId = RouteTemplate.Parse("/products/{id:int}");
        RouteTemplate bySlug = RouteTemplate.Parse("/products/{slug:alpha}");

        Assert.Null(byId.Match("/products/widget"));
        Assert.Equal("widget", bySlug.Match("/products/widget")?["slug"]);
        Assert.Null(bySlug.Match("/products/42"));
        Assert.Equal("42", byId.Match("/products/42")?["id"]);
    }

    [Fact]
    public void AnUnconstrainedParameterStillAcceptsAnyText()
    {
        RouteTemplate template = RouteTemplate.Parse("/{a}/{b:int}");

        Assert.Equal(" x%y ", template.Match("/%20x%25y%20/1")?["a"]);
    }

    private static void AssertAccepts(string constraint, string segment)
    {
        RouteTemplate template = RouteTemplate.Parse($"/items/{{v:{constraint}}}/details");

        IReadOnlyDictionary<string, string>? values = template.Match($"/items/{segment}/details");

        Assert.NotNull(values);
        Assert.Equal(Uri.UnescapeDataString(segment), Assert.Single(values).Value);
    }

    private static void AssertRejects(string constraint, string segment)
    {
        RouteTemplate template = RouteTemplate.Parse($"/items/{{v:{constraint}}}/details");

        Assert.Null(template.Match($"/items/{segment}/details"));
        Assert.NotNull(RouteTemplate.Parse("/items/{v}/details").Match($"/items/{segment}/details"));
    }
}
