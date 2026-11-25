using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteFormatBuilderTests
{
    private const string NotDeclared = "does not declare path parameter";
    private const string NoSuchParameter = "has no parameter named";
    private const string AlreadyDeclared = "already declared";
    private const string Unsupported = "cannot be written as type";
    private const string NotRegistered = "AddEnumValueCodec";
    private const string BothFormat = "is mapped with a format function to both";

    [Fact]
    public void MappingWithAFormatFunctionReturnsTheSameBuilder()
    {
        var builder = new RouteTableBuilder();

        RouteTableBuilder returned =
            builder.Map("/items/{id}", c => new ItemRoute(c.Path("id")), f => f.Path("id", r => r.Id));

        Assert.Same(builder, returned);
    }

    [Fact]
    public void TheFormatActionRunsOnceDuringMap()
    {
        var calls = 0;
        var builder = new RouteTableBuilder();
        builder.Map("/items/{id}", c => new ItemRoute(c.Path("id")), f =>
        {
            calls++;
            f.Path("id", r => r.Id);
        });

        Assert.Equal(1, calls);

        RouteTable first = builder.Build();
        RouteTable second = builder.Build();
        Assert.Equal("/items/a", first.Format(new ItemRoute("a")));
        Assert.Equal("/items/b", second.Format(new ItemRoute("b")));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void PathReturnsTheSameFormatBuilder()
    {
        RouteFormatBuilder<PairRoute>? seen = null;
        new RouteTableBuilder().Map("/{a}/{b}", c => new PairRoute(c.Path("a"), c.Path("b")), f =>
        {
            seen = f;
            Assert.Same(f, f.Path("a", r => r.A));
            Assert.Same(f, f.Path("b", r => r.B));
        });

        Assert.NotNull(seen);
    }

    // Required, optional and catch-all parameters must all be declared, and the first left undeclared, in template
    // order, is the one named.
    [Theory]
    [InlineData("/{a}/{b}", "b")]
    [InlineData("/{a}/{b?}", "b")]
    [InlineData("/{a}/{*b}", "b")]
    [InlineData("/x/{b}/{a}", "b")]
    public void EveryParameterOfTheTemplateMustBeDeclared(string template, string undeclared)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => builder.Map(
            template,
            c => new PairRoute(c.Path("a"), "b"),
            f => f.Path("a", r => r.A)));

        Assert.Equal("format", exception.ParamName);
        Assert.Contains(NotDeclared, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{template}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{undeclared}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFirstUndeclaredParameterInTemplateOrderIsNamed()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new RouteTableBuilder().Map(
            "/{first}/{second}/{third?}",
            _ => new PairRoute("a", "b"),
            f => f.Path("second", r => r.A)));

        Assert.Equal("format", exception.ParamName);
        Assert.Contains("'first'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'third'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("")]
    [InlineData("i d")]
    [InlineData("items")]
    public void ANameThatIsNotAParameterIsRejected(string name)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new RouteTableBuilder().Map(
            "/items/{id}",
            c => new ItemRoute(c.Path("id")),
            f => f.Path(name, r => r.Id)));

        Assert.Equal("name", exception.ParamName);
        Assert.Contains(NoSuchParameter, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/items/{id}'", exception.Message, StringComparison.Ordinal);
    }

    // Names compare without regard to case, as the typed reads compare them, so a name in another case declares the
    // parameter, and declaring it again in any case is a repeat.
    [Fact]
    public void NamesAreComparedWithoutRegardToCase()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/items/{id}", c => new ItemRoute(c.Path("ID")), f => f.Path("ID", r => r.Id))
            .Build();

        Assert.Equal("/items/a", table.Format(new ItemRoute("a")));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("ID")]
    [InlineData("Id")]
    public void AParameterDeclaredTwiceIsRejected(string second)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new RouteTableBuilder().Map(
            "/items/{id}",
            c => new ItemRoute(c.Path("id")),
            f => f.Path("id", r => r.Id).Path(second, r => r.Id)));

        Assert.Equal("name", exception.ParamName);
        Assert.Contains(AlreadyDeclared, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'id'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        var builder = new RouteTableBuilder();

        Assert.Equal("template", Assert.Throws<ArgumentNullException>(() => builder.Map<ItemRoute>(
            null!, c => new ItemRoute("a"), _ => { })).ParamName);
        Assert.Equal("createRoute", Assert.Throws<ArgumentNullException>(() => builder.Map<ItemRoute>(
            "/a", null!, _ => { })).ParamName);
        Assert.Equal("format", Assert.Throws<ArgumentNullException>(() => builder.Map<ItemRoute>(
            "/a", _ => new ItemRoute("a"), null!)).ParamName);
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => builder.Map(
            "/{id}", c => new ItemRoute(c.Path("id")), f => f.Path(null!, r => r.Id))).ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => builder.Map(
            "/{id}", c => new ItemRoute(c.Path("id")), f => f.Path<string>("id", null!))).ParamName);

        Assert.Empty(builder.Build().Definitions);
    }

    [Fact]
    public void AnInvalidTemplateIsRejectedBeforeTheFormatActionRuns()
    {
        var called = false;

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new RouteTableBuilder().Map(
            "/{id",
            c => new ItemRoute(c.Path("id")),
            _ => called = true));

        Assert.Equal("template", exception.ParamName);
        Assert.False(called);
    }

    public static TheoryData<Action<RouteFormatBuilder<ItemRoute>>, string> UnsupportedTypes => new()
    {
        { f => f.Path("id", _ => Half.One), "Half" },
        { f => f.Path("id", _ => (DateTime?)null), "DateTime?" },
        { f => f.Path("id", _ => new object()), "Object" },
        { f => f.Path("id", _ => 'c'), "Char" },
        { f => f.Path("id", _ => Int128.One), "Int128" },
        { f => f.Path("id", _ => new[] { 1 }), "Int32[]" },
        { f => f.Path("id", r => (AppRoute)r), "AppRoute" },
    };

    [Theory]
    [MemberData(nameof(UnsupportedTypes))]
    public void AValueOfAnUnsupportedTypeIsRejectedByPath(Action<RouteFormatBuilder<ItemRoute>> declare, string type)
    {
        var builder = new RouteTableBuilder();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => builder.Map(
            "/{id}",
            c => new ItemRoute(c.Path("id")),
            declare));

        Assert.Contains(Unsupported, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{type}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ulong and any enum registered", exception.Message, StringComparison.Ordinal);
        Assert.Empty(builder.Build().Definitions);
    }

    // The type is checked first, so a type that can never be written fails even with a name that is not a parameter.
    [Fact]
    public void TheTypeIsCheckedBeforeTheName()
    {
        Assert.Throws<InvalidOperationException>(() => new RouteTableBuilder().Map(
            "/{id}",
            c => new ItemRoute(c.Path("id")),
            f => f.Path("other", _ => Half.One)));
    }

    // AddEnumValueCodec may come after Map, so an enum that is not registered is reported by Build, naming the enum,
    // the template and the method that registers it.
    [Fact]
    public void AnEnumThatIsNotRegisteredIsReportedByBuild()
    {
        RouteTableBuilder plain = new RouteTableBuilder()
            .Map("/s/{status}", c => new StatusRoute(c.Path<Status>("status")), f => f.Path("status", r => r.Status));
        RouteTableBuilder nullable = new RouteTableBuilder()
            .Map("/n/{status?}", _ => new StatusRoute(Status.Open), f => f.Path("status", _ => (Status?)null));

        foreach ((RouteTableBuilder builder, string template) in
                 new[] { (plain, "/s/{status}"), (nullable, "/n/{status?}") })
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);
            Assert.Contains(NotRegistered, exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Status), exception.Message, StringComparison.Ordinal);
            Assert.Contains($"{nameof(StatusRoute)}'", exception.Message, StringComparison.Ordinal);
            Assert.Contains($"'{template}'", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ARegistrationAfterMapServesTheFormatFunction()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .Map("/s/{status}", c => new StatusRoute(c.Path<Status>("status")), f => f.Path("status", r => r.Status));
        Assert.Throws<InvalidOperationException>(builder.Build);

        RouteTable table = builder.AddEnumValueCodec<Status>().Build();

        Assert.Equal("/s/Closed", table.Format(new StatusRoute(Status.Closed)));
    }

    [Fact]
    public void TwoMappingsOfOneTypeWithFormatFunctionsAreRejectedByBuild()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .Map("/home", _ => new HomeRoute(), _ => { })
            .Map("/settings", _ => new SettingsRoute())
            .Map("/start", _ => new HomeRoute(), _ => { });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(BothFormat, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{nameof(HomeRoute)}'", exception.Message, StringComparison.Ordinal);
        int home = exception.Message.IndexOf("'/home'", StringComparison.Ordinal);
        int start = exception.Message.IndexOf("'/start'", StringComparison.Ordinal);
        Assert.True(home >= 0 && start > home, exception.Message);
    }

    // Only mappings with format functions are limited, so several templates can create a route while one writes it.
    [Fact]
    public void MappingsWithoutFormatFunctionsAreNotLimited()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/", _ => new HomeRoute())
            .Map("/home", _ => new HomeRoute(), _ => { })
            .Map("/start", _ => new HomeRoute())
            .Build();

        Assert.Equal("/home", table.Format(new HomeRoute()));
        Assert.Equal(new HomeRoute(), table.Match(new Uri("/", UriKind.Relative)).Route);
        Assert.Equal(new HomeRoute(), table.Match(new Uri("/start", UriKind.Relative)).Route);
    }

    // The rule is about the exact type, so a base type and a type derived from it can each have a format function.
    [Fact]
    public void ABaseTypeAndADerivedTypeCanEachHaveAFormatFunction()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/base/{id}", c => new BaseRoute(c.Path("id")), f => f.Path("id", r => r.Id))
            .Map("/derived/{id}", c => new DerivedRoute(c.Path("id")), f => f.Path("id", r => r.Id))
            .Build();

        Assert.Equal("/base/a", table.Format(new BaseRoute("a")));
        Assert.Equal("/derived/a", table.Format(new DerivedRoute("a")));
    }

    [Fact]
    public void ATemplateWithNoParametersIsFormattableWithAnEmptyFormatAction()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/settings/profile", _ => new SettingsRoute(), _ => { })
            .Map("/", _ => new HomeRoute(), _ => { })
            .Build();

        Assert.Equal("/settings/profile", table.Format(new SettingsRoute()));
        Assert.Equal("/", table.Format(new HomeRoute()));
    }

    [Fact]
    public void TheTwoArgumentMapMakesAMappingThatCannotFormat()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/settings", _ => new SettingsRoute())
            .Build();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => table.Format(new SettingsRoute()));
        Assert.Contains("none of its mappings has a format function", exception.Message, StringComparison.Ordinal);
    }

    // Format looks a route up by its exact type, which is never abstract, so a format function for an abstract type
    // could never be used. That happens easily when the type argument is inferred from a create function that returns
    // a base type.
    [Fact]
    public void AnAbstractRouteTypeCannotBeGivenAFormatFunction()
    {
        RouteTableBuilder builder = new RouteTableBuilder().Map("/settings", _ => new SettingsRoute(), _ => { });
        var called = false;

        ArgumentException explicitBase = Assert.Throws<ArgumentException>(() => builder.Map<AppRoute>(
            "/home", _ => new HomeRoute(), _ => called = true));
        ArgumentException inferred = Assert.Throws<ArgumentException>(() => builder.Map(
            "/shape/{id}",
            c => c.Path("id") == "c" ? new CircleRoute(c.Path("id")) : (ShapeRoute)new SquareRoute(c.Path("id")),
            f => f.Path("id", r => r.Id)));

        foreach ((ArgumentException exception, string type) in
                 new[] { (explicitBase, nameof(AppRoute)), (inferred, nameof(ShapeRoute)) })
        {
            Assert.Equal("TRoute", exception.ParamName);
            Assert.Contains("is abstract", exception.Message, StringComparison.Ordinal);
            Assert.Contains($"{type}'", exception.Message, StringComparison.Ordinal);
        }

        Assert.False(called);
        RouteTable table = builder
            .Map("/shape/{id}", c => (ShapeRoute)new CircleRoute(c.Path("id")))
            .Build();
        Assert.Equal(2, table.Definitions.Count);
        Assert.Equal("/settings", table.Format(new SettingsRoute()));
        Assert.Equal(new CircleRoute("x"), table.Match(new Uri("/shape/x", UriKind.Relative)).Route);
    }

    public static TheoryData<string, Action<RouteTableBuilder>> FailedMappings => new()
    {
        {
            "undeclared",
            b => b.Map("/items/{id}/{tab?}", c => new ItemRoute(c.Path("id")), f => f.Path("id", r => r.Id))
        },
        { "unknown name", b => b.Map("/items/{id}", c => new ItemRoute(c.Path("id")), f => f.Path("x", r => r.Id)) },
        {
            "repeated",
            b => b.Map(
                "/items/{id}",
                c => new ItemRoute(c.Path("id")),
                f => f.Path("id", r => r.Id).Path("Id", r => r.Id))
        },
        { "unsupported", b => b.Map("/items/{id}", c => new ItemRoute(c.Path("id")), f => f.Path("id", _ => 'c')) },
        {
            "throwing action",
            b => b.Map("/items/{id}", c => new ItemRoute(c.Path("id")), _ => throw new FormatTestException())
        },
    };

    // A mapping that fails leaves no definition behind, so the builder builds exactly what it built before, and the
    // failed mapping's template and route type are free for another mapping.
    [Theory]
    [MemberData(nameof(FailedMappings))]
    public void AFailedMapLeavesTheBuilderUnchanged(string description, Action<RouteTableBuilder> map)
    {
        RouteTableBuilder builder = new RouteTableBuilder().Map("/settings", _ => new SettingsRoute(), _ => { });

        Exception exception = Assert.ThrowsAny<Exception>(() => map(builder));
        Assert.True(exception is ArgumentException or InvalidOperationException or FormatTestException, description);

        RouteTable table = builder
            .Map("/items/{id}", c => new ItemRoute(c.Path("id")), f => f.Path("id", r => r.Id))
            .Build();
        Assert.Equal(2, table.Definitions.Count);
        Assert.Equal("/items/a", table.Format(new ItemRoute("a")));
    }

    [Fact]
    public void AnExceptionFromTheFormatActionPropagatesUnchanged()
    {
        var thrown = new FormatTestException();

        FormatTestException caught = Assert.Throws<FormatTestException>(() => new RouteTableBuilder().Map(
            "/items/{id}",
            c => new ItemRoute(c.Path("id")),
            _ => throw thrown));

        Assert.Same(thrown, caught);
    }

    // A builder kept past the call to Map can no longer declare anything, whether the mapping succeeded or not.
    [Fact]
    public void AFormatBuilderCannotBeUsedAfterMapReturns()
    {
        RouteFormatBuilder<ItemRoute>? kept = null;
        RouteFormatBuilder<ItemRoute>? failed = null;
        var builder = new RouteTableBuilder();
        builder.Map("/items/{id}", c => new ItemRoute(c.Path("id")), f =>
        {
            kept = f;
            f.Path("id", r => r.Id);
        });
        Assert.Throws<ArgumentException>(() => builder.Map("/other/{id}", c => new ItemRoute(c.Path("id")), f =>
        {
            failed = f;
        }));

        Assert.Throws<InvalidOperationException>(() => kept!.Path("id", r => r.Id));
        Assert.Throws<InvalidOperationException>(() => failed!.Path("id", r => r.Id));
        Assert.Equal("/items/a", builder.Build().Format(new ItemRoute("a")));
    }

    // Build checks the templates first, so a repeated template is reported even when the format functions also
    // conflict.
    [Fact]
    public void BuildReportsTemplateConflictsBeforeFormatFunctions()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .Map("/a", _ => new HomeRoute(), _ => { })
            .Map("/b", _ => new HomeRoute(), _ => { })
            .Map("/a", _ => new SettingsRoute());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("are the same template", exception.Message, StringComparison.Ordinal);
    }

    public sealed record ItemRoute(string Id) : AppRoute;

    private sealed record PairRoute(string A, string B) : AppRoute;

    private sealed record HomeRoute : AppRoute;

    private sealed record StatusRoute(Status Status) : AppRoute;

    private record BaseRoute(string Id) : AppRoute;

    private sealed record DerivedRoute(string Id) : BaseRoute(Id);

    private abstract record ShapeRoute(string Id) : AppRoute;

    private sealed record CircleRoute(string Id) : ShapeRoute(Id);

    private sealed record SquareRoute(string Id) : ShapeRoute(Id);

    private enum Status
    {
        Open,
        Closed,
    }

    private sealed class FormatTestException : Exception;
}
