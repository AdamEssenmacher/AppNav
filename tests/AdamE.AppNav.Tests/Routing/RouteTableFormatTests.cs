using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
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
    private const string NullElement = "has a null element";

    // Text that stands for a secret a route can carry, distinctive enough that finding it in a message is not a
    // coincidence.
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

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

    // Query values follow the path in the order they were declared, each left out when null, with the '?' only when
    // one is written.
    [Fact]
    public void QueryValuesAreWrittenInDeclarationOrderAfterThePath()
    {
        RouteTable table = ProductsTable();

        Assert.Equal("/products?page=2&sort=name", table.Format(new ProductsRoute(2, "name")));
        Assert.Equal("/products?page=2", table.Format(new ProductsRoute(2, null)));
        Assert.Equal("/products?sort=name", table.Format(new ProductsRoute(null, "name")));
        Assert.Equal("/products", table.Format(new ProductsRoute(null, null)));
        foreach (int? page in new int?[] { null, 0, 2, -1 })
        {
            foreach (string? sort in new[] { null, "", "name", "a b" })
                AssertRoundTrip(table, new ProductsRoute(page, sort));
        }

        RouteTable reversed = new RouteTableBuilder()
            .Map("/products", ReadProducts, f => f.Query("sort", r => r.Sort).Query("page", r => r.Page))
            .Build();
        Assert.Equal("/products?sort=name&page=2", reversed.Format(new ProductsRoute(2, "name")));
    }

    [Fact]
    public void AQueryIsWrittenAfterThePathValues()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/orders/{id:int}/{tab?}",
                c => new OrderTabRoute(
                    c.Path<int>("id"),
                    c.TryGetPath("tab", out string? tab) ? tab : null,
                    c.TryGetQuery("q", out string? q) ? q : null),
                f => f.Query("q", r => r.Query).Path("tab", r => r.Tab).Path("id", r => r.Id))
            .Build();

        Assert.Equal("/orders/42/items?q=a%20b", table.Format(new OrderTabRoute(42, "items", "a b")));
        Assert.Equal("/orders/42?q=x", table.Format(new OrderTabRoute(42, null, "x")));
        Assert.Equal("/orders/42", table.Format(new OrderTabRoute(42, null, null)));
        AssertRoundTrip(table, new OrderTabRoute(42, "items", "a b"));
        AssertRoundTrip(table, new OrderTabRoute(42, null, "x"));
    }

    // The empty string is a value, written as 'name=', which the read gives back as the empty string; only null
    // leaves a value out.
    [Fact]
    public void AnEmptyQueryValueIsWrittenAndReadBack()
    {
        RouteTable table = ProductsTable();

        Assert.Equal("/products?sort=", table.Format(new ProductsRoute(null, "")));
        Assert.Equal("/products?page=1&sort=", table.Format(new ProductsRoute(1, "")));
        Assert.Equal(new ProductsRoute(null, ""), Match(table, "/products?sort=").Route);
    }

    // The empty name is a name, on the read side and here.
    [Fact]
    public void TheEmptyNameIsWrittenAndReadBack()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/p", c => new TextRoute(c.TryGetQuery("", out string? v) ? v : null), f => f.Query("", r => r.Text))
            .Build();

        Assert.Equal("/p?=x", table.Format(new TextRoute("x")));
        Assert.Equal("/p?=", table.Format(new TextRoute("")));
        Assert.Equal("/p", table.Format(new TextRoute(null)));
        AssertRoundTrip(table, new TextRoute("x"));
        AssertRoundTrip(table, new TextRoute(""));
        AssertRoundTrip(table, new TextRoute(null));
    }

    // Names and values are encoded with Uri.EscapeDataString, so a space is %20, never '+', which the query reader
    // takes for a space, and a '+' is %2B; '&', '=', '#', '?' and '%' are encoded so that they read back as data.
    [Theory]
    [InlineData("a b", "a%20b")]
    [InlineData("a+b", "a%2Bb")]
    [InlineData("a&b", "a%26b")]
    [InlineData("a=b", "a%3Db")]
    [InlineData("#1", "%231")]
    [InlineData("why?", "why%3F")]
    [InlineData("100%", "100%25")]
    [InlineData("%20", "%2520")]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("-._~", "-._~")]
    [InlineData("\u00E9t\u00E9", "%C3%A9t%C3%A9")]
    [InlineData("\U0001F600", "%F0%9F%98%80")]
    [InlineData("a\tb\n", "a%09b%0A")]
    [InlineData("\u0000", "%00")]
    [InlineData(".", ".")]
    [InlineData(" ", "%20")]
    public void AQueryNameAndValueAreWrittenEscaped(string text, string expected)
    {
        RouteTable values = new RouteTableBuilder()
            .Map("/p", c => new TextRoute(c.TryGetQuery("v", out string? v) ? v : null), f => f.Query("v", r => r.Text))
            .Build();
        RouteTable names = new RouteTableBuilder()
            .Map("/p",
                c => new TextRoute(c.TryGetQuery(text, out string? v) ? v : null),
                f => f.Query(text, r => r.Text))
            .Build();

        Assert.Equal("/p?v=" + expected, values.Format(new TextRoute(text)));
        Assert.Equal("/p?" + expected + "=x", names.Format(new TextRoute("x")));
        AssertRoundTrip(values, new TextRoute(text));
        AssertRoundTrip(names, new TextRoute("x"));
    }

    [Fact]
    public void AQueryValueThatHasNoTextIsRejected()
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Access>()
            .Map("/q", _ => new QueryValuesRoute(null, null, null),
                f => f.Query("text", r => r.Text).Query("number", r => r.Number).Query("access", r => r.Access))
            .Build();

        AssertQueryNotWritten(table, new QueryValuesRoute(null, double.NaN, null), "/q", "number", NoText);
        AssertQueryNotWritten(table, new QueryValuesRoute("a", double.NegativeInfinity, null), "/q", "number", NoText);
        AssertQueryNotWritten(table, new QueryValuesRoute(null, null, (Access)8), "/q", "access", NoText);
        AssertQueryNotWritten(table, new QueryValuesRoute("a\uD800", null, null), "/q", "text", LoneSurrogate);
        AssertQueryNotWritten(table, new QueryValuesRoute("\uDC00b", 1, Access.Read), "/q", "text", LoneSurrogate);
        Assert.Equal("/q?text=a%F0%9F%98%80", table.Format(new QueryValuesRoute("a\uD83D\uDE00", null, null)));
    }

    // A route value can be a secret, so a query value that cannot be written is never quoted, nor is any other value.
    [Fact]
    public void AQueryValueThatCannotBeWrittenIsNeverQuoted()
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Access>()
            .Map("/s/{token}", _ => new QueryValuesRoute(null, null, null),
                f => f.Path("token", r => r.Text).Query("n", r => r.Number).Query("again", r => r.Text + "\uD800"))
            .Build();

        ArgumentException infinite = Assert.Throws<ArgumentException>(
            () => table.Format(new QueryValuesRoute(Secret, double.PositiveInfinity, null)));
        ArgumentException surrogate = Assert.Throws<ArgumentException>(
            () => table.Format(new QueryValuesRoute(Secret, 1, null)));

        Assert.Contains(LoneSurrogate, surrogate.Message, StringComparison.Ordinal);
        foreach (ArgumentException exception in new[] { surrogate, infinite })
        {
            Assert.Equal("route", exception.ParamName);
            Assert.Null(exception.InnerException);
            Assert.Empty(exception.Data);
            Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Infinity", exception.ToString(), StringComparison.Ordinal);
        }

        Assert.Contains("query value 'n'", infinite.Message, StringComparison.Ordinal);
        Assert.Contains("query value 'again'", surrogate.Message, StringComparison.Ordinal);
    }

    // The path functions are called first, in template order, and then the query functions in the order they were
    // declared, whatever order the declarations were made in, each once.
    [Fact]
    public void PathFunctionsAreCalledBeforeQueryFunctionsEachOnce()
    {
        var calls = new List<string>();
        RouteTable table = new RouteTableBuilder()
            .Map("/{a}/{b}", _ => new TwoRoute("a", "b"), f => f
                .Query("y", r => Log(calls, "y", r.A))
                .Path("b", r => Log(calls, "b", r.B))
                .Query("x", r => Log(calls, "x", r.B))
                .Path("a", r => Log(calls, "a", r.A)))
            .Build();

        Assert.Equal("/1/2?y=1&x=2", table.Format(new TwoRoute("1", "2")));
        Assert.Equal(["a", "b", "y", "x"], calls);

        // A path the template rejects stops the calls before any query function, as a value without text does.
        calls.Clear();
        Assert.Throws<ArgumentException>(() => table.Format(new TwoRoute("", "2")));
        Assert.Equal(["a", "b"], calls);
    }

    [Fact]
    public void LaterQueryFunctionsAreNotCalledAfterAValueThatCannotBeWrittenOrAnException()
    {
        var calls = new List<string>();
        var thrown = new InvalidOperationException("from the app");
        RouteTable table = new RouteTableBuilder()
            .Map("/q/{a}", _ => new QueryValuesRoute(null, null, null), f => f
                .Path("a", r => Log(calls, "a", r.Text))
                .Query("b", r =>
                {
                    calls.Add("b");
                    return r.Text == "throw" ? throw thrown : r.Number;
                })
                .Query("c", r => Log(calls, "c", r.Text)))
            .Build();

        AssertQueryNotWritten(table, new QueryValuesRoute("t", double.NaN, null), "/q/{a}", "b", NoText);
        Assert.Equal(["a", "b"], calls);

        calls.Clear();
        Assert.Same(thrown, Assert.Throws<InvalidOperationException>(
            () => table.Format(new QueryValuesRoute("throw", 1, null))));
        Assert.Equal(["a", "b"], calls);

        calls.Clear();
        Assert.Equal("/q/t?b=1&c=t", table.Format(new QueryValuesRoute("t", 1, null)));
        Assert.Equal(["a", "b", "c"], calls);

        // A path that cannot be written stops the calls before the query, whose value would not be written either.
        calls.Clear();
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.Format(new QueryValuesRoute(null, double.PositiveInfinity, null)));
        Assert.Contains("path parameter 'a'", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["a"], calls);
    }

    // The capture check reads the path alone, after every value function has been called once: a query value that
    // cannot be written is reported rather than the capture, and a query value never changes which template matches.
    [Fact]
    public void TheCaptureCheckReadsThePathAloneAfterEveryValueFunction()
    {
        var calls = new List<string>();
        RouteTable table = new RouteTableBuilder()
            .Map("/products/new", _ => new SettingsRoute())
            .Map("/products/{name}",
                c => new NamedQueryRoute(c.Path("name"), c.TryGetQuery("q", out double? q) ? q : null),
                f => f
                    .Path("name", r => Log(calls, "name", r.Name))
                    .Query("q", r =>
                    {
                        calls.Add("q");
                        return r.Q;
                    }))
            .Build();

        AssertCaptured(table, new NamedQueryRoute("new", 1), "/products/{name}", "/products/new");
        Assert.Equal(["name", "q"], calls);

        calls.Clear();
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.Format(new NamedQueryRoute("new", double.NaN)));
        Assert.Contains("query value 'q'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(CapturedFirst, exception.Message, StringComparison.Ordinal);
        Assert.Equal(["name", "q"], calls);

        Assert.Equal("/products/widget?q=1", table.Format(new NamedQueryRoute("widget", 1)));
        AssertRoundTrip(table, new NamedQueryRoute("widget", 1));
        AssertRoundTrip(table, new NamedQueryRoute("widget", null));
        AssertCaptured(table, new NamedQueryRoute("new", null), "/products/{name}", "/products/new");
    }

    // A query with text that looks like a path or a template literal changes nothing about the match.
    [Fact]
    public void AQueryValueNeverChangesWhichTemplateMatches()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/products/new", _ => new SettingsRoute())
            .Map("/products/{name}",
                c => new ProductQueryRoute(c.Path("name"), c.TryGetQuery("new", out string? q) ? q : null),
                f => f.Path("name", r => r.Name).Query("new", r => r.Q))
            .Build();

        foreach (string q in new[] { "/products/new", "new", "?new", "#new", "../new", "" })
        {
            string text = table.Format(new ProductQueryRoute("widget", q));
            Assert.StartsWith("/products/widget?new=", text, StringComparison.Ordinal);
            AssertRoundTrip(table, new ProductQueryRoute("widget", q));
            Assert.Equal(new ProductQueryRoute("widget", q), table.Match(new Uri("https://h" + text)).Route);
        }
    }

    // A mapping whose query values are all null writes its path alone, and allocates nothing more than a mapping with
    // no query declarations, which returns the path the template wrote as it is.
    [Fact]
    public void FormatAllocatesNothingMoreForQueryValuesThatAreAllNull()
    {
        RouteTable plain = TextTable("/products/{id}");
        RouteTable nulls = new RouteTableBuilder()
            .Map("/products/{id}", c => new TextRoute(c.Path("id")), f => f
                .Path("id", r => r.Text)
                .Query("page", _ => (int?)null)
                .Query("sort", _ => (string?)null)
                .Query("when", _ => (Guid?)null)
                .Query("access", _ => (Access?)null))
            .AddEnumValueCodec<Access>()
            .Build();
        var route = new TextRoute("widget");

        Assert.Equal("/products/widget", nulls.Format(route));
        long plainBytes = AllocatedBytesPerFormat(plain, route);
        long nullBytes = AllocatedBytesPerFormat(nulls, route);

        Assert.InRange(nullBytes - plainBytes, long.MinValue, 0);
    }

    // A value type is written without a box. Each pair writes the same text, one from the value and one from the
    // value's own text, which the value function makes, so both do the same work to make the text and only a box
    // could tell them apart. Each box would be at least 24 bytes.
    [Fact]
    public void AQueryValueOfAValueTypeIsNotBoxed()
    {
        var route = new NumbersRoute(12345, new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff"), 12345.678m);

        AssertNoBox(route, f => f.Query("v", r => r.Int), f => f.Query("v", r => r.Int.ToString(Invariant)));
        AssertNoBox(route, f => f.Query("v", r => r.Guid), f => f.Query("v", r => r.Guid.ToString()));
        AssertNoBox(route, f => f.Query("v", r => r.Decimal), f => f.Query("v", r => r.Decimal.ToString(Invariant)));
    }

    // System.Uri drops %20 sequences only at the very end of an absolute URI that contains non-ASCII text. A path
    // value followed by a query keeps its trailing space; a query value written last loses it, from an absolute link
    // only, unless another value follows it or the URI is ASCII. This pins how System.Uri reads the written text.
    [Fact]
    public void OnlyTheEndOfTheWholeTextLosesATrailingSpaceInAnAbsoluteLink()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/\u00E9/new", _ => new SettingsRoute())
            .Map("/\u00E9/{name}", ReadSpaced, WriteSpaced)
            .Build();
        RouteTable ascii = new RouteTableBuilder()
            .Map("/a/{name}", ReadSpaced, WriteSpaced)
            .Build();
        string[] prefixes = ["https://h", "myapp://", "myapp:///"];

        var pathSpace = new SpacedRoute("new ", "x", null);
        Assert.Equal("/%C3%A9/new%20?q=x", table.Format(pathSpace));
        var notLast = new SpacedRoute("a", "x ", "y");
        Assert.Equal("/%C3%A9/a?q=x%20&r=y", table.Format(notLast));
        var last = new SpacedRoute("a", "x", "y ");
        Assert.Equal("/%C3%A9/a?q=x&r=y%20", table.Format(last));
        var onlySpaces = new SpacedRoute("a", null, "  ");

        // A non-ASCII host the application puts in front counts as well, though the written text is all ASCII.
        const string IdnHost = "https://\u00E9.example";
        Assert.Equal("/a/a?q=x&r=y%20", ascii.Format(last));
        Assert.Equal(last with { R = "y" }, ascii.Match(new Uri(IdnHost + ascii.Format(last))).Route);
        Assert.Equal(pathSpace, ascii.Match(new Uri(IdnHost + ascii.Format(pathSpace))).Route);
        Assert.Equal(
            pathSpace with { Name = "new", Q = null },
            ascii.Match(new Uri(IdnHost + ascii.Format(pathSpace with { Q = null }))).Route);

        foreach (string prefix in prefixes)
        {
            Assert.Equal(pathSpace, table.Match(new Uri(prefix + table.Format(pathSpace))).Route);
            Assert.Equal(notLast, table.Match(new Uri(prefix + table.Format(notLast))).Route);
            Assert.Equal(last with { R = "y" }, table.Match(new Uri(prefix + table.Format(last))).Route);
            Assert.Equal(onlySpaces with { R = "" }, table.Match(new Uri(prefix + table.Format(onlySpaces))).Route);
            Assert.Equal(last, ascii.Match(new Uri(prefix + ascii.Format(last))).Route);
            Assert.Equal(
                new SettingsRoute(),
                table.Match(new Uri(prefix + table.Format(pathSpace with { Q = null }))).Route);
        }

        foreach (SpacedRoute route in new[] { pathSpace, notLast, last, onlySpaces })
            AssertRoundTrip(table, route);
    }

    // Each element is its own pair, in the order the list gives it, where the list was declared among the others.
    [Fact]
    public void TheElementsOfAListAreWrittenInOrderWhereTheListWasDeclared()
    {
        RouteTable table = TagsTable();

        Assert.Equal("/t?page=2&tag=a&tag=b&sort=x", table.Format(new TagsRoute(2, ["a", "b"], "x")));
        Assert.Equal("/t?tag=b&tag=a&tag=b", table.Format(new TagsRoute(null, ["b", "a", "b"], null)));
        Assert.Equal("/t?page=1&tag=", table.Format(new TagsRoute(1, [""], null)));
        Assert.Equal("/t?tag=&tag=&sort=", table.Format(new TagsRoute(null, ["", ""], "")));
        Assert.Equal("/t?tag=a%20b&tag=a%2Bb&tag=%26%3D%23", table.Format(new TagsRoute(null, ["a b", "a+b", "&=#"],
            null)));
        AssertRoundTrip(table, new TagsRoute(2, ["a", "b"], "x"));

        RouteTable interleaved = new RouteTableBuilder()
            .Map("/i", _ => new TagsRoute(null, null, null), f => f
                .QueryValues("a", r => r.Tags)
                .Query("x", r => r.Sort)
                .QueryValues("b", r => r.Tags?.Reverse())
                .Query("y", r => r.Page))
            .Build();
        Assert.Equal("/i?a=1&a=2&x=s&b=2&b=1&y=3", interleaved.Format(new TagsRoute(3, ["1", "2"], "s")));
    }

    // A null list and an empty one write nothing, and the '?' only appears when some pair is written.
    [Fact]
    public void ANullOrEmptyListWritesNothing()
    {
        RouteTable table = TagsTable();

        Assert.Equal("/t", table.Format(new TagsRoute(null, null, null)));
        Assert.Equal("/t", table.Format(new TagsRoute(null, [], null)));
        Assert.Equal("/t?sort=x", table.Format(new TagsRoute(null, [], "x")));
        Assert.Equal("/t?page=1", table.Format(new TagsRoute(1, null, null)));
        Assert.Equal(new TagsRoute(null, [], null), Match(table, "/t").Route);
    }

    // A null element is never written, since an element of a list read back is never absent; an empty string is a
    // value.
    [Fact]
    public void ANullElementIsRejected()
    {
        var calls = new List<string>();
        RouteTable table = new RouteTableBuilder()
            .Map("/n", _ => new TagsRoute(null, null, null), f => f
                .QueryValues("tag", r =>
                {
                    calls.Add("tag");
                    return r.Tags;
                })
                .Query("sort", r => Log(calls, "sort", r.Sort)))
            .Build();

        AssertQueryNotWritten(table, new TagsRoute(null, ["a", null!, "b"], "x"), "/n", "tag", NullElement);
        AssertQueryNotWritten(table, new TagsRoute(null, [null!], null), "/n", "tag", "never absent");
        Assert.Equal(["tag", "tag"], calls);
        Assert.Equal("/n?tag=&sort=x", table.Format(new TagsRoute(null, [""], "x")));
    }

    // An element with no text fails the format; the elements after it are not read and the later value functions are
    // not called. The list is enumerated once, and its enumerator disposed.
    [Fact]
    public void AnElementThatCannotBeWrittenStopsTheList()
    {
        var calls = new List<string>();
        var numbers = new RecordingList<double>([1, double.NaN, 3, 4]);
        var accesses = new RecordingList<Access>([Access.Read, (Access)8, Access.Write]);
        var texts = new RecordingList<string>(["a", "b\uD800", "c"]);
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Access>()
            .Map("/e", _ => new SettingsRoute(), f => f
                .QueryValues("n", _ => Log(calls, "n", numbers))
                .QueryValues("a", _ => Log(calls, "a", accesses))
                .QueryValues("t", _ => Log(calls, "t", texts))
                .Query("after", _ => Log(calls, "after", "x")))
            .Build();

        AssertQueryNotWritten(table, new SettingsRoute(), "/e", "n", NoText);
        Assert.Equal(["n"], calls);
        Assert.Equal((1, 2, 1), numbers.Counts);

        numbers.Replace([1, 3]);
        calls.Clear();
        AssertQueryNotWritten(table, new SettingsRoute(), "/e", "a", NoText);
        Assert.Equal(["n", "a"], calls);
        Assert.Equal((1, 2, 1), accesses.Counts);

        accesses.Replace([Access.Write]);
        calls.Clear();
        AssertQueryNotWritten(table, new SettingsRoute(), "/e", "t", LoneSurrogate);
        Assert.Equal(["n", "a", "t"], calls);
        Assert.Equal((1, 2, 1), texts.Counts);

        texts.Replace(["c"]);
        calls.Clear();
        Assert.Equal("/e?n=1&n=3&a=Write&t=c&after=x", table.Format(new SettingsRoute()));
        Assert.Equal(["n", "a", "t", "after"], calls);
        Assert.Equal((1, 2, 1), texts.Counts);
    }

    // An exception from the list propagates unchanged, and the enumerator is still disposed.
    [Fact]
    public void AnExceptionFromTheListPropagatesUnchanged()
    {
        var thrown = new InvalidOperationException("from the app");
        var list = new RecordingList<int>([1, 2, 3]) { ThrowAt = 2, Thrown = thrown };
        var calls = new List<string>();
        RouteTable table = new RouteTableBuilder()
            .Map("/x", _ => new SettingsRoute(), f => f
                .QueryValues("v", _ => list)
                .Query("after", _ => Log(calls, "after", "x")))
            .Build();

        Assert.Same(thrown, Assert.Throws<InvalidOperationException>(() => table.Format(new SettingsRoute())));
        Assert.Equal((1, 3, 1), list.Counts);
        Assert.Empty(calls);
    }

    // The value function is called once and its list enumerated once, so a lazy list is evaluated once, and a list
    // that changes between calls writes what it holds at each call.
    [Fact]
    public void AListIsReadOnceEachTimeARouteIsFormatted()
    {
        var evaluations = 0;
        var calls = 0;
        var list = new List<int> { 1 };
        RouteTable table = new RouteTableBuilder()
            .Map("/l", _ => new SettingsRoute(), f => f
                .QueryValues("lazy", _ =>
                {
                    calls++;
                    return Lazy();
                })
                .QueryValues("list", _ => list))
            .Build();

        Assert.Equal("/l?lazy=0&lazy=1&lazy=2&list=1", table.Format(new SettingsRoute()));
        Assert.Equal((1, 3), (calls, evaluations));

        list.Add(2);
        Assert.Equal("/l?lazy=3&lazy=4&lazy=5&list=1&list=2", table.Format(new SettingsRoute()));
        list.Clear();
        Assert.Equal("/l?lazy=6&lazy=7&lazy=8", table.Format(new SettingsRoute()));
        Assert.Equal((3, 9), (calls, evaluations));

        IEnumerable<int> Lazy()
        {
            for (var i = 0; i < 3; i++)
                yield return evaluations++;
        }
    }

    // The values of a list that cannot be written, the failing one and its neighbours alike, never appear in the
    // exception, nor does where in the list the failing one is.
    [Fact]
    public void AnElementThatCannotBeWrittenIsNeverQuoted()
    {
        const string Before = "nb-Q7x2Lk9Pd";
        const string After = "na-W4m8Rt1Zc";
        RouteTable[] tables =
        [
            new RouteTableBuilder()
                .Map("/s", _ => new SettingsRoute(), f => f.QueryValues("t", _ => new[] { Before, Secret + "\uD800",
                    After }))
                .Build(),
            new RouteTableBuilder()
                .Map("/s", _ => new SettingsRoute(), f => f.QueryValues("t", _ => new[] { Before, null!, After }))
                .Build(),
            new RouteTableBuilder()
                .Map("/s", _ => new SettingsRoute(), f => f.QueryValues("t", _ => new[] { 7654.321, double.NaN,
                    1234.567 }))
                .Build(),
        ];

        foreach (RouteTable failing in tables)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => failing.Format(new SettingsRoute()));

            Assert.Equal("route", exception.ParamName);
            Assert.Null(exception.InnerException);
            Assert.Empty(exception.Data);
            Assert.Contains("query value 't'", exception.Message, StringComparison.Ordinal);
            foreach (string quoted in new[] { Secret, Before, After, "7654.321", "1234.567", "\uFFFD" })
                Assert.DoesNotContain(quoted, exception.ToString(), StringComparison.Ordinal);
        }
    }

    // Nothing in the message says where in the list the failing element is: with a template, a name and a route type
    // that have no digits, the message of each kind of element failure, for the fourth of seven elements, has none
    // beyond the fixed words "UTF-16".
    [Fact]
    public void AFailingElementIsNotLocatedInTheMessage()
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Access>()
            .Map("/s", _ => new SettingsRoute(), f => f
                .QueryValues("t", _ => Seven("a", null!))
                .QueryValues("u", _ => Seven("a", "b\uD800"))
                .QueryValues("n", _ => Seven(1.5, double.NaN))
                .QueryValues("e", _ => Seven(Access.Read, (Access)8)))
            .Build();
        RouteTable[] failing =
        [
            table,
            new RouteTableBuilder().Map("/s", _ => new SettingsRoute(), f => f
                .QueryValues("u", _ => Seven("a", "b\uD800"))).Build(),
            new RouteTableBuilder().Map("/s", _ => new SettingsRoute(), f => f
                .QueryValues("n", _ => Seven(1.5, double.NaN))).Build(),
            new RouteTableBuilder().AddEnumValueCodec<Access>().Map("/s", _ => new SettingsRoute(), f => f
                .QueryValues("e", _ => Seven(Access.Read, (Access)8))).Build(),
        ];
        string[] kinds = [NullElement, LoneSurrogate, NoText, NoText];

        for (var i = 0; i < failing.Length; i++)
        {
            RouteTable current = failing[i];
            ArgumentException exception = Assert.Throws<ArgumentException>(() => current.Format(new SettingsRoute()));

            Assert.Equal("route", exception.ParamName);
            Assert.Contains(kinds[i], exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(exception.Message.Replace("UTF-16", "", StringComparison.Ordinal), char.IsAsciiDigit);
        }

        static T[] Seven<T>(T good, T bad)
        {
            return [good, good, good, bad, good, good, good];
        }
    }

    // A default ImmutableArray is neither null nor empty: enumerating it throws, and that exception propagates as any
    // exception from the app's list does.
    [Fact]
    public void ADefaultImmutableArrayThrowsItsOwnException()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/i", _ => new SettingsRoute(), f => f.QueryValues("v", _ => default(ImmutableArray<int>)))
            .Build();

        Assert.IsType<InvalidOperationException>(Assert.ThrowsAny<Exception>(() => table.Format(new SettingsRoute())));
    }

    // The capture check still reads the path alone, after every value function and every list.
    [Fact]
    public void TheCaptureCheckReadsThePathAloneAfterTheLists()
    {
        var list = new RecordingList<string>(["new", "x"]);
        RouteTable table = new RouteTableBuilder()
            .Map("/products/new", _ => new SettingsRoute())
            .Map("/products/{name}",
                c => new ProductQueryRoute(c.Path("name"), null),
                f => f.Path("name", r => r.Name).QueryValues("new", _ => list))
            .Build();

        AssertCaptured(table, new ProductQueryRoute("new", null), "/products/{name}", "/products/new");
        Assert.Equal((1, 3, 1), list.Counts);
        Assert.Equal("/products/widget?new=new&new=x", table.Format(new ProductQueryRoute("widget", null)));
    }

    // A route whose lists are all null or empty arrays allocates nothing more than one with no query declarations: no
    // builder and, for an empty array, no enumerator. An empty List allocates one boxed enumerator, whose size is the
    // runtime's affair, so it is bounded loosely.
    [Fact]
    public void FormatAllocatesNothingMoreForListsThatAreNullOrEmpty()
    {
        RouteTable plain = TextTable("/products/{id}");
        RouteTable empty = new RouteTableBuilder()
            .Map("/products/{id}", c => new TextRoute(c.Path("id")), f => f
                .Path("id", r => r.Text)
                .QueryValues("a", _ => (int[]?)null)
                .QueryValues("b", _ => Array.Empty<string>())
                .QueryValues("c", _ => (IEnumerable<Guid>?)null)
                .QueryValues("d", _ => Array.Empty<Access>()))
            .AddEnumValueCodec<Access>()
            .Build();
        var emptyList = new List<int>();
        RouteTable list = new RouteTableBuilder()
            .Map("/products/{id}", c => new TextRoute(c.Path("id")), f => f
                .Path("id", r => r.Text)
                .QueryValues("a", _ => emptyList))
            .Build();
        var route = new TextRoute("widget");

        Assert.Equal("/products/widget", empty.Format(route));
        Assert.Equal("/products/widget", list.Format(route));
        long plainBytes = AllocatedBytesPerFormat(plain, route);

        Assert.InRange(AllocatedBytesPerFormat(empty, route) - plainBytes, long.MinValue, 0);
        Assert.InRange(AllocatedBytesPerFormat(list, route) - plainBytes, long.MinValue, 64);
    }

    // What each further element of an int[] allocates is its text and its share of the query: the text of a five digit
    // int is a 32-byte string, which Uri.EscapeDataString returns as it is, and its pair, '&v=' and the digits, is 8
    // chars, 16 bytes. The StringBuilder's chunks hold at most about twice the chars written and ToString copies them
    // once more, so a pair costs at most 48 bytes in all, plus a chunk header now and then. A list of the same texts as
    // strings allocates exactly the 32 bytes of each text less, so no element is boxed, which would add 24 bytes.
    [Fact]
    public void EachElementAllocatesOnlyItsTextAndItsShareOfTheQuery()
    {
        var random = new Random(41);
        var numbers = new int[110];
        for (var i = 0; i < numbers.Length; i++)
            numbers[i] = random.Next(10000, 100000);
        string[] texts = Array.ConvertAll(numbers, n => n.ToString(Invariant));

        long numberBytes = AllocatedBytesPerFormat(ListTable(numbers), new SettingsRoute())
            - AllocatedBytesPerFormat(ListTable(numbers[..10]), new SettingsRoute());
        long textBytes = AllocatedBytesPerFormat(ListTable(texts), new SettingsRoute())
            - AllocatedBytesPerFormat(ListTable(texts[..10]), new SettingsRoute());

        Assert.InRange(textBytes / 100, long.MinValue, 48 + 4);
        Assert.InRange((numberBytes - textBytes) / 100, 32 - 2, 32 + 2);
    }

    // A long list is written in linear time: ten times the elements allocate about ten times the bytes, as a single
    // StringBuilder that grows by chunks does, where copying the query for each element would allocate about a hundred
    // times as much. Only that upper bound is asserted, since how the builder sizes its chunks is the runtime's affair.
    [Fact]
    public void ALongListIsWrittenInLinearTime()
    {
        int[] thousand = Enumerable.Range(10000, 1000).ToArray();
        int[] tenThousand = Enumerable.Range(10000, 10000).ToArray();
        RouteTable large = ListTable(tenThousand);

        string text = large.Format(new SettingsRoute());
        Assert.Equal(10000, text.Split('&').Length);
        Assert.Equal(tenThousand, ReadList(text));

        long smallBytes = AllocatedBytesPerFormat(ListTable(thousand), new SettingsRoute());
        long largeBytes = AllocatedBytesPerFormat(large, new SettingsRoute());
        Assert.InRange(largeBytes, long.MinValue, smallBytes * 15);
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

    private static RouteTable TagsTable()
    {
        return new RouteTableBuilder()
            .Map("/t",
                c => new TagsRoute(
                    c.TryGetQuery("page", out int page) ? page : null,
                    c.GetQueryValues("tag"),
                    c.TryGetQuery("sort", out string? sort) ? sort : null),
                f => f.Query("page", r => r.Page).QueryValues("tag", r => r.Tags).Query("sort", r => r.Sort))
            .Build();
    }

    private static RouteTable ListTable<T>(T[] values)
    {
        return new RouteTableBuilder()
            .Map("/l", _ => new SettingsRoute(), f => f.QueryValues("v", _ => values))
            .Build();
    }

    private static IReadOnlyList<int>? ReadList(string text)
    {
        IReadOnlyList<int>? read = null;
        RouteTable reader = new RouteTableBuilder()
            .Map("/l", c =>
            {
                read = c.GetQueryValues<int>("v");
                return new SettingsRoute();
            })
            .Build();
        Assert.True(reader.Match(new Uri(text, UriKind.Relative)).IsSuccess);
        return read;
    }

    private static T Log<T>(List<string> calls, string name, T value)
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

    private static RouteTable ProductsTable()
    {
        return new RouteTableBuilder()
            .Map("/products", ReadProducts, f => f.Query("page", r => r.Page).Query("sort", r => r.Sort))
            .Build();
    }

    private static ProductsRoute ReadProducts(RouteMatchContext context)
    {
        return new ProductsRoute(
            context.TryGetQuery("page", out int page) ? page : null,
            context.TryGetQuery("sort", out string? sort) ? sort : null);
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

    private static void AssertNoBox(
        NumbersRoute route,
        Action<RouteFormatBuilder<NumbersRoute>> typed,
        Action<RouteFormatBuilder<NumbersRoute>> text)
    {
        RouteTable typedTable = new RouteTableBuilder().Map("/p", _ => route, typed).Build();
        RouteTable textTable = new RouteTableBuilder().Map("/p", _ => route, text).Build();

        Assert.Equal(textTable.Format(route), typedTable.Format(route));
        long difference = AllocatedBytesPerFormat(typedTable, route) - AllocatedBytesPerFormat(textTable, route);

        Assert.InRange(difference, long.MinValue, 23);
    }

    private static ArgumentException AssertQueryNotWritten(
        RouteTable table,
        AppRoute route,
        string template,
        string name,
        string category)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => table.Format(route));

        Assert.Equal("route", exception.ParamName);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
        Assert.Contains(category, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{route.GetType().Name}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{template}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"query value '{name}'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("path parameter", exception.Message, StringComparison.Ordinal);
        return exception;
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

    private sealed record ProductsRoute(int? Page, string? Sort) : AppRoute;

    private sealed record OrderTabRoute(int Id, string? Tab, string? Query) : AppRoute;

    private sealed record QueryValuesRoute(string? Text, double? Number, Access? Access) : AppRoute;

    private sealed record NamedQueryRoute(string Name, double? Q) : AppRoute;

    private sealed record ProductQueryRoute(string Name, string? Q) : AppRoute;

    private sealed record SpacedRoute(string Name, string? Q, string? R) : AppRoute;

    private sealed record NumbersRoute(int Int, Guid Guid, decimal Decimal) : AppRoute;

    // Lists compare by their elements, so that a route read back is equal to the one written.
    private sealed record TagsRoute(int? Page, IReadOnlyList<string>? Tags, string? Sort) : AppRoute
    {
        public bool Equals(TagsRoute? other)
        {
            return other is not null && Page == other.Page && Sort == other.Sort
                && (Tags ?? []).SequenceEqual(other.Tags ?? []);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Page, Sort);
        }
    }

    // A list that counts the calls made to it, and can throw from MoveNext at an index.
    private sealed class RecordingList<T>(IReadOnlyList<T> items) : IEnumerable<T>
    {
        private int _enumerators;
        private int _moves;
        private int _disposals;

        public IReadOnlyList<T> Items { get; private set; } = items;

        public int ThrowAt { get; init; } = -1;

        public Exception? Thrown { get; init; }

        public (int Enumerators, int Moves, int Disposals) Counts => (_enumerators, _moves, _disposals);

        // Starts over with other items, and counts from zero.
        public void Replace(IReadOnlyList<T> items)
        {
            Items = items;
            (_enumerators, _moves, _disposals) = (0, 0, 0);
        }

        public IEnumerator<T> GetEnumerator()
        {
            _enumerators++;
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private sealed class Enumerator(RecordingList<T> owner) : IEnumerator<T>
        {
            private int _index = -1;

            public T Current => owner.Items[_index];

            object? IEnumerator.Current => Current;

            public bool MoveNext()
            {
                owner._moves++;
                if (_index + 1 == owner.ThrowAt)
                    throw owner.Thrown!;

                return ++_index < owner.Items.Count;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                owner._disposals++;
            }
        }
    }

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
