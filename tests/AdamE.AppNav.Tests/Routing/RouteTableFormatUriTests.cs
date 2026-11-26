using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTableFormatUriTests
{
    private const string NotAbsolute = "must be an absolute URI";
    private const string HasPath = "has a path";
    private const string HasQuery = "has a query";
    private const string HasFragment = "has a fragment";
    private const string ZoneId = "zone id";
    private const string TrailingSpace = "the last value written ends with a space";
    private const string SchemeChanges = "does not keep the link as written";
    private const string Required = "is required, but its value is null";
    private const string NoText = "has a value that cannot be written:";
    private const string NotMapped = "no mapping of exactly that type exists";
    private const string NoFormat = "none of its mappings has a format function";
    private const string CapturedFirst = "is matched first by the template";

    // Text that stands for a secret a route or a base can carry, distinctive enough that finding it in a message is
    // not a coincidence.
    private const string Secret = "tok-Vn3Jq8Rw-9Wq4Lm7Tz";

    // The base gives the scheme and authority as System.Uri writes them in its AbsoluteUri, followed by exactly the
    // text Format writes, and Match reads the link back as the route.
    [Theory]
    [InlineData("https://example.com", "https://example.com/orders/42")]
    [InlineData("https://example.com/", "https://example.com/orders/42")]
    [InlineData("HTTPS://Example.COM", "https://example.com/orders/42")]
    [InlineData("https://user:pw@example.com:8443", "https://user:pw@example.com:8443/orders/42")]
    [InlineData("https://example.com:443", "https://example.com/orders/42")]
    [InlineData("http://localhost:5000", "http://localhost:5000/orders/42")]
    [InlineData("http://[::1]:5000", "http://[::1]:5000/orders/42")]
    [InlineData("https://\u00E9.example", "https://\u00E9.example/orders/42")]
    [InlineData("https://xn--9ca.example", "https://xn--9ca.example/orders/42")]
    [InlineData("myapp://host", "myapp://host/orders/42")]
    [InlineData("myapp://host/", "myapp://host/orders/42")]
    [InlineData("myapp:///", "myapp:///orders/42")]
    [InlineData("myapp://", "myapp:///orders/42")]
    [InlineData("myapp:/", "myapp:/orders/42")]
    [InlineData("myapp:", "myapp:/orders/42")]
    [InlineData("mailto:", "mailto:/orders/42")]
    [InlineData("mailto:a@b.c", "mailto:a@b.c/orders/42")]
    [InlineData("urn:", "urn:/orders/42")]
    [InlineData("about:", "about:/orders/42")]
    [InlineData("data:", "data:/orders/42")]
    [InlineData("ws://h", "ws://h/orders/42")]
    [InlineData("wss://h", "wss://h/orders/42")]
    [InlineData("ftp://h", "ftp://h/orders/42")]
    public void ARouteIsWrittenBehindTheSchemeAndAuthorityOfTheBase(string baseUri, string expected)
    {
        RouteTable table = OrdersTable();

        Uri uri = table.FormatUri(new IdRoute(42), new Uri(baseUri));

        Assert.True(uri.IsAbsoluteUri);
        Assert.Equal(expected, uri.AbsoluteUri);
        AssertMatches(table, uri, new IdRoute(42));
    }

    // A query is written after the path as Format writes it, under every base whose scheme has a query.
    [Theory]
    [InlineData("https://example.com", "https://example.com/s/a%20b?q=x%20y")]
    [InlineData("https://user:pw@example.com:8443", "https://user:pw@example.com:8443/s/a%20b?q=x%20y")]
    [InlineData("https://\u00E9.example", "https://\u00E9.example/s/a%20b?q=x%20y")]
    [InlineData("myapp://host", "myapp://host/s/a%20b?q=x%20y")]
    [InlineData("myapp:///", "myapp:///s/a%20b?q=x%20y")]
    [InlineData("myapp:", "myapp:/s/a%20b?q=x%20y")]
    [InlineData("urn:", "urn:/s/a%20b?q=x%20y")]
    [InlineData("ws://h", "ws://h/s/a%20b?q=x%20y")]
    public void AQueryIsWrittenAfterThePath(string baseUri, string expected)
    {
        RouteTable table = SpacedTable("/s/{name}");
        var route = new SpacedRoute("a b", "x y", null);

        Uri uri = table.FormatUri(route, new Uri(baseUri));

        Assert.Equal(expected, uri.AbsoluteUri);
        AssertMatches(table, uri, route);
    }

    // How System.Uri reads a file URI depends on the platform, so on every platform the link either reads back as the
    // route or is refused for the base; the exact text, measured on Unix-like platforms, is checked only there.
    [Theory]
    [InlineData("file:///", "file:///s/a%20b?q=x%20y")]
    [InlineData("file://host/", "file://host/s/a%20b?q=x%20y")]
    public void AFileBaseWritesALinkThatReadsBack(string baseUri, string expected)
    {
        RouteTable table = SpacedTable("/s/{name}");
        var route = new SpacedRoute("a b", "x y", null);

        AssertReadsBackOrRefusesTheBase(table, route, new Uri(baseUri));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(expected, table.FormatUri(route, new Uri(baseUri)).AbsoluteUri);
    }

    // A custom scheme with a host keeps it as the host, so the whole path is the route's, where a hand-written link
    // with two slashes would lose its first segment to the host.
    [Fact]
    public void ACustomSchemeHostStaysTheHost()
    {
        RouteTable table = OrdersTable();

        Uri withHost = table.FormatUri(new IdRoute(42), new Uri("myapp://host"));
        Uri withoutHost = table.FormatUri(new IdRoute(42), new Uri("myapp:///"));

        Assert.Equal("host", withHost.Host);
        Assert.Equal("/orders/42", withHost.AbsolutePath);
        Assert.Equal("", withoutHost.Host);
        Assert.Equal("/orders/42", withoutHost.AbsolutePath);
        Assert.Equal("orders", new Uri("myapp://orders/42").Host);
    }

    // The host is written as System.Uri writes it in the base's AbsoluteUri, in Unicode; Match does not read it.
    [Fact]
    public void AnInternationalHostIsWrittenInUnicode()
    {
        RouteTable table = OrdersTable();

        Uri uri = table.FormatUri(new IdRoute(42), new Uri("https://\u00E9.example"));

        Assert.Equal("\u00E9.example", uri.Host);
        Assert.Equal("xn--9ca.example", uri.IdnHost);
        AssertMatches(table, uri, new IdRoute(42));
    }

    // Uri.ToString unescapes %20, and the text it gives is trimmed when it is parsed again, so only the Uri itself or
    // its AbsoluteUri carries the link. This is System.Uri's behaviour, pinned so that the documentation stays true.
    [Fact]
    public void OnlyTheAbsoluteUriOfTheResultKeepsTheLink()
    {
        RouteTable table = SpacedTable("/s/{name}");
        var route = new SpacedRoute("x ", null, null);

        Uri uri = table.FormatUri(route, new Uri("https://h"));

        Assert.Equal("https://h/s/x%20", uri.AbsoluteUri);
        Assert.Equal("https://h/s/x ", uri.ToString());
        Assert.Equal("https://h/s/x ", $"{uri}");
        AssertMatches(table, new Uri(uri.AbsoluteUri), route);
        Assert.NotEqual(route, table.Match(new Uri(uri.ToString())).Route);
    }

    // Percent-encoded user information is written as the base has it, so that nothing in it can become the host,
    // the port or a separator of the link.
    [Theory]
    [InlineData("https://a%40evil.com:p%23w@good.com", "https://a%40evil.com:p%23w@good.com/orders/42", "good.com",
        "a%40evil.com:p%23w")]
    [InlineData("https://us%20er@h", "https://us%20er@h/orders/42", "h", "us%20er")]
    [InlineData("myapp://u%2Fx@h", "myapp://u%2Fx@h/orders/42", "h", "u%2Fx")]
    public void EncodedUserInformationStaysUserInformation(string baseUri, string expected, string host, string user)
    {
        RouteTable table = OrdersTable();

        Uri uri = table.FormatUri(new IdRoute(42), new Uri(baseUri));

        Assert.Equal(expected, uri.AbsoluteUri);
        Assert.Equal(host, uri.Host);
        Assert.Equal(user, uri.UserInfo);
        Assert.Equal(new Uri(baseUri).Port, uri.Port);
        AssertMatches(table, uri, new IdRoute(42));
    }

    // System.Uri leaves a zone id out of AbsoluteUri, so a link written behind such a host would name another
    // address; one without a zone id is written.
    [Fact]
    public void AnIPv6HostWithAZoneIdIsRejected()
    {
        RouteTable table = OrdersTable();
        var zoned = new Uri("http://[fe80::1%25en0]:5000");

        Assert.Equal("http://[fe80::1]:5000/", zoned.AbsoluteUri);
        AssertBaseRejected(table, zoned, ZoneId);
        Assert.Equal(
            "http://[fe80::1]:5000/orders/42",
            table.FormatUri(new IdRoute(42), new Uri("http://[fe80::1]:5000")).AbsoluteUri);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        RouteTable table = OrdersTable();

        Assert.Equal(
            "route",
            Assert.Throws<ArgumentNullException>(() => table.FormatUri(null!, new Uri("https://h"))).ParamName);
        Assert.Equal(
            "baseUri",
            Assert.Throws<ArgumentNullException>(() => table.FormatUri(new IdRoute(1), null!)).ParamName);
    }

    [Fact]
    public void ARelativeBaseIsRejected()
    {
        RouteTable table = OrdersTable();

        foreach (Uri baseUri in new[]
                 {
                     new Uri("orders", UriKind.Relative),
                     new Uri("", UriKind.Relative),
                     new Uri("/", UriKind.RelativeOrAbsolute),
                     new Uri("//example.com", UriKind.Relative),
                 })
        {
            AssertBaseRejected(table, baseUri, NotAbsolute);
        }
    }

    // On Unix-like platforms new Uri("/") is a file URI, which Match reads as the relative URI the caller wrote, so it
    // is no base either.
    [Fact]
    public void AnImplicitFileUriIsRejectedAsRelative()
    {
        if (!Uri.TryCreate("/", UriKind.Absolute, out Uri? implicitFile))
            Assert.Skip("On this platform a string that starts with '/' is not an absolute URI.");

        Assert.True(implicitFile.IsFile);
        AssertBaseRejected(OrdersTable(), implicitFile, NotAbsolute);
    }

    [Theory]
    [InlineData("https://example.com/app", HasPath)]
    [InlineData("https://example.com/app/", HasPath)]
    [InlineData("https://example.com//", HasPath)]
    [InlineData("https://example.com/%2F", HasPath)]
    [InlineData("myapp:///app", HasPath)]
    [InlineData("myapp://host/app", HasPath)]
    [InlineData("myapp:app", HasPath)]
    [InlineData("urn:isbn:1", HasPath)]
    [InlineData("about:blank", HasPath)]
    [InlineData("data:,x", HasPath)]
    [InlineData("https://example.com?a=1", HasQuery)]
    [InlineData("https://example.com/?a=1", HasQuery)]
    [InlineData("https://example.com#top", HasFragment)]
    [InlineData("myapp:///#top", HasFragment)]
    public void ABaseWithAPathQueryOrFragmentIsRejected(string baseUri, string category)
    {
        AssertBaseRejected(OrdersTable(), new Uri(baseUri), category);
    }

    // System.Uri reports an empty query as "?" and an empty fragment as "#", and the text written after either would
    // be read as part of it, so they count as present.
    [Theory]
    [InlineData("https://example.com?", "?", "", HasQuery)]
    [InlineData("https://example.com/?", "?", "", HasQuery)]
    [InlineData("https://example.com#", "", "#", HasFragment)]
    [InlineData("myapp:///?", "?", "", HasQuery)]
    public void AnEmptyQueryOrFragmentIsRejected(string baseUri, string query, string fragment, string category)
    {
        var parsed = new Uri(baseUri);

        Assert.Equal(query, parsed.Query);
        Assert.Equal(fragment, parsed.Fragment);
        AssertBaseRejected(OrdersTable(), parsed, category);
    }

    // The base can carry user information, so no message about it quotes the base, or any part of it.
    [Theory]
    [InlineData("https://u:" + Secret + "@example.com/app", HasPath)]
    [InlineData("https://u:" + Secret + "@example.com/?x", HasQuery)]
    [InlineData("myapp://u:" + Secret + "@h0st/#x", HasFragment)]
    public void ARejectedBaseIsNeverQuoted(string baseUri, string category)
    {
        ArgumentException exception = AssertBaseRejected(OrdersTable(), new Uri(baseUri), category);

        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
        foreach (string quoted in new[] { "example.com", "h0st", "/app", "?x", "#x" })
            Assert.DoesNotContain(quoted, exception.Message, StringComparison.Ordinal);
    }

    // System.Uri drops %20 from the very end of an absolute URI that contains a non-ASCII character anywhere, so a
    // link whose last value ends with a space would read back without it, and is refused.
    [Theory]
    [MemberData(nameof(TrailingSpaceCases))]
    public void ALinkThatWouldLoseATrailingSpaceIsRefused(string category, string baseUri, AppRoute route)
    {
        RouteTable table = TrailingTable();
        string text = table.Format(route);
        Uri naive = new(new Uri(baseUri).GetLeftPart(UriPartial.Authority) + text);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => table.FormatUri(route, new Uri(baseUri)));

        Assert.Equal("route", exception.ParamName);
        Assert.Null(exception.InnerException);
        Assert.Contains(TrailingSpace, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{route.GetType().Name}'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(text, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("example", exception.Message, StringComparison.Ordinal);
        Assert.True(text.EndsWith("%20", StringComparison.Ordinal), category);
        Assert.True(text != naive.AbsolutePath + naive.Query, category);
    }

    public static TheoryData<string, string, AppRoute> TrailingSpaceCases => new()
    {
        { "non-ASCII path value", "https://example.com", new SpacedRoute("\u00E9" + Secret + " ", null, null) },
        { "non-ASCII query value", "https://example.com", new SpacedRoute(Secret, "\u00E9" + Secret + " ", null) },
        { "non-ASCII literal", "https://example.com", new LiteralRoute(Secret + " ") },
        { "non-ASCII host only", "https://\u00E9.example", new SpacedRoute(Secret + " ", null, null) },
        { "non-ASCII user information", "https://\u00E9@example.com", new SpacedRoute(Secret + " ", null, null) },
        { "last query value", "https://\u00E9.example", new SpacedRoute(Secret, "x", Secret + " ") },
        { "last element of a list", "https://\u00E9.example", new ListRoute([Secret, Secret + " "]) },
        { "several trailing spaces", "https://\u00E9.example", new SpacedRoute(Secret + "   ", null, null) },
        { "path value of spaces only", "https://\u00E9.example", new SpacedRoute("   ", null, null) },
        { "query value of spaces only", "https://\u00E9.example", new SpacedRoute(Secret, null, "  ") },
        { "no-query scheme", "ftp://\u00E9.example", new SpacedRoute(Secret + " ", null, null) },
        { "custom scheme", "myapp://\u00E9", new SpacedRoute(Secret + " ", null, null) },
    };

    // The neighbours of the refused links keep every space, and are written.
    [Theory]
    [MemberData(nameof(KeptSpaceCases))]
    public void ALinkThatKeepsItsSpacesIsWritten(string category, string baseUri, AppRoute route)
    {
        RouteTable table = TrailingTable();

        Uri uri = table.FormatUri(route, new Uri(baseUri));

        Assert.True(uri.AbsoluteUri.EndsWith(table.Format(route), StringComparison.Ordinal), category);
        AssertMatches(table, uri, route);
    }

    public static TheoryData<string, string, AppRoute> KeptSpaceCases => new()
    {
        { "all-ASCII path value", "https://example.com", new SpacedRoute("a ", null, null) },
        { "all-ASCII query value", "https://example.com", new SpacedRoute("a", null, "b ") },
        { "all-ASCII spaces only", "myapp:///", new SpacedRoute("  ", null, "  ") },
        { "all-ASCII list", "https://example.com", new ListRoute(["a", "b "]) },
        { "path value followed by a query", "https://\u00E9.example", new SpacedRoute("a ", "b", null) },
        { "query value followed by another", "https://\u00E9.example", new SpacedRoute("a ", "b ", "c") },
        { "list element followed by another", "https://\u00E9.example", new ListRoute(["a ", "b"]) },
        { "space inside the last value", "https://\u00E9.example", new SpacedRoute("a b", null, null) },
    };

    // Under a scheme for which System.Uri changes the path or the query, the link is refused for the base, naming the
    // scheme alone.
    [Theory]
    [InlineData("ftp://u:" + Secret + "@example.com", "ftp", "a", "x")]
    [InlineData("gopher://example.com", "gopher", "a", "x")]
    [InlineData("telnet://example.com", "telnet", "a", "x")]
    [InlineData("news:", "news", "a", "x")]
    [InlineData("nntp://example.com", "nntp", "a", "x")]
    [InlineData("uuid:", "uuid", "a", "x")]
    [InlineData("net.tcp://example.com", "net.tcp", "a\\b", null)]
    [InlineData("net.pipe://example.com", "net.pipe", "a\\b", null)]
    public void ASchemeThatChangesTheLinkIsRefused(string baseUri, string scheme, string name, string? q)
    {
        RouteTable table = SpacedTable("/s/{name}");
        var route = new SpacedRoute(name, q, null);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => table.FormatUri(route, new Uri(baseUri)));

        AssertSchemeRefused(exception, scheme);
    }

    // file and vsmacros decode %2F in a path segment into a separator, so a value with a '/' there is refused, while a
    // catch-all value's '/' and a query value's %2F are kept. Every platform gets the oracle; the exact outcomes,
    // measured on Unix-like platforms, are checked only there for file, whose reading depends on the platform.
    [Theory]
    [InlineData("file:///")]
    [InlineData("vsmacros://example.com")]
    public void ASchemeThatDecodesASlashRefusesItInASegment(string baseUri)
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/s/{name}", ReadSpaced, WriteSpaced)
            .Map("/f/{*path}", c => new LiteralRoute(c.Path("path")), f => f.Path("path", r => r.Name))
            .Build();
        var parsed = new Uri(baseUri);
        var slashInSegment = new SpacedRoute("a/b", null, null);
        var slashInCatchAll = new LiteralRoute("a/b");

        AssertReadsBackOrRefusesTheBase(table, slashInSegment, parsed);
        AssertReadsBackOrRefusesTheBase(table, slashInCatchAll, parsed);
        if (OperatingSystem.IsWindows() && parsed.IsFile)
            return;

        AssertSchemeRefused(
            Assert.Throws<ArgumentException>(() => table.FormatUri(slashInSegment, parsed)),
            parsed.Scheme);
        Assert.EndsWith("/f/a/b", table.FormatUri(slashInCatchAll, parsed).AbsoluteUri, StringComparison.Ordinal);
        if (parsed.IsFile)
        {
            Assert.Equal(
                "file:///s/a?q=%2F",
                table.FormatUri(new SpacedRoute("a", "/", null), parsed).AbsoluteUri);
        }
    }

    // A scheme with no query writes a route that has none, and refuses only a link with a query.
    [Fact]
    public void ASchemeWithNoQueryWritesALinkWithoutOne()
    {
        RouteTable table = SpacedTable("/s/{name}");

        Uri uri = table.FormatUri(new SpacedRoute("a b", null, null), new Uri("ftp://example.com"));

        Assert.Equal("ftp://example.com/s/a%20b", uri.AbsoluteUri);
        AssertMatches(table, uri, new SpacedRoute("a b", null, null));
        AssertSchemeRefused(
            Assert.Throws<ArgumentException>(
                () => table.FormatUri(new SpacedRoute("a b", "", null), new Uri("ftp://example.com"))),
            "ftp");
    }

    // Each failure of Format surfaces from FormatUri unchanged: the same type, ParamName and message.
    [Fact]
    public void EveryFailureOfFormatSurfacesUnchanged()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/orders/{id:int}", c => new IdRoute(c.Path<int>("id")), f => f.Path("id", r => r.Id))
            .Map("/unwritable/{id:int}", c => new UnwritableRoute(c.Path<int>("id")))
            .Map("/products/new", _ => new SettingsRoute())
            .Map("/products/{name}", c => new NameRoute(c.Path("name")), f => f.Path("name", r => r.Name))
            .Map("/d/{v}", c => new DoubleRoute(c.Path<double>("v")), f => f.Path("v", r => r.Value))
            .Build();
        var baseUri = new Uri("https://example.com");

        AssertSameFailure<InvalidOperationException>(table, new SettingsRoute(), baseUri, NoFormat);
        AssertSameFailure<InvalidOperationException>(table, new UnwritableRoute(1), baseUri, NoFormat);
        AssertSameFailure<InvalidOperationException>(table, new UnmappedRoute(), baseUri, NotMapped);
        AssertSameFailure<ArgumentException>(table, new NameRoute(null!), baseUri, Required);
        AssertSameFailure<ArgumentException>(table, new NameRoute("new"), baseUri, CapturedFirst);
        AssertSameFailure<ArgumentException>(table, new DoubleRoute(double.NaN), baseUri, NoText);
    }

    [Fact]
    public void AnExceptionFromAValueFunctionPropagatesUnchanged()
    {
        var thrown = new FormatException(Secret);
        RouteTable table = new RouteTableBuilder()
            .Map("/n/{name}", c => new NameRoute(c.Path("name")), f => f.Path<string>("name", _ => throw thrown))
            .Build();

        Assert.Same(thrown, Assert.Throws<FormatException>(
            () => table.FormatUri(new NameRoute("a"), new Uri("https://example.com"))));
    }

    // The base is checked before the route is written, so a bad base is reported even for a route that cannot be
    // written, and before any value function is called; a null route is reported before the base is looked at.
    [Fact]
    public void TheArgumentsAreCheckedBeforeTheBaseAndTheBaseBeforeTheRoute()
    {
        var calls = 0;
        RouteTable table = new RouteTableBuilder()
            .Map("/n/{name}", c => new NameRoute(c.Path("name")), f => f.Path("name", r =>
            {
                calls++;
                return r.Name;
            }))
            .Build();
        var badBase = new Uri("https://example.com/app");

        AssertBaseRejected(table, badBase, HasPath, new NameRoute(null!));
        AssertBaseRejected(table, badBase, HasPath, new UnmappedRoute());
        AssertBaseRejected(table, new Uri("orders", UriKind.Relative), NotAbsolute, new UnmappedRoute());
        Assert.Equal(0, calls);
        Assert.Equal("route", Assert.Throws<ArgumentNullException>(() => table.FormatUri(null!, badBase)).ParamName);
        Assert.Equal(
            "baseUri",
            Assert.Throws<ArgumentNullException>(() => table.FormatUri(new UnmappedRoute(), null!)).ParamName);
        Assert.Equal(0, calls);
    }

    // The route is written once, whether the link is returned or refused.
    [Fact]
    public void EachValueFunctionIsCalledOnce()
    {
        var calls = new List<string>();
        RouteTable table = new RouteTableBuilder()
            .Map("/s/{name}", ReadSpaced, f => f
                .Path("name", r => Log(calls, "name", r.Name))
                .Query("q", r => Log(calls, "q", r.Q))
                .Query("r", r => Log(calls, "r", r.R)))
            .Build();

        table.FormatUri(new SpacedRoute("a", "b", "c"), new Uri("https://example.com"));
        Assert.Equal(["name", "q", "r"], calls);

        calls.Clear();
        Assert.Throws<ArgumentException>(
            () => table.FormatUri(new SpacedRoute("a", "b", "c "), new Uri("https://\u00E9.example")));
        Assert.Equal(["name", "q", "r"], calls);

        calls.Clear();
        Assert.Throws<ArgumentException>(
            () => table.FormatUri(new SpacedRoute("a", "b", null), new Uri("ftp://example.com")));
        Assert.Equal(["name", "q", "r"], calls);
    }

    // For generated tables, routes and bases: whenever FormatUri returns, Match of the link selects the mapping that
    // wrote it and an equal route; whenever it refuses the link for a trailing space, the same text put behind the
    // base by hand is read back as something other than what was written.
    [Fact]
    public void FormatUriReturnsOnlyLinksThatReadBackAsWritten()
    {
        (string Base, string Prefix)[] bases =
        [
            ("https://example.com", "https://example.com"),
            ("https://\u00E9.example", "https://\u00E9.example"),
            ("http://localhost:5000/", "http://localhost:5000"),
            ("myapp:///", "myapp://"),
            ("myapp:", "myapp:"),
        ];
        var random = new Random(20261126);
        int returned = 0, refused = 0, notWritable = 0, tables = 0;
        for (var attempt = 0; tables < 200 && attempt < 10_000; attempt++)
        {
            int count = random.Next(1, Makers.Length + 1);
            var templates = new string[count];
            var parameters = new int[count];
            var builder = new RouteTableBuilder();
            for (var i = 0; i < count; i++)
            {
                templates[i] = GenerateTemplate(random, out parameters[i]);
                MapGenerated(builder, i, templates[i], parameters[i]);
            }

            RouteTable table;
            try
            {
                table = builder.Build();
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            tables++;
            for (var i = 0; i < count; i++)
            {
                for (var n = 0; n < 10; n++)
                {
                    var values = new string?[3];
                    for (var k = 0; k < parameters[i]; k++)
                        values[k] = PathValues[random.Next(PathValues.Length)];

                    GenRoute route = Makers[i](
                        values[0],
                        values[1],
                        values[2],
                        QueryValues[random.Next(QueryValues.Length)],
                        QueryValues[random.Next(QueryValues.Length)]);
                    (string baseText, string prefix) = bases[random.Next(bases.Length)];
                    RouteDefinition writer = table.Definitions[i];

                    string text;
                    try
                    {
                        text = table.Format(route);
                    }
                    catch (ArgumentException expected)
                    {
                        ArgumentException actual = Assert.Throws<ArgumentException>(
                            () => table.FormatUri(route, new Uri(baseText)));
                        Assert.Equal(expected.Message, actual.Message);
                        notWritable++;
                        continue;
                    }

                    Uri uri;
                    try
                    {
                        uri = table.FormatUri(route, new Uri(baseText));
                    }
                    catch (ArgumentException exception) when (exception.ParamName == "route")
                    {
                        Assert.Contains(TrailingSpace, exception.Message, StringComparison.Ordinal);
                        var naive = new Uri(prefix + text);
                        Assert.NotEqual(text, naive.AbsolutePath + naive.Query);
                        RouteMatchResult misread = table.Match(naive);
                        Assert.False(misread.IsSuccess && ReferenceEquals(writer, misread.Definition)
                            && route.Equals(misread.Route));
                        refused++;
                        continue;
                    }

                    Assert.Equal(prefix + text, uri.AbsoluteUri);
                    RouteMatchResult result = table.Match(uri);
                    Assert.True(result.IsSuccess, uri.AbsoluteUri);
                    Assert.Same(writer, result.Definition);
                    Assert.Equal(route, result.Route);
                    returned++;
                }
            }
        }

        string budget = $"returned {returned}, refused {refused}, not writable {notWritable}";
        Assert.True(tables == 200, budget);
        Assert.True(returned >= 2000, budget);
        Assert.True(refused >= 1000, budget);
    }

    private static readonly string?[] PathValues =
        ["a", "new", "a b", "a ", "  ", "\u00E9", "\u00E9 ", "x/y", "b  ", "42", null];

    private static readonly string?[] QueryValues = [null, null, "", "v", "v ", " ", "\u00E9", "\u00E9 ", "a&b "];

    private static readonly string[] Literals = ["a", "new", "\u00E9", "42"];

    private static readonly Func<string?, string?, string?, string?, string?, GenRoute>[] Makers =
    [
        (a, b, c, q, r) => new G0(a, b, c, q, r),
        (a, b, c, q, r) => new G1(a, b, c, q, r),
        (a, b, c, q, r) => new G2(a, b, c, q, r),
        (a, b, c, q, r) => new G3(a, b, c, q, r),
    ];

    // Up to three segments, the first most often "a" so that templates share prefixes, each a literal, a parameter,
    // or, last, an optional parameter or a catch-all.
    private static string GenerateTemplate(Random random, out int parameters)
    {
        int count = random.Next(4);
        parameters = 0;
        if (count == 0)
            return "/";

        var text = new System.Text.StringBuilder();
        for (var i = 0; i < count; i++)
        {
            int kind = random.Next(i == count - 1 ? 5 : 3);
            string name = $"p{parameters}";
            string segment = kind switch
            {
                0 => i == 0 && random.Next(3) > 0 ? "a" : Literals[random.Next(Literals.Length)],
                1 or 2 => $"{{{name}}}",
                3 => $"{{{name}?}}",
                _ => $"{{*{name}}}",
            };

            if (kind > 0)
                parameters++;

            text.Append('/').Append(segment);
        }

        return text.ToString();
    }

    // Each mapping has its own sealed route type, so that Map infers a concrete type for its format function.
    private static void MapGenerated(RouteTableBuilder builder, int index, string template, int parameters)
    {
        switch (index)
        {
            case 0:
                MapGenerated(builder, template, parameters, (a, b, c, q, r) => new G0(a, b, c, q, r));
                break;
            case 1:
                MapGenerated(builder, template, parameters, (a, b, c, q, r) => new G1(a, b, c, q, r));
                break;
            case 2:
                MapGenerated(builder, template, parameters, (a, b, c, q, r) => new G2(a, b, c, q, r));
                break;
            default:
                MapGenerated(builder, template, parameters, (a, b, c, q, r) => new G3(a, b, c, q, r));
                break;
        }
    }

    private static void MapGenerated<TRoute>(
        RouteTableBuilder builder,
        string template,
        int parameters,
        Func<string?, string?, string?, string?, string?, TRoute> make)
        where TRoute : GenRoute
    {
        builder.Map(
            template,
            c => make(
                ReadPath(c, "p0"),
                ReadPath(c, "p1"),
                ReadPath(c, "p2"),
                c.TryGetQuery("q", out string? q) ? q : null,
                c.TryGetQuery("r", out string? r) ? r : null),
            f =>
            {
                if (parameters > 0)
                    f.Path("p0", r => r.A);
                if (parameters > 1)
                    f.Path("p1", r => r.B);
                if (parameters > 2)
                    f.Path("p2", r => r.C);

                f.Query("q", r => r.Q).Query("r", r => r.R);
            });
    }

    private static string? ReadPath(RouteMatchContext context, string name)
    {
        return context.TryGetPath(name, out string? value) ? value : null;
    }

    private static void AssertMatches(RouteTable table, Uri uri, AppRoute route)
    {
        RouteMatchResult result = table.Match(uri);

        Assert.True(result.IsSuccess, uri.AbsoluteUri);
        Assert.Equal(route, result.Route);
    }

    private static ArgumentException AssertBaseRejected(
        RouteTable table,
        Uri baseUri,
        string category,
        AppRoute? route = null)
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(() => table.FormatUri(route ?? new IdRoute(42), baseUri));

        Assert.Equal("baseUri", exception.ParamName);
        Assert.Null(exception.InnerException);
        Assert.Contains(category, exception.Message, StringComparison.Ordinal);
        return exception;
    }

    private static void AssertReadsBackOrRefusesTheBase(RouteTable table, AppRoute route, Uri baseUri)
    {
        Uri uri;
        try
        {
            uri = table.FormatUri(route, baseUri);
        }
        catch (ArgumentException exception)
        {
            AssertSchemeRefused(exception, baseUri.Scheme);
            return;
        }

        AssertMatches(table, uri, route);
    }

    private static void AssertSchemeRefused(ArgumentException exception, string scheme)
    {
        Assert.Equal("baseUri", exception.ParamName);
        Assert.Null(exception.InnerException);
        Assert.Contains(SchemeChanges, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{scheme}'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
        foreach (string quoted in new[] { "example.com", "/s/", "%5C", "%2F" })
            Assert.DoesNotContain(quoted, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertSameFailure<TException>(
        RouteTable table,
        AppRoute route,
        Uri baseUri,
        string category)
        where TException : Exception
    {
        TException expected = Assert.Throws<TException>(() => table.Format(route));
        TException actual = Assert.Throws<TException>(() => table.FormatUri(route, baseUri));

        Assert.Contains(category, actual.Message, StringComparison.Ordinal);
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal((expected as ArgumentException)?.ParamName, (actual as ArgumentException)?.ParamName);
        Assert.Null(actual.InnerException);
    }

    private static RouteTable OrdersTable()
    {
        return new RouteTableBuilder()
            .Map("/orders/{id:int}", c => new IdRoute(c.Path<int>("id")), f => f.Path("id", r => r.Id))
            .Build();
    }

    private static RouteTable SpacedTable(string template)
    {
        return new RouteTableBuilder().Map(template, ReadSpaced, WriteSpaced).Build();
    }

    // An all-ASCII template, one with a non-ASCII literal, and a list, for the cases where a space is kept or lost.
    private static RouteTable TrailingTable()
    {
        return new RouteTableBuilder()
            .Map("/s/{name}", ReadSpaced, WriteSpaced)
            .Map("/\u00E9/{name}", c => new LiteralRoute(c.Path("name")), f => f.Path("name", r => r.Name))
            .Map("/l", c => new ListRoute(c.GetQueryValues("tag")), f => f.QueryValues("tag", r => r.Tags))
            .Build();
    }

    private static SpacedRoute ReadSpaced(RouteMatchContext context)
    {
        return new SpacedRoute(
            context.Path("name"),
            context.TryGetQuery("q", out string? q) ? q : null,
            context.TryGetQuery("r", out string? r) ? r : null);
    }

    private static void WriteSpaced(RouteFormatBuilder<SpacedRoute> format)
    {
        format.Path("name", r => r.Name).Query("q", r => r.Q).Query("r", r => r.R);
    }

    private static T Log<T>(List<string> calls, string name, T value)
    {
        calls.Add(name);
        return value;
    }

    private sealed record IdRoute(int Id) : AppRoute;

    private sealed record UnwritableRoute(int Id) : AppRoute;

    private sealed record UnmappedRoute : AppRoute;

    private sealed record NameRoute(string Name) : AppRoute;

    private sealed record DoubleRoute(double Value) : AppRoute;

    private sealed record SpacedRoute(string Name, string? Q, string? R) : AppRoute;

    private sealed record LiteralRoute(string Name) : AppRoute;

    // Lists compare by their elements, so that a route read back is equal to the one written.
    private sealed record ListRoute(IReadOnlyList<string> Tags) : AppRoute
    {
        public bool Equals(ListRoute? other)
        {
            return other is not null && Tags.SequenceEqual(other.Tags);
        }

        public override int GetHashCode()
        {
            return Tags.Count;
        }
    }

    private abstract record GenRoute(string? A, string? B, string? C, string? Q, string? R) : AppRoute;

    private sealed record G0(string? A, string? B, string? C, string? Q, string? R) : GenRoute(A, B, C, Q, R);

    private sealed record G1(string? A, string? B, string? C, string? Q, string? R) : GenRoute(A, B, C, Q, R);

    private sealed record G2(string? A, string? B, string? C, string? Q, string? R) : GenRoute(A, B, C, Q, R);

    private sealed record G3(string? A, string? B, string? C, string? Q, string? R) : GenRoute(A, B, C, Q, R);
}
