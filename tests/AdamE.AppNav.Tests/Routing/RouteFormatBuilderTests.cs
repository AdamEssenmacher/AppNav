using System.Collections.Immutable;
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

    // Registering the enum would not make the second mapping valid, so the second format function is what Build
    // reports: otherwise fixing the table would take two rounds.
    [Fact]
    public void ASecondFormatFunctionIsReportedBeforeAnEnumThatIsNotRegistered()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .Map("/a/{status}", c => new StatusRoute(c.Path<Status>("status")), f => f.Path("status", r => r.Status))
            .Map("/b/{status}", c => new StatusRoute(c.Path<Status>("status")), f => f.Path("status", r => r.Status));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(BothFormat, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NotRegistered, exception.Message, StringComparison.Ordinal);
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
            "repeated query",
            b => b.Map(
                "/items/{id}",
                c => new ItemRoute(c.Path("id")),
                f => f.Path("id", r => r.Id).Query("q", r => r.Id).Query("Q", r => r.Id))
        },
        {
            "unsupported query",
            b => b.Map(
                "/items/{id}",
                c => new ItemRoute(c.Path("id")),
                f => f.Path("id", r => r.Id).Query("q", _ => 'c'))
        },
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

    [Fact]
    public void QueryReturnsTheSameFormatBuilder()
    {
        RouteFormatBuilder<ItemRoute>? seen = null;
        new RouteTableBuilder().Map("/{id}", c => new ItemRoute(c.Path("id")), f =>
        {
            seen = f;
            Assert.Same(f, f.Query("q", r => r.Id));
            Assert.Same(f, f.Path("id", r => r.Id));
            Assert.Same(f, f.Query("r", _ => 1));
        });

        Assert.NotNull(seen);
    }

    [Fact]
    public void QueryRejectsNullArguments()
    {
        var builder = new RouteTableBuilder();

        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => builder.Map(
            "/a", _ => new ItemRoute("a"), f => f.Query(null!, r => r.Id))).ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => builder.Map(
            "/a", _ => new ItemRoute("a"), f => f.Query<string>("q", null!))).ParamName);

        Assert.Empty(builder.Build().Definitions);
    }

    // Names compare as the query reads compare them, ordinally ignoring case, so a second declaration in any case would
    // write a value that no read returns.
    [Theory]
    [InlineData("page", "page")]
    [InlineData("page", "PAGE")]
    [InlineData("Page", "pAge")]
    [InlineData("", "")]
    [InlineData("\u00e9", "\u00c9")]
    public void AQueryNameDeclaredTwiceIsRejected(string first, string second)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => builder.Map(
            "/a",
            _ => new ItemRoute("a"),
            f => f.Query(first, r => r.Id).Query("other", r => r.Id).Query(second, _ => 1)));

        Assert.Equal("name", exception.ParamName);
        Assert.Contains(AlreadyDeclared, exception.Message, StringComparison.Ordinal);
        Assert.Empty(builder.Build().Definitions);
    }

    // Names that differ other than by case, under ordinal comparison, are different names, as on the read side.
    [Fact]
    public void QueryNamesThatDifferOnlyByNormalizationAreDifferentNames()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/a", _ => new ItemRoute("a"), f => f.Query("\u00e9", _ => 1).Query("e\u0301", _ => 2))
            .Build();

        Assert.Equal("/a?%C3%A9=1&e%CC%81=2", table.Format(new ItemRoute("a")));
    }

    // The path and the query are read separately, so a query value can have the name of a path parameter.
    [Fact]
    public void AQueryNameCanBeThatOfAPathParameter()
    {
        RouteTable table = new RouteTableBuilder()
            .Map(
                "/p/{id}",
                c => new PairRoute(c.Path("id"), c.TryGetQuery("id", out string? q) ? q : "none"),
                f => f.Query("ID", r => r.B).Path("id", r => r.A))
            .Build();

        string text = table.Format(new PairRoute("x", "y"));

        Assert.Equal("/p/x?ID=y", text);
        Assert.Equal(new PairRoute("x", "y"), table.Match(new Uri(text, UriKind.Relative)).Route);
    }

    [Fact]
    public void AQueryNameWithALoneSurrogateIsRejected()
    {
        foreach (string name in new[] { "a\ud800", "\udc00", "\ud800\ud800" })
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => new RouteTableBuilder().Map(
                "/a", _ => new ItemRoute("a"), f => f.Query(name, r => r.Id)));

            Assert.Equal("name", exception.ParamName);
            Assert.Contains("lone surrogate", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(name, exception.Message, StringComparison.Ordinal);
        }
    }

    public static TheoryData<Action<RouteFormatBuilder<ItemRoute>>, string> UnsupportedQueryTypes => new()
    {
        { f => f.Query("q", _ => Half.One), "Half" },
        { f => f.Query("q", _ => (DateTime?)null), "DateTime?" },
        { f => f.Query("q", _ => new object()), "Object" },
        { f => f.Query("q", _ => 'c'), "Char" },
        { f => f.Query("q", r => (AppRoute)r), "AppRoute" },
        { f => f.Query("q", _ => new Uri("/", UriKind.Relative)), "Uri" },
    };

    [Theory]
    [MemberData(nameof(UnsupportedQueryTypes))]
    public void AValueOfAnUnsupportedTypeIsRejectedByQuery(Action<RouteFormatBuilder<ItemRoute>> declare, string type)
    {
        var builder = new RouteTableBuilder();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => builder.Map(
            "/a",
            _ => new ItemRoute("a"),
            declare));

        Assert.Contains("A query value cannot be written as type", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{type}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ulong and any enum registered", exception.Message, StringComparison.Ordinal);
        Assert.Contains("convert the value to a string", exception.Message, StringComparison.Ordinal);
        Assert.Empty(builder.Build().Definitions);
    }

    public static TheoryData<Action<RouteFormatBuilder<ItemRoute>>, string> CollectionQueryTypes => new()
    {
        { f => f.Query("q", _ => new[] { "a" }), "String[]" },
        { f => f.Query("q", _ => new[] { 1 }), "Int32[]" },
        { f => f.Query("q", _ => new List<int>()), "List<Int32>" },
        { f => f.Query("q", _ => (IEnumerable<string>)[]), "IEnumerable<String>" },
        { f => f.Query("q", _ => (IReadOnlyList<Guid>)[]), "IReadOnlyList<Guid>" },
        { f => f.Query("q", _ => new HashSet<string>()), "HashSet<String>" },
        { f => f.Query("q", _ => new Dictionary<string, string>()), "Dictionary<String, String>" },
        { f => f.Query("q", _ => (ImmutableArray<int>?)null), "ImmutableArray<Int32>?" },
    };

    // A collection is not supported, and converting it to one string would write a value that the typed read of the
    // item type rejects, so the message does not advise that.
    [Theory]
    [MemberData(nameof(CollectionQueryTypes))]
    public void ACollectionIsRejectedByQueryWithoutAdvisingAString(
        Action<RouteFormatBuilder<ItemRoute>> declare,
        string type)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new RouteTableBuilder().Map("/a", _ => new ItemRoute("a"), declare));

        Assert.Contains("A query value cannot be written as type", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{type}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("A collection is not one of them", exception.Message, StringComparison.Ordinal);
        Assert.Contains("declare several values for one name with QueryValues", exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("not supported in this version", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("convert", exception.Message, StringComparison.Ordinal);
    }

    // The path message for a collection is as it was.
    [Fact]
    public void ACollectionIsRejectedByPathAsBefore()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new RouteTableBuilder().Map(
                "/{id}",
                c => new ItemRoute(c.Path("id")),
                f => f.Path("id", _ => new[] { 1 })));

        Assert.Contains("A path value cannot be written as type", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Int32[]'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("convert the value to a string", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("collection", exception.Message, StringComparison.Ordinal);
    }

    // The type is checked first, as for Path, so a type that can never be written fails even for a repeated name.
    [Fact]
    public void QueryChecksTheTypeBeforeTheName()
    {
        Assert.Throws<InvalidOperationException>(() => new RouteTableBuilder().Map(
            "/a",
            _ => new ItemRoute("a"),
            f => f.Query("q", r => r.Id).Query("q", _ => Half.One)));
    }

    [Fact]
    public void AQueryEnumThatIsNotRegisteredIsReportedByBuild()
    {
        RouteTableBuilder plain = new RouteTableBuilder()
            .Map("/s", _ => new StatusRoute(Status.Open), f => f.Query("status", r => r.Status));
        RouteTableBuilder nullable = new RouteTableBuilder()
            .Map("/n", _ => new StatusRoute(Status.Open), f => f.Query("status", _ => (Status?)null));

        foreach ((RouteTableBuilder builder, string template) in new[] { (plain, "/s"), (nullable, "/n") })
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);
            Assert.Contains(NotRegistered, exception.Message, StringComparison.Ordinal);
            Assert.Contains("query value 'status'", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("path parameter", exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Status), exception.Message, StringComparison.Ordinal);
            Assert.Contains($"{nameof(StatusRoute)}'", exception.Message, StringComparison.Ordinal);
            Assert.Contains($"'{template}'", exception.Message, StringComparison.Ordinal);
        }

        RouteTable table = plain.AddEnumValueCodec<Status>().Build();
        Assert.Equal("/s?status=Closed", table.Format(new StatusRoute(Status.Closed)));
    }

    // A query name is quoted in a message with its control characters escaped, so that it cannot break the line.
    [Fact]
    public void AQueryNameIsQuotedSafelyInMessages()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new RouteTableBuilder().Map(
            "/a",
            _ => new ItemRoute("a"),
            f => f.Query("a\nb", r => r.Id).Query("A\nB", r => r.Id)));

        Assert.Contains("'a\\u000Ab'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("as 'a\\u000Ab'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", exception.Message, StringComparison.Ordinal);
    }

    // The spelling the name was first declared with is added only when it differs from the repeated one.
    [Fact]
    public void ARepeatedQueryNameIsNamedOnceWhenSpelledTheSame()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new RouteTableBuilder().Map(
            "/a",
            _ => new ItemRoute("a"),
            f => f.Query("page", r => r.Id).Query("page", r => r.Id)));

        Assert.Contains("'page' is already declared;", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(", as ", exception.Message, StringComparison.Ordinal);
    }

    // The quoted name, escaped, is what a failure to write the value and an enum that is not registered name.
    [Fact]
    public void AQueryNameIsQuotedSafelyWhenAValueCannotBeWritten()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/a", _ => new ItemRoute("a"), f => f.Query("a\nb", _ => double.NaN))
            .Build();
        RouteTableBuilder unregistered = new RouteTableBuilder()
            .Map("/a", _ => new StatusRoute(Status.Open), f => f.Query("a\nb", r => r.Status));

        ArgumentException notWritten = Assert.Throws<ArgumentException>(() => table.Format(new ItemRoute("a")));
        InvalidOperationException notRegistered = Assert.Throws<InvalidOperationException>(unregistered.Build);

        foreach (string message in new[] { notWritten.Message, notRegistered.Message })
        {
            Assert.Contains("query value 'a\\u000Ab'", message, StringComparison.Ordinal);
            Assert.DoesNotContain("\n", message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AFormatBuilderCannotDeclareAQueryValueAfterMapReturns()
    {
        RouteFormatBuilder<ItemRoute>? kept = null;
        var builder = new RouteTableBuilder();
        builder.Map("/items", _ => new ItemRoute("a"), f =>
        {
            kept = f;
            f.Query("q", r => r.Id);
        });

        Assert.Throws<InvalidOperationException>(() => kept!.Query("r", r => r.Id));
        Assert.Equal("/items?q=a", builder.Build().Format(new ItemRoute("a")));
    }

    [Fact]
    public void QueryValuesReturnsTheSameFormatBuilder()
    {
        RouteFormatBuilder<ItemRoute>? seen = null;
        new RouteTableBuilder().Map("/{id}", c => new ItemRoute(c.Path("id")), f =>
        {
            seen = f;
            Assert.Same(f, f.QueryValues("a", _ => new[] { "x" }));
            Assert.Same(f, f.Path("id", r => r.Id));
            Assert.Same(f, f.Query("b", r => r.Id));
            Assert.Same(f, f.QueryValues("c", _ => new List<int>()));
        });

        Assert.NotNull(seen);
    }

    [Fact]
    public void QueryValuesRejectsNullArguments()
    {
        var builder = new RouteTableBuilder();

        Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => builder.Map(
            "/a", _ => new ItemRoute("a"), f => f.QueryValues(null!, _ => new[] { 1 }))).ParamName);
        Assert.Equal("values", Assert.Throws<ArgumentNullException>(() => builder.Map(
            "/a", _ => new ItemRoute("a"), f => f.QueryValues<string>("q", null!))).ParamName);

        Assert.Empty(builder.Build().Definitions);
    }

    // The element types are those the read of a list accepts: every built-in type and any enum, never a nullable form.
    [Fact]
    public void QueryValuesAcceptsEveryTypeThatTheReadOfAListReads()
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .Map("/a", _ => new ItemRoute("a"), f => f
                .QueryValues("string", _ => new[] { "s" })
                .QueryValues("bool", _ => new[] { true })
                .QueryValues("guid", _ => new[] { Guid.Empty })
                .QueryValues("decimal", _ => new[] { 1.5m })
                .QueryValues("double", _ => new[] { 2.5 })
                .QueryValues("float", _ => new[] { 3.5f })
                .QueryValues("sbyte", _ => new sbyte[] { -1 })
                .QueryValues("byte", _ => new byte[] { 1 })
                .QueryValues("short", _ => new short[] { -2 })
                .QueryValues("ushort", _ => new ushort[] { 2 })
                .QueryValues("int", _ => new[] { -3 })
                .QueryValues("uint", _ => new uint[] { 3 })
                .QueryValues("long", _ => new long[] { -4 })
                .QueryValues("ulong", _ => new ulong[] { 4 })
                .QueryValues("status", _ => new[] { Status.Closed }))
            .Build();

        Assert.Equal(
            "/a?string=s&bool=true&guid=00000000-0000-0000-0000-000000000000&decimal=1.5&double=2.5&float=3.5" +
            "&sbyte=-1&byte=1&short=-2&ushort=2&int=-3&uint=3&long=-4&ulong=4&status=Closed",
            table.Format(new ItemRoute("a")));
    }

    public static TheoryData<Action<RouteFormatBuilder<ItemRoute>>, string> UnsupportedListTypes => new()
    {
        { f => f.QueryValues("q", _ => new[] { Half.One }), "Half" },
        { f => f.QueryValues("q", _ => new[] { DateTime.MinValue }), "DateTime" },
        { f => f.QueryValues("q", _ => new DateTime?[] { null }), "DateTime?" },
        { f => f.QueryValues("q", _ => new[] { new object() }), "Object" },
        { f => f.QueryValues("q", _ => new[] { new Uri("/", UriKind.Relative) }), "Uri" },
        { f => f.QueryValues("q", _ => new Dictionary<string, string>()), "KeyValuePair<String, String>" },
    };

    // The message lists the supported types without nullable forms, which a list does not have.
    [Theory]
    [MemberData(nameof(UnsupportedListTypes))]
    public void AnElementOfAnUnsupportedTypeIsRejectedByQueryValues(
        Action<RouteFormatBuilder<ItemRoute>> declare,
        string type)
    {
        var builder = new RouteTableBuilder();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => builder.Map(
            "/a",
            _ => new ItemRoute("a"),
            declare));

        Assert.Contains("A list of query values cannot be written as type", exception.Message,
            StringComparison.Ordinal);
        Assert.Contains($"'{type}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ulong and any enum registered", exception.Message, StringComparison.Ordinal);
        Assert.Contains("convert the values to strings", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("nullable", exception.Message, StringComparison.Ordinal);
        Assert.Empty(builder.Build().Definitions);
    }

    public static TheoryData<Action<RouteFormatBuilder<ItemRoute>>, string> NestedListTypes => new()
    {
        { f => f.QueryValues("q", _ => new[] { new[] { "a" } }), "String[]" },
        { f => f.QueryValues("q", _ => new List<List<int>>()), "List<Int32>" },
        { f => f.QueryValues("q", _ => new[] { new HashSet<string>() }), "HashSet<String>" },
        { f => f.QueryValues("q", _ => new ImmutableArray<int>?[] { null }), "ImmutableArray<Int32>?" },
    };

    // An element that is itself a collection is told to be a list of its own, not to be converted to a string.
    [Theory]
    [MemberData(nameof(NestedListTypes))]
    public void AnElementThatIsACollectionIsRejectedByQueryValues(
        Action<RouteFormatBuilder<ItemRoute>> declare,
        string type)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new RouteTableBuilder().Map("/a", _ => new ItemRoute("a"), declare));

        Assert.Contains($"'{type}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("is itself a collection", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("convert", exception.Message, StringComparison.Ordinal);
    }

    // A string is a list of chars, so a function that returns one makes the element type char, and the message says
    // to declare the string with Query rather than to convert the values to strings.
    [Fact]
    public void AStringGivenToQueryValuesIsToldToUseQuery()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new RouteTableBuilder().Map("/a", _ => new ItemRoute("a"), f => f.QueryValues("q", r => r.Id)));

        Assert.Contains("A string is one value", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("convert", exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<Action<RouteFormatBuilder<ItemRoute>>> NullableListTypes => new()
    {
        f => f.QueryValues("q", _ => new int?[] { 1 }),
        f => f.QueryValues("q", _ => new List<Guid?>()),
        f => f.QueryValues("q", _ => (IEnumerable<bool?>)[]),
        f => f.QueryValues("q", _ => new Status?[] { Status.Open }),
    };

    // An element of a list is never absent, so a nullable element type is rejected, as the read of a list rejects it,
    // whether or not the enum of a nullable enum is registered.
    [Theory]
    [MemberData(nameof(NullableListTypes))]
    public void ANullableElementTypeIsRejectedByQueryValues(Action<RouteFormatBuilder<ItemRoute>> declare)
    {
        foreach (RouteTableBuilder builder in new[] { new RouteTableBuilder(), new RouteTableBuilder()
                     .AddEnumValueCodec<Status>() })
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => builder.Map(
                "/a",
                _ => new ItemRoute("a"),
                declare));

            Assert.Contains("never absent", exception.Message, StringComparison.Ordinal);
            Assert.Empty(builder.Build().Definitions);
        }
    }

    [Fact]
    public void AListEnumThatIsNotRegisteredIsReportedByBuild()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .Map("/s", _ => new StatusRoute(Status.Open), f => f.QueryValues("status", r => new[] { r.Status }));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(NotRegistered, exception.Message, StringComparison.Ordinal);
        Assert.Contains("query value 'status'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{nameof(Status)}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{nameof(StatusRoute)}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/s'", exception.Message, StringComparison.Ordinal);

        RouteTable table = builder.AddEnumValueCodec<Status>().Build();
        Assert.Equal("/s?status=Closed", table.Format(new StatusRoute(Status.Closed)));
    }

    // One name has one declaration, whichever method made it and whichever came first, compared ordinally without
    // regard to case.
    [Theory]
    [InlineData("tag", "tag")]
    [InlineData("tag", "TAG")]
    [InlineData("Tag", "tAg")]
    [InlineData("", "")]
    [InlineData("\u00e9", "\u00c9")]
    public void AQueryNameIsDeclaredOnceAcrossQueryAndQueryValues(string first, string second)
    {
        var declarations = new (string Description, Action<RouteFormatBuilder<ItemRoute>> Declare)[]
        {
            ("Query, QueryValues", f => f.Query(first, r => r.Id).Query("other", r => r.Id)
                .QueryValues(second, _ => new[] { 1 })),
            ("QueryValues, Query", f => f.QueryValues(first, _ => new[] { "a" }).QueryValues("other", _ => new[] { 1 })
                .Query(second, r => r.Id)),
            ("QueryValues, QueryValues", f => f.QueryValues(first, _ => new[] { "a" })
                .QueryValues(second, _ => new[] { 2 })),
        };

        foreach ((string description, Action<RouteFormatBuilder<ItemRoute>> declare) in declarations)
        {
            var builder = new RouteTableBuilder();

            ArgumentException exception = Assert.Throws<ArgumentException>(() => builder.Map(
                "/a",
                _ => new ItemRoute("a"),
                declare));

            Assert.True(exception.ParamName == "name", description);
            Assert.Contains(AlreadyDeclared, exception.Message, StringComparison.Ordinal);
            Assert.Empty(builder.Build().Definitions);
        }
    }

    // A repeated name is reported the same way by both methods, with the first spelling when it differs.
    [Fact]
    public void ARepeatedListNameIsNamedAsAQueryValue()
    {
        ArgumentException same = Assert.Throws<ArgumentException>(() => new RouteTableBuilder().Map(
            "/a",
            _ => new ItemRoute("a"),
            f => f.Query("tag", r => r.Id).QueryValues("tag", _ => new[] { "a" })));
        ArgumentException spelled = Assert.Throws<ArgumentException>(() => new RouteTableBuilder().Map(
            "/a",
            _ => new ItemRoute("a"),
            f => f.QueryValues("tag", _ => new[] { "a" }).Query("Tag", r => r.Id)));

        Assert.Equal("name", same.ParamName);
        Assert.Equal("name", spelled.ParamName);
        Assert.Contains(AlreadyDeclared, same.Message, StringComparison.Ordinal);
        Assert.Contains(", as 'tag'", spelled.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryValuesChecksTheTypeBeforeTheName()
    {
        Assert.Throws<InvalidOperationException>(() => new RouteTableBuilder().Map(
            "/a",
            _ => new ItemRoute("a"),
            f => f.Query("q", r => r.Id).QueryValues("q", _ => new[] { Half.One })));
        Assert.Throws<InvalidOperationException>(() => new RouteTableBuilder().Map(
            "/a",
            _ => new ItemRoute("a"),
            f => f.QueryValues("q\ud800", _ => new int?[] { 1 })));
    }

    [Fact]
    public void AListNameWithALoneSurrogateIsRejected()
    {
        foreach (string name in new[] { "a\ud800", "\udc00", "\ud800\ud800" })
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => new RouteTableBuilder().Map(
                "/a", _ => new ItemRoute("a"), f => f.QueryValues(name, _ => new[] { "x" })));

            Assert.Equal("name", exception.ParamName);
            Assert.Contains("lone surrogate", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(name, exception.Message, StringComparison.Ordinal);
        }
    }

    // A list can have the empty name, and the name of a path parameter, which is read separately.
    [Fact]
    public void AListNameCanBeEmptyOrThatOfAPathParameter()
    {
        RouteTable table = new RouteTableBuilder()
            .Map(
                "/p/{id}",
                c => new PairRoute(c.Path("id"), string.Join(",", c.GetQueryValues("ID"))),
                f => f.QueryValues("ID", r => r.B.Split(',')).Path("id", r => r.A).QueryValues("", _ => new[] { 1 }))
            .Build();

        string text = table.Format(new PairRoute("x", "y,z"));

        Assert.Equal("/p/x?ID=y&ID=z&=1", text);
        Assert.Equal(new PairRoute("x", "y,z"), table.Match(new Uri(text, UriKind.Relative)).Route);
    }

    [Fact]
    public void AFormatBuilderCannotDeclareAListAfterMapReturns()
    {
        RouteFormatBuilder<ItemRoute>? kept = null;
        var builder = new RouteTableBuilder();
        builder.Map("/items", _ => new ItemRoute("a"), f =>
        {
            kept = f;
            f.QueryValues("q", r => new[] { r.Id });
        });

        Assert.Throws<InvalidOperationException>(() => kept!.QueryValues("r", r => new[] { r.Id }));
        Assert.Throws<InvalidOperationException>(() => kept!.QueryValues("r", _ => new[] { Half.One }));
        Assert.Equal("/items?q=a", builder.Build().Format(new ItemRoute("a")));
    }

    // TValue is inferred from the function for the common shapes of a list, a struct and a registered enum included.
    [Fact]
    public void TheElementTypeIsInferredFromCommonListShapes()
    {
        var route = new ShapesRoute(
            ["a", "b"],
            [1, 2],
            [new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff")],
            ["h"],
            [3, 4],
            Longs(),
            [Status.Open, Status.Closed]);
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .Map("/s", _ => route, f => f
                .QueryValues("array", r => r.Array)
                .QueryValues("list", r => r.List)
                .QueryValues("readOnly", r => r.ReadOnly)
                .QueryValues("set", r => r.Set)
                .QueryValues("immutable", r => r.Immutable)
                .QueryValues("enumerable", r => r.Enumerable)
                .QueryValues("enum", r => r.Statuses))
            .Build();

        Assert.Equal(
            "/s?array=a&array=b&list=1&list=2&readOnly=6f9619ff-8b86-d011-b42d-00cf4fc964ff&set=h&immutable=3" +
            "&immutable=4&enumerable=-5&enumerable=6&enum=Open&enum=Closed",
            table.Format(route));

        static IEnumerable<long> Longs()
        {
            yield return -5;
            yield return 6;
        }
    }

    public sealed record ItemRoute(string Id) : AppRoute;

    private sealed record PairRoute(string A, string B) : AppRoute;

    private sealed record HomeRoute : AppRoute;

    private sealed record StatusRoute(Status Status) : AppRoute;

    private sealed record ShapesRoute(
        string[] Array,
        List<int> List,
        IReadOnlyList<Guid> ReadOnly,
        HashSet<string> Set,
        ImmutableArray<int> Immutable,
        IEnumerable<long> Enumerable,
        Status[] Statuses) : AppRoute;

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
