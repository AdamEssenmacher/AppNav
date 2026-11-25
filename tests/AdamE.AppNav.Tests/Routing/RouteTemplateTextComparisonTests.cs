using System.Globalization;
using System.Text;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTemplateTextComparisonTests
{
    // The same visible text, 'cafe' with an acute accent on the 'e', with the precomposed U+00E9 (NFC) and with 'e'
    // followed by the combining acute accent U+0301 (NFD), written as it is and percent-encoded.
    private const string Composed = "caf\u00E9";
    private const string ComposedEncoded = "caf%C3%A9";
    private const string Decomposed = "cafe\u0301";
    private const string DecomposedEncoded = "cafe%CC%81";

    [Theory]
    [InlineData("/" + ComposedEncoded, "/" + DecomposedEncoded)]
    [InlineData("/" + ComposedEncoded, "/" + Decomposed)]
    [InlineData("/" + Composed, "/" + DecomposedEncoded)]
    [InlineData("/" + DecomposedEncoded, "/" + ComposedEncoded)]
    [InlineData("/" + DecomposedEncoded, "/" + Composed)]
    [InlineData("/" + Decomposed, "/" + ComposedEncoded)]
    public void ALiteralDoesNotMatchTheSameTextInAnotherNormalizationForm(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.Null(template.Match(path));
    }

    [Theory]
    [InlineData("/" + ComposedEncoded, "/" + ComposedEncoded)]
    [InlineData("/" + ComposedEncoded, "/" + Composed)]
    [InlineData("/" + Composed, "/" + ComposedEncoded)]
    [InlineData("/" + DecomposedEncoded, "/" + DecomposedEncoded)]
    [InlineData("/" + DecomposedEncoded, "/" + Decomposed)]
    [InlineData("/" + Decomposed, "/" + DecomposedEncoded)]
    public void ALiteralMatchesTheSameTextInTheSameNormalizationForm(string value, string path)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.NotNull(template.Match(path));
    }

    [Theory]
    [InlineData("/" + DecomposedEncoded, Decomposed, "/" + DecomposedEncoded)]
    [InlineData("/" + Decomposed, Decomposed, "/" + DecomposedEncoded)]
    [InlineData("/" + ComposedEncoded, Composed, "/" + ComposedEncoded)]
    public void ACapturedValueKeepsItsNormalizationFormThroughFormatAndMatch(
        string path,
        string expected,
        string expectedPath)
    {
        RouteTemplate template = RouteTemplate.Parse("/{name}");

        string? captured = template.Match(path)?["name"];
        Assert.NotNull(captured);
        Assert.Equal(expected, captured, StringComparer.Ordinal);

        string formatted = template.Format(new Dictionary<string, string> { ["name"] = captured });
        Assert.Equal(expectedPath, formatted);
        Assert.Equal(expected, template.Match(formatted)?["name"], StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("/" + DecomposedEncoded, "/" + DecomposedEncoded)]
    [InlineData("/" + Decomposed, "/" + DecomposedEncoded)]
    [InlineData("/" + ComposedEncoded, "/" + ComposedEncoded)]
    public void FormatDoesNotNormalizeALiteral(string value, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse(value);

        Assert.Equal(expected, template.Format(new Dictionary<string, string>()));
    }

    [Fact]
    public void ThePathNormalizationInTheGuideMakesADecomposedPathMatchAComposedTemplate()
    {
        RouteTemplate template = RouteTemplate.Parse("/" + ComposedEncoded);

        Assert.Null(template.Match("/" + DecomposedEncoded));
        Assert.Null(template.Match(("/" + DecomposedEncoded).Normalize(NormalizationForm.FormC)));
        Assert.NotNull(template.Match(NormalizeAsTheGuideDescribes("/" + DecomposedEncoded)));
        Assert.NotNull(template.Match(NormalizeAsTheGuideDescribes("/" + Decomposed)));
    }

    [Theory]
    [InlineData("/a%2Fb", "a/b")]
    [InlineData("/%2541", "%41")]
    [InlineData("/%zz", "%zz")]
    [InlineData("/%FF", "%FF")]
    [InlineData("/" + ComposedEncoded, Composed)]
    [InlineData("/" + DecomposedEncoded, Composed)]
    public void ThePathNormalizationInTheGuideKeepsWhatEachSegmentMeans(string path, string expected)
    {
        RouteTemplate template = RouteTemplate.Parse("/{v}");

        Assert.Equal(expected, template.Match(NormalizeAsTheGuideDescribes(path))?["v"], StringComparer.Ordinal);
    }

    [Fact]
    public void ThePathNormalizationInTheGuideThrowsForALoneSurrogate()
    {
        Assert.Throws<ArgumentException>(() => NormalizeAsTheGuideDescribes("/a\uD800"));
    }

    // CultureInfo.CurrentCulture is stored in an AsyncLocal, so setting it affects only this test's execution flow,
    // not tests running in parallel; it is restored before the test returns so that nothing after it in the same
    // flow sees it either. In tr-TR, 'i' and 'I' are not a case pair: 'i' pairs with U+0130 and 'I' with U+0131, so a
    // culture-aware comparison ignoring case would equate different letters than an invariant one.
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("az-Latn-AZ")]
    [InlineData("en-US")]
    public void LiteralsAreComparedCaseSensitivelyInEveryCulture(string culture)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            RouteTemplate upper = RouteTemplate.Parse("/I");
            Assert.NotNull(upper.Match("/I"));
            Assert.Null(upper.Match("/i"));
            Assert.Null(upper.Match("/\u0131"));
            Assert.Null(upper.Match("/%C4%B1"));

            RouteTemplate lower = RouteTemplate.Parse("/i");
            Assert.NotNull(lower.Match("/i"));
            Assert.Null(lower.Match("/I"));
            Assert.Null(lower.Match("/\u0130"));
            Assert.Null(lower.Match("/%C4%B0"));

            RouteTemplate dotted = RouteTemplate.Parse("/%C4%B0");
            Assert.NotNull(dotted.Match("/\u0130"));
            Assert.Null(dotted.Match("/i"));
            Assert.Null(dotted.Match("/I"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // Exactly the steps the routing guide gives for a path that can arrive in another normalization form, for a path
    // without a query string or fragment.
    private static string NormalizeAsTheGuideDescribes(string path)
    {
        return string.Join(
            '/',
            path.Split('/').Select(segment =>
                Uri.EscapeDataString(Uri.UnescapeDataString(segment).Normalize(NormalizationForm.FormC))));
    }
}
