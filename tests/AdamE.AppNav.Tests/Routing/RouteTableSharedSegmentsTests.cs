using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// A table splits a path once and decodes each segment at most once for all its definitions, so these tests check that
// an earlier definition's attempt, which decodes some segments and then fails, leaves a later definition to capture
// exactly what its template alone captures from the same path.
public sealed class RouteTableSharedSegmentsTests
{
    // In each row the first definition, which precedence also tries first, decodes a segment and then fails, and the
    // path encodes '%' in that segment, so decoding it a second time for the later definition would change the
    // captured value.
    [Theory]
    [InlineData("/{a}/lit", "/{a}/{b}", "/%2541/x", "a", "%41")]
    [InlineData("/a/{x:int}", "/a/{y}", "/a/%2520b", "y", "%20b")]
    [InlineData("/x%2Fy/lit", "/{a}/{b}", "/x%2Fy/q%2541", "b", "q%41")]
    [InlineData("/{a}/{b:int}", "/{a}/{b?}", "/%41/%2525", "b", "%25")]
    [InlineData("/p/{*rest}", "/{a}/{b}", "/p/q%2F..%2F%2541", "b", "q/../%41")]
    [InlineData("/p/{*rest}", "/{a}/{b}", "/p/%2E%2E%2F%2541", "b", "../%41")]
    [InlineData("/f/{x:int}/{*r}", "/f/{*r}", "/f/%256E/a%2F%2Fb/c%20d", "r", "%6E/a/b/c d")]
    [InlineData("/o/{a}/lit", "/o/{a?}/{b?}", "/o/%E2%82%AC%2525/%E0%A4", "b", "%E0%A4")]
    [InlineData("/o/{a}/lit", "/o/{a?}/{b?}", "/o/%E2%82%AC%2525/%E0%A4", "a", "\u20AC%25")]
    [InlineData("/{a}/{b}/lit", "/{*all}", "/%2E%2E%2E/b%2F/%252F", "all", ".../b/%2F")]
    public void ALaterDefinitionCapturesWhatItsTemplateAloneCaptures(
        string first,
        string second,
        string path,
        string name,
        string value)
    {
        (int winner, IReadOnlyDictionary<string, string>? values) = MatchTable([first, second], path);

        Assert.True(RouteTemplate.Parse(first).ComparePrecedence(RouteTemplate.Parse(second)) < 0);
        Assert.Equal(1, winner);
        Assert.Equal(value, values![name]);
        AssertSameAsFirstMatchingTemplate([first, second], path);
    }

    [Fact]
    public void AFirstDefinitionRejectedOnCountLeavesTheSegmentsForTheNextOne()
    {
        (int winner, IReadOnlyDictionary<string, string>? values) = MatchTable(["/{a}", "/{a}/{b?}"], "/%41/%zz");

        Assert.Equal(1, winner);
        Assert.Equal("A", values!["a"]);
        Assert.Equal("%zz", values["b"]);
    }

    // The shared segments are decoded only as a prefix: a template that fails leaves every segment from decodedCount
    // on as written, and the next template reads the decoded ones without decoding them again.
    [Fact]
    public void TheSegmentsBeforeTheDecodedCountAreDecodedAndTheRestAreNot()
    {
        string[] segments = ["%2541", "x", "%2542"];
        var decodedCount = 0;

        Assert.Null(RouteTemplate.Parse("/{a}/{b}/{c}/{d}").Match(segments, ref decodedCount));
        Assert.Equal(0, decodedCount);
        Assert.Equal(["%2541", "x", "%2542"], segments);

        Assert.Null(RouteTemplate.Parse("/{a}/lit/{b}").Match(segments, ref decodedCount));
        Assert.Equal(2, decodedCount);
        Assert.Equal(["%41", "x", "%2542"], segments);

        IReadOnlyDictionary<string, string>? values =
            RouteTemplate.Parse("/{a}/{b}/{c}").Match(segments, ref decodedCount);
        Assert.Equal(3, decodedCount);
        Assert.Equal(["%41", "x", "%42"], segments);
        Assert.Equal("%41", values!["a"]);
        Assert.Equal("%42", values["c"]);
    }

    [Fact]
    public void ALaterDefinitionThatCapturesNothingReturnsTheSharedEmptyDictionary()
    {
        (int winner, IReadOnlyDictionary<string, string>? values) = MatchTable(["/z/{a:int}", "/z/{*rest}"], "/z/%2F");

        Assert.Equal(1, winner);
        Assert.Same(ReadOnlyDictionary<string, string>.Empty, values);
    }

    [Fact]
    public void ADotSegmentThatAnEarlierDefinitionDecodedStillPreventsEveryMatch()
    {
        AssertSameAsFirstMatchingTemplate(["/{a}/lit", "/{a}/{b}", "/{*rest}"], "/x/%2E");
        Assert.Equal(-1, MatchTable(["/{a}/lit", "/{a}/{b}", "/{*rest}"], "/x/%2E").Winner);
    }

    [Fact]
    public void RandomTablesMatchLikeTheirFirstMatchingTemplateByPrecedence()
    {
        string[] pathPool =
        [
            "a", "A", "%41", "b", "1", "-2", "%20", "%2F", "a%2Fb", "%25", "%252F", "%2E", "%2E%2E", "..x",
            "%zz", "%E2%82%AC", "%C3", "b%2F%2F", "%2Fa%2F", "%2E%2E%2Fc", "lit", "",
        ];
        var random = new Random(20261125);
        for (var iteration = 0; iteration < 3000; iteration++)
        {
            var templates = new string[random.Next(1, 7)];
            for (var i = 0; i < templates.Length; i++)
                templates[i] = RandomTemplate(random);

            var path = new StringBuilder();
            int segmentCount = random.Next(0, 6);
            for (var i = 0; i < segmentCount; i++)
                path.Append('/').Append(pathPool[random.Next(pathPool.Length)]);

            AssertSameAsFirstMatchingTemplate(templates, path.Length == 0 ? "/" : path.ToString());
        }
    }

    // A miss on the first literal only splits the path; a miss on a dot segment at the end of a catch-all decodes every
    // segment first. Splitting and decoding again for each definition would allocate about twenty times as much.
    [Theory]
    [InlineData("/q{0}/{{x}}/{{*rest}}", "s%20")]
    [InlineData("/{{a{0}}}/{{*rest}}", "%2E%2E")]
    public void AMissOnManyDefinitionsAllocatesAboutAsMuchAsAMissOnOne(string templateFormat, string lastSegment)
    {
        var uri = new Uri(
            "/" + string.Join('/', Enumerable.Range(0, 999).Select(i => "s%20" + i)) + "/" + lastSegment,
            UriKind.Relative);
        RouteTable one = BuildTable([string.Format(CultureInfo.InvariantCulture, templateFormat, 0)]);
        RouteTable twenty = BuildTable(
            [.. Enumerable.Range(0, 20).Select(i => string.Format(CultureInfo.InvariantCulture, templateFormat, i))]);

        long oneBytes = AllocatedBytesPerMiss(one, uri);
        long twentyBytes = AllocatedBytesPerMiss(twenty, uri);

        Assert.True(
            twentyBytes < 3 * oneBytes,
            $"1 definition: {oneBytes} bytes per miss; 20 definitions: {twentyBytes} bytes per miss.");
    }

    private static long AllocatedBytesPerMiss(RouteTable table, Uri uri)
    {
        const int Calls = 20;
        for (var i = 0; i < Calls; i++)
            Assert.False(table.Match(uri).IsSuccess);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Calls; i++)
            table.Match(uri);

        return (GC.GetAllocatedBytesForCurrentThread() - before) / Calls;
    }

    // Up to three literals and parameters, then nothing, one or two optional parameters, or a catch-all.
    private static string RandomTemplate(Random random)
    {
        string[] literals = ["a", "A", "b", "lit", "%41", "a%2Fb", "%25"];
        string[] parameters = ["{#}", "{#:int}", "{#:alpha}"];
        var template = new StringBuilder();
        var names = 0;
        int positionalCount = random.Next(0, 4);
        for (var i = 0; i < positionalCount; i++)
        {
            template.Append('/').Append(random.Next(2) == 0
                ? literals[random.Next(literals.Length)]
                : parameters[random.Next(parameters.Length)].Replace("#", "p" + names++, StringComparison.Ordinal));
        }

        switch (random.Next(4))
        {
            case 1:
                template.Append("/{o").Append(names++).Append("?}");
                break;
            case 2:
                template.Append("/{o").Append(names++).Append("?}/{o").Append(names++).Append(":int?}");
                break;
            case 3:
                template.Append("/{*rest}");
                break;
        }

        return template.Length == 0 ? "/" : template.ToString();
    }

    // The first template in precedence order, with ties in registration order, that matches the path on its own.
    private static void AssertSameAsFirstMatchingTemplate(string[] templates, string path)
    {
        int expectedWinner = -1;
        IReadOnlyDictionary<string, string>? expected = null;
        IEnumerable<int> order = Enumerable.Range(0, templates.Length).OrderBy(
            i => RouteTemplate.Parse(templates[i]),
            Comparer<RouteTemplate>.Create((x, y) => x.ComparePrecedence(y)));
        foreach (int i in order)
        {
            expected = RouteTemplate.Parse(templates[i]).Match(path);
            if (expected is not null)
            {
                expectedWinner = i;
                break;
            }
        }

        (int winner, IReadOnlyDictionary<string, string>? values) = MatchTable(templates, path);

        string context = $"{string.Join(' ', templates)} against {path}";
        Assert.True(expectedWinner == winner, $"{context}: expected {expectedWinner}, got {winner}.");
        if (expected is null)
            return;

        Assert.Equal(
            expected.OrderBy(entry => entry.Key, StringComparer.Ordinal),
            values!.OrderBy(entry => entry.Key, StringComparer.Ordinal));
        if (expected.Count == 0)
            Assert.Same(expected, values);
        else
            Assert.Equal(expected[expected.Keys.First()], values![expected.Keys.First().ToUpperInvariant()]);
    }

    private static (int Winner, IReadOnlyDictionary<string, string>? Values) MatchTable(string[] templates, string path)
    {
        IReadOnlyDictionary<string, string>? captured = null;
        RouteTable table = BuildTable(templates, values => captured = values);

        RouteMatchResult result = table.Match(new Uri(path, UriKind.Relative));
        if (!result.IsSuccess)
            return (-1, null);

        for (var i = 0; i < table.Definitions.Count; i++)
        {
            if (ReferenceEquals(table.Definitions[i], result.Definition))
                return (i, captured);
        }

        throw new InvalidOperationException("The winning definition is not in the table.");
    }

    private static RouteTable BuildTable(
        string[] templates,
        Action<IReadOnlyDictionary<string, string>>? capture = null)
    {
        var builder = new RouteTableBuilder();
        foreach (string template in templates)
        {
            builder.Map(template, context =>
            {
                capture?.Invoke(context.PathValues);
                return new SettingsRoute();
            });
        }

        return builder.Build();
    }
}
