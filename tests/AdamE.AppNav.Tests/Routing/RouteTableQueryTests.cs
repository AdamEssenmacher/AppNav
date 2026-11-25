using System.Globalization;
using System.Text;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// Which part of a URI the route table reads as the query, how the values a create function reads come out of it, and
// that it never changes which route matches.
public sealed class RouteTableQueryTests
{
    private const string NotAnAbsoluteUriHere =
        "On this platform a string that starts with '/' is not an absolute URI, as it is on Unix-like platforms.";

    [Theory]
    [InlineData("https://example.com/a?tab=1", "1")]
    [InlineData("http://example.com:8080/a?tab=1#top", "1")]
    [InlineData("https://user:secret@example.com/a?x=2&tab=1", "1")]
    [InlineData("https://example.com?tab=1", "1")]
    [InlineData("myapp:///a?tab=1#top", "1")]
    [InlineData("myapp://open?tab=1", "1")]
    [InlineData("app:/a?tab=1", "1")]
    [InlineData("app:a?tab=1", "1")]
    [InlineData("file:///a?tab=1", "1")]
    [InlineData("https://example.com/a?tab=1#x?tab=2", "1")]
    [InlineData("https://example.com/a?x?tab=2&tab=1", "1")]
    [InlineData("https://example.com/a?tab=1?2", "1?2")]
    public void AnAbsoluteUriIsReadAsItsQuery(string uri, string tab)
    {
        Assert.Equal(tab, Value(new Uri(uri), "tab"));
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("https://example.com/a?")]
    [InlineData("https://example.com/a#tab=1")]
    [InlineData("https://example.com/a#x?tab=1")]
    [InlineData("myapp:///a#?tab=1")]
    public void AnAbsoluteUriWithoutAQueryBeforeItsFragmentHasNoValues(string uri)
    {
        AssertNoQueryValue(new Uri(uri), "tab");
    }

    [Fact]
    public void SystemUriGivesTheQueryWithItsQuestionMarkAndWithoutTheFragment()
    {
        Assert.Equal("", new Uri("https://example.com/a").Query);
        Assert.Equal("?", new Uri("https://example.com/?").Query);
        Assert.Equal("?tab=1", new Uri("https://example.com/a?tab=1#top").Query);
        Assert.Equal("", new Uri("https://example.com/a#top?tab=1").Query);
        Assert.Throws<InvalidOperationException>(() => new Uri("/a?tab=1", UriKind.Relative).Query);
    }

    [Theory]
    [InlineData("/a?tab=1", "1")]
    [InlineData("a?tab=1", "1")]
    [InlineData("?tab=1", "1")]
    [InlineData("/a?tab=1#top", "1")]
    [InlineData("/a?tab=1#", "1")]
    [InlineData("/a?tab=1#f#g", "1")]
    [InlineData("/a?tab=1##y", "1")]
    [InlineData("/a?tab=1#x?tab=2", "1")]
    [InlineData("/a?x=1&tab=2#top&tab=3", "2")]
    [InlineData("/a??tab=1", null)]
    [InlineData("/a??tab=1&tab=2", "2")]
    [InlineData("/a?tab=1?2", "1?2")]
    [InlineData("/a?tab= 1 ", " 1 ")]
    [InlineData("/a?tab=%41%20", "A ")]
    [InlineData("/a", null)]
    [InlineData("/a?", null)]
    [InlineData("?", null)]
    [InlineData("", null)]
    [InlineData("/a#tab=1", null)]
    [InlineData("/a#x?tab=1", null)]
    [InlineData("#?tab=1", null)]
    public void ARelativeUriIsReadFromTheFirstQuestionMarkUpToTheNextHash(string uri, string? tab)
    {
        Assert.Equal(tab, Value(new Uri(uri, UriKind.Relative), "tab"));
    }

    [Theory]
    [InlineData("/a?tab=1", "1")]
    [InlineData("/a?tab=1#x?tab=2", "1")]
    [InlineData("/a#x?tab=1", null)]
    [InlineData("/a b?tab=c d", "c d")]
    public void AnImplicitFileUriIsReadAsTheRelativeUriTheCallerWrote(string text, string? tab)
    {
        // On Unix-like platforms these are absolute file URIs whose Query is empty, because the '?' is escaped into
        // the path; on Windows they cannot be created without a UriKind, so there is nothing to test.
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? implicitFile))
            Assert.Skip(NotAnAbsoluteUriHere);

        Assert.True(implicitFile.IsFile);
        Assert.Equal("", implicitFile.Query);
        Assert.Equal(tab, Value(implicitFile, "tab"));
        Assert.Equal(tab, Value(new Uri(text, UriKind.RelativeOrAbsolute), "tab"));
    }

    // Each row is read both as a relative URI and after an absolute prefix, which System.Uri may re-escape.
    [Theory]
    [InlineData("?v=a%26b&w=1", "a&b")]
    [InlineData("?v=a%3Db", "a=b")]
    [InlineData("?v=a=b", "a=b")]
    [InlineData("?v=a+b", "a b")]
    [InlineData("?v=a%2Bb", "a+b")]
    [InlineData("?v=a+%2541", "a %41")]
    [InlineData("?v=a%20b", "a b")]
    [InlineData("?v=a b", "a b")]
    [InlineData("?v=%25", "%")]
    [InlineData("?v=%2541", "%41")]
    [InlineData("?v=%41", "A")]
    [InlineData("?v=%zz%", "%zz%")]
    [InlineData("?v=%FF", "%FF")]
    [InlineData("?v=%E2%82", "%E2%82")]
    [InlineData("?v=%E2%82%AC", "\u20AC")]
    [InlineData("?v=\u00E9", "\u00E9")]
    [InlineData("?v=\"<>`{}|^\\", "\"<>`{}|^\\")]
    [InlineData("?v=%23", "#")]
    [InlineData("?v=a?b", "a?b")]
    [InlineData("?v=%09", "\u0009")]
    [InlineData("?v", "")]
    [InlineData("?v=", "")]
    [InlineData("?v=1;w=2", "1;w=2")]
    [InlineData("?%76=1", "1")]
    [InlineData("?v+=1&v=2", "2")]
    public void ValuesAreDecodedAsTheLinkWroteThem(string query, string value)
    {
        Assert.Equal(value, Value(new Uri("/a" + query, UriKind.Relative), "v"));
        Assert.Equal(value, Value(new Uri("https://example.com/a" + query), "v"));
        Assert.Equal(value, Value(new Uri("myapp:///a" + query + "#top"), "v"));
    }

    [Theory]
    [InlineData("?v=a+b", "v", "a b")]
    [InlineData("?v+w=1", "v w", "1")]
    public void APlusIsASpaceInTheQueryAlthoughItIsLiteralInThePath(string query, string name, string value)
    {
        (string path, string? read) = Read(
            "/files/{name}",
            new Uri("/files/a+b" + query, UriKind.Relative),
            c => (c.Path("name"), ReadQuery(c, name).Value));

        Assert.Equal("a+b", path);
        Assert.Equal(value, read);
    }

    // System.Uri drops %20 at the very end of a URI that contains, anywhere including the host, a non-ASCII
    // character, a percent-encoded byte from %80 to %FF, or a percent-encoded unreserved character.
    [Theory]
    [InlineData("https://example.com/a?v=%41%20", "?v=A", "A")]
    [InlineData("https://example.com/a?v=%41%20%20", "?v=A", "A")]
    [InlineData("https://example.com/a?v=%2D%20", "?v=-", "-")]
    [InlineData("https://example.com/a?v=%30%20", "?v=0", "0")]
    [InlineData("https://example.com/\u00E9?v=1%20", "?v=1", "1")]
    [InlineData("https://example.com/%41?v=%20", "?v=", "")]
    [InlineData("https://example.com/%41%20?v=%20", "?v=", "")]
    [InlineData("https://example.com/%C3%A9?v=%20", "?v=", "")]
    [InlineData("https://example.com/a?v=\u00E9%20", "?v=%C3%A9", "\u00E9")]
    [InlineData("https://example.com/a?v=%FF%20", "?v=%FF", "%FF")]
    [InlineData("https://example.com/a?v=%80%20", "?v=%80", "%80")]
    [InlineData("https://example.com/a?v=%e9%20", "?v=%e9", "%e9")]
    [InlineData("https://b\u00FCcher.example/a?v=%20", "?v=", "")]
    [InlineData("myapp:///a?v=%7E%20", "?v=~", "~")]
    public void SystemUriDropsTrailingEncodedSpacesAfterNonAsciiOrAnEncodedUnreservedOrHighByte(
        string uri,
        string query,
        string value)
    {
        var parsed = new Uri(uri);

        Assert.Equal(query, parsed.Query);
        Assert.Equal(value, Value(parsed, "v"));
    }

    [Theory]
    [InlineData("https://example.com/a?v=1%20", "1 ")]
    [InlineData("https://example.com/a?v=%41%20#top", "A ")]
    [InlineData("https://example.com/a?v=%41%20&w", "A ")]
    [InlineData("https://example.com/a?v=%41%20#", "A ")]
    [InlineData("https://example.com/a?v=%41%20&", "A ")]
    [InlineData("/a?v=%41%20", "A ")]
    [InlineData("https://example.com/a?v=%41%20+", "A  ")]
    [InlineData("https://example.com/a?v=<%20", "< ")]
    [InlineData("https://example.com/a?v=%3c%20", "< ")]
    [InlineData("https://example.com/a?v=%25%20", "% ")]
    [InlineData("https://example.com/a?v=%7F%20", "\u007F ")]
    [InlineData("https://example.com/a?v=%2F%20", "/ ")]
    [InlineData("HTTPS://EXAMPLE.com:443/x/../a?v=%20", " ")]
    [InlineData("https://xn--bcher-kva.example/a?v=%20", " ")]
    public void TrailingEncodedSpacesAreKeptWhenSomethingFollowsThemOrNothingTriggersTheDrop(string uri, string value)
    {
        Assert.Equal(value, Value(new Uri(uri, UriKind.RelativeOrAbsolute), "v"));
    }

    [Theory]
    [InlineData("https://example.com/a?v=a ", "a")]
    [InlineData("https://example.com/a?v=a\u0009", "a")]
    [InlineData("https://example.com/a?v=a\n", "a")]
    [InlineData("https://example.com/a?v=a\r\n", "a")]
    [InlineData(" https://example.com/a?v=a", "a")]
    [InlineData("/a?v=a ", "a ")]
    [InlineData("/a?v=a\u0009", "a\u0009")]
    public void SystemUriTrimsWhiteSpaceFromAnAbsoluteUriButNotFromARelativeOne(string uri, string value)
    {
        UriKind kind = uri.StartsWith('/') ? UriKind.Relative : UriKind.Absolute;

        Assert.Equal(value, Value(new Uri(uri, kind), "v"));
    }

    [Theory]
    [InlineData("ftp://host/a?x=1", "/a%3Fx=1")]
    [InlineData("news:a?x=1", "a%3Fx=1")]
    [InlineData("nntp://host/a?x=1", "/a%3Fx=1")]
    [InlineData("gopher://host/a?x=1", "/a%3Fx=1")]
    [InlineData("telnet://host/a?x=1", "/a%3Fx=1")]
    [InlineData("uuid:a?x=1", "a%3Fx=1")]
    [InlineData("vsmacros://host/a?x=1", "/a%3Fx=1")]
    public void SomeSchemesHaveNoQuerySoAQuestionMarkIsPartOfThePath(string uri, string path)
    {
        var parsed = new Uri(uri);

        Assert.Equal("", parsed.Query);
        Assert.Equal(path, parsed.AbsolutePath);
        AssertNoQueryValue(parsed, "x");
    }

    [Fact]
    public void ASlugRouteCapturesTheQuestionMarkOfAnFtpUri()
    {
        RouteTable table = new RouteTableBuilder().Map("/{slug}", c => new ProductByNameRoute(c.Path("slug"))).Build();

        Assert.Equal(new ProductByNameRoute("a?x=1"), table.Match(new Uri("ftp://host/a?x=1")).Route);
    }

    [Fact]
    public void AUncPathHasNoQuery()
    {
        if (!Uri.TryCreate(@"\\server\share\a?x=1", UriKind.Absolute, out Uri? unc))
            Assert.Skip("On this platform a UNC path is not an absolute URI.");

        Assert.True(unc.IsUnc);
        Assert.Equal("", unc.Query);
        Assert.Equal("/share/a%3Fx=1", unc.AbsolutePath);
        AssertNoQueryValue(unc, "x");
    }

    [Fact]
    public void AUriWithoutCanonicalizationIsReadAsSystemUriExposesIt()
    {
        var options = new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true };
        var queryWithFragment = new Uri("https://example.com/a?x=1#f", options);
        var pathWithHash = new Uri("https://example.com/a#f?x=1", options);

        Assert.Equal("?x=1#f", queryWithFragment.Query);
        Assert.Equal("1#f", Value(queryWithFragment, "x"));
        Assert.Equal("/a#f", pathWithHash.AbsolutePath);
        Assert.Equal("?x=1", pathWithHash.Query);
    }

    // ReadQuery checks that TryGetQuery gives the value Picked chooses from them.
    [Theory]
    [InlineData("/a?tab=a&tab=b", "a|b")]
    [InlineData("/a?tab=a&x=1&TAB=b&Tab=a", "a|b|a")]
    [InlineData("/a?tab=a&tab=", "a|")]
    [InlineData("/a?tab=&tab=a", "|a")]
    [InlineData("/a?tab&tab&tab=x", "||x")]
    [InlineData("/a?&&tab=1&&", "1")]
    public void ARepeatedNameKeepsEveryValueInOrder(string uri, string all)
    {
        var parsed = new Uri(uri, UriKind.Relative);

        Assert.Equal(all, string.Join('|', ReadQuery(parsed, "tab").All));
        Assert.Equal(all, string.Join('|', ReadQuery(parsed, "TAB").All));
    }

    [Theory]
    [InlineData("/a?=x", "x")]
    [InlineData("/a?=", "")]
    [InlineData("/a?=x&=y&a=1", "x|y")]
    public void TheEmptyNameIsAName(string uri, string all)
    {
        Assert.Equal(all, string.Join('|', ReadQuery(new Uri(uri, UriKind.Relative), "").All));
    }

    // The text after the path is a fragment or nothing, so no part of it, nor the end of the path, is read as a query.
    [Theory]
    [InlineData("/settings", "s")]
    [InlineData("/settings", "")]
    [InlineData("/a#b", "b")]
    [InlineData("/a#b", "a")]
    [InlineData("/a#b", "")]
    [InlineData("/a#b=1", "b")]
    public void ARelativeUriWithoutAQueryHasNoQueryValues(string uri, string name)
    {
        AssertNoQueryValue(new Uri(uri, UriKind.Relative), name);
    }

    [Theory]
    [InlineData("/a?x=1")]
    [InlineData("/a?&")]
    [InlineData("/a")]
    public void AQueryWithoutTheEmptyNameHasNoValueForIt(string uri)
    {
        AssertNoQueryValue(new Uri(uri, UriKind.Relative), "");
    }

    [Fact]
    public void TheQueryDoesNotChangeWhichRouteMatches()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/settings", _ => new SettingsRoute())
            .Map("/{*rest}", c => new FileRoute(c.TryGetPath("rest", out string? rest) ? rest : null))
            .Build();

        Assert.Equal(new SettingsRoute(), table.Match(new Uri("/settings?rest=x/y", UriKind.Relative)).Route);
        Assert.Equal(new SettingsRoute(), table.Match(new Uri("myapp:///settings?/other#/x")).Route);
        Assert.Equal(new FileRoute("a"), table.Match(new Uri("/a?b/c", UriKind.Relative)).Route);
        Assert.Equal(new FileRoute("a"), table.Match(new Uri("https://example.com/a?/settings")).Route);
        Assert.Equal(new SettingsRoute(), table.Match(new Uri("/settings#?x", UriKind.Relative)).Route);
    }

    [Fact]
    public void AQueryOnlyRelativeUriMatchesTheRootAndKeepsItsQuery()
    {
        Assert.Equal("1", Read("/", new Uri("?a=1", UriKind.Relative), c => ReadQuery(c, "a").Value));
    }

    [Theory]
    [InlineData("/missing?token=s3cr3t#frag", "/missing")]
    [InlineData("https://example.com/missing?token=s3cr3t", "/missing")]
    [InlineData("?token=s3cr3t", "")]
    public void AMissReportsOnlyThePath(string uri, string path)
    {
        RouteTable table = new RouteTableBuilder().Map("/settings", _ => new SettingsRoute()).Build();

        RouteDiagnostic diagnostic = Assert.Single(table.Match(new Uri(uri, UriKind.RelativeOrAbsolute)).Diagnostics);

        Assert.Equal(path, Assert.Single(diagnostic.Data).Value);
        Assert.Equal(path, diagnostic.Data["path"]);
        Assert.DoesNotContain("s3cr3t", diagnostic.Message, StringComparison.Ordinal);
    }

    // A URI that matches nothing builds no context, and a match whose create function reads no query value never
    // reads the query, so neither allocates more for a longer query. Each match gets a new Uri, because System.Uri
    // keeps a query it has computed, which would hide one built on every match.
    [Theory]
    [InlineData("https://example.com/products/1")]
    [InlineData("/products/1")]
    public void AQueryThatIsNotReadCostsNothing(string prefix)
    {
        RouteTable misses = new RouteTableBuilder().Map("/settings", _ => new SettingsRoute()).Build();
        RouteTable neverReads = new RouteTableBuilder()
            .Map("/products/{id}", c => new ProductRoute(c.Path("id")))
            .Build();
        RouteTable reads = new RouteTableBuilder()
            .Map("/products/{id}", c => new ProductRoute(c.TryGetQuery("n1", out string? v) ? v : c.Path("id")))
            .Build();
        UriKind kind = prefix.StartsWith('/') ? UriKind.Relative : UriKind.Absolute;
        string shortText = prefix + Query(1000);
        string longText = prefix + Query(50000);
        Func<Uri> shortQuery = () => new Uri(shortText, kind);
        Func<Uri> longQuery = () => new Uri(longText, kind);

        Assert.Equal(new ProductRoute("v1"), reads.Match(longQuery()).Route);
        long missShort = AllocatedBytesPerMatch(misses, shortQuery, success: false);
        long missLong = AllocatedBytesPerMatch(misses, longQuery, success: false);
        long neverReadShort = AllocatedBytesPerMatch(neverReads, shortQuery);
        long neverReadLong = AllocatedBytesPerMatch(neverReads, longQuery);
        long readShort = AllocatedBytesPerMatch(reads, shortQuery);
        long readLong = AllocatedBytesPerMatch(reads, longQuery);

        string sizes = $"bytes per match for 1,000 and 50,000 characters: miss {missShort}, {missLong}; never read " +
            $"{neverReadShort}, {neverReadLong}; read {readShort}, {readLong}.";

        // Copying the longer query would cost about 98,000 more bytes for each match. The bound is far below that and
        // well above the few hundred bytes for each match that a one-off allocation by the runtime adds when it lands
        // among the measured matches.
        const long Slack = 8192;
        Assert.True(Math.Abs(missLong - missShort) <= Slack, sizes);
        Assert.True(Math.Abs(neverReadLong - neverReadShort) <= Slack, sizes);
        Assert.True(readLong > 10 * readShort, sizes);
    }

    // Nothing in System.Uri or the table bounds the length of a query, so an app that receives links from outside
    // must bound it before matching.
    [Theory]
    [InlineData("https://example.com/a?v=")]
    [InlineData("/a?v=")]
    public void AQueryOfAMillionCharactersIsAccepted(string prefix)
    {
        var uri = new Uri(
            prefix + new string('x', 1_000_000),
            prefix.StartsWith('/') ? UriKind.Relative : UriKind.Absolute);

        Assert.Equal(1_000_000, Value(uri, "v")?.Length);
    }

    [Fact]
    public void AQueryIsParsedAtMostOncePerMatch()
    {
        var query = new StringBuilder("?");
        for (var i = 0; i < 5000; i++)
            query.Append('n').Append(i).Append("=v&");

        var uri = new Uri("/a" + query, UriKind.Relative);
        RouteTable once = BuildAsking(1);
        RouteTable often = BuildAsking(20);

        long onceBytes = AllocatedBytesPerMatch(once, () => uri);
        long oftenBytes = AllocatedBytesPerMatch(often, () => uri);

        Assert.True(oftenBytes < 2 * onceBytes, $"asked once: {onceBytes}; 20 times: {oftenBytes} bytes per match.");
    }

    [Fact]
    public void ManyThreadsMatchingAtOnceReadTheirOwnQueries()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/orders/{id}", c => new OrderRoute(
                c.Path("id"),
                ReadQuery(c, "tab") is { Value: { } tab, All: var all } ? tab + "|" + string.Join(',', all) : null))
            .Build();
        var failures = 0;

        Parallel.For(0, 4000, i =>
        {
            string id = i.ToString(CultureInfo.InvariantCulture);
            string picked = Picked([$"a{id}", $"b{id}"]);
            (string uri, OrderRoute expected) = (i % 4) switch
            {
                0 => ($"/orders/{id}?tab=a{id}&tab=b{id}", new OrderRoute(id, $"{picked}|a{id},b{id}")),
                1 => ($"https://example.com/orders/{id}?TAB=%2B{id}#tab=x", new OrderRoute(id, $"+{id}|+{id}")),
                2 => ($"myapp:///orders/{id}?other={id}", new OrderRoute(id, null)),
                _ => ($"/orders/{id}?tab", new OrderRoute(id, "|")),
            };

            if (!expected.Equals(table.Match(new Uri(uri, UriKind.RelativeOrAbsolute)).Route))
                Interlocked.Increment(ref failures);
        });

        Assert.Equal(0, failures);
    }

    private static RouteTable BuildAsking(int times)
    {
        return new RouteTableBuilder()
            .Map("/a", c =>
            {
                for (var i = 0; i < times; i++)
                    Assert.True(c.TryGetQuery("n" + i.ToString(CultureInfo.InvariantCulture), out _));

                return new SettingsRoute();
            })
            .Build();
    }

    [Fact]
    public void ACreateFunctionThatMatchesAgainKeepsItsOwnQuery()
    {
        RouteTable? table = null;
        string? inner = null;
        table = new RouteTableBuilder()
            .Map("/outer", c =>
            {
                string? before = ReadQuery(c, "x").Value;
                RouteMatchResult result = table!.Match(new Uri("/inner?x=2", UriKind.Relative));
                inner = ((FileRoute)result.Route!).Path;
                return new OrderRoute(before!, ReadQuery(c, "x").Value);
            })
            .Map("/inner", c => new FileRoute(ReadQuery(c, "x").Value))
            .Build();

        Assert.Equal(new OrderRoute("1", "1"), table.Match(new Uri("/outer?x=1", UriKind.Relative)).Route);
        Assert.Equal("2", inner);
    }

    // A query of about the given length that System.Uri does not rewrite, whose second pair is n1=v1.
    private static string Query(int length)
    {
        var query = new StringBuilder("?");
        for (var i = 0; query.Length < length; i++)
            query.Append('n').Append(i).Append("=v").Append(i).Append('&');

        return query.ToString();
    }

    // Matches a new Uri each time, made before measuring so that only the matches are counted, after enough matches
    // for the code under test to be compiled.
    private static long AllocatedBytesPerMatch(RouteTable table, Func<Uri> makeUri, bool success = true)
    {
        const int Calls = 20;
        for (var i = 0; i < 100; i++)
            Assert.Equal(success, table.Match(makeUri()).IsSuccess);

        var uris = new Uri[Calls];
        for (var i = 0; i < Calls; i++)
            uris[i] = makeUri();

        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (Uri uri in uris)
            table.Match(uri);

        return (GC.GetAllocatedBytesForCurrentThread() - before) / Calls;
    }

    private static void AssertNoQueryValue(Uri uri, string name)
    {
        (string? value, IReadOnlyList<string> all) = ReadQuery(uri, name);

        Assert.Null(value);
        Assert.Empty(all);
    }

    private static string? Value(Uri uri, string name)
    {
        return ReadQuery(uri, name).Value;
    }

    private static (string? Value, IReadOnlyList<string> All) ReadQuery(Uri uri, string name)
    {
        return Read("/{*rest}", uri, c => ReadQuery(c, name));
    }

    // The value TryGetQuery gives for a name that repeats, out of all the values GetQueryValues gives for it.
    private static string Picked(IReadOnlyList<string> values)
    {
        return values[0];
    }

    // Reads a name both ways and checks that they agree, so that each test states only the values it expects.
    private static (string? Value, IReadOnlyList<string> All) ReadQuery(RouteMatchContext context, string name)
    {
        IReadOnlyList<string> all = context.GetQueryValues(name);
        bool found = context.TryGetQuery(name, out string? value);

        Assert.Equal(all.Count > 0, found);
        Assert.Equal(found ? Picked(all) : null, value);
        return (value, all);
    }

    // Returns what the create function read, never the context itself, which is valid only during that call.
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
