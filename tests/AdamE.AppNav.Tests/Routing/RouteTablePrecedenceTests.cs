using System.Text;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// Which route wins is decided by the precedence of the templates, so every case here is checked in both registration
// orders, or in all of them.
public sealed class RouteTablePrecedenceTests
{
    // The letters RandomTemplate writes, highest rank first, with 'E' for a position past a template's last segment.
    private const string RankOrder = "LCEcRr*";

    // One template for each rank, highest first, all differing only in the position after /a; "/a" has ended there.
    private static readonly string[] OneTemplatePerRank =
    [
        "/a/5",
        "/a/{id:int}",
        "/a",
        "/a/{id:int?}",
        "/a/{name}",
        "/a/{name?}",
        "/a/{*rest}",
    ];

    [Fact]
    public void EachRankComesBeforeEveryLowerRank()
    {
        for (var i = 0; i < OneTemplatePerRank.Length; i++)
        {
            for (var j = 0; j < OneTemplatePerRank.Length; j++)
            {
                int comparison = Compare(OneTemplatePerRank[i], OneTemplatePerRank[j]);
                Assert.True(
                    Math.Sign(comparison) == Math.Sign(i.CompareTo(j)),
                    $"{OneTemplatePerRank[i]} against {OneTemplatePerRank[j]}: {comparison}.");
            }
        }
    }

    // Each row is a pair of templates that both match the path, the winner first: every adjacent pair of ranks that
    // can match the same path, and enough of the others to pin the order.
    [Theory]
    [InlineData("/a/5", "/a/{id:int}", "/a/5")]
    [InlineData("/a", "/a/{id:int?}", "/a")]
    [InlineData("/a/{id:int?}", "/a/{name}", "/a/5")]
    [InlineData("/a/{name}", "/a/{name?}", "/a/x")]
    [InlineData("/a/{name?}", "/a/{*rest}", "/a/x")]
    [InlineData("/a/{name?}", "/a/{*rest}", "/a")]
    [InlineData("/a/5", "/a/{id:int?}", "/a/5")]
    [InlineData("/a/5", "/a/{name}", "/a/5")]
    [InlineData("/a/5", "/a/{name?}", "/a/5")]
    [InlineData("/a/5", "/a/{*rest}", "/a/5")]
    [InlineData("/a/{id:int}", "/a/{id:int?}", "/a/5")]
    [InlineData("/a/{id:int}", "/a/{name}", "/a/5")]
    [InlineData("/a/{id:int}", "/a/{name?}", "/a/5")]
    [InlineData("/a/{id:int}", "/a/{*rest}", "/a/5")]
    [InlineData("/a", "/a/{name?}", "/a")]
    [InlineData("/a", "/a/{*rest}", "/a")]
    [InlineData("/a/{id:int?}", "/a/{name?}", "/a/5")]
    [InlineData("/a/{id:int?}", "/a/{*rest}", "/a")]
    [InlineData("/a/{name}", "/a/{*rest}", "/a/x")]
    [InlineData("/{x:int}/b", "/{y}/b", "/1/b")]
    [InlineData("/a/b/c", "/a/{*rest}", "/a/b/c")]
    public void TheHigherRankWinsInBothRegistrationOrders(string winner, string loser, string path)
    {
        Assert.Equal(winner, Winner([winner, loser], path));
        Assert.Equal(winner, Winner([loser, winner], path));
    }

    // A template that has ended and one that requires another segment never match the same path, so their order is
    // checked directly.
    [Theory]
    [InlineData("/a/{id:int}", "/a")]
    [InlineData("/a", "/a/{name}")]
    public void EndedSitsAboveTheOptionalKindsThoughNoPathTellsItFromTheRequiredKinds(string first, string second)
    {
        Assert.True(Compare(first, second) < 0);
        Assert.True(Compare(second, first) > 0);
    }

    [Fact]
    public void AnExactTemplateWinsOverOneThatContinuesWithAnOptionalOrCatchAllParameter()
    {
        Assert.Equal("/a", Winner(["/a/{b?}", "/a/{*rest}", "/a"], "/a"));
        Assert.Equal("/a", Winner(["/a", "/a/{*rest}", "/a/{b?}"], "/a"));
    }

    [Fact]
    public void AConstrainedOptionalParameterWinsOverAnUnconstrainedRequiredOne()
    {
        Assert.Equal("/a/{id:int?}", Winner(["/a/{name}", "/a/{id:int?}"], "/a/5"));
        Assert.Equal("/a/{name}", Winner(["/a/{name}", "/a/{id:int?}"], "/a/x"));
    }

    [Fact]
    public void TheThreeTemplateExampleSortsTheSameInEveryRegistrationOrder()
    {
        string[] templates = ["/a/{id:int?}", "/a/{name}", "/a"];

        foreach (string[] order in Permutations(templates))
        {
            RouteTable table = Build(order);

            Assert.Equal(["/a", "/a/{id:int?}", "/a/{name}"], Values(table.MatchOrder.ToArray()));
            Assert.Equal("/a", Winner(table, "/a"));
            Assert.Equal("/a/{id:int?}", Winner(table, "/a/5"));
            Assert.Equal("/a/{name}", Winner(table, "/a/x"));
        }
    }

    [Fact]
    public void TheLeftmostDifferenceDecidesWhenTemplatesCross()
    {
        Assert.Equal("/a/{y}", Winner(["/a/{y}", "/{x}/b"], "/a/b"));
        Assert.Equal("/a/{y}", Winner(["/{x}/b", "/a/{y}"], "/a/b"));
    }

    [Fact]
    public void EqualRankTemplatesCurrentlyMatchInRegistrationOrder()
    {
        Assert.Equal("/p/{id:int?}", Winner(["/p/{id:int?}", "/p/{n:decimal?}"], "/p/1"));
        Assert.Equal("/p/{n:decimal?}", Winner(["/p/{n:decimal?}", "/p/{id:int?}"], "/p/1"));
        Assert.Equal("/p/{id:int}", Winner(["/p/{id:int}", "/p/{n:long}"], "/p/1"));
        Assert.Equal("/p/{n:long}", Winner(["/p/{n:long}", "/p/{id:int}"], "/p/1"));
    }

    [Theory]
    [InlineData("/p/{id:int}", "/p/{id:long}")]
    [InlineData("/p/{id:int?}", "/p/{id:guid?}")]
    [InlineData("/p/a", "/p/b")]
    [InlineData("/p/{id}", "/p/{name}")]
    [InlineData("/p/{id?}", "/p/{name?}")]
    [InlineData("/p/{*rest}", "/p/{*path}")]
    [InlineData("/{a:alpha}/x/{b?}", "/{c:bool}/y/{d?}")]
    public void TheConstraintAndTheNamesDoNotAffectTheRank(string first, string second)
    {
        Assert.Equal(0, Compare(first, second));
        Assert.Equal(0, Compare(second, first));
    }

    [Fact]
    public void DefinitionsStayInRegistrationOrder()
    {
        RouteTable table = Build(["/a/{*rest}", "/a/{name}", "/a/b"]);

        Assert.Equal(["/a/{*rest}", "/a/{name}", "/a/b"], Values([.. table.Definitions]));
        Assert.Equal(["/a/b", "/a/{name}", "/a/{*rest}"], Values(table.MatchOrder.ToArray()));
        Assert.Equal("/a/b", Winner(table, "/a/b"));
    }

    // Below 17 elements Array.Sort happens to be stable, so only a larger table shows whether ties are broken on
    // registration order rather than left to the sort. The parameter templates differ only in a literal after the
    // parameter, so they rank equally without being the same template.
    [Fact]
    public void ManyEqualRankTemplatesCurrentlyKeepRegistrationOrder()
    {
        string[] parameters = [.. Enumerable.Range(0, 40).Select(i => $"/p/{{n{i}}}/l{i}")];
        string[] literals = [.. Enumerable.Range(0, 40).Select(i => $"/p/l{i}")];
        string[] interleaved = [.. parameters.Zip(literals).SelectMany(pair => new[] { pair.First, pair.Second })];

        Assert.Equal(parameters, Values(Build(parameters).MatchOrder.ToArray()));
        Assert.Equal([.. literals, .. parameters], Values(Build(interleaved).MatchOrder.ToArray()));
    }

    [Fact]
    public void AConstraintEarlierOutranksALiteralLater()
    {
        Assert.Equal("/a/{id:int}/{*rest}", Winner(["/a/{id:int}/{*rest}", "/a/{name}/edit"], "/a/5/edit"));
        Assert.Equal("/a/{id:int}/{*rest}", Winner(["/a/{name}/edit", "/a/{id:int}/{*rest}"], "/a/5/edit"));
    }

    [Fact]
    public void GivingTheLiteralRouteTheSameConstraintLetsItWin()
    {
        Assert.Equal("/a/{id:int}/edit", Winner(["/a/{id:int}/{*rest}", "/a/{id:int}/edit"], "/a/5/edit"));
        Assert.Equal("/a/{id:int}/edit", Winner(["/a/{id:int}/edit", "/a/{id:int}/{*rest}"], "/a/5/edit"));
    }

    // The model here is the rule written out independently of the library: one letter per segment, compared in
    // RankOrder, with the shorter signature padded with the letter for a template that has ended.
    [Fact]
    public void RandomTablesAreTriedInTheOrderOfTheModelWhateverTheRegistrationOrder()
    {
        var random = new Random(20261125);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            // A table cannot map the same template twice, so a template with the shape of an earlier one is drawn
            // again.
            var templates = new (string Value, string Ranks)[random.Next(2, 8)];
            var shapes = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < templates.Length; i++)
            {
                string shape;
                do
                    (templates[i].Value, templates[i].Ranks, shape) = RandomTemplate(random, i);
                while (!shapes.Add(shape));
            }

            for (var i = 0; i < templates.Length; i++)
            {
                for (var j = 0; j < templates.Length; j++)
                {
                    Assert.True(
                        Math.Sign(Compare(templates[i].Value, templates[j].Value)) ==
                            Math.Sign(CompareRanks(templates[i].Ranks, templates[j].Ranks)),
                        $"{templates[i].Value} against {templates[j].Value}");
                }
            }

            for (var shuffle = 0; shuffle < 12; shuffle++)
            {
                (string Value, string Ranks)[] order = [.. templates];
                random.Shuffle(order);
                RouteTable table = Build([.. order.Select(t => t.Value)]);

                // OrderBy is stable, so equal signatures stay in registration order.
                int[] expected = [.. Enumerable.Range(0, order.Length).OrderBy(
                    i => order[i].Ranks,
                    Comparer<string>.Create(CompareRanks))];
                int[] actual = [.. table.MatchOrder.ToArray().Select(d => IndexOf(table.Definitions, d))];
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void TheComparisonIsAntisymmetricAndTransitive()
    {
        var random = new Random(1125);
        for (var iteration = 0; iteration < 5000; iteration++)
        {
            RouteTemplate x = RouteTemplate.Parse(RandomTemplate(random, 0).Value);
            RouteTemplate y = RouteTemplate.Parse(RandomTemplate(random, 1).Value);
            RouteTemplate z = RouteTemplate.Parse(RandomTemplate(random, 2).Value);
            string context = $"{x.Value} {y.Value} {z.Value}";

            Assert.Equal(0, x.ComparePrecedence(x));
            Assert.True(Math.Sign(x.ComparePrecedence(y)) == -Math.Sign(y.ComparePrecedence(x)), context);
            int xy = Math.Sign(x.ComparePrecedence(y));
            int yz = Math.Sign(y.ComparePrecedence(z));
            int xz = Math.Sign(x.ComparePrecedence(z));
            if (xy <= 0 && yz <= 0)
                Assert.True(xz == Math.Min(xy, yz), context);
        }
    }

    // A template of up to three literals, constrained and unconstrained parameters, then nothing, optional
    // parameters or a catch-all, with its ranks as one letter per segment, so that two templates rank equally at
    // every position exactly when the letters are equal. Names and constraints vary within a rank. The shape is the
    // template before names are filled in, which is the same for two templates exactly when they are the same.
    private static (string Value, string Ranks, string Shape) RandomTemplate(Random random, int index)
    {
        var value = new StringBuilder();
        var ranks = new StringBuilder();
        var shape = new StringBuilder();
        var names = 0;
        void Append(string segment, char rank)
        {
            value.Append('/').Append(segment.Replace("#", $"p{index}x{names++}", StringComparison.Ordinal));
            ranks.Append(rank);
            shape.Append('/').Append(segment);
        }

        int positionalCount = random.Next(0, 4);
        for (var i = 0; i < positionalCount; i++)
        {
            switch (random.Next(5))
            {
                case 0: Append("a", 'L'); break;
                case 1: Append("b", 'L'); break;
                case 2: Append("{#:int}", 'C'); break;
                case 3: Append("{#:long}", 'C'); break;
                default: Append("{#}", 'R'); break;
            }
        }

        switch (random.Next(6))
        {
            case 1:
                Append("{#?}", 'r');
                break;
            case 2:
                Append("{#:int?}", 'c');
                Append("{#?}", 'r');
                break;
            case 3:
                Append("{#?}", 'r');
                Append("{#:alpha?}", 'c');
                break;
            case 4:
                Append("{*#}", '*');
                break;
            case 5:
                Append("{#:int?}", 'c');
                break;
        }

        return (value.Length == 0 ? "/" : value.ToString(), ranks.ToString(), shape.ToString());
    }

    private static int CompareRanks(string first, string second)
    {
        for (var i = 0; i < Math.Max(first.Length, second.Length); i++)
        {
            int difference = RankOrder.IndexOf(i < first.Length ? first[i] : 'E', StringComparison.Ordinal) -
                RankOrder.IndexOf(i < second.Length ? second[i] : 'E', StringComparison.Ordinal);
            if (difference != 0)
                return difference;
        }

        return 0;
    }

    private static int IndexOf(IReadOnlyList<RouteDefinition> definitions, RouteDefinition definition)
    {
        for (var i = 0; i < definitions.Count; i++)
        {
            if (ReferenceEquals(definitions[i], definition))
                return i;
        }

        return -1;
    }

    private static int Compare(string first, string second)
    {
        return RouteTemplate.Parse(first).ComparePrecedence(RouteTemplate.Parse(second));
    }

    private static string Winner(string[] templates, string path)
    {
        return Winner(Build(templates), path);
    }

    private static string Winner(RouteTable table, string path)
    {
        RouteMatchResult result = table.Match(new Uri(path, UriKind.Relative));
        Assert.True(result.IsSuccess, path);
        return result.Definition.Template.Value;
    }

    private static RouteTable Build(string[] templates)
    {
        var builder = new RouteTableBuilder();
        foreach (string template in templates)
            builder.Map(template, _ => new SettingsRoute());

        return builder.Build();
    }

    private static string[] Values(RouteDefinition[] definitions)
    {
        return [.. definitions.Select(d => d.Template.Value)];
    }

    private static IEnumerable<string[]> Permutations(string[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Length; i++)
        {
            string[] rest = [.. items[..i], .. items[(i + 1)..]];
            foreach (string[] tail in Permutations(rest))
                yield return [items[i], .. tail];
        }
    }
}
