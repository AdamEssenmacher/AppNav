using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// Build rejects two templates that rank equally at every position and both match some path, because the one mapped
// second would never win for that path, so which route won would depend on the order of registration. Every rejection
// here also checks that the path the message gives matches both templates.
public sealed class RouteTableAmbiguousTemplateTests
{
    private const string Ambiguous = "rank equally and both match the path";
    private const string SameTemplate = "are the same template";

    // The two endings of the message: the general advice, and the advice for templates that only optional parameters
    // tell apart, where changing constraints or literals at a required position would not apply.
    private const string GeneralAdvice = "Keep them apart at a required position";
    private const string OptionalAdvice = "never keep templates apart";

    private static readonly string[] ConstraintNames = ["int", "long", "decimal", "bool", "guid", "alpha"];

    private static readonly string Zeros = new('0', 32);
    private static readonly string Letters = new('a', 32);

    // The guid the message gives, and a guid that is all letters but for its hyphens.
    private const string ZeroGuid = "00000000-0000-0000-0000-000000000000";
    private const string LetterGuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    // The pairs of distinct built-in constraints that some text satisfies both, with the value the message gives.
    // Every other pair of distinct built-in constraints is disjoint, which the corpus test checks.
    private static readonly (string First, string Second, string Value)[] Overlapping =
    [
        ("int", "long", "0"),
        ("int", "decimal", "0"),
        ("long", "decimal", "0"),
        ("bool", "alpha", "true"),
    ];

    private static readonly (string First, string Second)[] Disjoint =
    [
        ("alpha", "int"),
        ("alpha", "long"),
        ("alpha", "decimal"),
        ("bool", "int"),
        ("bool", "long"),
        ("bool", "decimal"),
        ("bool", "guid"),
        ("guid", "int"),
        ("guid", "long"),
        ("guid", "decimal"),
        ("guid", "alpha"),
    ];

    // The value each constraint shares with itself.
    private static readonly (string Constraint, string Value)[] Own =
    [
        ("int", "0"),
        ("long", "0"),
        ("decimal", "0"),
        ("bool", "true"),
        ("guid", ZeroGuid),
        ("alpha", "a"),
    ];

    public static TheoryData<string, string, string> OverlappingPairs => new(Overlapping);

    public static TheoryData<string, string> DisjointPairs => new(Disjoint);

    public static TheoryData<string, string> OwnValues => new(Own);

    // The tables here are written by hand, so a new built-in constraint must fail this until they cover it.
    [Fact]
    public void TheConstraintsListedHereAreTheBuiltInOnes()
    {
        string[] supported =
            [.. Regex.Matches(RouteConstraint.SupportedNames, "'([a-z]+)'").Select(match => match.Groups[1].Value)];

        Assert.Equal(supported, ConstraintNames);
    }

    [Fact]
    public void EveryPairOfDistinctBuiltInConstraintsIsListedExactlyOnce()
    {
        var listed = new List<string>();
        foreach ((string first, string second, _) in Overlapping)
            listed.Add(PairKey(first, second));
        foreach ((string first, string second) in Disjoint)
            listed.Add(PairKey(first, second));

        var expected = new List<string>();
        for (var i = 0; i < ConstraintNames.Length; i++)
        {
            for (int j = i + 1; j < ConstraintNames.Length; j++)
                expected.Add(PairKey(ConstraintNames[i], ConstraintNames[j]));
        }

        Assert.Equal(expected.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(OverlappingPairs))]
    public void OverlappingConstraintsAreRejectedInBothOrdersWithAPathBothMatch(
        string first,
        string second,
        string value)
    {
        AssertAmbiguous($"/p/{{a:{first}}}", $"/p/{{b:{second}}}", $"/p/{value}");
        AssertAmbiguous($"/p/{{b:{second}}}", $"/p/{{a:{first}}}", $"/p/{value}");
    }

    [Fact]
    public void TheExampleFromTheGuideIsRejected()
    {
        AssertAmbiguous("/products/{id:int}", "/products/{n:long}", "/products/0");
        Assert.NotNull(RouteTemplate.Parse("/products/{id:int}").Match("/products/42"));
        Assert.NotNull(RouteTemplate.Parse("/products/{n:long}").Match("/products/42"));
    }

    [Theory]
    [MemberData(nameof(DisjointPairs))]
    public void DisjointConstraintsBuildInBothOrders(string first, string second)
    {
        Assert.Equal(2, BuildTable([$"/p/{{a:{first}}}", $"/p/{{b:{second}}}"]).Definitions.Count);
        Assert.Equal(2, BuildTable([$"/p/{{b:{second}}}", $"/p/{{a:{first}}}"]).Definitions.Count);
    }

    // On its own, the same constraint at the same position makes the same template; beside a position whose
    // constraints differ but overlap, it makes two templates that cannot be told apart.
    [Theory]
    [MemberData(nameof(OwnValues))]
    public void TheSameConstraintOverlapsItself(string constraint, string value)
    {
        string upper = constraint.ToUpperInvariant();
        AssertSame($"/p/{{a:{constraint}}}", $"/p/{{b:{upper}}}");
        AssertAmbiguous($"/p/{{a:{constraint}}}/{{b:int}}", $"/p/{{c:{upper}}}/{{d:long}}", $"/p/{value}/0");
        AssertAmbiguous($"/p/{{c:{upper}}}/{{d:long}}", $"/p/{{a:{constraint}}}/{{b:int}}", $"/p/{value}/0");
    }

    // The value Build names for each pair, in both orders, satisfies both constraints, and only the pairs listed as
    // overlapping have one; every other pair of built-in constraints is disjoint, never only possibly overlapping.
    [Fact]
    public void TheCommonValueOfEveryPairSatisfiesBothAndIsTheSameInBothOrders()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string first, string second, string value) in Overlapping)
            expected[PairKey(first, second)] = value;
        foreach ((string constraint, string value) in Own)
            expected[PairKey(constraint, constraint)] = value;

        foreach (string first in ConstraintNames)
        {
            foreach (string second in ConstraintNames)
            {
                RouteConstraint x = RouteConstraint.Find(first)!;
                RouteConstraint y = RouteConstraint.Find(second)!;
                ConstraintOverlap overlap = RouteConstraint.FindCommonValue(x, y, out string? value);
                ConstraintOverlap reversed = RouteConstraint.FindCommonValue(y, x, out string? reversedValue);

                string? expectedValue = expected.GetValueOrDefault(PairKey(first, second));
                Assert.Equal(expectedValue, value);
                Assert.Equal(
                    expectedValue is null ? ConstraintOverlap.Disjoint : ConstraintOverlap.CommonValue,
                    overlap);
                Assert.Equal(overlap, reversed);
                Assert.Equal(value, reversedValue);
                if (value is not null)
                    Assert.True(x.IsSatisfiedBy(value) && y.IsSatisfiedBy(value), $"{first} {second} {value}");
            }
        }
    }

    // The test of the overlap table itself: no text in a large corpus satisfies both constraints of a pair listed as
    // disjoint, and the corpus has text for every pair listed as overlapping, so it is not trivially empty.
    [Fact]
    public void NoTextInTheCorpusSatisfiesBothConstraintsOfADisjointPair()
    {
        List<string> corpus = Corpus();
        foreach ((string first, string second) in Disjoint)
        {
            RouteConstraint x = RouteConstraint.Find(first)!;
            RouteConstraint y = RouteConstraint.Find(second)!;
            foreach (string text in corpus)
                Assert.False(x.IsSatisfiedBy(text) && y.IsSatisfiedBy(text), $"{first} and {second}: '{text}'");
        }

        foreach ((string first, string second, _) in Overlapping)
        {
            RouteConstraint x = RouteConstraint.Find(first)!;
            RouteConstraint y = RouteConstraint.Find(second)!;
            int both = corpus.Count(text => x.IsSatisfiedBy(text) && y.IsSatisfiedBy(text));
            Assert.True(both > 1, $"{first} and {second}: {both}");
        }

        foreach (string name in ConstraintNames)
            Assert.True(corpus.Count(RouteConstraint.Find(name)!.IsSatisfiedBy) > 40, name);
    }

    [Fact]
    public void DifferentLiteralsKeepOverlappingConstraintsApart()
    {
        Assert.Equal(2, BuildTable(["/a/{x:int}", "/b/{y:long}"]).Definitions.Count);
        Assert.Equal(2, BuildTable(["/A/{x:int}", "/a/{y:long}"]).Definitions.Count);
        Assert.Equal(2, BuildTable(["/{x:int}/a", "/{y:long}/b"]).Definitions.Count);
        Assert.Equal(2, BuildTable(["/a", "/b"]).Definitions.Count);
    }

    [Fact]
    public void TheSameLiteralWithOverlappingConstraintsIsRejectedWithTheLiteralAsFormatWritesIt()
    {
        AssertAmbiguous("/a/{x:int}", "/a/{y:long}", "/a/0");
        AssertAmbiguous("/my files/{x:int}", "/my%20files/{y:decimal}", "/my%20files/0");
        AssertAmbiguous("/x%2Fy/{x:bool}", "/x%2fy/{y:alpha}", "/x%2Fy/true");
        AssertAmbiguous("/caf\u00E9/{x}/{y:int}", "/caf%C3%A9/{z}/{w:long}", "/caf%C3%A9/x/0");
    }

    // An optional parameter or a catch-all never keeps two templates apart, because the path that leaves it out
    // matches both, so the path given is the required prefix, even when that is only the root.
    // When the required positions are the same, only the optional parameters tell the templates apart, and the
    // message gives the advice for that instead of the general one.
    [Theory]
    [InlineData("/a/{x:int?}", "/a/{y:alpha?}", "/a", OptionalAdvice)]
    [InlineData("/p/{id:int?}", "/p/{n:decimal?}", "/p", OptionalAdvice)]
    [InlineData("/{x:int?}", "/{y:alpha?}", "/", OptionalAdvice)]
    [InlineData("/{x:bool?}/{z?}", "/{y:guid?}/{w?}", "/", OptionalAdvice)]
    [InlineData("/a/{x:bool?}/{z:int?}", "/a/{y:int?}/{w:alpha?}", "/a", OptionalAdvice)]
    [InlineData("/a/{n:int}/{x:int?}", "/a/{m:INT}/{y:alpha?}", "/a/0", OptionalAdvice)]
    [InlineData("/a/{x:int}/{o:alpha?}", "/a/{y:long}/{p:bool?}", "/a/0", GeneralAdvice)]
    [InlineData("/f/{x:bool}/{*rest}", "/f/{y:alpha}/{*path}", "/f/true", GeneralAdvice)]
    [InlineData("/f/{x}/{y:int}/{*rest}", "/f/{z}/{w:long}/{*path}", "/f/x/0", GeneralAdvice)]
    public void OptionalParametersAndCatchAllsAlwaysOverlap(string first, string second, string path, string advice)
    {
        AssertAmbiguous(first, second, path, advice);
        AssertAmbiguous(second, first, path, advice);
    }

    [Fact]
    public void KeepingOneParameterOptionalMakesTheTemplatesRankDifferently()
    {
        RouteTable table = BuildTable(["/a/{x:int?}", "/a/{y:alpha}"]);

        Assert.Equal("/a/{x:int?}", Winner(table, "/a"));
        Assert.Equal("/a/{x:int?}", Winner(table, "/a/5"));
        Assert.Equal("/a/{y:alpha}", Winner(table, "/a/b"));
    }

    // The example from the guide: a guid has hyphens between its hex digits, which no number or letters can have, so a
    // guid, a number and a word can share a position, in any order, and each path goes to its own route.
    [Fact]
    public void AGuidBesideANumberOrAWordBuildsAndEachPathGoesToItsOwnRoute()
    {
        string[] templates = ["/orders/{id:int}", "/orders/{key:guid}", "/orders/{slug:alpha}"];
        foreach (string[] order in new[] { templates, [templates[1], templates[2], templates[0]] })
        {
            RouteTable table = BuildTable(order);
            Assert.Equal("/orders/{id:int}", Winner(table, "/orders/42"));
            Assert.Equal("/orders/{id:int}", Winner(table, $"/orders/{Zeros}"));
            Assert.Equal("/orders/{key:guid}", Winner(table, "/orders/6f9619ff-8b86-d011-b42d-00cf4fc964ff"));
            Assert.Equal("/orders/{key:guid}", Winner(table, $"/orders/{ZeroGuid}"));
            Assert.Equal("/orders/{key:guid}", Winner(table, $"/orders/{LetterGuid}"));
            Assert.Equal("/orders/{slug:alpha}", Winner(table, $"/orders/{Letters}"));
            Assert.Null(WinnerOrNull(table, "/orders/6f9619ff8b86d011b42d00cf4fc964ff"));
        }
    }

    // One class of overlapping constraints each: the numbers, bool and alpha, and guid. So three templates of one
    // shape can share a position, which the cost of the check in Build allows for.
    [Fact]
    public void ANumberABoolAndAGuidBuildTogetherInEveryOrder()
    {
        string[] templates = ["/p/{a:int}", "/p/{a:bool}", "/p/{a:guid}"];
        int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        foreach (int[] order in orders)
        {
            RouteTable table = BuildTable([templates[order[0]], templates[order[1]], templates[order[2]]]);
            Assert.Equal("/p/{a:int}", Winner(table, "/p/42"));
            Assert.Equal("/p/{a:bool}", Winner(table, "/p/true"));
            Assert.Equal("/p/{a:guid}", Winner(table, $"/p/{ZeroGuid}"));
        }
    }

    // What the guide says of accepting both forms of a guid with a parameter without a constraint: beside a number
    // and a word, a guid without hyphens made only of digits or only of letters goes to the constrained route.
    [Fact]
    public void AGuidWithoutHyphensGoesToAConstrainedRouteWhenItSatisfiesOne()
    {
        RouteTable table = BuildTable(["/orders/{id:int}", "/orders/{x:alpha}", "/orders/{key}"]);

        Assert.Equal("/orders/{id:int}", Winner(table, $"/orders/{Zeros}"));
        Assert.Equal("/orders/{x:alpha}", Winner(table, $"/orders/{Letters}"));
        Assert.Equal("/orders/{key}", Winner(table, "/orders/6f9619ff8b86d011b42d00cf4fc964ff"));
        Assert.Equal("/orders/{key}", Winner(table, $"/orders/{ZeroGuid}"));

        // Digits alone are not enough: 32 of them that are out of range for the number are not that number.
        Assert.Equal("/orders/{key}", Winner(table, "/orders/12345678901234567890123456789012"));
    }

    // A guid still overlaps itself, so two templates of one shape with a guid at the same position are told apart
    // only by their other positions, and the path given has the zero guid there.
    [Theory]
    [InlineData("/a/{x:guid}/{p:int}", "/a/{y:guid}/{q:long}", $"/a/{ZeroGuid}/0")]
    [InlineData("/a/{x:guid}/{p:bool}", "/a/{y:GUID}/{q:alpha}", $"/a/{ZeroGuid}/true")]
    public void TwoGuidsAtOnePositionAreStillRejected(string first, string second, string path)
    {
        AssertAmbiguous(first, second, path);
        AssertAmbiguous(second, first, path);
    }

    [Fact]
    public void WhatToWriteInsteadOfOverlappingOptionalParametersBuilds()
    {
        RouteTable table = BuildTable(["/a", "/a/{x:int}", "/a/{y:alpha}"]);

        Assert.Equal("/a", Winner(table, "/a"));
        Assert.Equal("/a/{x:int}", Winner(table, "/a/5"));
        Assert.Equal("/a/{y:alpha}", Winner(table, "/a/b"));
    }

    [Fact]
    public void OneDisjointRequiredPositionKeepsTemplatesApart()
    {
        string[] templates = ["/p/{a:int}/{b:alpha}/{c:guid}/{d}", "/p/{e:long}/{f:int}/{g:alpha}/{h}"];

        Assert.Equal(2, BuildTable(templates).Definitions.Count);
        Assert.Equal(2, BuildTable([templates[1], templates[0]]).Definitions.Count);
    }

    [Fact]
    public void TemplatesThatOverlapAtEveryRequiredPositionAreRejected()
    {
        AssertAmbiguous(
            "/p/{a:int}/{b:alpha}/{c:guid}/{d}/q/{o:int?}",
            "/p/{e:long}/{f:bool}/{g:guid}/{h}/q/{r:bool?}",
            $"/p/0/true/{ZeroGuid}/x/q");
    }

    // Templates of different rank can match the same path, because precedence, not registration, decides.
    [Theory]
    [InlineData("/a/{id:int}", "/a/{name}", "/a/5")]
    [InlineData("/a/{id:int}", "/a/{n:long?}", "/a/5")]
    [InlineData("/a/{id:int?}", "/a/{name}", "/a/5")]
    [InlineData("/a/{id:alpha}", "/a/{*rest}", "/a/b")]
    [InlineData("/a", "/a/{x:int?}", "/a")]
    public void TemplatesOfDifferentRankThatMatchTheSamePathBuildAndPrecedenceDecides(
        string winner,
        string loser,
        string path)
    {
        Assert.NotNull(RouteTemplate.Parse(loser).Match(path));
        Assert.Equal(winner, Winner(BuildTable([winner, loser]), path));
        Assert.Equal(winner, Winner(BuildTable([loser, winner]), path));
    }

    [Fact]
    public void TheSameRouteTypeCanStillBeMappedToTemplatesThatCannotMatchTheSamePath()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/p/{id:int}", c => new ProductRoute(c.Path("id")))
            .Map("/p/{id:alpha}", c => new ProductRoute(c.Path("id")))
            .Build();

        Assert.Same(table.Definitions[0], table.Match(new Uri("/p/5", UriKind.Relative)).Definition);
        Assert.Same(table.Definitions[1], table.Match(new Uri("/p/b", UriKind.Relative)).Definition);
    }

    [Fact]
    public void TheSameRouteTypeOnTemplatesThatCannotBeToldApartIsStillRejected()
    {
        var builder = new RouteTableBuilder()
            .Map("/p/{id:int}", c => new ProductRoute(c.Path("id")))
            .Map("/p/{id:long}", c => new ProductRoute(c.Path("id")));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(Ambiguous, exception.Message, StringComparison.Ordinal);
        AssertInOrder(exception.Message, Quoted(typeof(ProductRoute)), Quoted(typeof(ProductRoute)));
    }

    // Reported is the first definition, in registration order, that conflicts with an earlier one: here the fifth,
    // with the third, before the sixth conflicts with the first. The second and fourth conflict with nothing.
    [Fact]
    public void TheFirstConflictInRegistrationOrderIsReported()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Build(
            ("/a/{x:int}", typeof(SettingsRoute)),
            ("/b/{x:alpha}", typeof(ProductRoute)),
            ("/b/{y:int}", typeof(ProductByNameRoute)),
            ("/a/{y:alpha}", typeof(OrderRoute)),
            ("/b/{z:long}", typeof(FileRoute)),
            ("/a/{z:decimal}", typeof(SettingsRoute))));

        Assert.Contains(Ambiguous, exception.Message, StringComparison.Ordinal);
        AssertInOrder(exception.Message, "'/b/{y:int}'", "'/b/{z:long}'");
        AssertInOrder(exception.Message, Quoted(typeof(ProductByNameRoute)), Quoted(typeof(FileRoute)));
        Assert.Contains("'/b/0'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'/a/", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'/b/{x:alpha}'", exception.Message, StringComparison.Ordinal);
    }

    // The same-template check runs over the whole table first, so a repeat is reported even when an ambiguous pair
    // comes before it.
    [Fact]
    public void TheSameTemplateIsReportedAsTheSameEvenAfterAnAmbiguousPair()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Build(
            ("/a/{x:int}", typeof(SettingsRoute)),
            ("/a/{y:long}", typeof(ProductRoute)),
            ("/a/{z:INT}", typeof(FileRoute))));

        Assert.Contains(SameTemplate, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Ambiguous, exception.Message, StringComparison.Ordinal);
        AssertInOrder(exception.Message, "'/a/{x:int}'", "'/a/{z:INT}'");
    }

    [Theory]
    [InlineData("/a/{x:int}", "/a/{y:Int}")]
    [InlineData("/a/{x:int?}", "/a/{y:int?}")]
    [InlineData("/a/{x}/{*r}", "/a/{y}/{*s}")]
    [InlineData("/%61/{x:guid}", "/a/{y:GUID}")]
    public void TheSameTemplateIsStillReportedAsTheSame(string first, string second)
    {
        AssertSame(first, second);
        AssertSame(second, first);
    }

    [Fact]
    public void AFailedBuildCanBeRepeatedAndLeavesEarlierTablesAsTheyWere()
    {
        var builder = new RouteTableBuilder().Map("/p/{id:int}", c => new ProductRoute(c.Path("id")));
        RouteTable earlier = builder.Build();
        builder.Map("/p/{n:long}", c => new ProductByNameRoute(c.Path("n")));

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;
        Assert.Contains(Ambiguous, message, StringComparison.Ordinal);
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(builder.Build).Message);

        builder.Map("/q", _ => new SettingsRoute());
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(builder.Build).Message);

        Assert.Single(earlier.Definitions);
        Assert.Equal(new ProductRoute("5"), earlier.Match(new Uri("/p/5", UriKind.Relative)).Route);
    }

    // Random tables over every kind of segment and every constraint, checked against a model written independently
    // of the library: the same-template pair is reported if there is one, and otherwise the first ambiguous pair, as
    // the rule sets out, with a path that matches both.
    [Fact]
    public void RandomTablesAreRejectedExactlyWhenTheModelFindsAConflict()
    {
        var random = new Random(20261125);
        int same = 0, ambiguous = 0, onlyOptionalCount = 0;
        for (var iteration = 0; iteration < 4000; iteration++)
        {
            Template[] templates = RandomTable(random, random.Next(1, 6));
            string context = string.Join(' ', templates.Select(t => t.Value));

            (int Earlier, int Later)? expectedSame = FirstConflict(templates, IsSameModel);
            (int Earlier, int Later)? expectedAmbiguous = FirstConflict(templates, IsAmbiguousModel);
            Exception? exception = Record.Exception(() => BuildTable([.. templates.Select(t => t.Value)]));
            if (expectedSame is (int sameEarlier, int sameLater))
            {
                same++;
                Assert.True(exception?.Message.Contains(SameTemplate, StringComparison.Ordinal) == true, context);
                AssertInOrder(exception!.Message, Quoted(templates[sameEarlier]), Quoted(templates[sameLater]));
            }
            else if (expectedAmbiguous is (int earlier, int later))
            {
                ambiguous++;
                Assert.True(exception?.Message.Contains(Ambiguous, StringComparison.Ordinal) == true, context);
                (string first, string second, _) = AssertWitnessMatchesBoth(exception!.Message);
                Assert.Equal(templates[earlier].Value, first);
                Assert.Equal(templates[later].Value, second);
                bool onlyOptional = HasSameRequiredModel(templates[earlier], templates[later]);
                onlyOptionalCount += onlyOptional ? 1 : 0;
                Assert.True(
                    exception.Message.Contains(onlyOptional ? OptionalAdvice : GeneralAdvice, StringComparison.Ordinal),
                    context);
                Assert.DoesNotContain(
                    onlyOptional ? GeneralAdvice : OptionalAdvice,
                    exception.Message,
                    StringComparison.Ordinal);
            }
            else
            {
                Assert.True(exception is null, $"{context}: {exception?.Message}");
            }
        }

        Assert.InRange(same, 400, 1600);
        Assert.InRange(ambiguous, 450, 1800);
        Assert.InRange(onlyOptionalCount, 150, ambiguous - 150);
    }

    // For tables that Build accepts, no two templates that rank equally both match any path, and which template wins
    // does not depend on the order of registration. Half the paths are written from the templates themselves, so
    // that matches are common.
    [Fact]
    public void InAnAcceptedTableNoPathMatchesTwoEqualRankTemplatesAndTheWinnerIgnoresOrder()
    {
        var random = new Random(1125);
        int accepted = 0, matched = 0, equalRankChecks = 0;
        for (var iteration = 0; iteration < 1500; iteration++)
        {
            Template[] templates = RandomTable(random, random.Next(2, 8));
            string[] values = [.. templates.Select(t => t.Value)];
            RouteTable table;
            try
            {
                table = BuildTable(values);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            accepted++;
            RouteTemplate[] parsed = [.. values.Select(RouteTemplate.Parse)];
            RouteTable[] reordered = [.. Reorderings(random, values).Select(BuildTable)];
            for (var p = 0; p < 20; p++)
            {
                string path = random.Next(2) == 0
                    ? RandomPath(random)
                    : PathFrom(random, templates[random.Next(templates.Length)]);
                for (var i = 0; i < parsed.Length; i++)
                {
                    for (int j = i + 1; j < parsed.Length; j++)
                    {
                        if (parsed[i].ComparePrecedence(parsed[j]) != 0)
                            continue;

                        equalRankChecks++;

                        Assert.False(
                            parsed[i].Match(path) is not null && parsed[j].Match(path) is not null,
                            $"{values[i]} and {values[j]} both match {path}");
                    }
                }

                string? winner = WinnerOrNull(table, path);
                if (winner is not null)
                    matched++;

                foreach (RouteTable other in reordered)
                    Assert.Equal(winner, WinnerOrNull(other, path));
            }
        }

        Assert.InRange(accepted, 250, 1100);
        Assert.True(matched > 3000, $"{matched} paths matched.");
        Assert.True(equalRankChecks > 3000, $"{equalRankChecks} equal-rank pairs checked.");
    }

    // Templates are grouped by shape before they are compared, and a group's members are assumed to have the same
    // literals. The hash already keeps different literals apart, so only this direct comparison shows that equality
    // does too, which a table would need a hash collision to reveal.
    [Theory]
    [InlineData("/a/{x:int}", "/a/{y:long}", true)]
    [InlineData("/a/{x:int}", "/a/{y:int?}", false)]
    [InlineData("/a/{x:int}", "/a/{y}", false)]
    [InlineData("/a/{x:int}", "/b/{y:long}", false)]
    [InlineData("/a/{x:int}/c", "/a/{y:long}/d", false)]
    [InlineData("/A/{x:int}", "/a/{y:long}", false)]
    public void TemplatesHaveTheSameShapeOnlyWithTheSameLiteralsAndKinds(string first, string second, bool same)
    {
        RouteTemplate x = RouteTemplate.Parse(first);
        RouteTemplate y = RouteTemplate.Parse(second);

        Assert.Equal(same, SameTemplateComparer.SameShape.Equals(x, y));
        Assert.Equal(same, SameTemplateComparer.SameShape.Equals(y, x));
        if (same)
            Assert.Equal(SameTemplateComparer.SameShape.GetHashCode(x), SameTemplateComparer.SameShape.GetHashCode(y));
    }

    // Each group of four has one shape, with constraints that keep its members apart in pairs: int and alpha, then
    // bool and guid, which differ in their last literal from the first two.
    [Fact]
    public void ManySmallGroupsOfOneShapeBuild()
    {
        string[] constraints = ["int", "alpha", "bool", "guid"];
        var builder = new RouteTableBuilder();
        for (var i = 0; i < 20_000; i++)
        {
            string last = i % 4 < 2 ? "y" : "z";
            builder.Map($"/p/l{i / 4}/{{id:{constraints[i % 4]}}}/{last}", _ => new SettingsRoute());
        }

        RouteTable table = builder.Build();

        Assert.Equal(20_000, table.Definitions.Count);
        Assert.Equal("/p/l7/{id:alpha}/y", Winner(table, "/p/l7/b/y"));
        Assert.Equal("/p/l7/{id:bool}/z", Winner(table, "/p/l7/true/z"));
    }

    // A template drawn with its model: one (kind, key) pair for each segment, where the key is a literal's decoded
    // text or a parameter's constraint, empty for none.
    private sealed record Template(string Value, (char Kind, string Key)[] Model);

    // A third of the templates take the shape of an earlier one with its constraints drawn again, so that templates
    // that rank equally, and both outcomes for them, are common.
    private static Template[] RandomTable(Random random, int count)
    {
        string[] constraints = ["int", "long", "decimal", "bool", "guid", "alpha"];
        var templates = new Template[count];
        for (var i = 0; i < count; i++)
        {
            if (i == 0 || random.Next(3) != 0)
            {
                templates[i] = Write(random, RandomModel(random));
                continue;
            }

            (char Kind, string Key)[] model = [.. templates[random.Next(i)].Model];
            for (var j = 0; j < model.Length; j++)
            {
                if (model[j].Kind != 'L' && model[j].Key.Length > 0)
                    model[j].Key = constraints[random.Next(constraints.Length)];
            }

            templates[i] = Write(random, model);
        }

        return templates;
    }

    // Up to three literals and required parameters, then nothing, one or two optional parameters, or a catch-all,
    // over a few literals and every constraint.
    private static (char Kind, string Key)[] RandomModel(Random random)
    {
        string[] literals = ["a", "b"];
        string[] constraints = ["", "", "int", "long", "decimal", "bool", "guid", "alpha"];
        var model = new List<(char Kind, string Key)>();
        int positionalCount = random.Next(0, 4);
        for (var i = 0; i < positionalCount; i++)
        {
            model.Add(random.Next(3) == 0
                ? ('L', literals[random.Next(literals.Length)])
                : ('R', constraints[random.Next(constraints.Length)]));
        }

        int tail = random.Next(4);
        for (var i = 0; i < (tail == 3 ? 2 : tail == 1 ? 1 : 0); i++)
            model.Add(('O', constraints[random.Next(constraints.Length)]));

        if (tail == 2)
            model.Add(('*', ""));

        return [.. model];
    }

    // Writes a model as template text with random names.
    private static Template Write(Random random, (char Kind, string Key)[] model)
    {
        var value = new StringBuilder();
        for (var i = 0; i < model.Length; i++)
        {
            (char kind, string key) = model[i];
            string name = $"{(random.Next(2) == 0 ? "n" : "v")}{i}";
            string constraint = key.Length == 0 ? "" : ":" + key;
            value.Append('/').Append(kind switch
            {
                'L' => key,
                'R' => $"{{{name}{constraint}}}",
                'O' => $"{{{name}{constraint}?}}",
                _ => $"{{*{name}}}",
            });
        }

        return new Template(value.Length == 0 ? "/" : value.ToString(), model);
    }

    private static bool IsSameModel(Template x, Template y)
    {
        return x.Model.SequenceEqual(y.Model);
    }

    // The rule written out from the model: the same kinds and literals, parameters either both constrained or both
    // not, and, at every required position, constraints that the table above lists as overlapping.
    private static bool IsAmbiguousModel(Template x, Template y)
    {
        if (x.Model.Length != y.Model.Length)
            return false;

        for (var i = 0; i < x.Model.Length; i++)
        {
            ((char kind, string key), (char otherKind, string otherKey)) = (x.Model[i], y.Model[i]);
            if (kind != otherKind || (key.Length == 0) != (otherKey.Length == 0))
                return false;

            if (kind == 'L' && key != otherKey)
                return false;

            if (kind == 'R' && key.Length > 0 && key != otherKey && !IsListedAsOverlapping(key, otherKey))
                return false;
        }

        return true;
    }

    // Whether two models are the same at every required position, so that only optional parameters differ.
    private static bool HasSameRequiredModel(Template x, Template y)
    {
        for (var i = 0; i < x.Model.Length; i++)
        {
            if (x.Model[i].Kind is 'L' or 'R' && x.Model[i] != y.Model[i])
                return false;
        }

        return true;
    }

    private static bool IsListedAsOverlapping(string first, string second)
    {
        foreach ((string x, string y, _) in Overlapping)
        {
            if (PairKey(x, y) == PairKey(first, second))
                return true;
        }

        return false;
    }

    // The first template, in registration order, that conflicts with an earlier one, with the earliest such one.
    private static (int Earlier, int Later)? FirstConflict(
        Template[] templates,
        Func<Template, Template, bool> conflict)
    {
        for (var later = 0; later < templates.Length; later++)
        {
            for (var earlier = 0; earlier < later; earlier++)
            {
                if (conflict(templates[earlier], templates[later]))
                    return (earlier, later);
            }
        }

        return null;
    }

    // A path written from a template: each literal, a value for each parameter that its constraint usually accepts,
    // and for an optional parameter or a catch-all, sometimes nothing.
    private static string PathFrom(Random random, Template template)
    {
        var path = new StringBuilder();
        foreach ((char kind, string key) in template.Model)
        {
            if (kind is 'O' or '*' && random.Next(3) == 0)
                break;

            path.Append('/').Append(kind == 'L' ? key : ValueFor(random, key));
            if (kind == '*' && random.Next(2) == 0)
                path.Append('/').Append(ValueFor(random, ""));
        }

        return path.Length == 0 ? "/" : path.ToString();
    }

    private static string ValueFor(Random random, string constraint)
    {
        string[] values = constraint switch
        {
            "int" => ["0", "-5", "+7", "2147483648"],
            "long" => ["0", "42", "9223372036854775807", "2147483648"],
            "decimal" => ["0", "1.5", ".5", "99999999999999999999"],
            "bool" => ["true", "FALSE", "tRuE"],
            "guid" => [ZeroGuid, LetterGuid, "6F9619FF-8b86-D011-b42d-00CF4fc964ff"],
            "alpha" => ["a", "true", "false", Letters, "caf%C3%A9", "x"],
            _ => ["x", "a", "b", "0", "true", Zeros, "%20"],
        };

        return values[random.Next(values.Length)];
    }

    private static string RandomPath(Random random)
    {
        string[] pool = ["a", "b", "x", "0", "5", "-1", "1.5", "true", "False", Zeros, Letters, "%C3%A9", "%20", ""];
        var path = new StringBuilder();
        int count = random.Next(0, 6);
        for (var i = 0; i < count; i++)
            path.Append('/').Append(pool[random.Next(pool.Length)]);

        return path.Length == 0 ? "/" : path.ToString();
    }

    // Numbers of every shape and length, guids with and without hyphens, bool text in every case, letters including the
    // hex letters and letters outside ASCII and the Basic Multilingual Plane, the empty string, and random mixtures.
    private static List<string> Corpus()
    {
        var random = new Random(27);
        var corpus = new List<string>
        {
            "", "-", "+", ".", "-.", "+.", "..", "1..2", "1.2.3", " 1", "1 ", "1e5", "0x10", "1,000", "\u0663",
            int.MinValue.ToString(CultureInfo.InvariantCulture), int.MaxValue.ToString(CultureInfo.InvariantCulture),
            long.MinValue.ToString(CultureInfo.InvariantCulture), long.MaxValue.ToString(CultureInfo.InvariantCulture),
            "2147483648", "-2147483649", "9223372036854775808", "79228162514264337593543950335",
            "79228162514264337593543950336", "a1", "1a", "true1", "1true", "-a", "+true", "t", "f", "tru", "fals",
            "truee", "true ", "\u00E9", "\u03B1\u03B2", "\uD835\uDC9C", "e\u0301", "\u0430\u0431", "\u65E5\u672C",
            Zeros, Letters, new string('f', 32), new string('F', 32), new string('g', 32), new string('a', 31),
            new string('a', 33), new string('0', 36), new string('-', 36), ZeroGuid, LetterGuid,
            "{" + ZeroGuid + "}", ZeroGuid + "\n",
        };

        for (var length = 1; length <= 40; length++)
        {
            foreach (string sign in new[] { "", "+", "-" })
            {
                corpus.Add(sign + new string('0', length));
                corpus.Add(sign + new string('9', length));
                corpus.Add(sign + RandomText(random, "0123456789", length));
                corpus.Add(sign + "." + RandomText(random, "0123456789", length));
                corpus.Add(sign + RandomText(random, "0123456789", length) + ".");
                int point = random.Next(length + 1);
                string digits = RandomText(random, "0123456789", length);
                corpus.Add(sign + digits[..point] + "." + digits[point..]);
            }

            corpus.Add(RandomText(random, "abcdef", length));
            corpus.Add(RandomText(random, "ABCDEFabcdef", length));
            corpus.Add(RandomText(random, "abcdefghijklmnopqrstuvwxyzTRUEFALS", length));
            corpus.Add(RandomText(random, "\u00E9\u03B1\u0430\u65E5", length));
        }

        for (var i = 0; i < 200; i++)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            var guid = new Guid(bytes);
            corpus.Add(guid.ToString("N"));
            corpus.Add(guid.ToString("D"));
            corpus.Add(guid.ToString("N").ToUpperInvariant());
            corpus.Add(guid.ToString("D").ToUpperInvariant());
            corpus.Add(RandomText(random, "0123456789", 32));
            corpus.Add(RandomText(random, "abcdefABCDEF", 32));
            foreach (string hex in new[] { "abcdef", "0123456789" })
            {
                string digits = RandomText(random, hex, 32);
                corpus.Add($"{digits[..8]}-{digits[8..12]}-{digits[12..16]}-{digits[16..20]}-{digits[20..]}");
            }
        }

        foreach (string word in new[] { "true", "false" })
        {
            for (var mask = 0; mask < 1 << word.Length; mask++)
            {
                corpus.Add(string.Concat(word.Select(
                    (c, i) => (mask & (1 << i)) != 0 ? char.ToUpperInvariant(c) : c)));
            }
        }

        for (var i = 0; i < 20_000; i++)
            corpus.Add(RandomText(random, "0123456789abcdefABCDEFxyztruefalsTRUEFALS-+.", random.Next(0, 40)));

        return corpus;
    }

    private static string RandomText(Random random, string alphabet, int length)
    {
        var text = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            text.Append(alphabet[random.Next(alphabet.Length)]);

        return text.ToString();
    }

    // Every order of a small table, and six random orders of a larger one.
    private static IEnumerable<string[]> Reorderings(Random random, string[] values)
    {
        if (values.Length <= 4)
            return Permutations(values);

        return Enumerable.Range(0, 6).Select(_ =>
        {
            string[] copy = [.. values];
            random.Shuffle(copy);
            return copy;
        }).ToArray();
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

    private static string PairKey(string first, string second)
    {
        return string.CompareOrdinal(first, second) <= 0 ? $"{first}+{second}" : $"{second}+{first}";
    }

    private static void AssertAmbiguous(string first, string second, string path, string advice = GeneralAdvice)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Build((first, typeof(SettingsRoute)), (second, typeof(ProductRoute))));

        Assert.Contains(Ambiguous, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SameTemplate, exception.Message, StringComparison.Ordinal);
        AssertInOrder(exception.Message, $"'{first}'", $"'{second}'");
        AssertInOrder(exception.Message, Quoted(typeof(SettingsRoute)), Quoted(typeof(ProductRoute)));
        Assert.Contains($"'{path}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(advice, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            advice == GeneralAdvice ? OptionalAdvice : GeneralAdvice,
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AddConstraint", exception.Message, StringComparison.Ordinal);
        AssertWitnessMatchesBoth(exception.Message);
    }

    private static void AssertSame(string first, string second)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Build((first, typeof(SettingsRoute)), (second, typeof(ProductRoute))));

        Assert.Contains(SameTemplate, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Ambiguous, exception.Message, StringComparison.Ordinal);
        AssertInOrder(exception.Message, $"'{first}'", $"'{second}'");
    }

    // The witness invariant: the path the message gives matches both templates it names, as a template matches it and
    // as a table holding only that template matches it pasted into a relative URI, or after an https origin. The
    // message is read only to find the two templates and the path, each in quotes, whichever advice follows; no
    // template or path here contains a quote.
    private static (string First, string Second, string Path) AssertWitnessMatchesBoth(string message)
    {
        Match parts = Regex.Match(
            message,
            "^The templates '([^']*)', .*?, and '([^']*)', .*? both match the path '([^']*)'",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        Assert.True(parts.Success, message);
        string first = parts.Groups[1].Value;
        string second = parts.Groups[2].Value;
        string path = parts.Groups[3].Value;

        Assert.True(RouteTemplate.Parse(first).Match(path) is not null, $"{first} does not match {path}");
        Assert.True(RouteTemplate.Parse(second).Match(path) is not null, $"{second} does not match {path}");
        foreach (string template in new[] { first, second })
        {
            RouteTable alone = BuildTable([template]);
            Assert.True(alone.Match(new Uri(path, UriKind.Relative)).IsSuccess, $"{template} against {path}");
            Assert.True(alone.Match(new Uri("https://example.com" + path)).IsSuccess, $"{template} against {path}");
        }

        return (first, second, path);
    }

    // Checks that both texts occur, the first before the second, which may be the same text occurring again.
    private static void AssertInOrder(string message, string first, string second)
    {
        int firstIndex = message.IndexOf(first, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"{first} is not in: {message}");
        Assert.True(
            message.IndexOf(second, firstIndex + first.Length, StringComparison.Ordinal) >= 0,
            $"{second} does not follow {first} in: {message}");
    }

    private static string Quoted(Type routeType)
    {
        return $"'{routeType.FullName}'";
    }

    private static string Quoted(Template template)
    {
        return $"'{template.Value}'";
    }

    private static string Winner(RouteTable table, string path)
    {
        return WinnerOrNull(table, path) ?? throw new InvalidOperationException($"Nothing matches {path}.");
    }

    private static string? WinnerOrNull(RouteTable table, string path)
    {
        RouteMatchResult result = table.Match(new Uri(path, UriKind.Relative));
        return result.IsSuccess ? result.Definition.Template.Value : null;
    }

    private static RouteTable BuildTable(string[] templates)
    {
        var builder = new RouteTableBuilder();
        foreach (string template in templates)
            builder.Map(template, _ => new SettingsRoute());

        return builder.Build();
    }

    private static RouteTable Build(params (string Template, Type RouteType)[] mappings)
    {
        var builder = new RouteTableBuilder();
        foreach ((string template, Type routeType) in mappings)
        {
            if (routeType == typeof(SettingsRoute))
                builder.Map(template, _ => new SettingsRoute());
            else if (routeType == typeof(ProductRoute))
                builder.Map(template, _ => new ProductRoute("p"));
            else if (routeType == typeof(ProductByNameRoute))
                builder.Map(template, _ => new ProductByNameRoute("p"));
            else if (routeType == typeof(OrderRoute))
                builder.Map(template, _ => new OrderRoute("p", null));
            else
                builder.Map(template, _ => new FileRoute(null));
        }

        return builder.Build();
    }
}
