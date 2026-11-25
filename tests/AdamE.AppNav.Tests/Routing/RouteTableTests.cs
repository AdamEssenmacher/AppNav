using System.Globalization;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTableTests
{
    private const string RepeatedName = "repeats the name";
    private const string ReturnedNull = "returned null";
    private const string MustStartWithSlash = "must start with '/'";
    private const string DotSegment = "is a dot segment";
    private const string UnknownConstraint = "unknown constraint";

    // A path that stands for a secret a deep link can carry, distinctive enough that finding it in a message is
    // not a coincidence.
    private const string SecretPath = "/reset/token-7Qx9Zr2Kp4";

    [Fact]
    public void MapRejectsANullTemplate()
    {
        var builder = new RouteTableBuilder();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => builder.Map(null!, _ => new SettingsRoute()));

        Assert.Equal("template", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void MapRejectsAnEmptyOrWhiteSpaceTemplateUnderItsOwnParameterName(string template)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.Map(template, _ => new SettingsRoute()));

        Assert.Equal("template", exception.ParamName);
    }

    [Theory]
    [InlineData("settings", MustStartWithSlash)]
    [InlineData("/{id}/{ID}", RepeatedName)]
    [InlineData("/a/../b", DotSegment)]
    [InlineData("/{id:slug}", UnknownConstraint)]
    public void MapRejectsAnInvalidTemplateUnderItsOwnParameterName(string template, string category)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.Map(template, _ => new SettingsRoute()));

        Assert.Equal("template", exception.ParamName);
        Assert.Contains(category, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapReportsWhichSegmentOfAnInvalidTemplateIsWrong()
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.Map("/{id}/{ID}", _ => new SettingsRoute()));

        Assert.Equal("template", exception.ParamName);
        Assert.Contains("'{ID}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(RepeatedName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapRejectsANullCreateFunction()
    {
        var builder = new RouteTableBuilder();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => builder.Map<SettingsRoute>("/settings", null!));

        Assert.Equal("createRoute", exception.ParamName);
    }

    [Fact]
    public void MapReturnsTheSameBuilder()
    {
        var builder = new RouteTableBuilder();

        Assert.Same(builder, builder.Map("/settings", _ => new SettingsRoute()));
    }

    [Fact]
    public void DefinitionsAreInRegistrationOrder()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/settings", _ => new SettingsRoute())
            .Map("/products/{id}", c => new ProductRoute(c.Path("id")))
            .Map("/files/{*path}", c => new FileRoute(c.TryGetPath("path", out string? path) ? path : null))
            .Build();

        Assert.Collection(
            table.Definitions,
            d => AssertDefinition(d, typeof(SettingsRoute), "/settings"),
            d => AssertDefinition(d, typeof(ProductRoute), "/products/{id}"),
            d => AssertDefinition(d, typeof(FileRoute), "/files/{*path}"));
    }

    [Fact]
    public void TheSameRouteTypeCanCurrentlyBeMappedToMoreThanOneTemplate()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/settings", _ => new SettingsRoute())
            .Map("/preferences", _ => new SettingsRoute())
            .Map("/options/{tab?}", _ => new SettingsRoute())
            .Build();

        Assert.Equal(3, table.Definitions.Count);
        Assert.Same(table.Definitions[1], table.Match(new Uri("/preferences", UriKind.Relative)).Definition);
        Assert.Same(table.Definitions[2], table.Match(new Uri("/options/a", UriKind.Relative)).Definition);
    }

    [Fact]
    public void DefinitionsCannotBeChangedThroughACast()
    {
        RouteTable table = new RouteTableBuilder().Map("/settings", _ => new SettingsRoute()).Build();

        Assert.IsNotType<RouteDefinition[]>(table.Definitions);
        var list = Assert.IsAssignableFrom<IList<RouteDefinition>>(table.Definitions);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = list[0]);
    }

    [Fact]
    public void BuildTakesASnapshotThatLaterMappingsDoNotChange()
    {
        var builder = new RouteTableBuilder().Map("/settings", _ => new SettingsRoute());
        RouteTable first = builder.Build();

        builder.Map("/products/{id}", c => new ProductRoute(c.Path("id")));
        RouteTable second = builder.Build();

        Assert.Single(first.Definitions);
        Assert.False(first.Match(new Uri("/products/42", UriKind.Relative)).IsSuccess);
        Assert.Equal(2, second.Definitions.Count);
        Assert.Equal(new ProductRoute("42"), second.Match(new Uri("/products/42", UriKind.Relative)).Route);
        Assert.Equal(new SettingsRoute(), second.Match(new Uri("/settings", UriKind.Relative)).Route);
    }

    [Fact]
    public void AnEmptyTableIsValidAndMatchesNothing()
    {
        RouteTable table = new RouteTableBuilder().Build();

        Assert.Empty(table.Definitions);
        RouteMatchResult result = table.Match(new Uri("/", UriKind.Relative));
        Assert.False(result.IsSuccess);
        Assert.Equal(RouteDiagnosticCodes.NotMatched, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void MatchRejectsANullUri()
    {
        RouteTable table = new RouteTableBuilder().Build();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => table.Match(null!));

        Assert.Equal("uri", exception.ParamName);
    }

    [Fact]
    public void ASuccessfulMatchHasTheRouteAndTheDefinitionAndCurrentlyNoDiagnostics()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/settings", _ => new SettingsRoute())
            .Map("/products/{id}", c => new ProductRoute(c.Path("id")))
            .Build();

        RouteMatchResult result = table.Match(new Uri("https://example.com/products/42"));

        Assert.True(result.IsSuccess);
        Assert.Equal(new ProductRoute("42"), result.Route);
        Assert.Same(table.Definitions[1], result.Definition);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ALaterDefinitionWinsWhenEarlierTemplatesDoNotMatch()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/products/{id:int}", c => new ProductRoute(c.Path("id")))
            .Map("/products/{name}", c => new ProductByNameRoute(c.Path("name")))
            .Build();

        RouteMatchResult result = table.Match(new Uri("/products/chair", UriKind.Relative));

        Assert.Equal(new ProductByNameRoute("chair"), result.Route);
        Assert.Same(table.Definitions[1], result.Definition);
    }

    [Fact]
    public void OnlyTheWinningCreateFunctionIsCalledAndOnlyOnce()
    {
        int before = 0, winner = 0, after = 0;
        RouteTable table = new RouteTableBuilder()
            .Map("/settings", _ => { before++; return new SettingsRoute(); })
            .Map("/products/{id:int}", c => { winner++; return new ProductRoute(c.Path("id")); })
            .Map("/products/{name}", c => { after++; return new ProductByNameRoute(c.Path("name")); })
            .Build();

        table.Match(new Uri("/products/42", UriKind.Relative));

        Assert.Equal(0, before);
        Assert.Equal(1, winner);
        Assert.Equal(0, after);
    }

    [Fact]
    public void NoCreateFunctionIsCalledWhenNothingMatches()
    {
        var calls = 0;
        RouteTable table = new RouteTableBuilder()
            .Map("/settings", _ => { calls++; return new SettingsRoute(); })
            .Build();

        table.Match(new Uri("/products", UriKind.Relative));

        Assert.Equal(0, calls);
    }

    [Fact]
    public void AnExceptionFromTheCreateFunctionPropagatesUnchangedWithoutTryingLaterDefinitions()
    {
        var thrown = new FormatException("not a product");
        var laterCalls = 0;
        RouteTable table = new RouteTableBuilder()
            .Map<ProductRoute>("/products/{id:int}", _ => throw thrown)
            .Map("/products/{name}", c => { laterCalls++; return new ProductByNameRoute(c.Path("name")); })
            .Build();

        FormatException exception = Assert.Throws<FormatException>(
            () => table.Match(new Uri("/products/42", UriKind.Relative)));

        Assert.Same(thrown, exception);
        Assert.Equal(0, laterCalls);
    }

    [Fact]
    public void ACreateFunctionThatReturnsNullThrowsNamingTheTemplateAndTheRouteType()
    {
        var laterCalls = 0;
        RouteTable table = new RouteTableBuilder()
            .Map<ProductRoute>("/products/{id:int}", _ => null!)
            .Map("/products/{name}", c => { laterCalls++; return new ProductByNameRoute(c.Path("name")); })
            .Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => table.Match(new Uri("/products/42", UriKind.Relative)));

        Assert.Contains(ReturnedNull, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/products/{id:int}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ProductRoute), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, laterCalls);
    }

    [Fact]
    public void AFailedMatchCurrentlyHasOneNotMatchedDiagnosticWithThePathInDataOnly()
    {
        RouteTable table = new RouteTableBuilder().Map("/settings", _ => new SettingsRoute()).Build();

        RouteMatchResult result = table.Match(new Uri($"https://example.com{SecretPath}?tab=1#top"));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Route);
        Assert.Null(result.Definition);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.NotMatched, diagnostic.Code);
        Assert.Equal("route.not_matched", diagnostic.Code);
        Assert.DoesNotContain("token-7Qx9Zr2Kp4", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("reset", diagnostic.Message, StringComparison.Ordinal);
        KeyValuePair<string, object?> entry = Assert.Single(diagnostic.Data);
        Assert.Equal("path", entry.Key);
        Assert.Equal(SecretPath, entry.Value);
    }

    // A lone surrogate cannot be an attribute argument, which the compiler writes as UTF-8, so the cases are members.
    public static TheoryData<string> UnprintablePaths { get; } = new()
    {
        "/reset/a\u0009b",
        "/reset/a\u000Ab",
        "/reset/a\uD800b",
        "/reset/a\u2028b",
    };

    [Theory]
    [MemberData(nameof(UnprintablePaths))]
    public void TheDiagnosticMessageHasNoTextFromThePathButDataKeepsItUnchanged(string path)
    {
        RouteTable table = new RouteTableBuilder().Map("/settings", _ => new SettingsRoute()).Build();

        RouteDiagnostic diagnostic = Assert.Single(table.Match(new Uri(path, UriKind.Relative)).Diagnostics);

        Assert.DoesNotContain("reset", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("a\\u", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(path, diagnostic.Data["path"]);
    }

    [Fact]
    public void AVeryLongRelativeUriFailsWithThePathIntactInData()
    {
        string path = "/" + new string('a', 100_000);
        RouteTable table = new RouteTableBuilder()
            .Map("/products/{id}", c => new ProductRoute(c.Path("id")))
            .Build();

        RouteMatchResult result = table.Match(new Uri(path, UriKind.Relative));

        Assert.False(result.IsSuccess);
        Assert.Equal(path, Assert.Single(result.Diagnostics).Data["path"]);
    }

    [Fact]
    public void DiagnosticDataCannotBeChangedThroughACast()
    {
        RouteTable table = new RouteTableBuilder().Build();

        RouteDiagnostic diagnostic = Assert.Single(table.Match(new Uri("/missing", UriKind.Relative)).Diagnostics);

        Assert.IsNotType<Dictionary<string, object?>>(diagnostic.Data);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, object?>>(diagnostic.Data);
        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary["path"] = "/other");
        Assert.Throws<NotSupportedException>(() => dictionary.Add("extra", 1));
        Assert.Equal("/missing", diagnostic.Data["path"]);
    }

    [Fact]
    public void EachFailedMatchHasItsOwnDiagnosticData()
    {
        RouteTable table = new RouteTableBuilder().Build();

        RouteDiagnostic first = Assert.Single(table.Match(new Uri("/a", UriKind.Relative)).Diagnostics);
        RouteDiagnostic second = Assert.Single(table.Match(new Uri("/b", UriKind.Relative)).Diagnostics);

        Assert.NotSame(first.Data, second.Data);
        Assert.Equal("/a", first.Data["path"]);
        Assert.Equal("/b", second.Data["path"]);
    }

    [Fact]
    public void DiagnosticsCannotBeChangedThroughACast()
    {
        RouteTable table = new RouteTableBuilder().Build();

        RouteMatchResult result = table.Match(new Uri("/missing", UriKind.Relative));

        var list = Assert.IsAssignableFrom<IList<RouteDiagnostic>>(result.Diagnostics);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = list[0]);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("%zz/%FF")]
    [InlineData("/a/../b")]
    [InlineData("/a/%2E%2E/b")]
    [InlineData("?")]
    [InlineData("#")]
    [InlineData("//")]
    [InlineData("/{id}")]
    [InlineData("/a\u2028b")]
    public void MatchDoesNotThrowForTheContentOfARelativeUri(string text)
    {
        AssertMatchesWithoutThrowing(new Uri(text, UriKind.Relative));
    }

    [Theory]
    [InlineData("mailto:a@b.c")]
    [InlineData("urn:x:y")]
    [InlineData("myapp:")]
    [InlineData("myapp://")]
    [InlineData("file:///c:/x")]
    [InlineData("C:\\x")]
    [InlineData("\\\\server\\share")]
    [InlineData("https://example.com/a/%2E%2E/%zz/%FF")]
    [InlineData("app:/a/../b")]
    public void MatchDoesNotThrowForTheContentOfAnAbsoluteUri(string text)
    {
        // However Uri classifies these on this platform, the result is only required to be consistent.
        AssertMatchesWithoutThrowing(new Uri(text, UriKind.RelativeOrAbsolute));
    }

    [Fact]
    public void ACreateFunctionCanMatchAnotherUriOnTheSameTable()
    {
        RouteTable? table = null;
        RouteMatchResult? inner = null;
        table = new RouteTableBuilder()
            .Map("/outer", _ =>
            {
                inner = table!.Match(new Uri("/products/7", UriKind.Relative));
                return new SettingsRoute();
            })
            .Map("/products/{id}", c => new ProductRoute(c.Path("id")))
            .Build();

        RouteMatchResult outer = table.Match(new Uri("/outer", UriKind.Relative));

        Assert.Equal(new SettingsRoute(), outer.Route);
        Assert.NotNull(inner);
        Assert.Equal(new ProductRoute("7"), inner.Route);
        Assert.Same(table.Definitions[1], inner.Definition);
    }

    [Fact]
    public void ACreateFunctionThatMapsOnTheBuilderDoesNotChangeTheBuiltTable()
    {
        var builder = new RouteTableBuilder();
        builder.Map("/settings", _ =>
        {
            builder.Map("/products/{id}", c => new ProductRoute(c.Path("id")));
            return new SettingsRoute();
        });
        RouteTable table = builder.Build();

        Assert.True(table.Match(new Uri("/settings", UriKind.Relative)).IsSuccess);

        Assert.Single(table.Definitions);
        Assert.False(table.Match(new Uri("/products/7", UriKind.Relative)).IsSuccess);
        Assert.Equal(new ProductRoute("7"), builder.Build().Match(new Uri("/products/7", UriKind.Relative)).Route);
    }

    [Fact]
    public void ManyThreadsMatchingAtOnceGetTheirOwnResults()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/settings", _ => new SettingsRoute())
            .Map("/products/{id:int}", c => new ProductRoute(c.Path("id")))
            .Map("/products/{name}", c => new ProductByNameRoute(c.Path("name")))
            .Map(
                "/orders/{id}/{tab?}",
                c => new OrderRoute(c.Path("id"), c.TryGetPath("tab", out string? tab) ? tab : null))
            .Build();
        var failures = 0;

        Parallel.For(0, 4000, i =>
        {
            string id = i.ToString(CultureInfo.InvariantCulture);
            (string uri, AppRoute? expected) = (i % 5) switch
            {
                0 => ($"/products/{id}", new ProductRoute(id)),
                // Values that are still percent text after one decoding, so that a count of decoded segments
                // shared between calls, which would decode them twice or not at all, gives a wrong route.
                1 => ($"https://example.com/products/p%2520{id}", new ProductByNameRoute($"p%20{id}")),
                2 => ($"myapp:///orders/%2541{id}/items?x={id}", new OrderRoute($"%41{id}", "items")),
                3 => ("/settings", new SettingsRoute()),
                _ => ($"/missing/{id}", (AppRoute?)null),
            };

            RouteMatchResult result = table.Match(new Uri(uri, UriKind.RelativeOrAbsolute));
            bool correct = expected is null
                ? !result.IsSuccess && Equals(uri, result.Diagnostics[0].Data["path"])
                : result.IsSuccess && result.Route.Equals(expected);
            if (!correct)
                Interlocked.Increment(ref failures);
        });

        Assert.Equal(0, failures);
    }

    // Matches the URI against a table that most URIs fail and one whose catch-all hands every path without a dot
    // segment to a create function, and checks that each result keeps the promises IsSuccess makes.
    private static void AssertMatchesWithoutThrowing(Uri uri)
    {
        RouteTable narrow = new RouteTableBuilder()
            .Map("/products/{id}", c => new ProductRoute(c.Path("id")))
            .Build();
        var calls = 0;
        RouteTable catchAll = new RouteTableBuilder()
            .Map("/{*rest}", c =>
            {
                calls++;
                return new FileRoute(c.TryGetPath("rest", out string? rest) ? rest : null);
            })
            .Build();

        foreach (RouteTable table in new[] { narrow, catchAll })
        {
            RouteMatchResult result = table.Match(uri);
            if (result.IsSuccess)
            {
                Assert.NotNull(result.Route);
                Assert.NotNull(result.Definition);
            }
            else
            {
                Assert.Null(result.Route);
                Assert.NotEmpty(result.Diagnostics);
            }
        }

        Assert.InRange(calls, 0, 1);
    }

    private static void AssertDefinition(RouteDefinition definition, Type routeType, string template)
    {
        Assert.Equal(routeType, definition.RouteType);
        Assert.Equal(template, definition.Template.Value);
    }
}
