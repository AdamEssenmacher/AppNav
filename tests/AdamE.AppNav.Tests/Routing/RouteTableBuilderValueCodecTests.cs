using System.Collections;
using System.Collections.Immutable;
using AdamE.AppNav.Routing;
using Xunit;
using static AdamE.AppNav.Tests.Routing.CustomValueTypes;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteTableBuilderValueCodecTests
{
    private const string BuiltIn = "built-in types";
    private const string NullableForm = "is the nullable form of";
    private const string Collection = "is a collection";
    private const string OneCodec = "a type has one codec";
    private const string NoCodec = "AddValueCodec";

    [Fact]
    public void RegisteringReturnsTheSameBuilder()
    {
        var builder = new RouteTableBuilder();

        Assert.Same(builder, builder.AddValueCodec<ProductId>(ProductId.TryParse, id => Format(id.Value)));
        Assert.Same(builder, builder.AddValueCodec<Slug>(Slug.TryParse, slug => slug.Value));
    }

    [Fact]
    public void NullFunctionsAreRejectedAndRegisterNothing()
    {
        var builder = new RouteTableBuilder();

        ArgumentNullException parser = Assert.Throws<ArgumentNullException>(
            () => builder.AddValueCodec<ProductId>(null!, id => Format(id.Value)));
        ArgumentNullException writer = Assert.Throws<ArgumentNullException>(
            () => builder.AddValueCodec<ProductId>(ProductId.TryParse, null!));

        Assert.Equal("tryParse", parser.ParamName);
        Assert.Equal("format", writer.ParamName);
        AssertBuilderStillWorks(builder);
    }

    public static TheoryData<string, Action<RouteTableBuilder>> BuiltInTypes => new()
    {
        { "string", Register<string>() },
        { "bool", Register<bool>() },
        { "Guid", Register<Guid>() },
        { "decimal", Register<decimal>() },
        { "double", Register<double>() },
        { "float", Register<float>() },
        { "sbyte", Register<sbyte>() },
        { "byte", Register<byte>() },
        { "short", Register<short>() },
        { "ushort", Register<ushort>() },
        { "int", Register<int>() },
        { "uint", Register<uint>() },
        { "long", Register<long>() },
        { "ulong", Register<ulong>() },
    };

    // The nullable form of a built-in type is named as such.
    [Fact]
    public void TheNullableFormOfABuiltInTypeCannotBeGivenACodec()
    {
        foreach (Action<RouteTableBuilder> register in new[] { Register<int?>(), Register<Guid?>() })
        {
            var builder = new RouteTableBuilder();

            ArgumentException exception = Assert.Throws<ArgumentException>(() => register(builder));

            Assert.Equal("TValue", exception.ParamName);
            Assert.Contains("the nullable form of a built-in type", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("instead", exception.Message, StringComparison.Ordinal);
            AssertBuilderStillWorks(builder);
        }
    }

    // A char is one character of a string, which QueryValues rejects as an element, so it cannot have a codec.
    [Fact]
    public void ACharCannotBeGivenACodec()
    {
        foreach (Action<RouteTableBuilder> register in new[] { Register<char>(), Register<char?>() })
        {
            var builder = new RouteTableBuilder();

            ArgumentException exception = Assert.Throws<ArgumentException>(() => register(builder));

            Assert.Equal("TValue", exception.ParamName);
            Assert.Contains("a char is one character of a string", exception.Message, StringComparison.Ordinal);
            AssertBuilderStillWorks(builder);
        }
    }

    // A built-in type reads and writes by rules that every table shares and the constraints check, so it keeps them.
    [Theory]
    [MemberData(nameof(BuiltInTypes))]
    public void ABuiltInTypeCannotBeGivenACodec(string type, Action<RouteTableBuilder> register)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => register(builder));

        Assert.Equal("TValue", exception.ParamName);
        Assert.Contains(BuiltIn, exception.Message, StringComparison.Ordinal);
        Assert.Contains("a type of your own", exception.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(type));
        AssertBuilderStillWorks(builder);
    }

    public static TheoryData<string, string, Action<RouteTableBuilder>> NullableTypes => new()
    {
        { "ProductId?", "ProductId", Register<ProductId?>() },
        { "Point?", "Point", Register<Point?>() },
        { "Stage?", "Stage", Register<Stage?>() },
        { "DateTime?", "DateTime", Register<DateTime?>() },
    };

    // The nullable form of a value type is read and written by the codec of the type itself, so it has none of its own.
    [Theory]
    [MemberData(nameof(NullableTypes))]
    public void ANullableTypeCannotBeGivenACodec(string type, string underlying, Action<RouteTableBuilder> register)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => register(builder));

        Assert.Equal("TValue", exception.ParamName);
        Assert.Contains(NullableForm, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{type}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{underlying}' instead", exception.Message, StringComparison.Ordinal);
        AssertBuilderStillWorks(builder);
    }

    public static TheoryData<string, Action<RouteTableBuilder>> CollectionTypes => new()
    {
        { "Int32[]", Register<int[]>() },
        { "List<CustomValueTypes.ProductId>", Register<List<ProductId>>() },
        { "IEnumerable<String>", Register<IEnumerable<string>>() },
        { "IEnumerable", Register<IEnumerable>() },
        { "Dictionary<String, Int32>", Register<Dictionary<string, int>>() },
        { "ImmutableArray<Int32>", Register<ImmutableArray<int>>() },
        { "RouteTableBuilderValueCodecTests.Tags", Register<Tags>() },
        { "ImmutableArray<Int32>?", Register<ImmutableArray<int>?>() },
    };

    // A codec reads and writes one value, so a collection, which the query writes as a list, cannot have one.
    [Theory]
    [MemberData(nameof(CollectionTypes))]
    public void ACollectionCannotBeGivenACodec(string type, Action<RouteTableBuilder> register)
    {
        var builder = new RouteTableBuilder();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => register(builder));

        Assert.Equal("TValue", exception.ParamName);
        Assert.Contains(Collection, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{type}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Wrap the collection in a type of your own", exception.Message, StringComparison.Ordinal);
        AssertBuilderStillWorks(builder);
    }

    // The second registration is rejected and the first one stays.
    [Fact]
    public void ATypeRegisteredTwiceKeepsItsFirstCodec()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddValueCodec<ProductId>(ProductId.TryParse, id => Format(id.Value));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.AddValueCodec<ProductId>((string _, out ProductId id) =>
            {
                id = new ProductId(-1);
                return true;
            }, _ => "other"));

        Assert.Equal("TValue", exception.ParamName);
        Assert.Contains(OneCodec, exception.Message, StringComparison.Ordinal);
        Assert.Contains("registered with AddValueCodec", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{nameof(ProductId)}'", exception.Message, StringComparison.Ordinal);
        AssertReadsProductId(ReadingProductId(builder).Build());
    }

    // An enum has one codec too, whichever method registered it and whichever came first.
    [Fact]
    public void AnEnumHasOneCodecWhicheverMethodRegisteredItFirst()
    {
        RouteTableBuilder byEnum = new RouteTableBuilder().AddEnumValueCodec<Stage>();
        RouteTableBuilder byValue = new RouteTableBuilder().AddValueCodec<Stage>(
            (string text, out Stage stage) => Enum.TryParse(text, ignoreCase: true, out stage),
            stage => stage.ToString().ToUpperInvariant());

        ArgumentException valueAfterEnum = Assert.Throws<ArgumentException>(
            () => byEnum.AddValueCodec<Stage>((string _, out Stage stage) =>
            {
                stage = Stage.Done;
                return true;
            }, _ => "done"));
        ArgumentException enumAfterValue = Assert.Throws<ArgumentException>(() => byValue.AddEnumValueCodec<Stage>());

        Assert.Equal("TValue", valueAfterEnum.ParamName);
        Assert.Contains(OneCodec, valueAfterEnum.Message, StringComparison.Ordinal);
        Assert.Contains("registered with AddEnumValueCodec", valueAfterEnum.Message, StringComparison.Ordinal);
        Assert.Equal("TEnum", enumAfterValue.ParamName);
        Assert.Contains(OneCodec, enumAfterValue.Message, StringComparison.Ordinal);
        Assert.Contains("registered with AddValueCodec", enumAfterValue.Message, StringComparison.Ordinal);

        // Each builder keeps the codec it had: the names in any case, and the codec of the app's own.
        RouteTable enumTable = ReadingStage(byEnum);
        RouteTable valueTable = ReadingStage(byValue);
        Assert.Equal(new ProductRoute("InProgress"), Match(enumTable, "/s/inprogress").Route);
        Assert.Equal("/w/InProgress", enumTable.Format(new StageRoute(Stage.InProgress)));
        Assert.Equal(new ProductRoute("InProgress"), Match(valueTable, "/s/InProgress").Route);
        Assert.Equal("/w/INPROGRESS", valueTable.Format(new StageRoute(Stage.InProgress)));
    }

    // Registering an enum twice with AddEnumValueCodec does nothing, as it always has.
    [Fact]
    public void AnEnumRegisteredTwiceByAddEnumValueCodecIsStillAccepted()
    {
        RouteTableBuilder builder = new RouteTableBuilder().AddEnumValueCodec<Stage>();

        Assert.Same(builder, builder.AddEnumValueCodec<Stage>());
        Assert.Equal(new ProductRoute("Done"), Match(ReadingStage(builder), "/s/DONE").Route);
    }

    // The message for a registration names the type as written in code, with the types it is declared in.
    [Fact]
    public void ATypeNestedInAGenericTypeIsNamedInFullWhenRegisteredTwice()
    {
        RouteTableBuilder builder = Builder();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => builder.AddValueCodec<Outer<int>.Inner>((string _, out Outer<int>.Inner inner) =>
            {
                inner = default;
                return false;
            }, _ => null));

        const string Inner = nameof(CustomValueTypes) + ".Outer<Int32>.Inner";
        Assert.Contains($"'{Inner}'", exception.Message, StringComparison.Ordinal);
    }

    // Any type that is not built in, nullable or a collection can be given a codec: classes, structs, records,
    // interfaces, abstract classes, enums, and even object.
    [Fact]
    public void AnyOtherTypeCanBeGivenACodec()
    {
        RouteTableBuilder builder = Builder()
            .AddValueCodec<Circle>(Never<Circle>(), _ => "c")
            .AddValueCodec<Shape>(Never<Shape>(), _ => "s")
            .AddValueCodec<object>(Never<object>(), _ => "o")
            .AddValueCodec<DayOfWeek>(Never<DayOfWeek>(), _ => "d")
            .AddValueCodec<DateTime>(Never<DateTime>(), _ => "t")
            .AddValueCodec<Half>(Never<Half>(), _ => "h");

        Assert.NotNull(builder.Build());
    }

    [Fact]
    public void ARegistrationAfterAMappingServesThatMapping()
    {
        RouteTableBuilder builder = ReadingProductId(new RouteTableBuilder());

        builder.AddValueCodec<ProductId>(ProductId.TryParse, id => Format(id.Value));

        AssertReadsProductId(builder.Build());
    }

    [Fact]
    public void ARegistrationAfterBuildChangesOnlyTheTablesBuiltAfterIt()
    {
        RouteTableBuilder builder = ReadingProductId(new RouteTableBuilder());
        RouteTable before = builder.Build();

        builder.AddValueCodec<ProductId>(ProductId.TryParse, id => Format(id.Value));
        RouteTable after = builder.Build();

        AssertNoCodec(before, "/p/7");
        AssertReadsProductId(after);
    }

    // Each table keeps the codecs of the builder that built it, and two builders' codecs for one type are separate.
    [Fact]
    public void TablesWithDifferentRegistrationsAreIndependent()
    {
        RouteTable plain = ReadingProductId(
                new RouteTableBuilder().AddValueCodec<ProductId>(ProductId.TryParse, id => Format(id.Value)))
            .Build();
        RouteTable doubled = ReadingProductId(
                new RouteTableBuilder().AddValueCodec<ProductId>(
                    (string text, out ProductId id) =>
                    {
                        bool parsed = ProductId.TryParse(text, out id);
                        id = new ProductId(id.Value * 2);
                        return parsed;
                    },
                    id => Format(id.Value)))
            .Build();
        RouteTable none = ReadingProductId(new RouteTableBuilder()).Build();

        Assert.Equal(new ProductRoute("7"), Match(plain, "/p/7").Route);
        Assert.Equal(new ProductRoute("14"), Match(doubled, "/p/7").Route);
        AssertNoCodec(none, "/p/7");
    }

    private static Action<RouteTableBuilder> Register<T>()
    {
        return builder => builder.AddValueCodec<T>(Never<T>(), _ => null);
    }

    private static RouteValueParser<T> Never<T>()
    {
        return (string _, out T? value) =>
        {
            value = default;
            return false;
        };
    }

    // A failed registration leaves nothing behind: the builder maps, registers and builds as before.
    private static void AssertBuilderStillWorks(RouteTableBuilder builder)
    {
        RouteTableBuilder mapped = ReadingProductId(builder);
        AssertNoCodec(mapped.Build(), "/p/1");
        AssertReadsProductId(mapped.AddValueCodec<ProductId>(ProductId.TryParse, _ => "1").Build());
    }

    private static RouteTableBuilder ReadingProductId(RouteTableBuilder builder)
    {
        return builder.Map("/p/{id}", c => new ProductRoute(Format(c.Path<ProductId>("id").Value)));
    }

    private static void AssertNoCodec(RouteTable table, string uri)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Match(table, uri));

        Assert.Contains(NoCodec, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertReadsProductId(RouteTable table)
    {
        Assert.Equal(new ProductRoute("7"), Match(table, "/p/007").Route);
        Assert.False(Match(table, "/p/x").IsSuccess);
    }

    private static RouteTable ReadingStage(RouteTableBuilder builder)
    {
        return builder
            .Map("/s/{stage}", c => new ProductRoute(c.Path<Stage>("stage").ToString()))
            .Map("/w/{stage}", c => new StageRoute(c.Path<Stage>("stage")), f => f.Path("stage", r => r.Stage))
            .Build();
    }

    private static RouteMatchResult Match(RouteTable table, string uri)
    {
        return table.Match(new Uri(uri, UriKind.Relative));
    }

    private sealed record StageRoute(Stage Stage) : AppRoute;

    private abstract class Shape;

    // A type of the app's own that is a collection.
    private sealed class Tags : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator()
        {
            yield break;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
