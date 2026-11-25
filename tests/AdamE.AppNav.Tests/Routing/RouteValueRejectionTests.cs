using System.Diagnostics;
using System.Globalization;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteValueRejectionTests
{
    // Text that stands for secrets a deep link can carry, distinctive enough that finding one in a message or in Data
    // is not a coincidence: a value in the path, which the create function rejects, and one in the query.
    private const string BadId = "bad-Vn3Jq8Rw";
    private const string QuerySecret = "Secret-9Wq4Lm7Tz";
    private const string Reason = "An order id is a positive number.";
    private const string Template = "/orders/{id}";

    [Theory]
    [InlineData($"/orders/{BadId}?token={QuerySecret}#top", UriKind.Relative)]
    [InlineData($"https://example.com/orders/{BadId}?token={QuerySecret}#top", UriKind.Absolute)]
    [InlineData($"myapp:///orders/{BadId}?token={QuerySecret}", UriKind.Absolute)]
    public void ARejectedValueFailsTheMatchWithOneValueInvalidDiagnostic(string text, UriKind kind)
    {
        AssertRejected(new Uri(text, kind));
    }

    [Fact]
    public void ARejectedValueOnAnImplicitFileUriFailsTheMatchTheSameWay()
    {
        // On Unix-like platforms a string that starts with '/' is a file URI, which the table reads as relative.
        if (!Uri.TryCreate($"/orders/{BadId}?token={QuerySecret}", UriKind.Absolute, out Uri? implicitFile))
            Assert.Skip("On this platform a string that starts with '/' is not an absolute URI.");

        Assert.True(implicitFile.IsFile);
        AssertRejected(implicitFile);
    }

    // The message is one fixed text. Two rejections that differ in everything they could put into it, the name, the
    // reason, the path and the template, must give the same message, which a check for one of them could miss.
    [Fact]
    public void EveryRejectionHasTheSameMessage()
    {
        RouteTable table = new RouteTableBuilder()
            .Map<ProductRoute>("/orders/{id}", _ => throw new RouteValueException("id", Reason))
            .Map<SettingsRoute>("/users/{user}/{*rest}", _ => throw new RouteValueException("user"))
            .Build();

        RouteDiagnostic first = Assert.Single(table.Match(new Uri("/orders/1", UriKind.Relative)).Diagnostics);
        RouteDiagnostic second = Assert.Single(table.Match(new Uri("/users/x/y", UriKind.Relative)).Diagnostics);

        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, first.Code);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, second.Code);
        Assert.NotEqual(first.Data["reason"], second.Data["reason"]);
        Assert.Equal(first.Message, second.Message);
    }

    // Only a null message is replaced by the default one, as for other exceptions.
    [Fact]
    public void AnEmptyMessageIsKeptAsTheReason()
    {
        RouteTable table = new RouteTableBuilder()
            .Map<ProductRoute>("/orders/{id}", _ => throw new RouteValueException("id", string.Empty))
            .Build();

        RouteDiagnostic diagnostic = Assert.Single(table.Match(new Uri("/orders/1", UriKind.Relative)).Diagnostics);

        Assert.Equal(string.Empty, diagnostic.Data["reason"]);
        Assert.Equal("id", diagnostic.Data["name"]);
    }

    [Fact]
    public void TheDiagnosticDataCannotBeChangedThroughACast()
    {
        RouteDiagnostic diagnostic = Assert.Single(
            OrdersTable().Match(new Uri($"/orders/{BadId}", UriKind.Relative)).Diagnostics);

        Assert.IsNotType<Dictionary<string, object?>>(diagnostic.Data);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, object?>>(diagnostic.Data);
        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary["name"] = "other");
        Assert.Throws<NotSupportedException>(() => dictionary.Add("extra", 1));
        Assert.Equal("id", diagnostic.Data["name"]);
    }

    [Fact]
    public void NeitherTheInnerExceptionNorItsMessageReachesTheResult()
    {
        RouteTable table = new RouteTableBuilder()
            .Map<ProductRoute>(Template, _ => throw new RouteValueException("id", null, new FormatException(BadId)))
            .Build();

        RouteDiagnostic diagnostic = Assert.Single(
            table.Match(new Uri("/orders/1", UriKind.Relative)).Diagnostics);

        Assert.DoesNotContain(BadId, diagnostic.Message, StringComparison.Ordinal);
        foreach (object? value in diagnostic.Data.Values)
            Assert.DoesNotContain(BadId, Assert.IsType<string>(value), StringComparison.Ordinal);
        Assert.Contains("'id'", Assert.IsType<string>(diagnostic.Data["reason"]), StringComparison.Ordinal);
    }

    [Fact]
    public void ANameWithAControlCharacterIsRawInNameAndEscapedInTheDefaultReason()
    {
        RouteTable table = new RouteTableBuilder()
            .Map<SettingsRoute>("/settings", _ => throw new RouteValueException("a\u000Ab"))
            .Build();

        RouteDiagnostic diagnostic = Assert.Single(
            table.Match(new Uri("/settings", UriKind.Relative)).Diagnostics);

        Assert.Equal("a\u000Ab", diagnostic.Data["name"]);
        string reason = Assert.IsType<string>(diagnostic.Data["reason"]);
        Assert.DoesNotContain("\u000A", reason, StringComparison.Ordinal);
        Assert.Contains("a\\u000Ab", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ARejectedQueryValueIsReportedByItsNameWithoutTheQueryText()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/settings", c => c.TryGetQuery("tab", out string? tab) && tab is "general" or "privacy"
                ? new SettingsRoute()
                : throw new RouteValueException("tab", "The tab is general or privacy."))
            .Build();

        RouteMatchResult result = table.Match(new Uri($"/settings?tab={QuerySecret}", UriKind.Relative));

        Assert.Same(table.Definitions[0], result.Definition);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("tab", diagnostic.Data["name"]);
        Assert.Equal("/settings", diagnostic.Data["path"]);
        Assert.DoesNotContain(QuerySecret, diagnostic.Message, StringComparison.Ordinal);
        foreach (object? value in diagnostic.Data.Values)
            Assert.DoesNotContain(QuerySecret, Assert.IsType<string>(value), StringComparison.Ordinal);
        Assert.True(table.Match(new Uri("/settings?tab=privacy", UriKind.Relative)).IsSuccess);
    }

    [Fact]
    public void AnEmptyNameIsReportedAsTheEmptyString()
    {
        RouteTable table = new RouteTableBuilder()
            .Map<SettingsRoute>("/settings", _ => throw new RouteValueException(""))
            .Build();

        RouteDiagnostic diagnostic = Assert.Single(
            table.Match(new Uri("/settings?=x", UriKind.Relative)).Diagnostics);

        Assert.Equal("", diagnostic.Data["name"]);
        Assert.Contains("''", Assert.IsType<string>(diagnostic.Data["reason"]), StringComparison.Ordinal);
    }

    [Fact]
    public void NoLaterTemplateIsTriedEvenWhenItAlsoMatches()
    {
        var laterCalls = 0;
        RouteTable table = new RouteTableBuilder()
            .Map("/orders/{*rest}", c => { laterCalls++; return new FileRoute(c.Path("rest")); })
            .Map("/{area}/{id}", c => { laterCalls++; return new ProductRoute(c.Path("id")); })
            .Map<ProductRoute>(Template, _ => throw new RouteValueException("id", Reason))
            .Build();

        RouteMatchResult result = table.Match(new Uri($"/orders/{BadId}", UriKind.Relative));

        Assert.False(result.IsSuccess);
        Assert.Same(table.Definitions[2], result.Definition);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, Assert.Single(result.Diagnostics).Code);
        Assert.Equal(0, laterCalls);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("lookalike")]
    [InlineData("canceled")]
    [InlineData("wrapped")]
    public void EveryOtherExceptionPropagatesAsTheSameInstanceWithItsStack(string kind)
    {
        Exception thrown = kind switch
        {
            "format" => new FormatException("not a number"),
            "lookalike" => new LookalikeRouteValueException(),
            "canceled" => new OperationCanceledException(),
            _ => new InvalidOperationException("wrapped", new RouteValueException("id")),
        };
        var laterCalls = 0;
        RouteTable table = new RouteTableBuilder()
            .Map<ProductRoute>("/products/{id:int}", _ => throw thrown)
            .Map("/products/{name}", c => { laterCalls++; return new ProductByNameRoute(c.Path("name")); })
            .Build();

        Exception exception = Assert.ThrowsAny<Exception>(
            () => table.Match(new Uri("/products/42", UriKind.Relative)));

        // The stack starts where the create function threw, in this class or a closure nested in it, so nothing
        // caught and threw it again.
        Assert.Same(thrown, exception);
        Type? thrower = new StackTrace(exception).GetFrame(0)?.GetMethod()?.DeclaringType;
        while (thrower is not null && thrower != typeof(RouteValueRejectionTests))
            thrower = thrower.DeclaringType;
        Assert.Equal(typeof(RouteValueRejectionTests), thrower);
        Assert.Equal(0, laterCalls);
    }

    [Fact]
    public void ACreateFunctionThatReturnsNullStillThrows()
    {
        RouteTable table = new RouteTableBuilder().Map<ProductRoute>(Template, _ => null!).Build();

        Assert.Throws<InvalidOperationException>(() => table.Match(new Uri("/orders/1", UriKind.Relative)));
    }

    [Fact]
    public void ARejectionInsideANestedMatchOnAnotherTableGivesTheInnerCallAResult()
    {
        RouteTable inner = OrdersTable();
        RouteMatchResult? innerResult = null;
        RouteTable outer = new RouteTableBuilder()
            .Map("/outer", _ =>
            {
                innerResult = inner.Match(new Uri($"/orders/{BadId}", UriKind.Relative));
                return new SettingsRoute();
            })
            .Build();

        RouteMatchResult result = outer.Match(new Uri("/outer", UriKind.Relative));

        Assert.Equal(new SettingsRoute(), result.Route);
        Assert.NotNull(innerResult);
        Assert.False(innerResult.IsSuccess);
        Assert.Same(inner.Definitions[0], innerResult.Definition);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, Assert.Single(innerResult.Diagnostics).Code);
    }

    [Fact]
    public void ARejectionInsideANestedMatchOnTheSameTableGivesTheInnerCallAResult()
    {
        RouteTable? table = null;
        RouteMatchResult? innerResult = null;
        table = new RouteTableBuilder()
            .Map("/outer", _ =>
            {
                innerResult = table!.Match(new Uri($"/orders/{BadId}", UriKind.Relative));
                return new SettingsRoute();
            })
            .Map(Template, CreateOrder)
            .Build();

        RouteMatchResult result = table.Match(new Uri("/outer", UriKind.Relative));

        Assert.Equal(new SettingsRoute(), result.Route);
        Assert.Same(table.Definitions[0], result.Definition);
        Assert.NotNull(innerResult);
        Assert.Null(innerResult.Route);
        Assert.Same(table.Definitions[1], innerResult.Definition);
    }

    [Fact]
    public void TheSameDefinitionSucceedsForAGoodValueRightAfterRejectingABadOne()
    {
        RouteTable table = OrdersTable();

        RouteMatchResult rejected = table.Match(new Uri($"/orders/{BadId}", UriKind.Relative));
        RouteMatchResult accepted = table.Match(new Uri("/orders/42", UriKind.Relative));

        Assert.False(rejected.IsSuccess);
        Assert.True(accepted.IsSuccess);
        Assert.Equal(new ProductRoute("42"), accepted.Route);
        Assert.Same(rejected.Definition, accepted.Definition);
        Assert.Empty(accepted.Diagnostics);
    }

    [Fact]
    public void ManyThreadsAcceptingAndRejectingValuesAtOnceGetTheirOwnResults()
    {
        RouteTable table = OrdersTable();
        var failures = 0;

        Parallel.For(0, 4000, i =>
        {
            string id = (i + 1).ToString(CultureInfo.InvariantCulture);
            bool good = i % 2 == 0;
            string path = good ? $"/orders/{id}" : $"/orders/x{id}";

            RouteMatchResult result = table.Match(new Uri(path, UriKind.Relative));
            bool correct = good
                ? result.IsSuccess && result.Route.Equals(new ProductRoute(id))
                : !result.IsSuccess && Equals(path, result.Diagnostics[0].Data["path"]) &&
                  Equals(RouteDiagnosticCodes.ValueInvalid, result.Diagnostics[0].Code);
            if (!correct)
                Interlocked.Increment(ref failures);
        });

        Assert.Equal(0, failures);
    }

    private static void AssertRejected(Uri uri)
    {
        RouteTable table = OrdersTable();

        RouteMatchResult result = table.Match(uri);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Route);
        Assert.Same(table.Definitions[0], result.Definition);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("route.value.invalid", diagnostic.Code);
        Assert.Equal(4, diagnostic.Data.Count);
        Assert.Equal("id", diagnostic.Data["name"]);
        Assert.Equal(Template, diagnostic.Data["template"]);
        Assert.Equal(Reason, diagnostic.Data["reason"]);
        Assert.Equal($"/orders/{BadId}", diagnostic.Data["path"]);
        Assert.DoesNotContain(BadId, diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("orders", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Reason, diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'id'", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(QuerySecret, diagnostic.Message, StringComparison.Ordinal);
        foreach (object? value in diagnostic.Data.Values)
            Assert.DoesNotContain(QuerySecret, Assert.IsType<string>(value), StringComparison.Ordinal);
    }

    // Accepts a positive number and rejects anything else, as an app would for a value the template cannot check.
    private static RouteTable OrdersTable()
    {
        return new RouteTableBuilder().Map(Template, CreateOrder).Build();
    }

    private static ProductRoute CreateOrder(RouteMatchContext context)
    {
        string id = context.Path("id");
        return int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0
            ? new ProductRoute(id)
            : throw new RouteValueException("id", Reason);
    }

    // Named like the exception the table catches, to show that the catch is by type and not by name or base type.
    private sealed class LookalikeRouteValueException : Exception;
}
