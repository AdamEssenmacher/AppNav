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

    // The table promises that the template reads back what it writes, not that the table picks the same mapping: a
    // template that ranks higher and matches the same path wins.
    [Fact]
    public void AHigherRankingTemplateCanMatchAWrittenPathFirst()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/products/new", _ => new SettingsRoute())
            .Map("/products/{name}", c => new TextRoute(c.Path("name")), f => f.Path("name", r => r.Text))
            .Build();

        string path = table.Format(new TextRoute("new"));

        Assert.Equal("/products/new", path);
        Assert.Equal(new SettingsRoute(), Match(table, path).Route);
        Assert.Equal(new TextRoute("new"), RouteTemplate.Parse("/products/{name}").Match(path) is { } values
            ? new TextRoute(values["name"])
            : null);
        AssertRoundTrip(table, new TextRoute("old"));
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

    private record BaseRoute(string Id) : AppRoute;

    private sealed record DerivedRoute(string Id) : BaseRoute(Id);

    [Flags]
    private enum Access
    {
        Read = 1,
        Write = 2,
    }
}
