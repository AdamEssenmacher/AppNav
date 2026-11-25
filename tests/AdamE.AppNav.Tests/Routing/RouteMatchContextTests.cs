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

        Within("/products/{id}", uri, context =>
        {
            Assert.Same(uri, context.SourceUri);
        });
    }

    [Fact]
    public void PathReturnsTheDecodedCapturedValue()
    {
        Within("/stores/{store}/products/{id}", "/stores/north%20wind/products/42", context =>
        {
            Assert.Equal("north wind", context.Path("store"));
            Assert.Equal("42", context.Path("id"));
        });
    }

    [Theory]
    [InlineData("id")]
    [InlineData("ID")]
    [InlineData("Id")]
    public void NamesAreLookedUpWithoutRegardToCase(string name)
    {
        Within("/products/{Id}", "/products/42", context =>
        {
            Assert.Equal("42", context.Path(name));
            Assert.True(context.TryGetPath(name, out string? value));
            Assert.Equal("42", value);
            Assert.Equal("42", context.PathValues[name]);
        });
    }

    [Fact]
    public void PathValuesAreTheValuesTheTemplateCaptured()
    {
        Within("/orders/{id}/{tab?}", "/orders/42/items", context =>
        {
            Assert.Equal(2, context.PathValues.Count);
            Assert.Equal("42", context.PathValues["id"]);
            Assert.Equal("items", context.PathValues["tab"]);
        });
    }

    [Fact]
    public void PathValuesAreEmptyForATemplateWithoutParameters()
    {
        Within("/settings", "/settings", context =>
        {
            Assert.Empty(context.PathValues);
        });
    }

    [Fact]
    public void PathThrowsForANameTheTemplateDoesNotHave()
    {
        Within("/products/{id}", "/products/42", context =>
        {
            KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() => context.Path("name"));

            Assert.Contains(NoValue, exception.Message, StringComparison.Ordinal);
            Assert.Contains("'name'", exception.Message, StringComparison.Ordinal);
            AssertHasNoValue(context, "name");
        });
    }

    [Fact]
    public void AnOptionalParameterThePathLeftOutHasNoValue()
    {
        Within("/orders/{id}/{tab?}", "/orders/42", context =>
        {
            KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() => context.Path("tab"));

            Assert.Contains("'tab'", exception.Message, StringComparison.Ordinal);
            AssertHasNoValue(context, "tab");
            Assert.Equal("42", context.Path("id"));
        });
    }

    [Fact]
    public void AnOptionalParameterThePathHasHasItsValue()
    {
        Within("/orders/{id}/{tab?}", "/orders/42/items", context =>
        {
            Assert.Equal("items", context.Path("tab"));
            Assert.True(context.TryGetPath("tab", out string? value));
            Assert.Equal("items", value);
        });
    }

    [Fact]
    public void ACatchAllParameterHasTheRestOfThePath()
    {
        Within("/files/{*path}", "/files/docs/a%20b.txt", context =>
        {
            Assert.Equal("docs/a b.txt", context.Path("path"));
            Assert.True(context.TryGetPath("path", out string? value));
            Assert.Equal("docs/a b.txt", value);
        });
    }

    [Fact]
    public void ACatchAllParameterWithNothingLeftHasNoValue()
    {
        Within("/files/{*path}", "/files", context =>
        {
            KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() => context.Path("path"));

            Assert.Contains("'path'", exception.Message, StringComparison.Ordinal);
            AssertHasNoValue(context, "path");
        });
    }

    [Fact]
    public void PathEscapesAControlCharacterInTheNameItQuotes()
    {
        Within("/settings", "/settings", context =>
        {
            KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() => context.Path("a\u0009b"));

            Assert.Contains("'a\\u0009b'", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void PathRejectsANullName()
    {
        Within("/products/{id}", "/products/42", context =>
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => context.Path(null!));

            Assert.Equal("name", exception.ParamName);
        });
    }

    [Fact]
    public void TryGetPathRejectsANullName()
    {
        Within("/products/{id}", "/products/42", context =>
        {
            ArgumentNullException exception =
                Assert.Throws<ArgumentNullException>(() => context.TryGetPath(null!, out _));

            Assert.Equal("name", exception.ParamName);
        });
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
        Within("/products/{id}", "/products/a%20b", context =>
        {
            Assert.True(context.TryGetPath("id", out string? value));
            Assert.Equal("a b", value);
        });
    }

    [Theory]
    [InlineData("/products/{id}", "/products/42", "name")]
    [InlineData("/orders/{id}/{tab?}", "/orders/42", "tab")]
    [InlineData("/files/{*path}", "/files", "path")]
    [InlineData("/files/{*path}", "/files/%2F", "PATH")]
    public void TryGetPathReturnsFalseAndNullWithoutAValue(string template, string uri, string name)
    {
        Within(template, uri, context =>
        {
            AssertHasNoValue(context, name);
        });
    }

    [Theory]
    [InlineData("tab")]
    [InlineData("TAB")]
    [InlineData("Tab")]
    public void QueryNamesAreLookedUpWithoutRegardToCase(string name)
    {
        Within("/settings", "/settings?Tab=a&tAB=b", context =>
        {
            Assert.True(context.TryGetQuery(name, out string? value));
            Assert.Equal(Picked(["a", "b"]), value);
            Assert.Equal(["a", "b"], GetQueryValues(context, name));
        });
    }

    [Fact]
    public void TryGetQueryReturnsTheFirstValue()
    {
        Within("/settings", "/settings?tab=a%20b&x=1&tab=c", context =>
        {
            Assert.True(context.TryGetQuery("tab", out string? value));
            Assert.Equal("a b", value);
            Assert.True(context.TryGetQuery("x", out string? x));
            Assert.Equal("1", x);
            Assert.Equal(Picked(GetQueryValues(context, "tab")), value);
        });
    }

    [Fact]
    public void NamesThatDifferOnlyByCaseAreOneNameAndTheFirstValueWins()
    {
        Within("/settings", "/settings?Tab=x&tab=y", context =>
        {
            Assert.True(context.TryGetQuery("tab", out string? value));
            Assert.Equal("x", value);
        });
    }

    [Theory]
    [InlineData("/settings?flag")]
    [InlineData("/settings?flag=")]
    [InlineData("/settings?flag&flag=x")]
    public void TryGetQueryReturnsTheEmptyStringForANameWithoutAValue(string uri)
    {
        Within("/settings", uri, context =>
        {
            Assert.True(context.TryGetQuery("flag", out string? value));
            Assert.Equal("", value);
        });
    }

    [Theory]
    [InlineData("/settings", "tab")]
    [InlineData("/settings?", "tab")]
    [InlineData("/settings?ta=1&tabs=2", "tab")]
    [InlineData("/settings?tab=1", "")]
    public void TryGetQueryReturnsFalseAndNullWithoutTheName(string uri, string name)
    {
        Within("/settings", uri, context =>
        {
            Assert.False(context.TryGetQuery(name, out string? value));
            Assert.Null(value);
        });
    }

    [Fact]
    public void GetQueryValuesReturnsEveryValueInOrder()
    {
        Within("/settings", "/settings?tab=b&x=1&tab=&tab=a&tab=b", context =>
        {
            Assert.Equal(["b", "", "a", "b"], GetQueryValues(context, "tab"));
            Assert.Equal(["1"], GetQueryValues(context, "x"));
        });
    }

    [Fact]
    public void GetQueryValuesReturnsOneSharedEmptyListWithoutTheName()
    {
        IReadOnlyList<string> values = Read("/settings", "/settings?tab=1", c => GetQueryValues(c, "x"));

        Assert.Empty(values);
        Assert.Same(values, Read("/settings", "/settings?tab=1", c => GetQueryValues(c, "y")));
        Assert.Same(values, Read("/settings", "/settings", c => GetQueryValues(c, "tab")));
    }

    [Theory]
    [InlineData("/settings?tab=a")]
    [InlineData("/settings?tab=a&tab=b")]
    public void AListGetQueryValuesReturnedCannotChangeALaterResult(string uri)
    {
        Within("/settings", uri, context =>
        {
            IReadOnlyList<string> first = GetQueryValues(context, "tab");
            string[] expected = [.. first];

            Assert.IsNotType<string[]>(first);
            Assert.IsNotType<List<string>>(first);
            var list = Assert.IsAssignableFrom<IList<string>>(first);
            Assert.True(list.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => list[0] = "changed");
            Assert.Throws<NotSupportedException>(() => list.Add("added"));

            Assert.Equal(expected, GetQueryValues(context, "tab"));
            Assert.True(context.TryGetQuery("tab", out string? value));
            Assert.Equal(Picked(expected), value);
        });
    }

    [Fact]
    public void AskingTwiceGivesTheSameValues()
    {
        Within("/settings", "/settings?tab=a&tab=b", context =>
        {
            Assert.True(context.TryGetQuery("tab", out string? first));
            Assert.True(context.TryGetQuery("tab", out string? second));
            Assert.Same(first, second);
            Assert.Equal(GetQueryValues(context, "tab"), GetQueryValues(context, "TAB"));
        });
    }

    [Fact]
    public void TryGetQueryRejectsANullName()
    {
        Within("/settings", "/settings?tab=1", context =>
        {
            ArgumentNullException exception =
                Assert.Throws<ArgumentNullException>(() => context.TryGetQuery(null!, out _));

            Assert.Equal("name", exception.ParamName);
        });
    }

    [Fact]
    public void GetQueryValuesRejectsANullName()
    {
        Within("/settings", "/settings?tab=1", context =>
        {
            ArgumentNullException exception =
                Assert.Throws<ArgumentNullException>(() => GetQueryValues(context, null!));

            Assert.Equal("name", exception.ParamName);
        });
    }

    [Fact]
    public void ARouteIsBuiltFromPathAndQueryValues()
    {
        RouteTable table = new RouteTableBuilder()
            .Map(
                "/orders/{id:int}",
                c => new OrderRoute(c.Path("id"), c.TryGetQuery("tab", out string? tab) ? tab : null))
            .Build();

        Assert.Equal(new OrderRoute("42", "items"), table.Match(new Uri("myapp:///orders/42?tab=items")).Route);
        Assert.Equal(new OrderRoute("42", null), table.Match(new Uri("/orders/42", UriKind.Relative)).Route);
    }

    private static void AssertHasNoValue(RouteMatchContext context, string name)
    {
        Assert.False(context.TryGetPath(name, out string? value));
        Assert.Null(value);
    }

    // The value TryGetQuery gives for a name that repeats, out of all the values GetQueryValues gives for it.
    private static string Picked(IReadOnlyList<string> values)
    {
        return values[0];
    }

    private static IReadOnlyList<string> GetQueryValues(RouteMatchContext context, string name)
    {
        return context.GetQueryValues(name);
    }

    // Runs the checks inside the create function, because a context is valid only during that call; an assertion
    // that fails there propagates out of Match unchanged.
    private static void Within(string template, string uri, Action<RouteMatchContext> check)
    {
        Within(template, new Uri(uri, UriKind.Relative), check);
    }

    private static void Within(string template, Uri uri, Action<RouteMatchContext> check)
    {
        Read(template, uri, c =>
        {
            check(c);
            return true;
        });
    }

    private static T Read<T>(string template, string uri, Func<RouteMatchContext, T> read)
    {
        return Read(template, new Uri(uri, UriKind.Relative), read);
    }

    // Returns what the create function read, never the context itself.
    private static T Read<T>(string template, Uri uri, Func<RouteMatchContext, T> read)
    {
        var called = false;
        T result = default!;
        RouteTable table = new RouteTableBuilder()
            .Map(template, c =>
            {
                result = read(c);
                called = true;
                return new SettingsRoute();
            })
            .Build();

        Assert.True(table.Match(uri).IsSuccess);
        Assert.True(called);
        return result;
    }
}
