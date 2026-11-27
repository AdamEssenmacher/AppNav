using AdamE.AppNav.Routing;
using Xunit;
using Custom = AdamE.AppNav.Tests.Routing.CustomValueTypes;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTableFormatRequestTests
{
    private const string NoText = "has a value that cannot be written:";
    private const string NoFormat = "none of its mappings has a format function";
    private const string TrailingSpace = "the last value written ends with a space";
    private const string SchemeChanges = "does not keep the link as written";

    // Text that stands for a secret a metadata value can carry, distinctive enough that finding it in a message is
    // not a coincidence.
    private const string Secret = "tok-Vn3Jq8Rw-9Wq4Lm7Tz";

    private static readonly RouteMetadataKey<string> Campaign = new("campaign");
    private static readonly RouteMetadataKey<int> Step = new("step");
    private static readonly RouteMetadataKey<bool> Preview = new("preview");

    [Fact]
    public void ANullRequestThrows()
    {
        RouteTable table = ItemsBuilder().Build();

        Assert.Equal(
            "request",
            Assert.Throws<ArgumentNullException>(() => table.Format((AppRouteRequest)null!)).ParamName);
        Assert.Equal(
            "request",
            Assert.Throws<ArgumentNullException>(
                () => table.FormatUri((AppRouteRequest)null!, new Uri("https://h"))).ParamName);
    }

    [Fact]
    public void ANullBaseThrowsForARequest()
    {
        RouteTable table = ItemsBuilder().Build();

        Assert.Equal(
            "baseUri",
            Assert.Throws<ArgumentNullException>(
                () => table.FormatUri(new AppRouteRequest(new ItemRoute(1)), null!)).ParamName);
    }

    [Fact]
    public void ATableThatDeclaresNoKeysWritesTheRouteAlone()
    {
        RouteTable table = ItemsBuilder().Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(42)).WithMetadata(Campaign, "spring");

        Assert.Equal("/items/42", table.Format(request));
    }

    [Fact]
    public void OneDeclaredKeyIsWrittenAsTheQuery()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(42)).WithMetadata(Campaign, "spring");

        Assert.Equal("/items/42?campaign=spring", table.Format(request));
    }

    // The values are written in the order the keys were declared, whatever order the entries were added in.
    [Fact]
    public void SeveralKeysAreWrittenInTheOrderTheyWereDeclared()
    {
        RouteTable table = ItemsBuilder()
            .AddQueryMetadata(Step)
            .AddQueryMetadata(Campaign)
            .AddQueryMetadata(Preview)
            .Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(1))
            .WithMetadata(Preview, true)
            .WithMetadata(Campaign, "spring")
            .WithMetadata(Step, 2);

        Assert.Equal("/items/1?step=2&campaign=spring&preview=true", table.Format(request));
    }

    [Fact]
    public void ADeclaredKeyWithNoEntryWritesNothing()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Step).AddQueryMetadata(Campaign).Build();

        Assert.Equal("/items/1", table.Format(new AppRouteRequest(new ItemRoute(1))));
        Assert.Equal(
            "/items/1?campaign=x",
            table.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(Campaign, "x")));
    }

    // Metadata comes after every query value of the route, a list's included.
    [Fact]
    public void MetadataIsWrittenAfterTheQueryValuesOfTheRoute()
    {
        RouteTable table = new RouteTableBuilder()
            .AddQueryMetadata(Campaign)
            .Map("/products", _ => new ProductsRoute(null, []), f => f
                .QueryValues("tag", r => r.Tags)
                .Query("page", r => r.Page))
            .Build();
        AppRouteRequest request = new AppRouteRequest(new ProductsRoute(2, ["a", "b"])).WithMetadata(Campaign, "s");

        Assert.Equal("/products?tag=a&tag=b&page=2&campaign=s", table.Format(request));
        Assert.Equal(
            "/products?campaign=s",
            table.Format(new AppRouteRequest(new ProductsRoute(null, [])).WithMetadata(Campaign, "s")));
    }

    // Metadata is part of a link only when the table says so: an entry for a key that is not declared is ignored, and
    // so is one for a key of a declared name and another type, which is a different key.
    [Fact]
    public void EntriesForKeysThatAreNotDeclaredAreIgnored()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(1))
            .WithMetadata(new RouteMetadataKey<int>("campaign"), 7)
            .WithMetadata(new RouteMetadataKey<string>("Campaign"), "upper")
            .WithMetadata(Step, 3);

        Assert.Equal("/items/1", table.Format(request));
        Assert.Equal("/items/1?campaign=x", table.Format(request.WithMetadata(Campaign, "x")));
    }

    [Fact]
    public void ValuesOfBuiltInTypesAreWrittenAsTheirCanonicalText()
    {
        var id = new RouteMetadataKey<Guid>("id");
        var amount = new RouteMetadataKey<decimal>("amount");
        var level = new RouteMetadataKey<Level>("level");
        RouteTable table = ItemsBuilder()
            .AddQueryMetadata(Step)
            .AddQueryMetadata(Preview)
            .AddQueryMetadata(id)
            .AddQueryMetadata(amount)
            .AddQueryMetadata(level)
            .AddEnumValueCodec<Level>()
            .Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(1))
            .WithMetadata(Step, -7)
            .WithMetadata(Preview, false)
            .WithMetadata(id, new Guid("6F9619FF-8B86-D011-B42D-00CF4FC964FF"))
            .WithMetadata(amount, 1.50m)
            .WithMetadata(level, Level.High);

        Assert.Equal(
            "/items/1?step=-7&preview=false&id=6f9619ff-8b86-d011-b42d-00cf4fc964ff&amount=1.5&level=High",
            table.Format(request));
    }

    [Fact]
    public void AValueOfATypeWithACodecIsWrittenByTheCodec()
    {
        var product = new RouteMetadataKey<Custom.ProductId>("product");
        var stage = new RouteMetadataKey<Custom.Stage>("stage");
        RouteTable table = Custom.Register(ItemsBuilder()).AddQueryMetadata(product).AddQueryMetadata(stage).Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(1))
            .WithMetadata(product, new Custom.ProductId(7))
            .WithMetadata(stage, Custom.Stage.NotStarted);

        Assert.Equal("/items/1?product=7&stage=not-started", table.Format(request));
    }

    // A string is written exactly as Query writes the same string, name and value percent-encoded alike.
    [Theory]
    [InlineData("a b")]
    [InlineData("a&b=c#d")]
    [InlineData("1+1")]
    [InlineData("caf\u00E9")]
    [InlineData("%")]
    [InlineData("")]
    public void AStringIsWrittenAsAQueryValueWritesIt(string value)
    {
        var key = new RouteMetadataKey<string>("q r\u00E9");
        RouteTable metadata = ItemsBuilder().AddQueryMetadata(key).Build();
        RouteTable query = new RouteTableBuilder()
            .Map("/items/{id:int}", c => new ItemRoute(c.Path<int>("id")), f => f
                .Path("id", r => r.Id)
                .Query("q r\u00E9", _ => value))
            .Build();

        string written = metadata.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(key, value));

        Assert.Equal(query.Format(new ItemRoute(1)), written);
        RouteMatchContext? context = null;
        RouteTable reader = new RouteTableBuilder()
            .Map("/items/{id:int}", c =>
            {
                context = c;
                return new ItemRoute(0);
            })
            .Build();
        Assert.True(reader.Match(new Uri(written, UriKind.Relative)).IsSuccess);
        Assert.True(context!.TryGetQuery("q r\u00E9", out string? read));
        Assert.Equal(value, read);
    }

    [Fact]
    public void AnEmptyStringIsWrittenAsTheNameAndAnEqualsSign()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();

        Assert.Equal(
            "/items/1?campaign=",
            table.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(Campaign, "")));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AValueWithNoTextThrows(double value)
    {
        var ratio = new RouteMetadataKey<double>("ratio");
        RouteTable table = ItemsBuilder().AddQueryMetadata(ratio).Build();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(ratio, value)));

        Assert.Equal("request", exception.ParamName);
        Assert.Contains(".ItemRoute'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/items/{id:int}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("route metadata key 'ratio' " + NoText, exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void AnEnumValueThatNoMemberHasThrows()
    {
        var level = new RouteMetadataKey<Level>("level");
        RouteTable table = ItemsBuilder().AddQueryMetadata(level).AddEnumValueCodec<Level>().Build();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(level, (Level)9)));

        Assert.Equal("request", exception.ParamName);
        Assert.Contains("route metadata key 'level' " + NoText, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueForWhichTheCodecGivesNullThrows()
    {
        var product = new RouteMetadataKey<Custom.ProductId>("product");
        RouteTable table = Custom.Register(ItemsBuilder()).AddQueryMetadata(product).Build();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(product, new Custom.ProductId(-1))));

        Assert.Equal("request", exception.ParamName);
        Assert.Contains("route metadata key 'product' " + NoText, exception.Message, StringComparison.Ordinal);
        Assert.Contains("its codec gives no text for the value", exception.Message, StringComparison.Ordinal);
    }

    // A codec's text is read back by its own parser, and one that does not accept it is refused; the message never
    // quotes the value or the text.
    [Fact]
    public void ATextThatTheCodecsParserDoesNotAcceptThrows()
    {
        var token = new RouteMetadataKey<Token>("token");
        RouteTable table = ItemsBuilder()
            .AddValueCodec<Token>(
                (string _, out Token? value) =>
                {
                    value = null;
                    return false;
                },
                t => t.Value)
            .AddQueryMetadata(token)
            .Build();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(token, new Token(Secret))));

        Assert.Equal("request", exception.ParamName);
        Assert.Contains("route metadata key 'token'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("its own parser does not accept", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    // A parser that accepts a text but gives null is a mistake in the codec, reported as for a query value.
    [Fact]
    public void ACodecWhoseParserGivesNullThrowsInvalidOperationException()
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

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => table.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(token, new Token(Secret))));

        Assert.Contains("accepted a text but gave a null value", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExceptionFromACodecPropagatesUnchanged()
    {
        var thrown = new FormatException("from the codec");
        var token = new RouteMetadataKey<Token>("token");
        RouteTable table = ItemsBuilder()
            .AddValueCodec<Token>(
                (string text, out Token? value) =>
                {
                    value = new Token(text);
                    return true;
                },
                _ => throw thrown)
            .AddQueryMetadata(token)
            .Build();

        FormatException exception = Assert.Throws<FormatException>(
            () => table.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(token, new Token("x"))));

        Assert.Same(thrown, exception);
    }

    [Fact]
    public void AStringWithALoneSurrogateThrows()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(Campaign, Secret + "\uD800")));

        Assert.Equal("request", exception.ParamName);
        Assert.Contains("route metadata key 'campaign'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("lone surrogate", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    // A value of the route that cannot be written is reported for the request with the parameter of the request.
    [Fact]
    public void ARouteValueThatCannotBeWrittenIsReportedAsTheRequest()
    {
        RouteTable table = new RouteTableBuilder()
            .AddQueryMetadata(Campaign)
            .Map("/s/{name}", c => new NameRoute(c.Path("name")), f => f.Path("name", r => r.Name))
            .Build();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.Format(new AppRouteRequest(new NameRoute(null!)).WithMetadata(Campaign, "x")));

        Assert.Equal("request", exception.ParamName);
        Assert.Contains("path parameter 'name' is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARouteWhoseMappingHasNoFormatFunctionCannotBeWritten()
    {
        RouteTable table = new RouteTableBuilder()
            .AddQueryMetadata(Campaign)
            .Map("/items/{id:int}", c => new ItemRoute(c.Path<int>("id")))
            .Build();

        Assert.Contains(
            NoFormat,
            Assert.Throws<InvalidOperationException>(() => table.Format(new AppRouteRequest(new ItemRoute(1))))
                .Message,
            StringComparison.Ordinal);
        Assert.Contains(
            NoFormat,
            Assert.Throws<InvalidOperationException>(
                () => table.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(Campaign, "x"))).Message,
            StringComparison.Ordinal);
    }

    // Format of a route is unchanged by keys the table declares, and is Format of a request with no metadata.
    [Fact]
    public void FormatOfARouteIsUnchangedAndEqualsARequestWithNoMetadata()
    {
        RouteTable table = new RouteTableBuilder()
            .AddQueryMetadata(Campaign)
            .Map("/products", _ => new ProductsRoute(null, []), f => f
                .Query("page", r => r.Page)
                .QueryValues("tag", r => r.Tags))
            .Build();
        var route = new ProductsRoute(2, ["a"]);

        Assert.Equal("/products?page=2&tag=a", table.Format(route));
        Assert.Equal(table.Format(route), table.Format(new AppRouteRequest(route)));
        Assert.Equal(
            table.FormatUri(route, new Uri("https://example.com")),
            table.FormatUri(new AppRouteRequest(route), new Uri("https://example.com")));
    }

    [Fact]
    public void FormatUriWritesTheRequestBehindTheBase()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(42)).WithMetadata(Campaign, "spring sale");

        Uri web = table.FormatUri(request, new Uri("https://example.com"));
        Uri app = table.FormatUri(request, new Uri("myapp:///"));

        Assert.Equal("https://example.com/items/42?campaign=spring%20sale", web.AbsoluteUri);
        Assert.Equal("myapp:///items/42?campaign=spring%20sale", app.AbsoluteUri);
        Assert.Equal(new ItemRoute(42), table.Match(app).Route);
    }

    [Fact]
    public void FormatUriKeepsTheChecksOfTheBase()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(42)).WithMetadata(Campaign, "spring");

        Assert.Equal(
            "baseUri",
            Assert.Throws<ArgumentException>(
                () => table.FormatUri(request, new Uri("https://example.com/app"))).ParamName);
        Assert.Equal(
            "baseUri",
            Assert.Throws<ArgumentException>(
                () => table.FormatUri(request, new Uri("/", UriKind.Relative))).ParamName);
    }

    // The trailing-space refusal applies to the whole link: a metadata value written last that ends with a space is
    // refused in a URI that contains a non-ASCII character.
    [Fact]
    public void FormatUriRefusesAMetadataValueThatEndsWithASpaceInANonAsciiLink()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(1)).WithMetadata(Campaign, "a ");

        Assert.Equal(
            "https://example.com/items/1?campaign=a%20",
            table.FormatUri(request, new Uri("https://example.com")).AbsoluteUri);
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.FormatUri(request, new Uri("https://\u00E9.example")));

        Assert.Equal("request", exception.ParamName);
        Assert.Contains(TrailingSpace, exception.Message, StringComparison.Ordinal);
    }

    // A scheme with no query in System.Uri writes a route alone, but refuses the link once metadata adds a query.
    [Fact]
    public void FormatUriRefusesAQueryOfMetadataUnderASchemeWithNoQuery()
    {
        RouteTable table = ItemsBuilder().AddQueryMetadata(Campaign).Build();
        var ftp = new Uri("ftp://example.com");

        Assert.Equal(
            "ftp://example.com/items/1",
            table.FormatUri(new AppRouteRequest(new ItemRoute(1)), ftp).AbsoluteUri);
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.FormatUri(new AppRouteRequest(new ItemRoute(1)).WithMetadata(Campaign, "x"), ftp));

        Assert.Equal("baseUri", exception.ParamName);
        Assert.Contains(SchemeChanges, exception.Message, StringComparison.Ordinal);
    }

    private static RouteTableBuilder ItemsBuilder()
    {
        return new RouteTableBuilder()
            .Map("/items/{id:int}", c => new ItemRoute(c.Path<int>("id")), f => f.Path("id", r => r.Id));
    }

    private enum Level
    {
        Low = 1,
        High = 2,
    }

    private sealed record ItemRoute(int Id) : AppRoute;

    private sealed record NameRoute(string Name) : AppRoute;

    private sealed record ProductsRoute(int? Page, IReadOnlyList<string> Tags) : AppRoute;

    private sealed record Token(string Value);
}
