using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTableFormatTests
{
    private const string Required = "is required, but its value is null";
    private const string Empty = "has an empty value";
    private const string Constraint = "does not satisfy its constraint";
    private const string DotSegment = "dot segment";
    private const string EmptyPart = "empty part";
    private const string LoneSurrogate = "lone surrogate";
    private const string LeftOut = "earlier optional parameter";
    private const string NoText = "has a value that cannot be written:";
    private const string NotMapped = "no mapping of exactly that type exists";
    private const string NoFormat = "none of its mappings has a format function";
    private const string CapturedFirst = "is matched first by the template";

    // Text that stands for a secret a route can carry, distinctive enough that finding it in a message is not a
    // coincidence.
    private const string Secret = "tok-Vn3Jq8Rw-9Wq4Lm7Tz";

    [Fact]
    public void ARouteIsWrittenAsThePathOfItsTemplate()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/orders/{id:int}", c => new OrderRoute(c.Path<int>("id")), format => format.Path("id", r => r.Id))
            .Build();

        Assert.Equal("/orders/42", table.Format(new OrderRoute(42)));
    }

    [Fact]
    public void ANullRouteIsRejected()
    {
        RouteTable table = new RouteTableBuilder().Build();

        Assert.Equal("route", Assert.Throws<ArgumentNullException>(() => table.Format(null!)).ParamName);
    }

    [Fact]
    public void ARouteTypeWithNoFormatFunctionIsRejected()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/orders/{id:int}", c => new OrderRoute(c.Path<int>("id")))
            .Build();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => table.Format(new OrderRoute(1)));
        Assert.Contains($"{nameof(OrderRoute)}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(NoFormat, exception.Message, StringComparison.Ordinal);
        Assert.Contains("_ => { }", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARouteTypeThatIsNotMappedIsRejected()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new RouteTableBuilder().Build().Format(new OrderRoute(1)));

        Assert.Contains($"{nameof(OrderRoute)}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(NotMapped, exception.Message, StringComparison.Ordinal);
    }

    // The mapping is found by the exact runtime type, so a route of a derived type is not written by its base type's
    // mapping, however the variable that holds it is typed.
    [Fact]
    public void OnlyAMappingOfTheExactTypeWritesARoute()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/base/{id}", c => new BaseRoute(c.Path("id")), f => f.Path("id", r => r.Id))
            .Build();
        BaseRoute derived = new DerivedRoute("a");

        Assert.Equal("/base/a", table.Format(new BaseRoute("a")));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => table.Format(derived));
        Assert.Contains($"{nameof(DerivedRoute)}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(NotMapped, exception.Message, StringComparison.Ordinal);
        Assert.Contains("base type", exception.Message, StringComparison.Ordinal);
    }

    // A value function after one whose value has no text, or one that throws, is not called. The template's checks
    // run on the texts afterwards, so a value the template rejects is read only after every function has run.
    [Fact]
    public void LaterValueFunctionsAreNotCalledAfterAValueThatCannotBeWritten()
    {
        var calls = new List<string>();
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Access>()
            .Map("/f/{a}/{b}/{c?}/{d?}", _ => new CallsRoute("a", double.NaN, Access.Read),
                f => f
                    .Path("a", r => Log(calls, "a", r.A))
                    .Path("b", r =>
                    {
                        calls.Add("b");
                        return r.B;
                    })
                    .Path("c", r =>
                    {
                        calls.Add("c");
                        return r.C;
                    })
                    .Path("d", r => Log(calls, "d", r.A)))
            .Build();

        Assert.Throws<ArgumentException>(() => table.Format(new CallsRoute("a", double.NaN, Access.Read)));
        Assert.Equal(["a", "b"], calls);

        calls.Clear();
        Assert.Throws<ArgumentException>(() => table.Format(new CallsRoute("a", 1, Access.Read | Access.Write)));
        Assert.Equal(["a", "b", "c"], calls);

        calls.Clear();
        Assert.Throws<ArgumentException>(() => table.Format(new CallsRoute("", 1, Access.Read)));
        Assert.Equal(["a", "b", "c", "d"], calls);

        calls.Clear();
        Assert.Equal("/f/a/1/Read/a", table.Format(new CallsRoute("a", 1, Access.Read)));
        Assert.Equal(["a", "b", "c", "d"], calls);
    }

    [Fact]
    public void LaterValueFunctionsAreNotCalledAfterOneThrows()
    {
        var calls = new List<string>();
        RouteTable table = new RouteTableBuilder()
            .Map("/f/{a}/{b}/{c?}/{d?}", _ => new OptionalRoute("b", "c"), f => f
                .Path("a", r => Log(calls, "a", r.B))
                .Path<string>("b", _ =>
                {
                    calls.Add("b");
                    throw new InvalidOperationException("from the app");
                })
                .Path("c", r => Log(calls, "c", r.C))
                .Path("d", r => Log(calls, "d", r.C)))
            .Build();

        Assert.Throws<InvalidOperationException>(() => table.Format(new OptionalRoute("x", "y")));
        Assert.Equal(["a", "b"], calls);
    }

    [Fact]
    public void OptionalParametersAreWrittenWhenPresentAndLeftOutWhenNull()
    {
        RouteTable table = OptionalTable();

        Assert.Equal("/a", table.Format(new OptionalRoute(null, null)));
        Assert.Equal("/a/x", table.Format(new OptionalRoute("x", null)));
        Assert.Equal("/a/x/y", table.Format(new OptionalRoute("x", "y")));
    }

    // An optional value cannot be written after one that is left out, because its segment would take the earlier
    // one's position.
    [Fact]
    public void AnOptionalValueAfterOneThatIsLeftOutIsRejected()
    {
        AssertNotWritten(OptionalTable(), new OptionalRoute(null, "y"), "/a/{b?}/{c?}", "c", LeftOut);
    }

    [Fact]
    public void ANullableValueTypeWithoutAValueLeavesAnOptionalParameterOut()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/list/{page:int?}", c => new PageRoute(c.TryGetPath("page", out int? page) ? page : null),
                f => f.Path("page", r => r.Page))
            .Build();

        Assert.Equal("/list", table.Format(new PageRoute(null)));
        Assert.Equal("/list/3", table.Format(new PageRoute(3)));
        AssertRoundTrip(table, new PageRoute(null));
        AssertRoundTrip(table, new PageRoute(3));
    }

    [Fact]
    public void ANullRequiredValueIsRejected()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/items/{id}", c => new TextRoute(c.Path("id")), f => f.Path("id", r => r.Text))
            .Map("/pages/{n:int}", c => new PageRoute(c.Path<int>("n")), f => f.Path("n", r => r.Page))
            .Build();

        AssertNotWritten(table, new TextRoute(null), "/items/{id}", "id", Required);
        AssertNotWritten(table, new PageRoute(null), "/pages/{n:int}", "n", Required);
    }

    // An empty string is not null, so it does not leave a parameter out, and a path segment cannot be empty.
    [Fact]
    public void AnEmptyValueIsRejectedEvenForAnOptionalParameter()
    {
        AssertNotWritten(OptionalTable(), new OptionalRoute("", null), "/a/{b?}/{c?}", "b", Empty);
        AssertNotWritten(TextTable("/items/{id}"), new TextRoute(""), "/items/{id}", "id", Empty);
        AssertNotWritten(FileTable(), new TextRoute(""), "/files/{*path}", "path", Empty);
    }

    [Theory]
    [InlineData("a/b/c", "/files/a/b/c")]
    [InlineData("a b/c+d", "/files/a%20b/c%2Bd")]
    [InlineData("a", "/files/a")]
    [InlineData("...", "/files/...")]
    [InlineData("a%2Fb", "/files/a%252Fb")]
    public void ACatchAllValueIsWrittenAsSegments(string value, string expected)
    {
        RouteTable table = FileTable();

        Assert.Equal(expected, table.Format(new TextRoute(value)));
        AssertRoundTrip(table, new TextRoute(value));
    }

    [Fact]
    public void ANullCatchAllValueIsLeftOut()
    {
        Assert.Equal("/files", FileTable().Format(new TextRoute(null)));
        AssertRoundTrip(FileTable(), new TextRoute(null));
    }

    [Theory]
    [InlineData("a//b", EmptyPart)]
    [InlineData("/a", EmptyPart)]
    [InlineData("a/", EmptyPart)]
    [InlineData("/", EmptyPart)]
    [InlineData("a/../b", DotSegment)]
    [InlineData("./a", DotSegment)]
    [InlineData("..", DotSegment)]
    public void ACatchAllValueThatCannotBeReadBackIsRejected(string value, string category)
    {
        AssertNotWritten(FileTable(), new TextRoute(value), "/files/{*path}", "path", category);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void ADotSegmentValueIsRejected(string value)
    {
        AssertNotWritten(TextTable("/items/{id}"), new TextRoute(value), "/items/{id}", "id", DotSegment);
    }

    [Fact]
    public void AValueWithALoneSurrogateIsRejected()
    {
        AssertNotWritten(TextTable("/items/{id}"), new TextRoute("a\uD800"), "/items/{id}", "id", LoneSurrogate);
    }

    // A value of one type can be written into a parameter constrained to another, and then its text must satisfy the
    // constraint, or the path would not match.
    [Fact]
    public void AValueThatDoesNotSatisfyTheConstraintIsRejected()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/orders/{id:int}", c => new LongRoute(c.Path<long>("id")), f => f.Path("id", r => r.Value))
            .Map("/names/{n:alpha}", c => new TextRoute(c.Path("n")), f => f.Path("n", r => r.Text))
            .Map("/keys/{k:guid}", c => new GuidTextRoute(c.Path("k")), f => f.Path("k", r => r.Text))
            .Build();

        Assert.Equal("/orders/2147483647", table.Format(new LongRoute(int.MaxValue)));
        AssertNotWritten(table, new LongRoute(int.MaxValue + 1L), "/orders/{id:int}", "id", Constraint + " 'int'");
        AssertNotWritten(table, new TextRoute("ab1"), "/names/{n:alpha}", "n", Constraint + " 'alpha'");

        // The guid constraint's hint about hyphens is left out, because whether it applies says something about the
        // value.
        ArgumentException exception = AssertNotWritten(
            table, new GuidTextRoute(new string('a', 32)), "/keys/{k:guid}", "k", Constraint + " 'guid'");
        Assert.DoesNotContain("hyphens", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFloatingPointValueThatIsNotFiniteHasNoText()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/d/{v}", c => new DoubleRoute(c.Path<double>("v")), f => f.Path("v", r => r.Value))
            .Map("/f/{v}", c => new FloatRoute(c.Path<float>("v")), f => f.Path("v", r => r.Value))
            .Build();

        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            AssertNotWritten(table, new DoubleRoute(value), "/d/{v}", "v", NoText);
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            AssertNotWritten(table, new FloatRoute(value), "/f/{v}", "v", NoText);
    }

    [Fact]
    public void AnEnumValueWithNoMemberHasNoText()
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Access>()
            .Map("/access/{a}", c => new AccessRoute(c.Path<Access>("a")), f => f.Path("a", r => r.Access))
            .Build();

        Assert.Equal("/access/Read", table.Format(new AccessRoute(Access.Read)));
        AssertNotWritten(table, new AccessRoute(Access.Read | Access.Write), "/access/{a}", "a", NoText);
        AssertNotWritten(table, new AccessRoute((Access)8), "/access/{a}", "a", NoText);
    }

    // Each value function is called once, in the order of the template, not in the order it was declared.
    [Fact]
    public void EachValueFunctionIsCalledOnceInTemplateOrder()
    {
        var calls = new List<string>();
        RouteTable table = new RouteTableBuilder()
            .Map("/{a}/x/{b}/{c?}", _ => new OptionalRoute("b", "c"), f => f
                .Path("c", r => Log(calls, "c", r.C))
                .Path("a", _ => Log(calls, "a", "1"))
                .Path("b", r => Log(calls, "b", r.B)))
            .Build();

        Assert.Equal("/1/x/2/3", table.Format(new OptionalRoute("2", "3")));
        Assert.Equal(["a", "b", "c"], calls);
    }

    // An exception from the app's value function propagates unchanged, and no later value function is called.
    [Fact]
    public void AnExceptionFromAValueFunctionPropagatesUnchanged()
    {
        var thrown = new InvalidOperationException("from the app");
        var later = false;
        RouteTable table = new RouteTableBuilder()
            .Map("/{a}/{b}", _ => new OptionalRoute("a", "b"), f => f
                .Path<string>("a", _ => throw thrown)
                .Path("b", r =>
                {
                    later = true;
                    return r.C;
                }))
            .Build();

        Assert.Same(thrown, Assert.Throws<InvalidOperationException>(() => table.Format(new OptionalRoute("a", "b"))));
        Assert.False(later);
    }

    // The path is written by the template's own Format, so it is escaped exactly as that writes it.
    [Theory]
    [InlineData("a b", "/items/a%20b")]
    [InlineData("a+b", "/items/a%2Bb")]
    [InlineData("100%", "/items/100%25")]
    [InlineData("a/b", "/items/a%2Fb")]
    [InlineData("\u00E9t\u00E9", "/items/%C3%A9t%C3%A9")]
    [InlineData("\U0001D49C", "/items/%F0%9D%92%9C")]
    [InlineData("why?", "/items/why%3F")]
    [InlineData("#1", "/items/%231")]
    [InlineData("a\tb", "/items/a%09b")]
    [InlineData("...", "/items/...")]
    public void AStringIsWrittenEscaped(string value, string expected)
    {
        RouteTable table = TextTable("/items/{id}");

        Assert.Equal(expected, table.Format(new TextRoute(value)));
        var values = new Dictionary<string, string> { ["id"] = value };
        Assert.Equal(expected, RouteTemplate.Parse("/items/{id}").Format(values));
        AssertRoundTrip(table, new TextRoute(value));
    }

    public static TheoryData<string, bool> CaptureFamilies
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (string family in CaptureCases.Keys)
            {
                data.Add(family, true);
                data.Add(family, false);
            }

            return data;
        }
    }

    // Each family is a way a template that ranks higher can match the path another writes: a literal equal to the
    // value, a constraint the value satisfies, a shorter template for a left-out parameter, and a longer one for a
    // catch-all value. The value next to it, which only the writer matches, still round-trips. The capturing mapping
    // has no format function, and the order of the Map calls makes no difference.
    [Theory]
    [MemberData(nameof(CaptureFamilies))]
    public void APathThatAHigherRankingTemplateMatchesFirstIsNotWritten(string family, bool capturingFirst)
    {
        CaptureCase capture = CaptureCases[family];
        RouteTable table = CaptureTable(capture, capturingFirst);

        AssertCaptured(table, capture.Captured, capture.Writer, capture.Capturing);
        string path = table.Format(capture.Neighbour);
        RouteMatchResult result = Match(table, path);
        Assert.Equal(capture.Writer, result.Definition!.Template.Value);
        Assert.Equal(capture.Neighbour, result.Route);
    }

    // A capturing mapping that can itself write routes refuses the path just as one that cannot.
    [Fact]
    public void ACapturingMappingWithAFormatFunctionAlsoCaptures()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/p/{name}", c => new TextRoute(c.Path("name")), f => f.Path("name", r => r.Text))
            .Map("/p/{id:int}", c => new OrderRoute(c.Path<int>("id")), f => f.Path("id", r => r.Id))
            .Build();

        AssertCaptured(table, new TextRoute("42"), "/p/{name}", "/p/{id:int}");
        AssertRoundTrip(table, new OrderRoute(42));
        AssertRoundTrip(table, new TextRoute("43x"));
    }

    // The template that captures is the first in match order, wherever it was mapped among the others.
    [Fact]
    public void TheFirstCapturingTemplateOfAHundredIsNamed()
    {
        var builder = new RouteTableBuilder();
        for (var i = 0; i < 50; i++)
            builder.Map($"/products/x{i}", _ => new SettingsRoute());

        builder.Map("/products/{name}", c => new TextRoute(c.Path("name")), f => f.Path("name", r => r.Text));
        builder.Map("/products/{id:int}", _ => new SettingsRoute());
        for (var i = 50; i < 98; i++)
            builder.Map($"/products/x{i}", _ => new SettingsRoute());

        RouteTable table = builder.Build();

        Assert.Equal(100, table.Definitions.Count);
        AssertCaptured(table, new TextRoute("x0"), "/products/{name}", "/products/x0");
        AssertCaptured(table, new TextRoute("x97"), "/products/{name}", "/products/x97");
        AssertCaptured(table, new TextRoute("7"), "/products/{name}", "/products/{id:int}");
        AssertRoundTrip(table, new TextRoute("x98"));
        AssertRoundTrip(table, new TextRoute("X0"));
    }

    // The template that matches first decides, even when its create function then rejects the value: the link
    // opens no route, so the message says which template matches and not which route opens.
    [Fact]
    public void ATemplateWhoseCreateFunctionRejectsTheValueStillCaptures()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/p/{id:int}", c =>
            {
                int id = c.Path<int>("id");
                return id > 1000 ? throw new RouteValueException("id") : new OrderRoute(id);
            })
            .Map("/p/{name}", c => new TextRoute(c.Path("name")), f => f.Path("name", r => r.Text))
            .Build();

        ArgumentException exception = AssertCaptured(table, new TextRoute("5000"), "/p/{name}", "/p/{id:int}");

        Assert.DoesNotContain("open", exception.Message, StringComparison.Ordinal);
        RouteMatchResult result = table.Match(new Uri("/p/5000", UriKind.Relative));
        Assert.False(result.IsSuccess);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, Assert.Single(result.Diagnostics).Code);
    }

    // A token that happens to be a number is captured by a sibling constrained to numbers. The message names the two
    // templates only: not the token, and not the path it would have been written as.
    [Fact]
    public void ACapturedValueIsNeverQuoted()
    {
        const string NumericSecret = "1938475629";
        RouteTable table = new RouteTableBuilder()
            .Map("/t/{token}", c => new SecretTextRoute(c.Path("token")), f => f.Path("token", r => r.Text))
            .Map("/t/{id:int}", _ => new SettingsRoute())
            .Build();

        ArgumentException exception = AssertCaptured(
            table, new SecretTextRoute(NumericSecret), "/t/{token}", "/t/{id:int}");

        Assert.DoesNotContain(NumericSecret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("/t/1", exception.ToString(), StringComparison.Ordinal);
    }

    // The path is checked once it is written, so each value function has been called once, and no more.
    [Fact]
    public void ValueFunctionsAreCalledOnceWhenThePathIsCaptured()
    {
        var calls = new List<string>();
        RouteTable table = new RouteTableBuilder()
            .Map("/c/x/{n:int}", _ => new SettingsRoute())
            .Map("/c/{a}/{b}", c => new TwoRoute(c.Path("a"), c.Path("b")), f => f
                .Path("b", r => Log(calls, "b", r.B))
                .Path("a", r => Log(calls, "a", r.A)))
            .Build();

        AssertCaptured(table, new TwoRoute("x", "1"), "/c/{a}/{b}", "/c/x/{n:int}");
        Assert.Equal(["a", "b"], calls);
    }

    // For generated tables of templates that share prefixes, every path Format returns selects, through Match, the
    // mapping that wrote it and an equal route, and every route it refuses is one whose path, as the template alone
    // writes it, Match gives to another mapping.
    [Fact]
    public void FormatReturnsExactlyThePathsThatMatchSelectsTheWriterFor()
    {
        var random = new Random(20261125);
        int written = 0, captured = 0, rejected = 0, tables = 0;
        for (var attempt = 0; tables < 300 && attempt < 10_000; attempt++)
        {
            int count = random.Next(2, GeneratedMakers.Length + 1);
            var templates = new string[count];
            var parameters = new int[count];
            var writes = new bool[count];
            var builder = new RouteTableBuilder();
            for (var i = 0; i < count; i++)
            {
                templates[i] = GenerateTemplate(random, out parameters[i]);
                writes[i] = random.Next(4) > 0;
                MapGenerated(builder, i, templates[i], parameters[i], writes[i]);
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
                if (!writes[i])
                    continue;

                for (var n = 0; n < 12; n++)
                {
                    string?[] values = new string?[3];
                    for (var k = 0; k < parameters[i]; k++)
                        values[k] = GeneratedValues[random.Next(GeneratedValues.Length)];

                    GenRoute route = GeneratedMakers[i](values[0], values[1], values[2]);
                    RouteDefinition writer = table.Definitions[i];
                    string path;
                    try
                    {
                        path = table.Format(route);
                    }
                    catch (ArgumentException exception) when (exception.Message.Contains(
                        CapturedFirst, StringComparison.Ordinal))
                    {
                        var pathValues = new Dictionary<string, string>();
                        for (var k = 0; k < parameters[i]; k++)
                        {
                            if (values[k] is { } value)
                                pathValues.Add($"p{k}", value);
                        }

                        RouteMatchResult other = Match(table, writer.Template.Format(pathValues));
                        Assert.True(other.IsSuccess);
                        Assert.NotSame(writer, other.Definition);
                        Assert.Contains(
                            $"'{other.Definition!.Template.Value}'",
                            exception.Message,
                            StringComparison.Ordinal);
                        captured++;
                        continue;
                    }
                    catch (ArgumentException)
                    {
                        rejected++;
                        continue;
                    }

                    RouteMatchResult result = Match(table, path);
                    Assert.True(result.IsSuccess, path);
                    Assert.Same(writer, result.Definition);
                    Assert.Equal(route, result.Route);
                    written++;
                }
            }
        }

        // Values that cannot be written at all, such as a null required value or text a constraint rejects, test
        // neither side, so they are counted to keep them from crowding out the paths that do.
        string budget = $"written {written}, captured {captured}, not writable {rejected}";
        Assert.True(tables == 300, budget);
        Assert.True(written >= 1000, budget);
        Assert.True(captured >= 300, budget);
        Assert.True(rejected < written + captured, budget);
    }

    // A table where no template ranks above the writer does not look for one, so it allocates what a table of the
    // writer alone does.
    [Fact]
    public void FormatAllocatesNoMoreWhenNothingRanksAboveTheWriter()
    {
        RouteTable one = TextTable("/products/{id}");
        var builder = new RouteTableBuilder();
        for (var i = 0; i < 99; i++)
            builder.Map($"/{{a}}/x{i}", _ => new SettingsRoute());

        RouteTable hundred = builder
            .Map("/products/{id}", c => new TextRoute(c.Path("id")), f => f.Path("id", r => r.Text))
            .Build();
        var route = new TextRoute("widget");

        long oneBytes = AllocatedBytesPerFormat(one, route);
        long hundredBytes = AllocatedBytesPerFormat(hundred, route);

        Assert.InRange(hundredBytes - oneBytes, long.MinValue, 8);
    }

    // A walk over 99 templates that rank above the writer and do not match allocates nothing for each: only the one
    // split of the written path, which a table with a single such template makes too.
    [Fact]
    public void FormatAllocatesNoMoreForEachTemplateThatRanksAboveTheWriter()
    {
        RouteTable one = AboveTable(1);
        RouteTable hundred = AboveTable(99);
        var route = new TextRoute("widget");

        Assert.Equal(99, hundred.MatchOrder.IndexOf(hundred.Definitions[^1]));
        long oneBytes = AllocatedBytesPerFormat(one, route);
        long hundredBytes = AllocatedBytesPerFormat(hundred, route);

        Assert.InRange(hundredBytes - oneBytes, long.MinValue, 8);
    }

    // A catch-all ranks above /{a}/{b} at its first position, but does not match a path whose captured part is a dot
    // segment once decoded, so the writer is the one Match selects and the path is written.
    [Fact]
    public void AHigherRankingCatchAllDoesNotCaptureAPathWithADotSegmentPart()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/f/{*rest}", _ => new SettingsRoute())
            .Map("/{a}/{b}", c => new TwoRoute(c.Path("a"), c.Path("b")),
                f => f.Path("a", r => r.A).Path("b", r => r.B))
            .Build();

        Assert.Equal("/f/x%2F..", table.Format(new TwoRoute("f", "x/..")));
        AssertRoundTrip(table, new TwoRoute("f", "x/.."));
        AssertCaptured(table, new TwoRoute("f", "x"), "/{a}/{b}", "/f/{*rest}");
    }

    // System.Uri drops %20 sequences at the very end of an absolute URI that contains non-ASCII text, so a value that
    // ends with a space and is written last reads back without it from an absolute link, and here matches another
    // template. The written text, matched as a relative URI, still selects the writer. This pins that known limit of
    // reading an absolute URI, so that a change to how the path is read notices it.
    [Fact]
    public void AnAbsoluteLinkLosesATrailingSpaceThatTheRelativeTextKeeps()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/\u00E9/new", _ => new SettingsRoute())
            .Map("/\u00E9/{name}", c => new TextRoute(c.Path("name")), f => f.Path("name", r => r.Text))
            .Build();
        RouteTable alone = new RouteTableBuilder()
            .Map("/\u00E9/{name}", c => new TextRoute(c.Path("name")), f => f.Path("name", r => r.Text))
            .Build();

        string path = table.Format(new TextRoute("new "));

        Assert.Equal("/%C3%A9/new%20", path);
        Assert.Equal(new TextRoute("new "), Match(table, path).Route);
        Assert.Equal(new SettingsRoute(), table.Match(new Uri("https://app" + path)).Route);
        Assert.Equal(new TextRoute("new"), alone.Match(new Uri("https://app" + path)).Route);
    }

    [Fact]
    public void FormatDoesNotChangeTheTable()
    {
        RouteTable table = OptionalTable();
        RouteDefinition[] before = [.. table.Definitions];

        table.Format(new OptionalRoute("x", "y"));
        Assert.Throws<ArgumentException>(() => table.Format(new OptionalRoute(null, "y")));

        Assert.Equal(before, table.Definitions);
        Assert.Equal(new OptionalRoute("x", "y"), Match(table, "/a/x/y").Route);
    }

    [Fact]
    public void FormatAndMatchCanRunOnSeveralThreadsAtOnce()
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Access>()
            .Map("/orders/{id:int}", c => new OrderRoute(c.Path<int>("id")), f => f.Path("id", r => r.Id))
            .Map("/access/{a}", c => new AccessRoute(c.Path<Access>("a")), f => f.Path("a", r => r.Access))
            .Map("/d/{v}", c => new DoubleRoute(c.Path<double>("v")), f => f.Path("v", r => r.Value))
            .Build();

        Parallel.For(0, 20_000, i =>
        {
            AppRoute route = (i % 3) switch
            {
                0 => new OrderRoute(i),
                1 => new AccessRoute(i % 2 == 0 ? Access.Read : Access.Write),
                _ => new DoubleRoute(i / 7.0),
            };

            string path = table.Format(route);
            Assert.Equal(route, Match(table, path).Route);
        });
    }

    public static TheoryData<string, AppRoute, string> SecretFailures => new()
    {
        { "constraint", new TextRoute(Secret), "/c/{v:int}" },
        { "lone surrogate", new SecretTextRoute(Secret + "\uD800"), "/s/{v}" },
        { "empty part", new FileSecretRoute(Secret + "//x"), "/f/{*v}" },
        { "dot segment part", new FileSecretRoute(Secret + "/../x"), "/f/{*v}" },
        { "left out", new OptionalRoute(null, Secret), "/a/{b?}/{c?}" },
        { "enum", new AccessRoute((Access)987654321), "/e/{v}" },
        { "infinity", new DoubleRoute(double.PositiveInfinity), "/d/{v}" },
    };

    // A route value can be a secret, such as a sign-in token, so no failure quotes it anywhere in the exception.
    [Theory]
    [MemberData(nameof(SecretFailures))]
    public void AValueThatCannotBeWrittenIsNeverQuoted(string category, AppRoute route, string template)
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Access>()
            .Map("/c/{v:int}", c => new TextRoute(c.Path("v")), f => f.Path("v", r => r.Text))
            .Map("/s/{v}", c => new SecretTextRoute(c.Path("v")), f => f.Path("v", r => r.Text))
            .Map("/f/{*v}", c => new FileSecretRoute(c.Path("v")), f => f.Path("v", r => r.Path))
            .Map("/a/{b?}/{c?}", _ => new OptionalRoute(null, null), f => f.Path("b", r => r.B).Path("c", r => r.C))
            .Map("/e/{v}", c => new AccessRoute(c.Path<Access>("v")), f => f.Path("v", r => r.Access))
            .Map("/d/{v}", c => new DoubleRoute(c.Path<double>("v")), f => f.Path("v", r => r.Value))
            .Build();
        ArgumentException exception = Assert.Throws<ArgumentException>(() => table.Format(route));

        Assert.Equal("route", exception.ParamName);
        Assert.Contains($"'{template}'", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
        foreach (string quoted in new[] { Secret, "987654321", "Infinity", "\u221E" })
        {
            Assert.DoesNotContain(quoted, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(quoted, exception.ToString(), StringComparison.Ordinal);
        }

        Assert.False(string.IsNullOrEmpty(category));
    }

    // The families of capture, each a writer and a template ranked above it that matches what the writer writes for
    // one value but not for the value next to it.
    private static readonly Dictionary<string, CaptureCase> CaptureCases = new()
    {
        ["literal"] = Text("/products/{name}", "name", "/products/new", "new", "NEW"),
        ["int"] = Text("/p/{name}", "name", "/p/{id:int}", "42", "43x"),
        ["bool"] = Text("/b/{name}", "name", "/b/{f:bool}", "TRUE", "yes"),
        ["optional"] = new(
            "/list/{p:int?}",
            "/list",
            new PageRoute(null),
            new PageRoute(3),
            b => b.Map("/list/{p:int?}", c => new PageRoute(c.TryGetPath("p", out int? p) ? p : null),
                f => f.Path("p", r => r.Page))),
        ["empty catch-all"] = Text("/f/{*rest}", "rest", "/f", null, "a"),
        ["catch-all"] = Text("/f/{*rest}", "rest", "/f/{a}/edit", "doc/edit", "doc/view"),
        ["enum"] = new(
            "/s/{e}",
            "/s/A",
            new LetterRoute(Letter.A),
            new LetterRoute(Letter.B),
            b => b.Map("/s/{e}", c => new LetterRoute(c.Path<Letter>("e")), f => f.Path("e", r => r.Letter))),
        ["alpha"] = Text("/n/{e}", "e", "/n/{n:alpha}", "abc", "abc1"),

        // The written path is percent-encoded, so these capture only when the walk decodes it before comparing.
        ["encoded literal"] = Text("/{x}", "x", "/a b", "a b", "a  b"),
        ["non-ASCII literal"] = Text("/u/{x}", "x", "/u/\u00E9", "\u00E9", "\u00E8"),
    };

    // Literals that a generated value can equal, so that generated tables capture as well as write.
    private static readonly string[] GeneratedLiterals = ["a", "new", "42", "true", "abc"];

    private static readonly string?[] GeneratedValues =
        ["a", "new", "42", "true", "TRUE", "abc", "7", "x/y", "new/42", "a b", null, null];

    private static readonly Func<string?, string?, string?, GenRoute>[] GeneratedMakers =
    [
        (a, b, c) => new G0(a, b, c),
        (a, b, c) => new G1(a, b, c),
        (a, b, c) => new G2(a, b, c),
        (a, b, c) => new G3(a, b, c),
        (a, b, c) => new G4(a, b, c),
        (a, b, c) => new G5(a, b, c),
    ];

    private static CaptureCase Text(string writer, string name, string capturing, string? captured, string? neighbour)
    {
        return new CaptureCase(
            writer,
            capturing,
            new TextRoute(captured),
            new TextRoute(neighbour),
            b => b.Map(writer, c => new TextRoute(c.TryGetPath(name, out string? v) ? v : null),
                f => f.Path(name, r => r.Text)));
    }

    private static RouteTable CaptureTable(CaptureCase capture, bool capturingFirst)
    {
        RouteTableBuilder builder = new RouteTableBuilder().AddEnumValueCodec<Letter>();
        if (capturingFirst)
            builder.Map(capture.Capturing, _ => new SettingsRoute());

        capture.MapWriter(builder);
        if (!capturingFirst)
            builder.Map(capture.Capturing, _ => new SettingsRoute());

        return builder.Build();
    }

    private static ArgumentException AssertCaptured(RouteTable table, AppRoute route, string writer, string capturing)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => table.Format(route));

        Assert.Equal("route", exception.ParamName);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
        Assert.Contains(CapturedFirst, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{route.GetType().Name}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{writer}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{capturing}'", exception.Message, StringComparison.Ordinal);
        return exception;
    }

    // Up to three segments, a first literal most often "a" so that templates share prefixes, each segment a literal,
    // a parameter with or without a constraint, an optional parameter, or, last, a catch-all, as a template allows.
    private static string GenerateTemplate(Random random, out int parameters)
    {
        int count = random.Next(4);
        if (count == 0)
        {
            parameters = 0;
            return "/";
        }

        var text = new System.Text.StringBuilder();
        parameters = 0;
        var optional = false;
        for (var i = 0; i < count; i++)
        {
            string name = $"p{parameters}";
            int kind = random.Next(i == count - 1 ? 9 : 8);
            if (optional)
                kind = 6 + (kind % 2);

            string segment = kind switch
            {
                0 or 1 => i == 0 && random.Next(3) > 0
                    ? "a"
                    : GeneratedLiterals[random.Next(GeneratedLiterals.Length)],
                2 => $"{{{name}}}",
                3 => $"{{{name}:int}}",
                4 => $"{{{name}:bool}}",
                5 => $"{{{name}:alpha}}",
                6 => $"{{{name}?}}",
                7 => $"{{{name}:int?}}",
                _ => $"{{*{name}}}",
            };

            optional |= kind is 6 or 7;
            if (kind >= 2)
                parameters++;

            text.Append('/').Append(segment);
        }

        return text.ToString();
    }

    // Each case names its own sealed route type so that Map infers it: Map rejects a format function for the abstract
    // GenRoute, which GeneratedMakers alone would give it.
    private static void MapGenerated(RouteTableBuilder builder, int index, string template, int parameters, bool writes)
    {
        switch (index)
        {
            case 0:
                MapGenerated(builder, template, parameters, writes, (a, b, c) => new G0(a, b, c));
                break;
            case 1:
                MapGenerated(builder, template, parameters, writes, (a, b, c) => new G1(a, b, c));
                break;
            case 2:
                MapGenerated(builder, template, parameters, writes, (a, b, c) => new G2(a, b, c));
                break;
            case 3:
                MapGenerated(builder, template, parameters, writes, (a, b, c) => new G3(a, b, c));
                break;
            case 4:
                MapGenerated(builder, template, parameters, writes, (a, b, c) => new G4(a, b, c));
                break;
            default:
                MapGenerated(builder, template, parameters, writes, (a, b, c) => new G5(a, b, c));
                break;
        }
    }

    private static void MapGenerated<TRoute>(
        RouteTableBuilder builder,
        string template,
        int parameters,
        bool writes,
        Func<string?, string?, string?, TRoute> make)
        where TRoute : GenRoute
    {
        TRoute Create(RouteMatchContext c)
        {
            return make(ReadText(c, "p0"), ReadText(c, "p1"), ReadText(c, "p2"));
        }

        if (!writes)
        {
            builder.Map(template, Create);
            return;
        }

        builder.Map(template, Create, f =>
        {
            if (parameters > 0)
                f.Path("p0", r => r.A);
            if (parameters > 1)
                f.Path("p1", r => r.B);
            if (parameters > 2)
                f.Path("p2", r => r.C);
        });
    }

    private static string? ReadText(RouteMatchContext context, string name)
    {
        return context.TryGetPath(name, out string? value) ? value : null;
    }

    // Formats the same route each time, after enough calls for the code under test to be compiled. The runtime
    // sometimes allocates once on its own account among the measured calls, which can only add bytes, so the least of
    // several samples is what the calls themselves allocate.
    private static long AllocatedBytesPerFormat(RouteTable table, AppRoute route)
    {
        const int Calls = 50;
        for (var i = 0; i < 200; i++)
            table.Format(route);

        long least = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Calls; i++)
                table.Format(route);

            least = Math.Min(least, (GC.GetAllocatedBytesForCurrentThread() - before) / Calls);
        }

        return least;
    }

    private static string? Log(List<string> calls, string name, string? value)
    {
        calls.Add(name);
        return value;
    }

    private static RouteTable OptionalTable()
    {
        return new RouteTableBuilder()
            .Map("/a/{b?}/{c?}",
                c => new OptionalRoute(
                    c.TryGetPath("b", out string? b) ? b : null,
                    c.TryGetPath("c", out string? cv) ? cv : null),
                f => f.Path("b", r => r.B).Path("c", r => r.C))
            .Build();
    }

    private static RouteTable TextTable(string template)
    {
        return new RouteTableBuilder()
            .Map(template, c => new TextRoute(c.Path("id")), f => f.Path("id", r => r.Text))
            .Build();
    }

    // The writer /products/{id}, mapped last, below count literal templates that rank above it and match nothing it
    // writes for the value used.
    private static RouteTable AboveTable(int count)
    {
        var builder = new RouteTableBuilder();
        for (var i = 0; i < count; i++)
            builder.Map($"/products/x{i}", _ => new SettingsRoute());

        return builder
            .Map("/products/{id}", c => new TextRoute(c.Path("id")), f => f.Path("id", r => r.Text))
            .Build();
    }

    private static RouteTable FileTable()
    {
        return new RouteTableBuilder()
            .Map("/files/{*path}", c => new TextRoute(c.TryGetPath("path", out string? path) ? path : null),
                f => f.Path("path", r => r.Text))
            .Build();
    }

    private static ArgumentException AssertNotWritten(
        RouteTable table,
        AppRoute route,
        string template,
        string parameter,
        string category)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => table.Format(route));

        Assert.Equal("route", exception.ParamName);
        Assert.Null(exception.InnerException);
        Assert.Contains(category, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{route.GetType().Name}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{template}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"path parameter '{parameter}'", exception.Message, StringComparison.Ordinal);
        return exception;
    }

    private static void AssertRoundTrip(RouteTable table, AppRoute route)
    {
        string path = table.Format(route);
        RouteMatchResult result = Match(table, path);

        Assert.True(result.IsSuccess, path);
        Assert.Equal(route, result.Route);
        Assert.Equal(path, table.Format(result.Route!));
    }

    private static RouteMatchResult Match(RouteTable table, string path)
    {
        return table.Match(new Uri(path, UriKind.Relative));
    }

    private sealed record OrderRoute(int Id) : AppRoute;

    private sealed record LongRoute(long Value) : AppRoute;

    private sealed record TextRoute(string? Text) : AppRoute;

    private sealed record SecretTextRoute(string Text) : AppRoute;

    private sealed record GuidTextRoute(string Text) : AppRoute;

    private sealed record FileSecretRoute(string? Path) : AppRoute;

    private sealed record OptionalRoute(string? B, string? C) : AppRoute;

    private sealed record CallsRoute(string A, double B, Access C) : AppRoute;

    private sealed record PageRoute(int? Page) : AppRoute;

    private sealed record DoubleRoute(double Value) : AppRoute;

    private sealed record FloatRoute(float Value) : AppRoute;

    private sealed record AccessRoute(Access Access) : AppRoute;

    private sealed record LetterRoute(Letter Letter) : AppRoute;

    private sealed record TwoRoute(string A, string B) : AppRoute;

    private sealed record CaptureCase(
        string Writer,
        string Capturing,
        AppRoute Captured,
        AppRoute Neighbour,
        Action<RouteTableBuilder> MapWriter);

    private abstract record GenRoute(string? A, string? B, string? C) : AppRoute;

    private sealed record G0(string? A, string? B, string? C) : GenRoute(A, B, C);

    private sealed record G1(string? A, string? B, string? C) : GenRoute(A, B, C);

    private sealed record G2(string? A, string? B, string? C) : GenRoute(A, B, C);

    private sealed record G3(string? A, string? B, string? C) : GenRoute(A, B, C);

    private sealed record G4(string? A, string? B, string? C) : GenRoute(A, B, C);

    private sealed record G5(string? A, string? B, string? C) : GenRoute(A, B, C);

    private record BaseRoute(string Id) : AppRoute;

    private sealed record DerivedRoute(string Id) : BaseRoute(Id);

    [Flags]
    private enum Access
    {
        Read = 1,
        Write = 2,
    }

    private enum Letter
    {
        A,
        B,
    }
}
