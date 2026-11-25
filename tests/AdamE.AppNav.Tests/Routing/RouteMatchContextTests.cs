using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteMatchContextTests
{
    private const string NoValue = "no value for path parameter";

    [Fact]
    public void SourceUriIsTheUriThatWasMatched()
    {
        var uri = new Uri("https://example.com/products/42?tab=1");

        RouteMatchContext context = Capture("/products/{id}", uri);

        Assert.Same(uri, context.SourceUri);
    }

    [Fact]
    public void PathReturnsTheDecodedCapturedValue()
    {
        RouteMatchContext context = Capture("/stores/{store}/products/{id}", "/stores/north%20wind/products/42");

        Assert.Equal("north wind", context.Path("store"));
        Assert.Equal("42", context.Path("id"));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("ID")]
    [InlineData("Id")]
    public void NamesAreLookedUpWithoutRegardToCase(string name)
    {
        RouteMatchContext context = Capture("/products/{Id}", "/products/42");

        Assert.Equal("42", context.Path(name));
        Assert.True(context.TryGetPath(name, out string? value));
        Assert.Equal("42", value);
        Assert.Equal("42", context.PathValues[name]);
    }

    [Fact]
    public void PathValuesAreTheValuesTheTemplateCaptured()
    {
        RouteMatchContext context = Capture("/orders/{id}/{tab?}", "/orders/42/items");

        Assert.Equal(2, context.PathValues.Count);
        Assert.Equal("42", context.PathValues["id"]);
        Assert.Equal("items", context.PathValues["tab"]);
    }

    [Fact]
    public void PathValuesAreEmptyForATemplateWithoutParameters()
    {
        RouteMatchContext context = Capture("/settings", "/settings");

        Assert.Empty(context.PathValues);
    }

    [Fact]
    public void PathThrowsForANameTheTemplateDoesNotHave()
    {
        RouteMatchContext context = Capture("/products/{id}", "/products/42");

        KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() => context.Path("name"));

        Assert.Contains(NoValue, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'name'", exception.Message, StringComparison.Ordinal);
        AssertHasNoValue(context, "name");
    }

    [Fact]
    public void AnOptionalParameterThePathLeftOutHasNoValue()
    {
        RouteMatchContext context = Capture("/orders/{id}/{tab?}", "/orders/42");

        KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() => context.Path("tab"));

        Assert.Contains("'tab'", exception.Message, StringComparison.Ordinal);
        AssertHasNoValue(context, "tab");
        Assert.Equal("42", context.Path("id"));
    }

    [Fact]
    public void AnOptionalParameterThePathHasHasItsValue()
    {
        RouteMatchContext context = Capture("/orders/{id}/{tab?}", "/orders/42/items");

        Assert.Equal("items", context.Path("tab"));
        Assert.True(context.TryGetPath("tab", out string? value));
        Assert.Equal("items", value);
    }

    [Fact]
    public void ACatchAllParameterHasTheRestOfThePath()
    {
        RouteMatchContext context = Capture("/files/{*path}", "/files/docs/a%20b.txt");

        Assert.Equal("docs/a b.txt", context.Path("path"));
        Assert.True(context.TryGetPath("path", out string? value));
        Assert.Equal("docs/a b.txt", value);
    }

    [Fact]
    public void ACatchAllParameterWithNothingLeftHasNoValue()
    {
        RouteMatchContext context = Capture("/files/{*path}", "/files");

        KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() => context.Path("path"));

        Assert.Contains("'path'", exception.Message, StringComparison.Ordinal);
        AssertHasNoValue(context, "path");
    }

    [Fact]
    public void PathEscapesAControlCharacterInTheNameItQuotes()
    {
        RouteMatchContext context = Capture("/settings", "/settings");

        KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() => context.Path("a\u0009b"));

        Assert.Contains("'a\\u0009b'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PathRejectsANullName()
    {
        RouteMatchContext context = Capture("/products/{id}", "/products/42");

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => context.Path(null!));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void TryGetPathRejectsANullName()
    {
        RouteMatchContext context = Capture("/products/{id}", "/products/42");

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => context.TryGetPath(null!, out _));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ARouteIsBuiltFromTheCapturedValues()
    {
        RouteTable table = new RouteTableBuilder()
            .Map(
                "/orders/{id:int}/{tab?}",
                c => new OrderRoute(c.Path("id"), c.TryGetPath("tab", out string? tab) ? tab : null))
            .Build();

        Assert.Equal(new OrderRoute("42", null), table.Match(new Uri("/orders/42", UriKind.Relative)).Route);
        Assert.Equal(
            new OrderRoute("42", "items"),
            table.Match(new Uri("myapp:///orders/42/items", UriKind.Absolute)).Route);
    }

    [Fact]
    public void TryGetPathReturnsTheValueOfARequiredParameter()
    {
        RouteMatchContext context = Capture("/products/{id}", "/products/a%20b");

        Assert.True(context.TryGetPath("id", out string? value));
        Assert.Equal("a b", value);
    }

    [Theory]
    [InlineData("/products/{id}", "/products/42", "name")]
    [InlineData("/orders/{id}/{tab?}", "/orders/42", "tab")]
    [InlineData("/files/{*path}", "/files", "path")]
    [InlineData("/files/{*path}", "/files/%2F", "PATH")]
    public void TryGetPathReturnsFalseAndNullWithoutAValue(string template, string uri, string name)
    {
        RouteMatchContext context = Capture(template, uri);

        AssertHasNoValue(context, name);
    }

    private static void AssertHasNoValue(RouteMatchContext context, string name)
    {
        Assert.False(context.TryGetPath(name, out string? value));
        Assert.Null(value);
    }

    private static RouteMatchContext Capture(string template, string uri)
    {
        return Capture(template, new Uri(uri, UriKind.Relative));
    }

    private static RouteMatchContext Capture(string template, Uri uri)
    {
        RouteMatchContext? captured = null;
        RouteTable table = new RouteTableBuilder()
            .Map(template, c =>
            {
                captured = c;
                return new SettingsRoute();
            })
            .Build();

        Assert.True(table.Match(uri).IsSuccess);
        return Assert.IsType<RouteMatchContext>(captured);
    }
}
