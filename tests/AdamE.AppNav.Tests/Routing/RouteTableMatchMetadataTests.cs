using System.Globalization;
using System.Text;
using AdamE.AppNav.Routing;
using Xunit;
using Custom = AdamE.AppNav.Tests.Routing.CustomValueTypes;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTableMatchMetadataTests
{
    // Text that stands for a secret a query value can carry, distinctive enough that finding it in a diagnostic is not
    // a coincidence.
    private const string Secret = "tok-Vn3Jq8Rw-9Wq4Lm7Tz";

    private static readonly RouteMetadataKey<string> Campaign = new("campaign");
    private static readonly RouteMetadataKey<int> Step = new("step");
    private static readonly RouteMetadataKey<bool> Preview = new("preview");
    private static readonly RouteMetadataKey<Guid> Session = new("session");
    private static readonly RouteMetadataKey<Level> Tier = new("tier");
    private static readonly RouteMetadataKey<Custom.ProductId> Product = new("product");
    private static readonly RouteMetadataKey<Custom.Stage> Stage = new("stage");

    [Fact]
    public void AFailedMatchHasNoMetadata()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();

        RouteMatchResult result = Match(table, "/orders/1?campaign=spring");

        Assert.False(result.IsSuccess);
        Assert.Same(RouteMetadata.Empty, result.Metadata);
    }

    [Fact]
    public void ATableThatDeclaresNoKeysGivesNoMetadata()
    {
        RouteTable table = ItemsBuilder().Build();

        RouteMatchResult result = Match(table, "/items/1?campaign=spring&step=2");

        Assert.True(result.IsSuccess);
        Assert.Same(RouteMetadata.Empty, result.Metadata);
    }

    [Theory]
    [InlineData("/items/1")]
    [InlineData("/items/1?")]
    [InlineData("/items/1?other=x&step2=3")]
    [InlineData("/items/1#campaign=spring")]
    [InlineData("https://example.com/items/1?other=x")]
    public void AQueryWithNoneOfTheNamesGivesNoMetadata(string uri)
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).AddQueryMetadata(Step).Build();

        RouteMatchResult result = Match(table, uri);

        Assert.True(result.IsSuccess);
        Assert.Same(RouteMetadata.Empty, result.Metadata);
    }

    [Fact]
    public void OneDeclaredKeyIsRead()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();

        RouteMatchResult result = Match(table, "/items/42?campaign=spring");

        Assert.Equal(new ItemRoute(42), result.Route);
        Assert.Equal(RouteMetadata.Empty.With(Campaign, "spring"), result.Metadata);
        Assert.Empty(result.Diagnostics);
    }

    // Equality does not depend on the order of the entries, so the order the keys were declared in is not visible.
    [Fact]
    public void SeveralDeclaredKeysAreReadWhateverTheOrderOfTheQuery()
    {
        RouteTable table = ItemsBuilder()
            .AddQueryMetadata(Step)
            .AddQueryMetadata(Campaign)
            .AddQueryMetadata(Preview)
            .Build();

        RouteMatchResult result = Match(table, "/items/1?preview=true&other=1&campaign=spring&step=2");

        Assert.Equal(
            RouteMetadata.Empty.With(Campaign, "spring").With(Preview, true).With(Step, 2),
            result.Metadata);
        Assert.Equal(3, result.Metadata.Count);
        Assert.Empty(result.Diagnostics);
    }

    // The keys are read in the order they were declared, so of two values that are not valid, the one of the key
    // declared first is reported, whatever order the query gives them in.
    [Theory]
    [InlineData(true, "/items/1?step=x&preview=x", "step")]
    [InlineData(true, "/items/1?preview=x&step=x", "step")]
    [InlineData(false, "/items/1?step=x&preview=x", "preview")]
    [InlineData(false, "/items/1?preview=x&step=x", "preview")]
    public void TheKeysAreReadInTheOrderTheyWereDeclared(bool stepFirst, string uri, string reported)
    {
        RouteTableBuilder builder = ItemsBuilder();
        RouteTable table = (stepFirst
                ? builder.AddQueryMetadata(Step).AddQueryMetadata(Preview)
                : builder.AddQueryMetadata(Preview).AddQueryMetadata(Step))
            .Build();

        RouteMatchResult result = Match(table, uri);

        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal(reported, diagnostic.Data["name"]);
    }

    [Fact]
    public void OnlyTheKeysWhoseNamesTheQueryHasHaveEntries()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).AddQueryMetadata(Step).Build();

        RouteMatchResult result = Match(table, "/items/1?step=3&campaign2=x");

        Assert.Equal(RouteMetadata.Empty.With(Step, 3), result.Metadata);
        Assert.False(result.Metadata.TryGet(Campaign, out _));
    }

    // A key of a declared name but another value type is a different key, which has no entry.
    [Fact]
    public void AKeyOfTheSameNameAndAnotherTypeFindsNoEntry()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Step).Build();

        RouteMatchResult result = Match(table, "/items/1?step=3");

        Assert.False(result.Metadata.TryGet(new RouteMetadataKey<string>("step"), out _));
        Assert.True(result.Metadata.TryGet(Step, out int step));
        Assert.Equal(3, step);
    }

    [Theory]
    [InlineData("/items/1?step=2&step=x", 2)]
    [InlineData("/items/1?step=7&step=2", 7)]
    [InlineData("/items/1?STEP=5&step=6", 5)]
    [InlineData("/items/1?Step=8", 8)]
    [InlineData("/items/1?step=007", 7)]
    [InlineData("/items/1?step=%2B7", 7)]
    public void TheFirstValueOfTheNameIgnoringCaseIsRead(string uri, int expected)
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Step).Build();

        Assert.Equal(RouteMetadata.Empty.With(Step, expected), Match(table, uri).Metadata);
    }

    [Theory]
    [InlineData("/items/1?campaign=spring%20sale", "spring sale")]
    [InlineData("/items/1?campaign=spring+sale", "spring sale")]
    [InlineData("/items/1?campaign=a%2Bb", "a+b")]
    [InlineData("/items/1?campaign=%26%3D%23%25", "&=#%")]
    [InlineData("/items/1?campaign=%C3%A9%E6%97%A5", "\u00E9\u65E5")]
    [InlineData("/items/1?campaign=\u00E9", "\u00E9")]
    [InlineData("/items/1?campaign=%F0%9F%98%80", "\U0001F600")]
    [InlineData("/items/1?campaign=", "")]
    [InlineData("/items/1?campaign", "")]
    [InlineData("/items/1?campaign&campaign=x", "")]
    [InlineData("/items/1?campaign=a=b", "a=b")]
    [InlineData("/items/1?campaign=%ZZ", "%ZZ")]
    [InlineData("/items/1?c%61mpaign=x", "x")]
    public void AStringIsReadAsTheQueryReadDecodesIt(string uri, string expected)
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();

        RouteMatchResult result = Match(table, uri);

        Assert.True(result.Metadata.TryGet(Campaign, out string? campaign));
        Assert.Equal(expected, campaign);
    }

    [Fact]
    public void EachBuiltInTypeIsRead()
    {
        var keys = new
        {
            Long = new RouteMetadataKey<long>("l"),
            SByte = new RouteMetadataKey<sbyte>("sb"),
            Byte = new RouteMetadataKey<byte>("b"),
            Short = new RouteMetadataKey<short>("s"),
            UShort = new RouteMetadataKey<ushort>("us"),
            UInt = new RouteMetadataKey<uint>("ui"),
            ULong = new RouteMetadataKey<ulong>("ul"),
            Decimal = new RouteMetadataKey<decimal>("m"),
            Double = new RouteMetadataKey<double>("d"),
            Float = new RouteMetadataKey<float>("f"),
        };
        RouteTable table = ItemsBuilder()
            .AddQueryMetadata(Step)
            .AddQueryMetadata(keys.Long)
            .AddQueryMetadata(keys.SByte)
            .AddQueryMetadata(keys.Byte)
            .AddQueryMetadata(keys.Short)
            .AddQueryMetadata(keys.UShort)
            .AddQueryMetadata(keys.UInt)
            .AddQueryMetadata(keys.ULong)
            .AddQueryMetadata(keys.Decimal)
            .AddQueryMetadata(keys.Double)
            .AddQueryMetadata(keys.Float)
            .AddQueryMetadata(Preview)
            .AddQueryMetadata(Session)
            .Build();
        var session = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        RouteMatchResult result = Match(
            table,
            "/items/1?step=-42&l=9223372036854775807&sb=-128&b=255&s=-32768&us=65535&ui=4294967295" +
            "&ul=18446744073709551615&m=-1.50&d=0.000015&f=0.25&preview=TRUE" +
            "&session=0F8FAD5B-D9CB-469F-A165-70867728950E");

        RouteMetadata expected = RouteMetadata.Empty
            .With(Step, -42)
            .With(keys.Long, long.MaxValue)
            .With(keys.SByte, sbyte.MinValue)
            .With(keys.Byte, byte.MaxValue)
            .With(keys.Short, short.MinValue)
            .With(keys.UShort, ushort.MaxValue)
            .With(keys.UInt, uint.MaxValue)
            .With(keys.ULong, ulong.MaxValue)
            .With(keys.Decimal, -1.50m)
            .With(keys.Double, 0.000015)
            .With(keys.Float, 0.25f)
            .With(Preview, true)
            .With(Session, session);
        Assert.Equal(expected, result.Metadata);
    }

    [Fact]
    public void ARegisteredEnumAndTypesWithACodecAreRead()
    {
        var slug = new RouteMetadataKey<Custom.Slug>("slug");
        var shape = new RouteMetadataKey<Custom.IShape>("shape");
        RouteTable table = Custom.Register(ItemsBuilder())
            .AddEnumValueCodec<Level>()
            .AddQueryMetadata(Tier)
            .AddQueryMetadata(Product)
            .AddQueryMetadata(Stage)
            .AddQueryMetadata(slug)
            .AddQueryMetadata(shape)
            .Build();

        RouteMatchResult result = Match(
            table,
            "/items/1?tier=high&product=17&stage=in-progress&slug=a-b&shape=circle");

        Assert.Equal(
            RouteMetadata.Empty
                .With(Tier, Level.High)
                .With(Product, new Custom.ProductId(17))
                .With(Stage, Custom.Stage.InProgress)
                .With(slug, new Custom.Slug("a-b"))
                .With(shape, new Custom.Circle()),
            result.Metadata);
    }

    // The diagnostic of a metadata value that is not valid is the one a create function gets by reading the same
    // name with the typed read, Data and all.
    [Theory]
    [InlineData("/items/1?step=x", "step")]
    [InlineData("/items/1?step=", "step")]
    [InlineData("/items/1?step", "step")]
    [InlineData("/items/1?step=x&step=2", "step")]
    [InlineData("/items/1?step=+5", "step")]
    [InlineData("/items/1?preview=yes", "preview")]
    [InlineData("/items/1?preview=", "preview")]
    [InlineData("/items/1?session=not-a-guid", "session")]
    [InlineData("/items/1?tier=Medium", "tier")]
    [InlineData("/items/1?tier=", "tier")]
    [InlineData("/items/1?product=-1", "product")]
    [InlineData("/items/1?stage=InProgress", "stage")]
    [InlineData("https://example.com/items/1?step=x", "step")]
    public void AValueThatIsNotValidFailsTheMatchAsTheTypedReadDoes(string uri, string name)
    {
        RouteTable table = Custom.Register(ItemsBuilder())
            .AddEnumValueCodec<Level>()
            .AddQueryMetadata(Step)
            .AddQueryMetadata(Preview)
            .AddQueryMetadata(Session)
            .AddQueryMetadata(Tier)
            .AddQueryMetadata(Product)
            .AddQueryMetadata(Stage)
            .Build();
        RouteTable reading = Custom.Register(new RouteTableBuilder())
            .AddEnumValueCodec<Level>()
            .Map("/items/{id:int}", c =>
            {
                _ = name switch
                {
                    "step" => c.TryGetQuery<int>(name, out _),
                    "preview" => c.TryGetQuery<bool>(name, out _),
                    "session" => c.TryGetQuery<Guid>(name, out _),
                    "tier" => c.TryGetQuery<Level>(name, out _),
                    "product" => c.TryGetQuery<Custom.ProductId>(name, out _),
                    _ => c.TryGetQuery<Custom.Stage>(name, out _),
                };
                return new ItemRoute(c.Path<int>("id"));
            })
            .Build();

        RouteMatchResult result = Match(table, uri);
        RouteMatchResult expected = Match(reading, uri);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Route);
        Assert.Same(RouteMetadata.Empty, result.Metadata);
        Assert.Same(table.Definitions[0], result.Definition);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        RouteDiagnostic typed = Assert.Single(expected.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal(typed.Code, diagnostic.Code);
        Assert.Equal(typed.Message, diagnostic.Message);
        Assert.Equal(["name", "path", "reason", "template"], diagnostic.Data.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(typed.Data, diagnostic.Data);
        Assert.Equal(name, diagnostic.Data["name"]);
        Assert.Equal("/items/{id:int}", diagnostic.Data["template"]);
        Assert.Equal("/items/1", diagnostic.Data["path"]);
        Assert.Contains($"The query value of '{name}' is not a valid", (string)diagnostic.Data["reason"]!,
            StringComparison.Ordinal);
    }

    // The message is fixed words, and Data holds the name, the template, the reason and the path, none of which
    // carries the value.
    [Fact]
    public void ADiagnosticOfAValueThatIsNotValidCarriesNoValue()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Step).Build();

        RouteMatchResult result = Match(table, "/items/1?step=" + Secret);

        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.DoesNotContain(Secret, diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("step", diagnostic.Message, StringComparison.Ordinal);
        foreach (object? value in diagnostic.Data.Values)
            Assert.DoesNotContain(Secret, (string)value!, StringComparison.Ordinal);
    }

    // Metadata is the same whichever template matched, so a value that is not valid fails the match on every one, and
    // no later template is tried, even one that also matches the path.
    [Theory]
    [InlineData("/items/1?step=x", "/items/{id:int}")]
    [InlineData("/items/abc?step=x", "/items/{*rest}")]
    [InlineData("/products?step=x", "/products")]
    public void AValueThatIsNotValidFailsTheMatchWhicheverTemplateMatched(string uri, string template)
    {
        var laterCalls = 0;
        RouteTable table = new RouteTableBuilder()
            .Map("/items/{id:int}", c => new ItemRoute(c.Path<int>("id")))
            .Map("/items/{*rest}", c =>
            {
                laterCalls++;
                return new NameRoute(c.Path("rest"));
            })
            .Map("/products", _ => new NameRoute("products"))
            .AddQueryMetadata(Step)
            .Build();

        RouteMatchResult result = Match(table, uri);

        Assert.False(result.IsSuccess);
        Assert.Equal(template, result.Definition?.Template.Value);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("step", diagnostic.Data["name"]);
        Assert.Equal(template, diagnostic.Data["template"]);
        Assert.Equal(template == "/items/{*rest}" ? 1 : 0, laterCalls);
    }

    [Fact]
    public void AURIThatNoTemplateMatchesIsNotMatchedWhateverItsMetadata()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Step).Build();

        RouteMatchResult result = Match(table, "/orders/1?step=x");

        Assert.Null(result.Definition);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.NotMatched, diagnostic.Code);
        Assert.Equal(["path"], diagnostic.Data.Keys);
        Assert.Same(RouteMetadata.Empty, result.Metadata);
    }

    // The create function runs first, so a value it rejects is what the result reports, and metadata is not read.
    [Fact]
    public void ACreateFunctionThatRejectsAValueWinsOverMetadata()
    {
        RouteTable table = new RouteTableBuilder()
            .Map<ItemRoute>("/items/{id:int}", _ => throw new RouteValueException("id", "An item id is positive."))
            .AddQueryMetadata(Step)
            .Build();

        RouteMatchResult result = Match(table, "/items/0?step=x");

        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("id", diagnostic.Data["name"]);
        Assert.Equal("An item id is positive.", diagnostic.Data["reason"]);
        Assert.Same(RouteMetadata.Empty, result.Metadata);
    }

    [Fact]
    public void ACreateFunctionThatThrowsWinsOverMetadata()
    {
        var thrown = new InvalidDataException("create");
        RouteTable table = new RouteTableBuilder()
            .Map<ItemRoute>("/items/{id:int}", _ => throw thrown)
            .AddQueryMetadata(Step)
            .Build();

        Assert.Same(thrown, Assert.Throws<InvalidDataException>(() => Match(table, "/items/0?step=x")));
    }

    [Fact]
    public void ACreateFunctionThatReturnsNullThrowsWhateverTheMetadata()
    {
        RouteTable table = new RouteTableBuilder()
            .Map<ItemRoute>("/items/{id:int}", _ => null!)
            .AddQueryMetadata(Step)
            .Build();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => Match(table, "/items/0?step=x"));

        Assert.Contains("returned null", exception.Message, StringComparison.Ordinal);
    }

    // A declared key takes nothing away from the query the create function reads.
    [Fact]
    public void ACreateFunctionReadsTheNameOfADeclaredKey()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/items/{id:int}", c => new NameRoute(
                (c.TryGetQuery("campaign", out string? text) ? text : "-") + "|" +
                string.Join(',', c.GetQueryValues("campaign")) + "|" +
                (c.TryGetQuery("step", out int step) ? step.ToString(CultureInfo.InvariantCulture) : "-")))
            .AddQueryMetadata(Campaign)
            .AddQueryMetadata(Step)
            .Build();

        RouteMatchResult result = Match(table, "/items/1?campaign=a&campaign=b&step=4");

        Assert.Equal(new NameRoute("a|a,b|4"), result.Route);
        Assert.Equal(RouteMetadata.Empty.With(Campaign, "a").With(Step, 4), result.Metadata);
    }

    [Fact]
    public void AMappingWithoutAFormatFunctionGetsMetadata()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/items/{id:int}", c => new ItemRoute(c.Path<int>("id")))
            .AddQueryMetadata(Campaign)
            .Build();

        Assert.Null(table.Definitions[0].Formatter);
        Assert.Equal(RouteMetadata.Empty.With(Campaign, "x"), Match(table, "/items/1?campaign=x").Metadata);
    }

    [Theory]
    [InlineData("/items/1?campaign=spring")]
    [InlineData("items/1?campaign=spring#step=x")]
    [InlineData("https://example.com/items/1?campaign=spring")]
    [InlineData("myapp:///items/1?campaign=spring#step=x")]
    [InlineData("myapp:/items/1?campaign=spring")]
    public void RelativeAndAbsoluteURIsAreRead(string uri)
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).AddQueryMetadata(Step).Build();

        Assert.Equal(RouteMetadata.Empty.With(Campaign, "spring"), Match(table, uri).Metadata);
    }

    // For a scheme that has no query, the ? is part of the path, so there are no metadata values.
    [Fact]
    public void ASchemeWithNoQueryGivesNoMetadata()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/{*rest}", c => new NameRoute(c.Path("rest")))
            .AddQueryMetadata(Campaign)
            .Build();

        RouteMatchResult result = table.Match(new Uri("ftp://host/a?campaign=x"));

        Assert.True(result.IsSuccess);
        Assert.Same(RouteMetadata.Empty, result.Metadata);
    }

    [Fact]
    public void ATableBuiltBeforeADeclarationDoesNotReadTheKey()
    {
        RouteTableBuilder builder = ItemsBuilder();
        RouteTable before = builder.Build();
        RouteTable after = builder.AddQueryMetadata(Step).Build();

        Assert.Same(RouteMetadata.Empty, Match(before, "/items/1?step=2").Metadata);
        Assert.True(Match(before, "/items/1?step=x").IsSuccess);
        Assert.Equal(RouteMetadata.Empty.With(Step, 2), Match(after, "/items/1?step=2").Metadata);
    }

    [Fact]
    public void AnExceptionThatAParserThrowsPropagates()
    {
        var thrown = new InvalidDataException("parser");
        var token = new RouteMetadataKey<Token>("token");
        RouteTable table = ItemsBuilder()
            .AddValueCodec<Token>((string _, out Token? value) => throw thrown, t => t.Value)
            .AddQueryMetadata(token)
            .Build();

        Assert.Same(thrown, Assert.Throws<InvalidDataException>(() => Match(table, "/items/1?token=a")));
        Assert.True(Match(table, "/items/1").IsSuccess);
    }

    // As for the typed read, a parser that accepts a text but gives null is a mistake in the codec, which throws.
    [Fact]
    public void AParserThatGivesNullThrows()
    {
        var token = new RouteMetadataKey<Token>("token");
        RouteTable table = ItemsBuilder()
            .AddValueCodec<Token>(
                (string _, out Token? value) =>
                {
                    value = null;
                    return true;
                },
                t => t.Value)
            .AddQueryMetadata(token)
            .Build();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => Match(table, "/items/1?token=a"));

        Assert.Contains("accepted a text but gave a null value", exception.Message, StringComparison.Ordinal);
    }

    // As for the typed read in a create function, a RouteValueException that a parser throws fails the match.
    [Fact]
    public void ARouteValueExceptionThatAParserThrowsFailsTheMatch()
    {
        var token = new RouteMetadataKey<Token>("token");
        RouteTable table = ItemsBuilder()
            .AddValueCodec<Token>(
                (string _, out Token? value) => throw new RouteValueException("token", "A token is short."),
                t => t.Value)
            .AddQueryMetadata(token)
            .Build();

        RouteMatchResult result = Match(table, "/items/1?token=a");

        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("token", diagnostic.Data["name"]);
        Assert.Equal("A token is short.", diagnostic.Data["reason"]);
        Assert.Same(RouteMetadata.Empty, result.Metadata);
    }

    public static TheoryData<string> RoundTripStrings => new()
    {
        "", "spring", "spring sale", " lead", "a+b", "&=#%?/", "%41", "\u00E9\u65E5", "\U0001F600", "a\u0000b",
    };

    [Theory]
    [MemberData(nameof(RoundTripStrings))]
    public void AStringRoundTripsThroughFormatAndFormatUri(string campaign)
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(5)).WithMetadata(Campaign, campaign);

        AssertRoundTrips(table, request, request.Metadata);
    }

    [Fact]
    public void EveryKindOfValueRoundTripsThroughFormatAndFormatUri()
    {
        var keys = new
        {
            Long = new RouteMetadataKey<long>("l"),
            Byte = new RouteMetadataKey<byte>("b"),
            ULong = new RouteMetadataKey<ulong>("ul"),
            Decimal = new RouteMetadataKey<decimal>("m"),
            Double = new RouteMetadataKey<double>("d"),
            Float = new RouteMetadataKey<float>("f"),
            Slug = new RouteMetadataKey<Custom.Slug>("slug"),
        };
        RouteTable table = Custom.Register(ProductsBuilder())
            .AddEnumValueCodec<Level>()
            .AddQueryMetadata(Campaign)
            .AddQueryMetadata(Step)
            .AddQueryMetadata(keys.Long)
            .AddQueryMetadata(keys.Byte)
            .AddQueryMetadata(keys.ULong)
            .AddQueryMetadata(keys.Decimal)
            .AddQueryMetadata(keys.Double)
            .AddQueryMetadata(keys.Float)
            .AddQueryMetadata(Preview)
            .AddQueryMetadata(Session)
            .AddQueryMetadata(Tier)
            .AddQueryMetadata(Product)
            .AddQueryMetadata(Stage)
            .AddQueryMetadata(keys.Slug)
            .Build();
        RouteMetadata metadata = RouteMetadata.Empty
            .With(keys.Slug, new Custom.Slug("a-1"))
            .With(Campaign, "a b&c=d")
            .With(Step, int.MinValue)
            .With(keys.Long, long.MaxValue)
            .With(keys.Byte, (byte)0)
            .With(keys.ULong, ulong.MaxValue)
            .With(keys.Decimal, -0.000001m)
            .With(keys.Double, double.Epsilon)
            .With(keys.Float, float.MaxValue)
            .With(Preview, false)
            .With(Session, Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"))
            .With(Tier, Level.Low)
            .With(Product, new Custom.ProductId(0))
            .With(Stage, Custom.Stage.Done);

        foreach (ProductsRoute route in new[] { new ProductsRoute(null, []), new ProductsRoute(3, ["x", "y z"]) })
            AssertRoundTrips(table, new AppRouteRequest(route, metadata), metadata);
    }

    // The promise holds for a type with a codec only as far as the codec keeps its contract: a class without value
    // equality reads back as a value with the same text, which is not equal to the one written, so the metadata are
    // not equal, although the link is the same when written again.
    [Fact]
    public void ATypeWithoutValueEqualityReadsBackUnequalWithTheSameText()
    {
        var tag = new RouteMetadataKey<Tag>("tag");
        RouteTable table = ItemsBuilder()
            .AddValueCodec<Tag>(
                (string text, out Tag? value) =>
                {
                    value = new Tag(text);
                    return true;
                },
                t => t.Value)
            .AddQueryMetadata(tag)
            .Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(5)).WithMetadata(tag, new Tag("spring"));
        string text = table.Format(request);

        RouteMatchResult result = table.Match(new Uri(text, UriKind.Relative));

        Assert.True(result.IsSuccess);
        Assert.Equal(request.Route, result.Route);
        Assert.NotEqual(request.Metadata, result.Metadata);
        Assert.True(result.Metadata.TryGet(tag, out Tag? read));
        Assert.Equal("spring", read.Value);
        Assert.Equal(text, table.Format(new AppRouteRequest(result.Route, result.Metadata)));
    }

    // Entries for keys the table does not declare are not written, so they are not read back.
    [Fact]
    public void UndeclaredEntriesAreDroppedOnTheRoundTrip()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Step).Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(5))
            .WithMetadata(Campaign, "spring")
            .WithMetadata(Step, 9)
            .WithMetadata(new RouteMetadataKey<string>("step"), "other");

        AssertRoundTrips(table, request, RouteMetadata.Empty.With(Step, 9));
    }

    [Fact]
    public void ARequestWithNoMetadataRoundTripsToNoMetadata()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Step).AddQueryMetadata(Campaign).Build();

        AssertRoundTrips(table, new AppRouteRequest(new ItemRoute(5)), RouteMetadata.Empty);
    }

    // Declaring keys costs a match with no query nothing, and one with a query that has none of their names exactly
    // what a create function that reads one query value pays: the query is parsed once, and no metadata is made.
    [Fact]
    public void AMatchWithNoneOfTheDeclaredNamesCostsOneQueryRead()
    {
        RouteTable none = ItemsBuilder().Build();
        RouteTable declared = ItemsBuilder().AddQueryMetadata(Campaign).AddQueryMetadata(Step).Build();
        RouteTable reading = new RouteTableBuilder()
            .Map("/items/{id:int}", c => new ItemRoute(c.TryGetQuery("campaign", out _) ? 0 : c.Path<int>("id")))
            .Build();
        var noQuery = new Uri("/items/1", UriKind.Relative);
        var otherNames = new Uri("/items/1?" + LongQuery("other"), UriKind.Relative);

        long noneBytes = AllocatedBytesPerMatch(none, noQuery);
        long declaredBytes = AllocatedBytesPerMatch(declared, noQuery);
        long declaredOtherBytes = AllocatedBytesPerMatch(declared, otherNames);
        long readingOtherBytes = AllocatedBytesPerMatch(reading, otherNames);

        string sizes = $"no keys: {noneBytes}; declared, no query: {declaredBytes}; declared, other names: " +
            $"{declaredOtherBytes}; one read, other names: {readingOtherBytes} bytes.";
        Assert.True(declaredBytes == noneBytes, sizes);
        Assert.True(declaredOtherBytes == readingOtherBytes, sizes);
    }

    // The entries are gathered in one array rather than a new collection for each key, so what the metadata adds to
    // a match, beyond reading the same names in a create function, grows linearly with the keys found: eight keys
    // cost at most twice what four do. A collection made for each key would copy an array per key, which grows
    // quadratically and breaks that bound.
    [Fact]
    public void TheMetadataOfAMatchIsBuiltInOneArray()
    {
        long Extra(int count)
        {
            RouteTableBuilder declaring = ItemsBuilder();
            for (var i = 0; i < count; i++)
                declaring.AddQueryMetadata(new RouteMetadataKey<string>(Name(i)));

            RouteTable reading = new RouteTableBuilder()
                .Map("/items/{id:int}", c =>
                {
                    for (var i = 0; i < count; i++)
                        Assert.True(c.TryGetQuery<string>(Name(i), out _));

                    return new ItemRoute(c.Path<int>("id"));
                })
                .Build();
            var query = new StringBuilder("/items/1?");
            for (var i = 0; i < count; i++)
                query.Append(Name(i)).Append("=v&");

            var uri = new Uri(query.ToString(), UriKind.Relative);
            return AllocatedBytesPerMatch(declaring.Build(), uri) - AllocatedBytesPerMatch(reading, uri);
        }

        long one = Extra(1);
        long four = Extra(4);
        long eight = Extra(8);

        string sizes = $"bytes added by the metadata for 1, 4 and 8 keys: {one}, {four}, {eight}.";
        Assert.True(one > 0, sizes);
        Assert.True(eight <= 2 * four, sizes);

        static string Name(int i) => "k" + i.ToString(CultureInfo.InvariantCulture);
    }

    private static void AssertRoundTrips(RouteTable table, AppRouteRequest request, RouteMetadata expected)
    {
        string text = table.Format(request);
        RouteMatchResult relative = table.Match(new Uri(text, UriKind.Relative));

        Assert.True(relative.IsSuccess, text);
        Assert.Equal(request.Route, relative.Route);
        Assert.Equal(expected, relative.Metadata);

        foreach (string baseUri in new[] { "myapp:///", "https://example.com" })
        {
            RouteMatchResult absolute = table.Match(table.FormatUri(request, new Uri(baseUri)));

            Assert.True(absolute.IsSuccess, text);
            Assert.Equal(request.Route, absolute.Route);
            Assert.Equal(expected, absolute.Metadata);
        }
    }

    private static string LongQuery(string prefix)
    {
        var query = new StringBuilder();
        for (var i = 0; i < 200; i++)
            query.Append(prefix).Append(i).Append("=v&");

        return query.ToString();
    }

    private static long AllocatedBytesPerMatch(RouteTable table, Uri uri)
    {
        const int Calls = 200;
        for (var i = 0; i < Calls; i++)
            Assert.True(table.Match(uri).IsSuccess);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Calls; i++)
            table.Match(uri);

        return (GC.GetAllocatedBytesForCurrentThread() - before) / Calls;
    }

    private static RouteMatchResult Match(RouteTable table, string uri)
    {
        return table.Match(new Uri(uri, UriKind.RelativeOrAbsolute));
    }

    private static RouteTableBuilder ItemsBuilder()
    {
        return new RouteTableBuilder()
            .Map("/items/{id:int}", c => new ItemRoute(c.Path<int>("id")), f => f.Path("id", r => r.Id));
    }

    private static RouteTableBuilder ProductsBuilder()
    {
        return new RouteTableBuilder()
            .Map(
                "/products",
                c => new ProductsRoute(
                    c.TryGetQuery("page", out int? page) ? page : null,
                    c.GetQueryValues("tag")),
                f => f.Query("page", r => r.Page).QueryValues("tag", r => r.Tags));
    }

    private enum Level
    {
        Low = 1,
        High = 2,
    }

    private sealed record ItemRoute(int Id) : AppRoute;

    private sealed record NameRoute(string Name) : AppRoute;

    // The tags compare by their elements, so that a route read back equals the one written.
    private sealed record ProductsRoute(int? Page, IReadOnlyList<string> Tags) : AppRoute
    {
        public bool Equals(ProductsRoute? other)
        {
            return other is not null && Page == other.Page && Tags.SequenceEqual(other.Tags);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Page, Tags.Count);
        }
    }

    private sealed record Token(string Value);

    // A type with a codec and no value equality of its own, so two tags of the same text are not equal.
    private sealed class Tag(string value)
    {
        public string Value { get; } = value;
    }
}
