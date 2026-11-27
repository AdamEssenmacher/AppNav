using AdamE.AppNav.Routing;
using Xunit;
using Custom = AdamE.AppNav.Tests.Routing.CustomValueTypes;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTableBuilderQueryMetadataTests
{
    private const string AlreadyDeclared = "is already declared with AddQueryMetadata";
    private const string NameTaken = "has the name of a query value";
    private const string NoCodec = "is not a built-in type and has no codec";

    private static readonly RouteMetadataKey<string> Campaign = new("campaign");

    [Fact]
    public void AddQueryMetadataReturnsTheBuilder()
    {
        var builder = new RouteTableBuilder();

        Assert.Same(builder, builder.AddQueryMetadata(Campaign));
    }

    [Fact]
    public void ANullKeyThrows()
    {
        var builder = new RouteTableBuilder();

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(() => builder.AddQueryMetadata<string>(null!));

        Assert.Equal("key", exception.ParamName);
    }

    // Declaring applies to the whole table, so it can come before or after the mapping and the codecs.
    [Fact]
    public void ADeclarationBeforeTheMappingAndTheCodecApplies()
    {
        var stage = new RouteMetadataKey<Custom.Stage>("stage");
        RouteTable table = Custom.Register(new RouteTableBuilder().AddQueryMetadata(stage).AddQueryMetadata(Campaign))
            .Map("/items/{id:int}", c => new ItemRoute(c.Path<int>("id")), f => f.Path("id", r => r.Id))
            .Build();

        AppRouteRequest request = new AppRouteRequest(new ItemRoute(42))
            .WithMetadata(stage, Custom.Stage.InProgress)
            .WithMetadata(Campaign, "spring");

        Assert.Equal("/items/42?stage=in-progress&campaign=spring", table.Format(request));
    }

    [Fact]
    public void ADeclarationAfterTheMappingAndTheCodecApplies()
    {
        var stage = new RouteMetadataKey<Custom.Stage>("stage");
        RouteTable table = Custom.Builder()
            .Map("/items/{id:int}", c => new ItemRoute(c.Path<int>("id")), f => f.Path("id", r => r.Id))
            .AddQueryMetadata(stage)
            .Build();

        AppRouteRequest request = new AppRouteRequest(new ItemRoute(42)).WithMetadata(stage, Custom.Stage.Done);

        Assert.Equal("/items/42?stage=done", table.Format(request));
    }

    [Fact]
    public void DeclaringAnEqualKeyAgainDoesNothing()
    {
        RouteTable table = ItemsBuilder()
            .AddQueryMetadata(Campaign)
            .AddQueryMetadata(new RouteMetadataKey<string>("campaign"))
            .Build();

        AppRouteRequest request = new AppRouteRequest(new ItemRoute(1)).WithMetadata(Campaign, "spring");

        Assert.Equal("/items/1?campaign=spring", table.Format(request));
    }

    [Fact]
    public void AKeyWhoseNameDiffersByCaseThrowsAndLeavesTheBuilderUnchanged()
    {
        var upper = new RouteMetadataKey<string>("Campaign");
        RouteTableBuilder builder = ItemsBuilder().AddQueryMetadata(Campaign);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => builder.AddQueryMetadata(upper));

        Assert.Equal("key", exception.ParamName);
        Assert.Contains(AlreadyDeclared, exception.Message, StringComparison.Ordinal);
        Assert.Contains("as 'campaign'", exception.Message, StringComparison.Ordinal);
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(1))
            .WithMetadata(Campaign, "a")
            .WithMetadata(upper, "b");
        Assert.Equal("/items/1?campaign=a", builder.Build().Format(request));
    }

    [Fact]
    public void AKeyOfTheSameNameAndAnotherTypeThrowsAndLeavesTheBuilderUnchanged()
    {
        var number = new RouteMetadataKey<int>("campaign");
        RouteTableBuilder builder = ItemsBuilder().AddQueryMetadata(Campaign);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => builder.AddQueryMetadata(number));

        Assert.Equal("key", exception.ParamName);
        Assert.Contains(AlreadyDeclared, exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "with the value type 'String', and this key has the value type 'Int32'",
            exception.Message,
            StringComparison.Ordinal);
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(1))
            .WithMetadata(Campaign, "a")
            .WithMetadata(number, 7);
        Assert.Equal("/items/1?campaign=a", builder.Build().Format(request));
    }

    // The name is checked as a query name is: a lone surrogate cannot be percent-encoded and read back.
    // The names are made here rather than given as data, which a test runner can carry as text that replaces a lone
    // surrogate.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ANameWithALoneSurrogateThrows(int which)
    {
        string name = which == 0 ? "\uD800" : "a" + '\uDC00' + "b";
        var builder = new RouteTableBuilder();

        ArgumentException exception =
            Assert.Throws<ArgumentException>(() => builder.AddQueryMetadata(new RouteMetadataKey<string>(name)));

        Assert.Equal("key", exception.ParamName);
        Assert.Contains("lone surrogate", exception.Message, StringComparison.Ordinal);

        // A recorded key would be written, or would fail to be, by a request with an entry for it.
        var key = new RouteMetadataKey<string>(name);
        RouteTable table = builder
            .Map("/items/{id:int}", c => new ItemRoute(c.Path<int>("id")), f => f.Path("id", r => r.Id))
            .Build();
        Assert.Equal("/items/1", table.Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(key, "x")));
    }

    // A collection is rejected at the call, as Query rejects it, since no codec can write one value of it.
    [Fact]
    public void ACollectionTypeThrowsAtTheCall()
    {
        var builder = new RouteTableBuilder();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddQueryMetadata(new RouteMetadataKey<string[]>("tags")));

        Assert.Contains(
            "A route metadata value cannot be written as type 'String[]'",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("written as one query value", exception.Message, StringComparison.Ordinal);

        // Had the rejected key been recorded, a key of its name and another type would now be refused.
        var tags = new RouteMetadataKey<string>("tags");
        builder.AddQueryMetadata(tags)
            .Map("/items/{id:int}", c => new ItemRoute(c.Path<int>("id")), f => f.Path("id", r => r.Id));
        Assert.Equal(
            "/items/1?tags=a",
            builder.Build().Format(new AppRouteRequest(new ItemRoute(1)).WithMetadata(tags, "a")));
    }

    // char is left to Build, as for Query, which says to write it as a string.
    [Fact]
    public void ACharTypeThrowsAtBuild()
    {
        RouteTableBuilder builder = ItemsBuilder().AddQueryMetadata(new RouteMetadataKey<char>("initial"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("route metadata key 'initial'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("a char is one character of a string", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATypeWithNoCodecThrowsAtBuildUntilACodecIsRegistered()
    {
        var point = new RouteMetadataKey<Custom.Point>("at");
        RouteTableBuilder builder = ItemsBuilder().AddQueryMetadata(point);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("route metadata key 'at'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AddQueryMetadata", exception.Message, StringComparison.Ordinal);
        Assert.Contains(NoCodec, exception.Message, StringComparison.Ordinal);
        Assert.Contains("AddValueCodec<CustomValueTypes.Point>", exception.Message, StringComparison.Ordinal);

        Custom.Register(builder);
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(1))
            .WithMetadata(point, new Custom.Point { X = 2, Y = 3 });
        Assert.Equal("/items/1?at=2x3", builder.Build().Format(request));
    }

    [Fact]
    public void AnEnumThatIsNotRegisteredThrowsAtBuildUntilItIsRegistered()
    {
        var level = new RouteMetadataKey<Level>("level");
        RouteTableBuilder builder = ItemsBuilder().AddQueryMetadata(level);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("route metadata key 'level'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("the enum is not registered", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Level>()", exception.Message, StringComparison.Ordinal);

        builder.AddEnumValueCodec<Level>();
        AppRouteRequest request = new AppRouteRequest(new ItemRoute(1)).WithMetadata(level, Level.High);
        Assert.Equal("/items/1?level=High", builder.Build().Format(request));
    }

    // Of several keys without a codec, the first declared is reported.
    [Fact]
    public void TheFirstDeclaredKeyWithNoCodecIsReported()
    {
        RouteTableBuilder builder = ItemsBuilder()
            .AddQueryMetadata(new RouteMetadataKey<Custom.Point>("second"))
            .AddQueryMetadata(new RouteMetadataKey<Level>("first"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("route metadata key 'second'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("campaign")]
    [InlineData("CAMPAIGN")]
    public void ANameThatAQueryValueDeclaresThrowsAtBuild(string queryName)
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddQueryMetadata(Campaign)
            .Map("/search", c => new SearchRoute(null), f => f.Query(queryName, r => r.Q));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(NameTaken, exception.Message, StringComparison.Ordinal);
        Assert.Contains("route metadata key 'campaign'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/search'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(".SearchRoute'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("campaign")]
    [InlineData("Campaign")]
    public void ANameThatAListOfQueryValuesDeclaresThrowsAtBuild(string queryName)
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .Map("/tags", _ => new TagsRoute([]), f => f.QueryValues(queryName, r => r.Tags))
            .AddQueryMetadata(Campaign);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(NameTaken, exception.Message, StringComparison.Ordinal);
        Assert.Contains("route metadata key 'campaign'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/tags'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(".TagsRoute'", exception.Message, StringComparison.Ordinal);
    }

    // A mapping without a format function declares no query names, whatever its create function reads.
    [Fact]
    public void AMappingWithoutAFormatFunctionDoesNotCollide()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/search", c => new SearchRoute(c.TryGetQuery("campaign", out string? q) ? q : null))
            .AddQueryMetadata(Campaign)
            .Build();

        Assert.Single(table.Definitions);
    }

    // The mapping that collides first, in mapping order, is reported, whichever key it collides with.
    [Fact]
    public void TheFirstMappingThatCollidesIsReported()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddQueryMetadata(Campaign)
            .AddQueryMetadata(new RouteMetadataKey<string>("ref"))
            .Map("/a", _ => new SearchRoute(null), f => f.Query("x", r => r.Q).Query("ref", r => r.Q))
            .Map("/b", _ => new ItemRoute(0), f => f.Query("campaign", r => r.Id));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("route metadata key 'ref'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/a'", exception.Message, StringComparison.Ordinal);
    }

    // A collision is reported before a key's missing codec, which registering a codec would not fix.
    [Fact]
    public void ACollisionIsReportedBeforeAMissingCodec()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddQueryMetadata(new RouteMetadataKey<Level>("level"))
            .AddQueryMetadata(Campaign)
            .Map("/search", _ => new SearchRoute(null), f => f.Query("campaign", r => r.Q));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(NameTaken, exception.Message, StringComparison.Ordinal);
    }

    // A second format function for a route type is reported before a key that collides with a query name.
    [Fact]
    public void ASecondFormatFunctionIsReportedBeforeACollision()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddQueryMetadata(Campaign)
            .Map("/a", _ => new SearchRoute(null), f => f.Query("campaign", r => r.Q))
            .Map("/b", _ => new SearchRoute(null), f => f.Query("q", r => r.Q));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("is mapped with a format function to both", exception.Message, StringComparison.Ordinal);
    }

    // A collision is reported before a format function's missing codec, even of a mapping that comes first.
    [Fact]
    public void ACollisionIsReportedBeforeAFormatFunctionsMissingCodec()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddQueryMetadata(new RouteMetadataKey<string>("a"))
            .Map("/levels", _ => new LevelRoute(Level.Low), f => f.Query("level", r => r.Level))
            .Map("/search", _ => new SearchRoute(null), f => f.Query("a", r => r.Q));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains(NameTaken, exception.Message, StringComparison.Ordinal);
        Assert.Contains("route metadata key 'a'", exception.Message, StringComparison.Ordinal);
    }

    // A format function's missing codec is reported before a key's missing codec, whichever was declared first.
    [Fact]
    public void AFormatFunctionsMissingCodecIsReportedBeforeAKeysMissingCodec()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddQueryMetadata(new RouteMetadataKey<Custom.Point>("at"))
            .Map("/levels", _ => new LevelRoute(Level.Low), f => f.Query("level", r => r.Level));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("The format function of route type", exception.Message, StringComparison.Ordinal);
        Assert.Contains("query value 'level'", exception.Message, StringComparison.Ordinal);
    }

    // Within one mapping, its first query name in the order declared decides which key is reported, not the order
    // the keys were declared in.
    [Fact]
    public void TheFirstQueryNameOfAMappingDecidesWhichKeyIsReported()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddQueryMetadata(new RouteMetadataKey<string>("b"))
            .AddQueryMetadata(new RouteMetadataKey<string>("a"))
            .Map("/search", _ => new SearchRoute(null), f => f.Query("a", r => r.Q).Query("b", r => r.Q));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("route metadata key 'a'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATableAlreadyBuiltIsNotChangedByALaterDeclaration()
    {
        RouteTableBuilder builder = ItemsBuilder();
        RouteTable before = builder.Build();
        builder.AddQueryMetadata(Campaign);
        RouteTable after = builder.Build();

        AppRouteRequest request = new AppRouteRequest(new ItemRoute(5)).WithMetadata(Campaign, "spring");

        Assert.Equal("/items/5", before.Format(request));
        Assert.Equal("/items/5?campaign=spring", after.Format(request));
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

    private sealed record SearchRoute(string? Q) : AppRoute;

    private sealed record LevelRoute(Level Level) : AppRoute;

    private sealed record TagsRoute(IReadOnlyList<string> Tags) : AppRoute;
}
