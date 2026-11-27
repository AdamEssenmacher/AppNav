extern alias OtherAssembly;

using System.Text;
using AdamE.AppNav.Routing;
using Xunit;
using OtherOrders = OtherAssembly::AdamE.AppNav.Tests.Routing.Orders;

namespace AdamE.AppNav.Tests.Routing;

// Build rejects a table that maps the same template twice, because the mapping made second could never be matched.
// Two templates are the same when their parsed segments are, position by position, ignoring parameter names.
public sealed class RouteTableDuplicateTemplateTests
{
    private const string SameTemplate = "are the same template";

    [Theory]
    [InlineData("/a/{x}", "/a/{y}")]
    [InlineData("/a/{id:int}", "/a/{n:INT}")]
    [InlineData("/caf%C3%A9", "/caf\u00E9")]
    [InlineData("/%41", "/A")]
    [InlineData("/a//b/", "/a/b")]
    [InlineData("/a/{x?}", "/a/{y?}")]
    [InlineData("/a/{x:alpha?}", "/a/{y:Alpha?}")]
    [InlineData("/files/{*rest}", "/files/{*path}")]
    [InlineData("/x%2Fy/{a}", "/x%2fy/{b}")]
    [InlineData("/", "/")]
    [InlineData("/", "//")]
    [InlineData("/settings", "/settings")]
    public void TheSameTemplateTwiceIsRejectedNamingBothInRegistrationOrder(string first, string second)
    {
        AssertRejected(first, second);
        AssertRejected(second, first);
    }

    [Theory]
    [InlineData("/Users", "/users")]
    [InlineData("/a/{id:int}", "/a/{id:alpha}")]
    [InlineData("/a/{id}", "/a/{id?}")]
    [InlineData("/a/{id}", "/a/{id:int}")]
    [InlineData("/a/{id:int}", "/a/{id:int?}")]
    [InlineData("/a/{rest?}", "/a/{*rest}")]
    [InlineData("/a/b", "/a/{b}")]
    [InlineData("/a", "/a/{b?}")]
    [InlineData("/a", "/a/{*rest}")]
    [InlineData("/caf%C3%A9", "/cafe%CC%81")]
    [InlineData("/a%2Fb", "/a/b")]
    public void TemplatesThatAreNotTheSameBuildInBothOrders(string first, string second)
    {
        Assert.Equal(2, Build((first, typeof(SettingsRoute)), (second, typeof(ProductRoute))).Definitions.Count);
        Assert.Equal(2, Build((second, typeof(SettingsRoute)), (first, typeof(ProductRoute))).Definitions.Count);
    }

    [Fact]
    public void TheSameTemplateMessageWritesANestedRouteTypeAsTheOtherMessagesDo()
    {
        AssertSameTemplateWrites<RouteHolder.NestedRoute>(RouteTypeNames.Nested);
    }

    [Fact]
    public void TheSameTemplateMessageWritesAGenericRouteTypeAsTheOtherMessagesDo()
    {
        AssertSameTemplateWrites<GenericRoute<int>>(RouteTypeNames.Generic);
    }

    [Fact]
    public void TheSameTemplateMessageWritesSameNamedRouteTypesOfTwoNamespacesWithTheirNamespaces()
    {
        string message = PairMessage<Orders.DetailRoute, Catalog.DetailRoute>();

        AssertInOrder(message, $"'{RouteTypeNames.OrdersDetail}'", $"'{RouteTypeNames.CatalogDetail}'");
    }

    [Fact]
    public void TheSameTemplateMessageWritesSameNamedNestedRouteTypesWithTheNamespaceOfTheirOuterTypes()
    {
        string message = PairMessage<Orders.Holder.Inner, Catalog.Holder.Inner>();

        AssertInOrder(message, $"'{RouteTypeNames.OrdersInner}'", $"'{RouteTypeNames.CatalogInner}'");
    }

    [Fact]
    public void TheSameTemplateMessageWritesARouteTypeOfTheGlobalNamespaceWithoutOne()
    {
        string message = PairMessage<global::DetailRoute, Orders.DetailRoute>();

        AssertInOrder(message, $"'{RouteTypeNames.GlobalDetail}'", $"'{RouteTypeNames.OrdersDetail}'");
    }

    [Fact]
    public void TheSameTemplateMessageWritesGenericRouteTypesThatOnlyTheirArgumentsTellApartInFull()
    {
        string message = PairMessage<GenericRoute<Orders.DetailRoute>, GenericRoute<Catalog.DetailRoute>>();

        AssertInOrder(
            message,
            $"'{RouteTypeNames.GenericOfOrdersDetail}'",
            $"'{RouteTypeNames.GenericOfCatalogDetail}'");
    }

    [Fact]
    public void TheSameTemplateMessageWritesRouteTypesOfTheSameFullNameWithTheirAssemblies()
    {
        string message = PairMessage<Orders.DetailRoute, OtherOrders.DetailRoute>();
        string reversed = PairMessage<OtherOrders.DetailRoute, Orders.DetailRoute>();

        AssertInOrder(
            message,
            $"'{RouteTypeNames.OrdersDetailOfTests}'",
            $"'{RouteTypeNames.OrdersDetailOfOtherAssembly}'");
        AssertInOrder(
            reversed,
            $"'{RouteTypeNames.OrdersDetailOfOtherAssembly}'",
            $"'{RouteTypeNames.OrdersDetailOfTests}'");
    }

    [Fact]
    public void TheSameTemplateMessageWritesTheSameRouteTypeTwiceWithoutItsNamespace()
    {
        string message = PairMessage<Orders.DetailRoute, Orders.DetailRoute>();

        AssertInOrder(message, "'DetailRoute'", "'DetailRoute'");
        Assert.DoesNotContain(RouteTypeNames.OrdersDetail, message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameTemplateForTheSameRouteTypeIsStillRejected()
    {
        var builder = new RouteTableBuilder()
            .Map("/settings", _ => new SettingsRoute())
            .Map("/settings", _ => new SettingsRoute());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(SameTemplate, exception.Message, StringComparison.Ordinal);
        AssertInOrder(exception.Message, "'/settings'", "'/settings'");
        AssertInOrder(exception.Message, Quoted(typeof(SettingsRoute)), Quoted(typeof(SettingsRoute)));
    }

    [Fact]
    public void TheFirstAndThirdOfThreeDefinitionsAreReportedWithoutTheSecond()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Build(
            ("/a/{x}", typeof(SettingsRoute)),
            ("/b", typeof(ProductRoute)),
            ("/a/{y}", typeof(FileRoute))));

        Assert.Contains(SameTemplate, exception.Message, StringComparison.Ordinal);
        AssertInOrder(exception.Message, "'/a/{x}'", "'/a/{y}'");
        AssertInOrder(exception.Message, Quoted(typeof(SettingsRoute)), Quoted(typeof(FileRoute)));
        Assert.DoesNotContain("'/b'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Quoted(typeof(ProductRoute)), exception.Message, StringComparison.Ordinal);
    }

    // The pair reported is the first definition, in registration order, that repeats an earlier template, with the
    // earliest it repeats: here the third definition repeats the second, before the fourth repeats the first.
    [Fact]
    public void TheFirstRepeatInRegistrationOrderIsReported()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Build(
            ("/a/{x}", typeof(SettingsRoute)),
            ("/b/{x}", typeof(ProductRoute)),
            ("/b/{y}", typeof(ProductByNameRoute)),
            ("/a/{y}", typeof(FileRoute))));

        AssertInOrder(exception.Message, "'/b/{x}'", "'/b/{y}'");
        AssertInOrder(exception.Message, Quoted(typeof(ProductRoute)), Quoted(typeof(ProductByNameRoute)));
        Assert.DoesNotContain("'/a/", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OfThreeSameTemplatesTheSecondIsReportedWithTheFirst()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Build(
            ("/a/{x}", typeof(SettingsRoute)),
            ("/a/{y}", typeof(ProductRoute)),
            ("/a/{z}", typeof(FileRoute))));

        AssertInOrder(exception.Message, "'/a/{x}'", "'/a/{y}'");
        AssertInOrder(exception.Message, Quoted(typeof(SettingsRoute)), Quoted(typeof(ProductRoute)));
        Assert.DoesNotContain("'/a/{z}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedBuildLeavesTheBuilderAsItWasSoBuildingAgainFailsTheSameWay()
    {
        var builder = new RouteTableBuilder()
            .Map("/a/{x}", _ => new SettingsRoute())
            .Map("/a/{y}", c => new ProductRoute(c.Path("y")));

        string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(builder.Build).Message);

        builder.Map("/b", _ => new SettingsRoute());
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(builder.Build).Message);
    }

    // A smoke test that a large table of distinct templates of one shape builds, through both the check for the same
    // template and the check for templates that cannot be told apart, which group these templates apart by their
    // literals. It asserts nothing about time.
    [Fact]
    public void ManyDistinctTemplatesOfOneShapeBuild()
    {
        var builder = new RouteTableBuilder();
        for (var i = 0; i < 20_000; i++)
            builder.Map($"/p/l{i}/{{id}}", _ => new SettingsRoute());

        Assert.Equal(20_000, builder.Build().Definitions.Count);
    }

    // Finding a repeated template stays linear in the number of templates only while distinct templates hash apart,
    // which no timing-free test of a build would show.
    [Fact]
    public void DistinctTemplatesOfOneShapeHaveDistinctHashes()
    {
        var hashes = new HashSet<int>();
        for (var i = 0; i < 10_000; i++)
            hashes.Add(SameTemplateComparer.Instance.GetHashCode(RouteTemplate.Parse($"/p/l{i}/{{id}}")));

        Assert.InRange(hashes.Count, 9_990, 10_000);
    }

    // The model describes each template as a list of (kind, key) pairs, where the key is a literal's decoded text or
    // a parameter's constraint in uppercase. The test writes the text from the model, so the model does not depend on
    // how the library parses or compares templates. A quarter of the templates repeat the model of an earlier one,
    // written differently, so that both outcomes are common. A table with no repeat can still have two templates that
    // rank equally and both match a path, which the model finds too, and which Build reports as that instead.
    [Fact]
    public void RandomTablesAreRejectedExactlyWhenTheModelFindsTheSameTemplateTwice()
    {
        var random = new Random(20261125);
        int rejected = 0, ambiguous = 0;
        for (var iteration = 0; iteration < 3000; iteration++)
        {
            var templates = new (string Value, (char Kind, string Key)[] Model)[random.Next(1, 6)];
            for (var i = 0; i < templates.Length; i++)
            {
                (char Kind, string Key)[] model =
                    i > 0 && random.Next(4) == 0 ? templates[random.Next(i)].Model : RandomModel(random);
                templates[i] = (Write(random, model), model);
            }

            (int Earlier, int Later)? expected = FirstRepeat(templates, IsSameModel);
            var builder = new RouteTableBuilder();
            foreach ((string value, _) in templates)
                builder.Map(value, _ => new SettingsRoute());

            string context = string.Join(' ', templates.Select(t => t.Value));
            if (expected is not (int earlier, int later))
            {
                if (FirstRepeat(templates, IsAmbiguousModel) is (int first, int second))
                {
                    ambiguous++;
                    string message = Assert.Throws<InvalidOperationException>(builder.Build).Message;
                    Assert.True(message.Contains("rank equally and both match", StringComparison.Ordinal), context);
                    AssertInOrder(message, $"'{templates[first].Value}'", $"'{templates[second].Value}'");
                }
                else
                {
                    Assert.Equal(templates.Length, builder.Build().Definitions.Count);
                }

                continue;
            }

            rejected++;
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);
            Assert.True(exception.Message.Contains(SameTemplate, StringComparison.Ordinal), context);
            AssertInOrder(exception.Message, $"'{templates[earlier].Value}'", $"'{templates[later].Value}'");
        }

        Assert.InRange(rejected, 600, 2400);
        Assert.InRange(ambiguous, 5, 200);
    }

    // Half the pairs share a model, written differently, and the other half are drawn independently.
    [Fact]
    public void TemplatesTheModelCallsTheSameAreEqualHashEquallyAndRankEqually()
    {
        var random = new Random(1125);
        var same = 0;
        for (var iteration = 0; iteration < 20_000; iteration++)
        {
            (char Kind, string Key)[] firstModel = RandomModel(random);
            (char Kind, string Key)[] secondModel = random.Next(2) == 0 ? firstModel : RandomModel(random);
            RouteTemplate first = RouteTemplate.Parse(Write(random, firstModel));
            RouteTemplate second = RouteTemplate.Parse(Write(random, secondModel));
            string context = $"{first.Value} {second.Value}";

            bool expected = firstModel.SequenceEqual(secondModel);
            Assert.True(expected == SameTemplateComparer.Instance.Equals(first, second), context);
            Assert.True(expected == SameTemplateComparer.Instance.Equals(second, first), context);
            if (!expected)
                continue;

            same++;
            Assert.True(
                SameTemplateComparer.Instance.GetHashCode(first) == SameTemplateComparer.Instance.GetHashCode(second),
                context);
            Assert.True(first.ComparePrecedence(second) == 0, context);
        }

        Assert.InRange(same, 8000, 12_000);
    }

    // Up to three literals and required parameters, then nothing, one or two optional parameters, or a catch-all.
    private static (char Kind, string Key)[] RandomModel(Random random)
    {
        string[] literals = ["a", "A", "b", "caf\u00E9", "cafe\u0301", "x/y"];
        string[] constraints = ["", "INT", "LONG", "ALPHA"];
        var model = new List<(char Kind, string Key)>();
        int positionalCount = random.Next(0, 4);
        for (var i = 0; i < positionalCount; i++)
        {
            model.Add(random.Next(2) == 0
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

    // Writes a model as template text with random names, case of constraint names, encoding and slashes.
    private static string Write(Random random, (char Kind, string Key)[] model)
    {
        var spellings = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["a"] = ["a", "%61"],
            ["A"] = ["A", "%41"],
            ["b"] = ["b", "%62"],
            ["caf\u00E9"] = ["caf\u00E9", "caf%C3%A9", "caf%c3%a9"],
            ["cafe\u0301"] = ["cafe\u0301", "cafe%CC%81"],
            ["x/y"] = ["x%2Fy", "x%2fy"],
        };
        var value = new StringBuilder();
        for (var i = 0; i < model.Length; i++)
        {
            (char kind, string key) = model[i];
            string name = $"{(random.Next(2) == 0 ? "n" : "v")}{i}";
            string constraint = key.Length == 0 ? "" : ":" + (random.Next(2) == 0 ? key : key.ToLowerInvariant());
            string[] literal = kind == 'L' ? spellings[key] : [];
            value.Append(random.Next(4) == 0 ? "//" : "/").Append(kind switch
            {
                'L' => literal[random.Next(literal.Length)],
                'R' => $"{{{name}{constraint}}}",
                'O' => $"{{{name}{constraint}?}}",
                _ => $"{{*{name}}}",
            });
        }

        if (random.Next(4) == 0)
            value.Append('/');

        return value.Length == 0 ? "/" : value.ToString();
    }

    // The first template, in registration order, whose model conflicts with an earlier one's, with the earliest such
    // one.
    private static (int Earlier, int Later)? FirstRepeat(
        (string Value, (char Kind, string Key)[] Model)[] templates,
        Func<(char Kind, string Key)[], (char Kind, string Key)[], bool> conflict)
    {
        for (var later = 0; later < templates.Length; later++)
        {
            for (var earlier = 0; earlier < later; earlier++)
            {
                if (conflict(templates[earlier].Model, templates[later].Model))
                    return (earlier, later);
            }
        }

        return null;
    }

    private static bool IsSameModel((char Kind, string Key)[] x, (char Kind, string Key)[] y)
    {
        return x.SequenceEqual(y);
    }

    // Two models that rank equally and both match a path: the same kinds and literals, parameters either both
    // constrained or both not, and at every required position the same constraint, or INT and LONG, which both
    // accept 0. ALPHA accepts no number.
    private static bool IsAmbiguousModel((char Kind, string Key)[] x, (char Kind, string Key)[] y)
    {
        if (x.Length != y.Length)
            return false;

        for (var i = 0; i < x.Length; i++)
        {
            if (x[i].Kind != y[i].Kind || (x[i].Key.Length == 0) != (y[i].Key.Length == 0))
                return false;

            bool numbers = x[i].Key is "INT" or "LONG" && y[i].Key is "INT" or "LONG";
            if (x[i].Kind is 'L' or 'R' && x[i].Key != y[i].Key && !numbers)
                return false;
        }

        return true;
    }

    private static void AssertRejected(string first, string second)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Build((first, typeof(SettingsRoute)), (second, typeof(ProductRoute))));

        Assert.Contains(SameTemplate, exception.Message, StringComparison.Ordinal);
        AssertInOrder(exception.Message, $"'{first}'", $"'{second}'");
        AssertInOrder(exception.Message, Quoted(typeof(SettingsRoute)), Quoted(typeof(ProductRoute)));
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

    private static void AssertSameTemplateWrites<TRoute>(string name)
        where TRoute : AppRoute, new()
    {
        var builder = new RouteTableBuilder()
            .Map("/settings", _ => new TRoute())
            .Map("/settings", _ => new TRoute());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(SameTemplate, exception.Message, StringComparison.Ordinal);
        RouteTypeNames.AssertWrittenAsInTheUnboundConstraintError<TRoute>(exception.Message, name);
    }

    // The message for two mappings of different route types, made so that Build rejects them.
    private static string PairMessage<TFirst, TSecond>()
        where TFirst : AppRoute, new()
        where TSecond : AppRoute, new()
    {
        var builder = new RouteTableBuilder()
            .Map("/settings", _ => new TFirst())
            .Map("/settings", _ => new TSecond());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(SameTemplate, exception.Message, StringComparison.Ordinal);
        return exception.Message;
    }

    private static string Quoted(Type routeType)
    {
        return $"'{RouteValueTypes.DisplayName(routeType)}'";
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
            else
                builder.Map(template, _ => new FileRoute(null));
        }

        return builder.Build();
    }
}
