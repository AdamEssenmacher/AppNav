using System.Globalization;
using System.Numerics;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteMatchContextTypedQueryTests
{
    private const string QueryValue = "query value";
    private const string PathValue = "path value";
    private const string ListOfQueryValues = "A list of query values";
    private const string NotValid = "is not a valid";
    private const string Unsupported = "cannot be read as type";
    private const string NeverAbsent = "never absent";
    private const string GuidText = "6f9619ff-8b86-d011-b42d-00cf4fc964ff";
    private const string OtherGuidText = "00000000-0000-0000-0000-000000000001";

    // Text that stands for a secret a deep link can carry, distinctive enough that finding it in a message or in Data
    // is not a coincidence.
    private const string Secret = "tok-Vn3Jq8Rw-9Wq4Lm7Tz";

    [Fact]
    public void AnIntFollowsEveryRule()
    {
        AssertRules("42", 42, "-7", -7, "int");
    }

    [Fact]
    public void ALongFollowsEveryRule()
    {
        AssertRules("3000000000", 3000000000L, "-1", -1L, "long");
    }

    [Fact]
    public void ADecimalFollowsEveryRule()
    {
        AssertRules("19.95", 19.95m, "-0.50", -0.50m, "decimal");
    }

    [Fact]
    public void ABoolFollowsEveryRule()
    {
        AssertRules("tRuE", true, "false", false, "bool");
    }

    [Fact]
    public void AGuidFollowsEveryRule()
    {
        AssertRules(GuidText, new Guid(GuidText), OtherGuidText, new Guid(OtherGuidText), "Guid");
    }

    [Fact]
    public void AnSbyteFollowsEveryRule()
    {
        AssertRules("-128", sbyte.MinValue, "127", sbyte.MaxValue, "sbyte");
    }

    [Fact]
    public void AByteFollowsEveryRule()
    {
        AssertRules("255", byte.MaxValue, "0", (byte)0, "byte");
    }

    [Fact]
    public void AShortFollowsEveryRule()
    {
        AssertRules("-32768", short.MinValue, "32767", short.MaxValue, "short");
    }

    [Fact]
    public void AUshortFollowsEveryRule()
    {
        AssertRules("65535", ushort.MaxValue, "007", (ushort)7, "ushort");
    }

    [Fact]
    public void AUintFollowsEveryRule()
    {
        AssertRules("4294967295", uint.MaxValue, "-0", 0u, "uint");
    }

    [Fact]
    public void AUlongFollowsEveryRule()
    {
        AssertRules("18446744073709551615", ulong.MaxValue, "1", 1UL, "ulong");
    }

    [Fact]
    public void AFloatFollowsEveryRule()
    {
        AssertRules("1.5", 1.5f, "-.5", -0.5f, "float");
    }

    [Fact]
    public void ADoubleFollowsEveryRule()
    {
        AssertRules("19.95", 19.95, "-0", -0.0, "double");
    }

    // The cases the path tests read, as the first value and as every value of a name, and the empty value, which
    // only a query can carry.
    [Theory]
    [InlineData("sbyte")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("ulong")]
    public void AnIntegerTypeFollowsTheRuleOfIntWithinItsRange(string type)
    {
        switch (type)
        {
            case "sbyte":
                AssertIntegerCases<sbyte>(type);
                break;
            case "byte":
                AssertIntegerCases<byte>(type);
                break;
            case "short":
                AssertIntegerCases<short>(type);
                break;
            case "ushort":
                AssertIntegerCases<ushort>(type);
                break;
            case "uint":
                AssertIntegerCases<uint>(type);
                break;
            default:
                AssertIntegerCases<ulong>(type);
                break;
        }
    }

    [Theory]
    [InlineData("float")]
    [InlineData("double")]
    public void AFloatingPointTypeAcceptsTheShapeOfDecimalAndNoMagnitudeTooLargeForIt(string type)
    {
        if (type == "float")
            AssertFloatingPointCases<float>(type, static c => c.IsFloat);
        else
            AssertFloatingPointCases<double>(type, static c => c.IsDouble);
    }

    // An out string? argument binds to the non-generic member, so the generic one is called with its type argument
    // given, and through a generic method, as code that is generic over the type would call it.
    [Theory]
    [InlineData("/q?v=a+b&v=&v&v=%2B1&v=caf%C3%A9")]
    [InlineData("/q?v=&v=a")]
    [InlineData("/q?v&v=a")]
    [InlineData("/q?V=x&v=y")]
    [InlineData("/q?w=1")]
    [InlineData("/q")]
    public void AStringIsTheTextAsTheStringReadsGiveIt(string uri)
    {
        Within(uri, context =>
        {
            foreach (string name in new[] { "v", "V" })
            {
                bool expectedFound = context.TryGetQuery(name, out string? expected);

                Assert.Equal(expectedFound, context.TryGetQuery<string>(name, out string? typed));
                Assert.Equal(expected, typed);
                Assert.Equal(expectedFound, GenericRead(context, name, out string? generic));
                Assert.Equal(expected, generic);

                IReadOnlyList<string> values = context.GetQueryValues<string>(name);
                Assert.Equal(context.GetQueryValues(name), values);
                Assert.Equal(context.GetQueryValues(name), GenericReadAll<string>(context, name));
                AssertCannotBeChanged(values, "changed");
            }

            AssertNullName(() => context.TryGetQuery<string>(null!, out string? _));
            AssertNullName(() => GenericRead(context, null!, out string? _));
            AssertNullName(() => context.GetQueryValues<string>(null!));
        });
    }

    [Fact]
    public void AStringReadGivesTheFirstValueAndTheEmptyValue()
    {
        Within("/q?v=a+b&v=&v&v=%2B1&v=caf%C3%A9", context =>
        {
            Assert.True(context.TryGetQuery<string>("v", out string? value));
            Assert.Equal("a b", value);
            Assert.True(GenericRead(context, "V", out string? generic));
            Assert.Equal("a b", generic);
            Assert.Equal(["a b", "", "", "+1", "caf\u00E9"], context.GetQueryValues<string>("v"));
        });

        foreach (string uri in new[] { "/q?flag", "/q?flag=", "/q?flag&flag=x" })
        {
            Within(uri, context =>
            {
                Assert.True(context.TryGetQuery<string>("flag", out string? value));
                Assert.Equal("", value);
                Assert.True(GenericRead(context, "flag", out string? generic));
                Assert.Equal("", generic);
                Assert.Equal("", context.GetQueryValues<string>("flag")[0]);
            });
        }

        Within("/q", context =>
        {
            Assert.False(context.TryGetQuery<string>("v", out string? value));
            Assert.Null(value);
            Assert.False(GenericRead(context, "v", out string? generic));
            Assert.Null(generic);
            AssertEmptyAndUnchangeable(context.GetQueryValues<string>("v"), "added");
        });
    }

    // + is a space in a query, so +5 must be written %2B5 to be read as a signed number.
    [Fact]
    public void APlusIsASpaceAndAPercentEncodedPlusIsASign()
    {
        Within("/q?n=+5&m=%2B5&e=%34%32&s=-%35", context =>
        {
            AssertInvalid(() => context.TryGetQuery("n", out int _), "n", "int");
            AssertInvalid(() => context.TryGetQuery("n", out int? _), "n", "int");
            AssertInvalid(() => context.GetQueryValues<int>("n"), "n", "int");
            Assert.True(context.TryGetQuery("n", out string? text));
            Assert.Equal(" 5", text);

            Assert.True(context.TryGetQuery("m", out int plus));
            Assert.Equal(5, plus);
            Assert.Equal([5], context.GetQueryValues<int>("m"));
            Assert.True(context.TryGetQuery("e", out long encoded));
            Assert.Equal(42L, encoded);
            Assert.True(context.TryGetQuery("s", out decimal? negative));
            Assert.Equal(-5m, negative);
        });
    }

    // An absolute URI has its query read from Uri.Query, and the typed reads follow the same rules there.
    [Fact]
    public void TypedReadsOfAnAbsoluteUriFollowTheSameRules()
    {
        Within("/q", "myapp://host/q?page=2&id=3&id=5&bad=x&n=+5#page=9", context =>
        {
            Assert.True(context.TryGetQuery("page", out int page));
            Assert.Equal(2, page);
            Assert.True(context.TryGetQuery("PAGE", out long? nullable));
            Assert.Equal(2L, nullable);
            Assert.Equal([3, 5], context.GetQueryValues<int>("id"));
            Assert.False(context.TryGetQuery("missing", out Guid? missing));
            Assert.Null(missing);
            AssertInvalid(() => context.TryGetQuery("bad", out bool _), "bad", "bool");
            AssertInvalid(() => context.GetQueryValues<decimal>("bad"), "bad", "decimal");
            AssertInvalid(() => context.TryGetQuery("n", out int _), "n", "int");
        }, UriKind.Absolute);
    }

    // Over the corpus the path tests use, the same text read as a query value and as a path value converts or is
    // rejected alike, to the same value.
    [Fact]
    public void AQueryValueConvertsExactlyAsAPathValueDoes()
    {
        AssertEquivalentToPath<int>();
        AssertEquivalentToPath<long>();
        AssertEquivalentToPath<decimal>();
        AssertEquivalentToPath<bool>();
        AssertEquivalentToPath<Guid>();
        AssertEquivalentToPath<sbyte>();
        AssertEquivalentToPath<byte>();
        AssertEquivalentToPath<short>();
        AssertEquivalentToPath<ushort>();
        AssertEquivalentToPath<uint>();
        AssertEquivalentToPath<ulong>();
        AssertEquivalentToPath<float>();
        AssertEquivalentToPath<double>();
    }

    [Fact]
    public void APathValueAndAQueryValueOfTheSameNameFailDistinguishably()
    {
        Within("/p/{id}", "/p/x?id=x", context =>
        {
            RouteValueException path = Assert.Throws<RouteValueException>(() => context.Path<int>("id"));
            RouteValueException query = Assert.Throws<RouteValueException>(() => context.TryGetQuery("id", out int _));
            RouteValueException list = Assert.Throws<RouteValueException>(() => context.GetQueryValues<int>("id"));

            Assert.Equal("id", path.ValueName);
            Assert.Equal("id", query.ValueName);
            Assert.Equal("id", list.ValueName);
            Assert.Contains(PathValue, path.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(QueryValue, path.Message, StringComparison.Ordinal);
            Assert.Contains(QueryValue, query.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(PathValue, query.Message, StringComparison.Ordinal);
            Assert.Contains(QueryValue, list.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(PathValue, list.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ARejectedQueryValueFailsTheMatchLikeARejectedPathValue()
    {
        var laterCalled = false;
        RouteTable table = new RouteTableBuilder()
            .Map("/orders", c =>
            {
                c.TryGetQuery("page", out int page);
                return new ProductRoute(page.ToString(CultureInfo.InvariantCulture));
            })
            .Map("/{*rest}", _ =>
            {
                laterCalled = true;
                return new SettingsRoute();
            })
            .Build();

        RouteMatchResult result = table.Match(new Uri("/orders?page=x", UriKind.Relative));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Route);
        Assert.Equal("/orders", result.Definition?.Template.Value);
        Assert.False(laterCalled);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal(["name", "path", "reason", "template"], diagnostic.Data.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("page", diagnostic.Data["name"]);
        Assert.Equal("/orders", diagnostic.Data["template"]);
        Assert.Equal("/orders", diagnostic.Data["path"]);
        Assert.Contains(QueryValue, (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
        Assert.EndsWith(" int.", (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
        Assert.DoesNotContain("page=", diagnostic.Message, StringComparison.Ordinal);

        Assert.Equal(new ProductRoute("3"), table.Match(new Uri("/orders?page=3", UriKind.Relative)).Route);
        Assert.Equal(new ProductRoute("0"), table.Match(new Uri("/orders", UriKind.Relative)).Route);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("guid")]
    [InlineData("int?")]
    [InlineData("guid?")]
    [InlineData("list int")]
    [InlineData("list guid")]
    [InlineData("float")]
    [InlineData("short?")]
    [InlineData("list byte")]
    [InlineData("list double")]
    [InlineData("enum")]
    [InlineData("enum?")]
    [InlineData("list enum")]
    public void AFailedConversionNeverQuotesTheValue(string type)
    {
        RouteValueException? thrown = null;
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .Map("/orders", c =>
            {
                try
                {
                    switch (type)
                    {
                        case "int":
                            c.TryGetQuery("token", out int _);
                            break;
                        case "long":
                            c.TryGetQuery("token", out long _);
                            break;
                        case "decimal":
                            c.TryGetQuery("token", out decimal _);
                            break;
                        case "bool":
                            c.TryGetQuery("token", out bool _);
                            break;
                        case "guid":
                            c.TryGetQuery("token", out Guid _);
                            break;
                        case "int?":
                            c.TryGetQuery("token", out int? _);
                            break;
                        case "guid?":
                            c.TryGetQuery("token", out Guid? _);
                            break;
                        case "list int":
                            _ = c.GetQueryValues<int>("token");
                            break;
                        case "float":
                            c.TryGetQuery("token", out float _);
                            break;
                        case "short?":
                            c.TryGetQuery("token", out short? _);
                            break;
                        case "list byte":
                            _ = c.GetQueryValues<byte>("token");
                            break;
                        case "list double":
                            _ = c.GetQueryValues<double>("token");
                            break;
                        case "enum":
                            c.TryGetQuery("token", out Status _);
                            break;
                        case "enum?":
                            c.TryGetQuery("token", out Status? _);
                            break;
                        case "list enum":
                            _ = c.GetQueryValues<Status>("token");
                            break;
                        default:
                            _ = c.GetQueryValues<Guid>("token");
                            break;
                    }
                }
                catch (RouteValueException exception)
                {
                    thrown = exception;
                    throw;
                }

                return new SettingsRoute();
            })
            .Build();

        RouteMatchResult result = table.Match(new Uri($"/orders?token={Secret}&k=1", UriKind.Relative));

        Assert.NotNull(thrown);
        Assert.Equal("token", thrown.ValueName);
        Assert.Null(thrown.InnerException);
        Assert.Contains(NotValid, thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, thrown.ToString(), StringComparison.Ordinal);

        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.DoesNotContain(Secret, diagnostic.Message, StringComparison.Ordinal);
        Assert.NotEmpty(diagnostic.Data);
        foreach ((string _, object? value) in diagnostic.Data)
            Assert.DoesNotContain(Secret, (string?)value, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameIsEscapedInTheMessageAsForAPathValue()
    {
        Within("/q?a%0Ab=x", context =>
        {
            RouteValueException exception = Assert.Throws<RouteValueException>(
                () => context.TryGetQuery("a\nb", out int _));

            Assert.Equal("a\nb", exception.ValueName);
            Assert.DoesNotContain("\n", exception.Message, StringComparison.Ordinal);
        });

        Within("/q?=x", context =>
        {
            AssertInvalid(() => context.TryGetQuery("", out bool _), "", "bool");
            AssertInvalid(() => context.GetQueryValues<bool>(""), "", "bool");
        });
    }

    [Fact]
    public void AnUnsupportedTypeIsAProgrammingErrorWhetherOrNotTheValueIsPresent()
    {
        foreach (string uri in new[] { "/q?v=42", "/q", "/q?v=" })
        {
            Within(uri, context =>
            {
                foreach (string? name in new[] { "v", "missing", null })
                {
                    AssertUnsupported(() => context.TryGetQuery(name!, out Half _), "'Half'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out Half? _), "'Half?'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out Int128 _), "'Int128'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out UInt128? _), "'UInt128?'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out char _), "'Char'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out nint? _), "'IntPtr?'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out nuint _), "'UIntPtr'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out DateTime _), "'DateTime'");
                    AssertUnregistered(() => context.TryGetQuery(name!, out DayOfWeek? _), "'DayOfWeek?'", "DayOfWeek");
                    AssertUnregistered(
                        () => context.TryGetQuery(name!, out Status _),
                        "'" + StatusName + "'",
                        StatusName);
                    AssertUnsupported(() => context.TryGetQuery(name!, out List<int>? _), "'List<Int32>'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out object? _), "'Object'");
                    AssertListUnsupported(() => context.GetQueryValues<Half>(name!), "'Half'");
                    AssertListUnsupported(() => context.GetQueryValues<Half?>(name!), "'Half?'");
                    AssertListUnsupported(() => context.GetQueryValues<Int128>(name!), "'Int128'");
                    AssertListUnsupported(() => context.GetQueryValues<char>(name!), "'Char'");
                    AssertListUnsupported(() => context.GetQueryValues<nint>(name!), "'IntPtr'");
                    AssertListUnsupported(() => context.GetQueryValues<DateTime>(name!), "'DateTime'");
                    AssertListUnregistered(() => context.GetQueryValues<DayOfWeek>(name!), "'DayOfWeek'", "DayOfWeek");
                    AssertListUnregistered(
                        () => context.GetQueryValues<Status>(name!),
                        "'" + StatusName + "'",
                        StatusName);
                    AssertListUnsupported(() => context.GetQueryValues<int[]>(name!), "'Int32[]'");
                    AssertListUnsupported(() => context.GetQueryValues<object>(name!), "'Object'");

                    AssertNullableList(() => context.GetQueryValues<int?>(name!), "'Int32?'", "'Int32'");
                    AssertNullableList(() => context.GetQueryValues<long?>(name!), "'Int64?'", "'Int64'");
                    AssertNullableList(() => context.GetQueryValues<decimal?>(name!), "'Decimal?'", "'Decimal'");
                    AssertNullableList(() => context.GetQueryValues<bool?>(name!), "'Boolean?'", "'Boolean'");
                    AssertNullableList(() => context.GetQueryValues<Guid?>(name!), "'Guid?'", "'Guid'");
                    AssertNullableList(() => context.GetQueryValues<sbyte?>(name!), "'SByte?'", "'SByte'");
                    AssertNullableList(() => context.GetQueryValues<byte?>(name!), "'Byte?'", "'Byte'");
                    AssertNullableList(() => context.GetQueryValues<short?>(name!), "'Int16?'", "'Int16'");
                    AssertNullableList(() => context.GetQueryValues<ushort?>(name!), "'UInt16?'", "'UInt16'");
                    AssertNullableList(() => context.GetQueryValues<uint?>(name!), "'UInt32?'", "'UInt32'");
                    AssertNullableList(() => context.GetQueryValues<ulong?>(name!), "'UInt64?'", "'UInt64'");
                    AssertNullableList(() => context.GetQueryValues<float?>(name!), "'Single?'", "'Single'");
                    AssertNullableList(() => context.GetQueryValues<double?>(name!), "'Double?'", "'Double'");
                    AssertNullableList(() => context.GetQueryValues<DayOfWeek?>(name!), "'DayOfWeek?'", "'DayOfWeek'");
                    AssertNullableList(
                        () => context.GetQueryValues<Status?>(name!),
                        "'" + StatusName + "?'",
                        "'" + StatusName + "'");
                }
            });

            // Registering the enum does not make its nullable form a list type.
            WithinEnums(uri, context =>
            {
                foreach (string? name in new[] { "v", "missing", null })
                {
                    AssertNullableList(
                        () => context.GetQueryValues<Status?>(name!),
                        "'" + StatusName + "?'",
                        "'" + StatusName + "'");
                }
            });
        }
    }

    // The messages for a single value, path or query, and for a list name every supported type by its keyword, in the
    // part of the message that lists them.
    [Fact]
    public void AnUnsupportedTypeMessageListsEverySupportedType()
    {
        string[] keywords =
        [
            "string", "bool", "Guid", "decimal", "double", "float", "sbyte", "byte", "short", "ushort", "int", "uint",
            "long", "ulong",
        ];

        Within("/p/{id}", "/p/1?v=1", context =>
        {
            string[] lists =
            [
                SupportedList(() => context.Path<Half>("id"), ", and the nullable forms"),
                SupportedList(() => context.TryGetPath("id", out Int128 _), ", and the nullable forms"),
                SupportedList(() => context.TryGetQuery("v", out char _), ", and the nullable forms"),
                SupportedList(() => context.GetQueryValues<Half>("v"), "; read the values"),
            ];

            foreach (string list in lists)
            {
                foreach (string keyword in keywords)
                    Assert.Matches($@"(^|[ ,]){keyword}($|[ ,])", list);
            }
        });
    }

    [Fact]
    public void AnUnsupportedTypeEscapesMatch()
    {
        RouteTable single = new RouteTableBuilder()
            .Map("/q", c => new ProductRoute(c.TryGetQuery("v", out Half _) ? "x" : "y"))
            .Build();
        RouteTable list = new RouteTableBuilder()
            .Map("/q", c => new ProductRoute(c.GetQueryValues<int?>("v").Count > 0 ? "x" : "y"))
            .Build();

        Assert.Throws<InvalidOperationException>(() => single.Match(new Uri("/q?v=1", UriKind.Relative)));
        Assert.Throws<InvalidOperationException>(() => list.Match(new Uri("/q", UriKind.Relative)));
    }

    // A typed read must cost no more than the string read of the same name and a parse of it, and a list of values no
    // more than the string list and an array of the parsed values. A boxed value is larger than a pointer for every
    // supported value type, so a box on each value read would make the difference at least one pointer per read, and
    // the bound allows less than that in all.
    [Theory]
    [InlineData("int", "42")]
    [InlineData("absolute int", "42")]
    [InlineData("long", "42")]
    [InlineData("decimal", "4.2")]
    [InlineData("bool", "true")]
    [InlineData("guid", GuidText)]
    [InlineData("int?", "42")]
    [InlineData("long?", "42")]
    [InlineData("decimal?", "4.2")]
    [InlineData("bool?", "true")]
    [InlineData("guid?", GuidText)]
    [InlineData("list int", "42")]
    [InlineData("list long", "42")]
    [InlineData("list decimal", "4.2")]
    [InlineData("list bool", "true")]
    [InlineData("list guid", GuidText)]
    [InlineData("sbyte", "42")]
    [InlineData("ushort?", "42")]
    [InlineData("ulong", "42")]
    [InlineData("float?", "4.2")]
    [InlineData("double", "4.2")]
    [InlineData("list byte", "42")]
    [InlineData("list uint", "42")]
    [InlineData("list float", "4.2")]
    [InlineData("list double", "4.2")]
    public void ATypedReadAllocatesNoMoreThanTheStringReadOfTheSameName(string type, string text)
    {
        const int Reads = 16;
        Func<RouteMatchContext, int> typedRead = type switch
        {
            "sbyte" => c => c.TryGetQuery("id", out sbyte value) ? value : 0,
            "ushort?" => c => c.TryGetQuery("id", out ushort? value) ? value!.Value : 0,
            "ulong" => c => c.TryGetQuery("id", out ulong value) ? (int)value : 0,
            "float?" => c => c.TryGetQuery("id", out float? value) ? (int)value!.Value : 0,
            "double" => c => c.TryGetQuery("id", out double value) ? (int)value : 0,
            "list byte" => c => c.GetQueryValues<byte>("id")[1],
            "list uint" => c => (int)c.GetQueryValues<uint>("id")[1],
            "list float" => c => (int)c.GetQueryValues<float>("id")[1],
            "list double" => c => (int)c.GetQueryValues<double>("id")[1],
            "int" or "absolute int" => c => c.TryGetQuery("id", out int value) ? value : 0,
            "long" => c => c.TryGetQuery("id", out long value) ? (int)value : 0,
            "decimal" => c => c.TryGetQuery("id", out decimal value) ? (int)value : 0,
            "bool" => c => c.TryGetQuery("id", out bool value) && value ? 1 : 0,
            "guid" => c => c.TryGetQuery("id", out Guid value) ? value.GetHashCode() : 0,
            "int?" => c => c.TryGetQuery("id", out int? value) ? value!.Value : 0,
            "long?" => c => c.TryGetQuery("id", out long? value) ? (int)value!.Value : 0,
            "decimal?" => c => c.TryGetQuery("id", out decimal? value) ? (int)value!.Value : 0,
            "bool?" => c => c.TryGetQuery("id", out bool? value) && value!.Value ? 1 : 0,
            "guid?" => c => c.TryGetQuery("id", out Guid? value) ? value!.Value.GetHashCode() : 0,
            "list int" => c => c.GetQueryValues<int>("id")[1],
            "list long" => c => (int)c.GetQueryValues<long>("id")[1],
            "list decimal" => c => (int)c.GetQueryValues<decimal>("id")[1],
            "list bool" => c => c.GetQueryValues<bool>("id")[1] ? 1 : 0,
            _ => c => c.GetQueryValues<Guid>("id")[1].GetHashCode(),
        };
        Func<RouteMatchContext, int> parsedRead = type switch
        {
            "sbyte" => c => c.TryGetQuery("id", out string? value) ? sbyte.Parse(value, Invariant) : 0,
            "ushort?" => c => c.TryGetQuery("id", out string? value) ? ushort.Parse(value, Invariant) : 0,
            "ulong" => c => c.TryGetQuery("id", out string? value) ? (int)ulong.Parse(value, Invariant) : 0,
            "float?" => c => c.TryGetQuery("id", out string? value) ? (int)float.Parse(value, Invariant) : 0,
            "double" => c => c.TryGetQuery("id", out string? value) ? (int)double.Parse(value, Invariant) : 0,
            "list byte" => c => ParsedList(c, s => byte.Parse(s, Invariant))[1],
            "list uint" => c => (int)ParsedList(c, s => uint.Parse(s, Invariant))[1],
            "list float" => c => (int)ParsedList(c, s => float.Parse(s, Invariant))[1],
            "list double" => c => (int)ParsedList(c, s => double.Parse(s, Invariant))[1],
            "int" or "absolute int" or "int?" =>
                c => c.TryGetQuery("id", out string? value) ? int.Parse(value, Invariant) : 0,
            "long" or "long?" => c => c.TryGetQuery("id", out string? value) ? (int)long.Parse(value, Invariant) : 0,
            "decimal" or "decimal?" =>
                c => c.TryGetQuery("id", out string? value) ? (int)decimal.Parse(value, Invariant) : 0,
            "bool" or "bool?" => c => c.TryGetQuery("id", out string? value) && bool.Parse(value) ? 1 : 0,
            "guid" or "guid?" =>
                c => c.TryGetQuery("id", out string? value) ? System.Guid.Parse(value).GetHashCode() : 0,
            "list int" => c => ParsedList(c, s => int.Parse(s, Invariant))[1],
            "list long" => c => (int)ParsedList(c, s => long.Parse(s, Invariant))[1],
            "list decimal" => c => (int)ParsedList(c, s => decimal.Parse(s, Invariant))[1],
            "list bool" => c => ParsedList(c, bool.Parse)[1] ? 1 : 0,
            _ => c => ParsedList(c, System.Guid.Parse)[1].GetHashCode(),
        };

        // An absolute URI reads its query from Uri.Query rather than from the original text.
        string query = $"/orders?id={text}&id={text}&id={text}";
        Uri uri = type == "absolute int"
            ? new Uri("myapp://host" + query, UriKind.Absolute)
            : new Uri(query, UriKind.Relative);
        long typedBytes = AllocatedBytesPerMatch(ReadingTable(typedRead, Reads), uri);
        long parsedBytes = AllocatedBytesPerMatch(ReadingTable(parsedRead, Reads), uri);

        Assert.True(
            typedBytes - parsedBytes < Reads * IntPtr.Size,
            $"{type}: typed {typedBytes} bytes, parsed {parsedBytes} bytes");
    }

    [Fact]
    public void AnEnumFollowsEveryRule()
    {
        AssertRules("Pending", Status.Pending, "SHIPPED", Status.Shipped, "Status", EnumBuilder);
        AssertRules("canceled", Status.Cancelled, "inProgress", Status.InProgress, "Status", EnumBuilder);
    }

    [Theory]
    [InlineData("Pending", Status.Pending)]
    [InlineData("pending", Status.Pending)]
    [InlineData("INPROGRESS", Status.InProgress)]
    [InlineData("Cancelled", Status.Cancelled)]
    [InlineData("Canceled", Status.Cancelled)]
    [InlineData("", null)]
    [InlineData("0", null)]
    [InlineData("2", null)]
    [InlineData("-1", null)]
    [InlineData("+2", null)]
    [InlineData("99", null)]
    [InlineData("0x1", null)]
    [InlineData("Pending,Shipped", null)]
    [InlineData("Pending, Shipped", null)]
    [InlineData(" Pending", null)]
    [InlineData("Pending ", null)]
    [InlineData("Pend\u0131ng", null)]
    [InlineData("PEND\u0130NG", null)]
    [InlineData("\u0130nProgress", null)]
    [InlineData("in-progress", null)]
    public void AnEnumQueryValueIsReadByTheRulesOfAPathValue(string text, Status? expected)
    {
        AssertCase(text, expected, "Status", EnumBuilder);
    }

    [Theory]
    [InlineData("Read", Access.Read)]
    [InlineData("readwrite", Access.ReadWrite)]
    [InlineData("Read,Write", null)]
    [InlineData("Read, Write", null)]
    [InlineData("3", null)]
    public void AFlagsEnumQueryValueIsTheNameOfASingleMember(string text, Access? expected)
    {
        AssertCase(text, expected, "Access", EnumBuilder);
    }

    // + is a space in a query, so it cannot be part of a name, and a list of names in one value is not read as several.
    [Fact]
    public void AnEnumQueryValueIsDecodedBeforeItIsRead()
    {
        foreach (string uri in new[] { "/q?v=+Pending", "/q?v=Pending+", "/q?v=%2BPending", "/q?v=Pending%2CShipped" })
        {
            WithinEnums(uri, context =>
            {
                AssertInvalid(() => context.TryGetQuery("v", out Status _), "v", "Status");
                AssertInvalid(() => context.GetQueryValues<Status>("v"), "v", "Status");
            });
        }

        WithinEnums("/q?v=Pend%69ng&v=Shipped", context =>
        {
            Assert.True(context.TryGetQuery("v", out Status? status));
            Assert.Equal(Status.Pending, status);
            Assert.Equal([Status.Pending, Status.Shipped], context.GetQueryValues<Status>("v"));
        });
    }

    [Fact]
    public void AnEnumQueryValueThatIsNotAMemberFailsTheMatch()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/orders", c => new ProductRoute(c.TryGetQuery("status", out Status s) ? s.ToString() : "none"))
            .AddEnumValueCodec<Status>()
            .Build();

        RouteMatchResult result = table.Match(new Uri("/orders?status=1", UriKind.Relative));

        Assert.False(result.IsSuccess);
        Assert.Equal("/orders", result.Definition?.Template.Value);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("status", diagnostic.Data["name"]);
        Assert.Contains(QueryValue, (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
        Assert.EndsWith(" Status.", (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
        Assert.Equal(new ProductRoute("none"), table.Match(new Uri("/orders", UriKind.Relative)).Route);
        Assert.Equal(
            new ProductRoute("InProgress"),
            table.Match(new Uri("/orders?status=inprogress", UriKind.Relative)).Route);
    }

    // An enum read must cost no more than the string read of the same name, and a list of enums no more than the string
    // list and an array of the same size: the name table gives each value without a box.
    [Theory]
    [InlineData("enum")]
    [InlineData("enum?")]
    [InlineData("list enum")]
    public void AnEnumReadAllocatesNoMoreThanTheStringReadOfTheSameName(string type)
    {
        const int Reads = 16;
        Func<RouteMatchContext, int> enumRead = type switch
        {
            "enum" => c => c.TryGetQuery("id", out Status value) ? (int)value : 0,
            "enum?" => c => c.TryGetQuery("id", out Status? value) ? (int)value!.Value : 0,
            _ => c => (int)c.GetQueryValues<Status>("id")[1],
        };
        Func<RouteMatchContext, int> stringRead = type switch
        {
            "list enum" => c => ParsedList(c, s => s.Length)[1],
            _ => c => c.TryGetQuery("id", out string? value) ? value.Length : 0,
        };

        var uri = new Uri("/orders?id=Shipped&id=shipped&id=SHIPPED", UriKind.Relative);
        long enumBytes = AllocatedBytesPerMatch(ReadingTable(enumRead, Reads, EnumBuilder()), uri);
        long stringBytes = AllocatedBytesPerMatch(ReadingTable(stringRead, Reads, EnumBuilder()), uri);

        Assert.True(
            enumBytes - stringBytes < Reads * IntPtr.Size,
            $"{type}: enum {enumBytes} bytes, string {stringBytes} bytes");
    }

    // The name of Status as the message for an enum that is not registered gives it.
    private const string StatusName = nameof(RouteMatchContextTypedQueryTests) + ".Status";

    private static CultureInfo Invariant => CultureInfo.InvariantCulture;

    // A builder with every enum of these tests registered.
    private static RouteTableBuilder EnumBuilder()
    {
        return new RouteTableBuilder().AddEnumValueCodec<Status>().AddEnumValueCodec<Access>();
    }

    // Every rule, for one value type and its nullable form. The two valid texts are different values.
    private static void AssertRules<T>(
        string first,
        T firstValue,
        string second,
        T secondValue,
        string typeName,
        Func<RouteTableBuilder>? builder = null)
        where T : struct
    {
        // Present and valid.
        Within(builder, $"/q?v={first}", context =>
        {
            Assert.True(context.TryGetQuery("v", out T value));
            AssertSameValue(firstValue, value);
            Assert.True(context.TryGetQuery("v", out T? nullable));
            AssertSameValue(firstValue, nullable);
            AssertSameValues([firstValue], context.GetQueryValues<T>("v"));
        });

        // Absent: no query at all, an empty query, and a query without the name. Never the default of T for a
        // nullable type.
        foreach (string uri in new[] { "/q", "/q?", "/q?w=" + first, "/q?vv=" + first, "/q#v=" + first })
        {
            Within(builder, uri, context =>
            {
                Assert.False(context.TryGetQuery("v", out T value));
                Assert.Equal(default, value);
                Assert.False(context.TryGetQuery("v", out T? nullable));
                Assert.Null(nullable);
                IReadOnlyList<T> values = context.GetQueryValues<T>("v");
                AssertEmptyAndUnchangeable(values, firstValue);
            });
        }

        // Present but not valid, an empty value in both spellings included.
        foreach (string uri in new[] { "/q?v=x", "/q?v=", "/q?v", "/q?v=%20", "/q?w=1&v&v=" + first })
        {
            Within(builder, uri, context =>
            {
                AssertInvalid(() => context.TryGetQuery("v", out T _), "v", typeName);
                AssertInvalid(() => context.TryGetQuery("v", out T? _), "v", typeName);
                AssertInvalid(() => context.GetQueryValues<T>("v"), "v", typeName);
            });
        }

        // The first value decides, in both orders.
        Within(builder, $"/q?v=x&v={first}", context =>
        {
            AssertInvalid(() => context.TryGetQuery("v", out T _), "v", typeName);
            AssertInvalid(() => context.TryGetQuery("v", out T? _), "v", typeName);
        });

        Within(builder, $"/q?v={first}&v=x", context =>
        {
            Assert.True(context.TryGetQuery("v", out T value));
            AssertSameValue(firstValue, value);
            Assert.True(context.TryGetQuery("v", out T? nullable));
            AssertSameValue(firstValue, nullable);
        });

        // Every value in order, repeated ones included, or none.
        Within(builder, $"/q?v={second}&w=x&v={first}&v={second}", context =>
        {
            Assert.True(context.TryGetQuery("v", out T value));
            AssertSameValue(secondValue, value);
            IReadOnlyList<T> values = context.GetQueryValues<T>("v");
            AssertSameValues([secondValue, firstValue, secondValue], values);
            AssertCannotBeChanged(values, firstValue);
            AssertSameValues([secondValue, firstValue, secondValue], context.GetQueryValues<T>("v"));
        });

        foreach (string uri in new[] { $"/q?v={first}&v=x&v={second}", $"/q?v={first}&v={second}&v" })
        {
            Within(builder, uri, context =>
            {
                AssertInvalid(() => context.GetQueryValues<T>("v"), "v", typeName);
                Assert.True(context.TryGetQuery("v", out T value));
                AssertSameValue(firstValue, value);
            });
        }

        // Names are compared without regard to case, and the exception has the name the caller gave.
        Within(builder, $"/q?Value={first}&Bad=x", context =>
        {
            foreach (string name in new[] { "value", "VALUE", "Value" })
            {
                Assert.True(context.TryGetQuery(name, out T value));
                AssertSameValue(firstValue, value);
                Assert.True(context.TryGetQuery(name, out T? nullable));
                AssertSameValue(firstValue, nullable);
                AssertSameValues([firstValue], context.GetQueryValues<T>(name));
            }

            foreach (string name in new[] { "bad", "BAD" })
            {
                AssertInvalid(() => context.TryGetQuery(name, out T _), name, typeName);
                AssertInvalid(() => context.TryGetQuery(name, out T? _), name, typeName);
                AssertInvalid(() => context.GetQueryValues<T>(name), name, typeName);
            }
        });

        // A null name is rejected, with or without a query.
        foreach (string uri in new[] { $"/q?v={first}", "/q" })
        {
            Within(builder, uri, context =>
            {
                AssertNullName(() => context.TryGetQuery(null!, out T _));
                AssertNullName(() => context.TryGetQuery(null!, out T? _));
                AssertNullName(() => context.GetQueryValues<T>(null!));
                AssertNullName(() => context.GetQueryValues<string>(null!));
                AssertNullName(() => context.TryGetQuery(null!, out string? _));
            });
        }
    }

    // For each text of the corpus, carried as both the path value and the query value of v.
    private static void AssertEquivalentToPath<T>()
        where T : struct
    {
        var converted = 0;
        RouteTable table = new RouteTableBuilder()
            .Map("/v/{v}", c =>
            {
                RouteValueException? rejected = null;
                T path = default;
                try
                {
                    path = c.Path<T>("v");
                }
                catch (RouteValueException exception)
                {
                    rejected = exception;
                }

                if (rejected is null)
                {
                    converted++;
                    Assert.True(c.TryGetQuery("v", out T value));
                    AssertSameValue(path, value);
                    Assert.True(c.TryGetQuery("v", out T? nullable));
                    AssertSameValue(path, nullable);
                    AssertSameValues([path], c.GetQueryValues<T>("v"));
                }
                else
                {
                    AssertInvalid(() => c.TryGetQuery("v", out T _), "v");
                    AssertInvalid(() => c.TryGetQuery("v", out T? _), "v");
                    AssertInvalid(() => c.GetQueryValues<T>("v"), "v");
                }

                return new SettingsRoute();
            })
            .Build();

        foreach (string text in TypedValueCorpus.Texts())
        {
            string escaped = Uri.EscapeDataString(text);
            var uri = new Uri($"/v/{escaped}?v={escaped}", UriKind.Relative);
            Assert.True(table.Match(uri).IsSuccess, $"{typeof(T).Name}: '{text}'");
        }

        Assert.True(converted > 40, $"{typeof(T).Name}: {converted}");
    }

    // The string list of id parsed into an array, which with the wrapper of the string list allocates what a typed
    // list of the same values must: its array and its wrapper.
    private static T[] ParsedList<T>(RouteMatchContext context, Func<string, T> parse)
    {
        IReadOnlyList<string> texts = context.GetQueryValues("id");
        var values = new T[texts.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = parse(texts[i]);

        return values;
    }

    // Each case as the first value of a name, read as T and as T?, and as every value of the name, given twice. The
    // empty value and '.', which a path segment cannot carry, are not valid for any of these types.
    private static void AssertIntegerCases<T>(string typeName)
        where T : struct, IBinaryInteger<T>, IMinMaxValue<T>
    {
        AssertCase<T>("", null, typeName);
        AssertCase<T>(".", null, typeName);
        foreach ((string text, T? expected) in TypedValueCorpus.IntegerCases<T>())
            AssertCase(text, expected, typeName);
    }

    private static void AssertFloatingPointCases<T>(
        string typeName,
        Func<(string Text, bool IsFloat, bool IsDouble), bool> isValid)
        where T : struct, IBinaryFloatingPointIeee754<T>
    {
        AssertCase<T>("", null, typeName);
        AssertCase<T>(".", null, typeName);
        foreach ((string Text, bool IsFloat, bool IsDouble) item in TypedValueCorpus.FloatingPointCases())
            AssertCase<T>(item.Text, isValid(item) ? T.Parse(item.Text, Invariant) : null, typeName);
    }

    private static void AssertCase<T>(
        string text,
        T? expected,
        string typeName,
        Func<RouteTableBuilder>? builder = null)
        where T : struct
    {
        string escaped = Uri.EscapeDataString(text);
        Within(builder, $"/q?v={escaped}&v={escaped}", context =>
        {
            if (expected is { } value)
            {
                Assert.True(context.TryGetQuery("v", out T read), text);
                AssertSameValue(value, read);
                Assert.True(context.TryGetQuery("v", out T? nullable), text);
                AssertSameValue(value, nullable);
                AssertSameValues([value, value], context.GetQueryValues<T>("v"));
            }
            else
            {
                AssertInvalid(() => context.TryGetQuery("v", out T _), "v", typeName);
                AssertInvalid(() => context.TryGetQuery("v", out T? _), "v", typeName);
                AssertInvalid(() => context.GetQueryValues<T>("v"), "v", typeName);
            }
        });
    }

    // Equal, and for a decimal also of the same scale and sign of zero, which decimal equality ignores, and for a
    // float or double of the same bits, since equality ignores the sign of zero.
    private static void AssertSameValue<T>(T expected, T? actual)
        where T : struct
    {
        Assert.Equal(expected, actual);
        if (expected is decimal number)
            Assert.Equal(decimal.GetBits(number), decimal.GetBits((decimal)(object)actual!.Value));
        if (expected is double d)
        {
            var read = (double)(object)actual!.Value;
            Assert.Equal(BitConverter.DoubleToInt64Bits(d), BitConverter.DoubleToInt64Bits(read));
        }

        if (expected is float f)
        {
            var read = (float)(object)actual!.Value;
            Assert.Equal(BitConverter.SingleToInt32Bits(f), BitConverter.SingleToInt32Bits(read));
        }
    }

    private static void AssertSameValues<T>(T[] expected, IReadOnlyList<T> actual)
        where T : struct
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
            AssertSameValue(expected[i], actual[i]);
    }

    private static bool GenericRead<T>(RouteMatchContext context, string name, out T? value)
    {
        return context.TryGetQuery(name, out value);
    }

    private static IReadOnlyList<T> GenericReadAll<T>(RouteMatchContext context, string name)
    {
        return context.GetQueryValues<T>(name);
    }

    private static void AssertEmptyAndUnchangeable<T>(IReadOnlyList<T> values, T other)
    {
        Assert.Empty(values);
        var list = Assert.IsAssignableFrom<IList<T>>(values);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(other));
    }

    private static void AssertCannotBeChanged<T>(IReadOnlyList<T> values, T other)
    {
        Assert.IsNotType<T[]>(values);
        Assert.IsNotType<List<T>>(values);
        var list = Assert.IsAssignableFrom<IList<T>>(values);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = other);
        Assert.Throws<NotSupportedException>(() => list.Add(other));
    }

    private static void AssertInvalid(Action read, string name, string? typeName = null)
    {
        RouteValueException exception = Assert.Throws<RouteValueException>(read);

        Assert.Equal(name, exception.ValueName);
        Assert.Null(exception.InnerException);
        Assert.Contains(QueryValue, exception.Message, StringComparison.Ordinal);
        Assert.Contains(NotValid, exception.Message, StringComparison.Ordinal);
        if (typeName is not null)
            Assert.EndsWith($" {typeName}.", exception.Message, StringComparison.Ordinal);
    }

    // The part of the message between the words that introduce the list of supported types and the given end.
    private static string SupportedList(Action read, string end)
    {
        const string Start = "The supported types are ";
        string message = Assert.Throws<InvalidOperationException>(read).Message;
        int start = message.IndexOf(Start, StringComparison.Ordinal);
        Assert.True(start >= 0, message);
        start += Start.Length;
        int stop = message.IndexOf(end, start, StringComparison.Ordinal);
        Assert.True(stop > start, message);
        return message[start..stop];
    }

    private static void AssertNullName(Action read)
    {
        Assert.Equal("name", Assert.Throws<ArgumentNullException>(read).ParamName);
    }

    private static void AssertUnsupported(Action read, string typeName)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(read);

        Assert.Contains(QueryValue, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ListOfQueryValues, exception.Message, StringComparison.Ordinal);
        Assert.Contains(Unsupported, exception.Message, StringComparison.Ordinal);
        Assert.Contains("nullable", exception.Message, StringComparison.Ordinal);
        Assert.Contains("read the value as a string", exception.Message, StringComparison.Ordinal);
        Assert.Contains("any enum registered with", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeName, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertUnregistered(Action read, string typeName, string enumName)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(read);

        Assert.Contains(QueryValue, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ListOfQueryValues, exception.Message, StringComparison.Ordinal);
        AssertUnregisteredMessage(exception, typeName, enumName);
    }

    private static void AssertListUnregistered(Action read, string typeName, string enumName)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(read);

        Assert.Contains(ListOfQueryValues, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NeverAbsent, exception.Message, StringComparison.Ordinal);
        AssertUnregisteredMessage(exception, typeName, enumName);
    }

    private static void AssertUnregisteredMessage(InvalidOperationException exception, string typeName, string enumName)
    {
        Assert.Contains(Unsupported, exception.Message, StringComparison.Ordinal);
        Assert.Contains("not registered", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"AddEnumValueCodec<{enumName}>", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeName, exception.Message, StringComparison.Ordinal);
    }

    // A list has no nullable forms and several values, so its message must not offer either.
    private static void AssertListUnsupported(Action read, string typeName)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(read);

        Assert.Contains(ListOfQueryValues, exception.Message, StringComparison.Ordinal);
        Assert.Contains(Unsupported, exception.Message, StringComparison.Ordinal);
        Assert.Contains("read the values as strings", exception.Message, StringComparison.Ordinal);
        Assert.Contains("any enum registered with", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("nullable", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NeverAbsent, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeName, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertNullableList(Action read, string typeName, string underlyingName)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(read);

        Assert.Contains(ListOfQueryValues, exception.Message, StringComparison.Ordinal);
        Assert.Contains(Unsupported, exception.Message, StringComparison.Ordinal);
        Assert.Contains(NeverAbsent, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(underlyingName, exception.Message, StringComparison.Ordinal);
    }

    // A table whose create function reads the query values of id the given number of times.
    private static RouteTable ReadingTable(
        Func<RouteMatchContext, int> read,
        int reads,
        RouteTableBuilder? builder = null)
    {
        return (builder ?? new RouteTableBuilder())
            .Map("/orders", c =>
            {
                var sum = 0;
                for (var i = 0; i < reads; i++)
                    sum += read(c);

                return new ProductRoute(sum > 0 ? "p" : "n");
            })
            .Build();
    }

    // Matches the same URI each time, after enough matches for the code under test to be compiled.
    private static long AllocatedBytesPerMatch(RouteTable table, Uri uri)
    {
        const int Calls = 50;
        for (var i = 0; i < 200; i++)
            Assert.True(table.Match(uri).IsSuccess);

        // The runtime sometimes allocates once on its own account among the measured matches, which can only add
        // bytes, so the least of several samples is what the matches themselves allocate.
        long least = long.MaxValue;
        for (var sample = 0; sample < 5; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Calls; i++)
                table.Match(uri);

            least = Math.Min(least, (GC.GetAllocatedBytesForCurrentThread() - before) / Calls);
        }

        return least;
    }

    private static void Within(string uri, Action<RouteMatchContext> check)
    {
        Within("/q", uri, check);
    }

    private static void WithinEnums(string uri, Action<RouteMatchContext> check)
    {
        Within(EnumBuilder, uri, check);
    }

    // On a table from the given builder, or from a new one that registers nothing.
    private static void Within(Func<RouteTableBuilder>? builder, string uri, Action<RouteMatchContext> check)
    {
        Within("/q", uri, check, builder: builder);
    }

    // Runs the checks inside the create function, because a context is valid only during that call; an assertion
    // that fails there propagates out of Match unchanged.
    private static void Within(
        string template,
        string uri,
        Action<RouteMatchContext> check,
        UriKind kind = UriKind.Relative,
        Func<RouteTableBuilder>? builder = null)
    {
        var called = false;
        RouteTable table = (builder?.Invoke() ?? new RouteTableBuilder())
            .Map(template, c =>
            {
                check(c);
                called = true;
                return new SettingsRoute();
            })
            .Build();

        Assert.True(table.Match(new Uri(uri, kind)).IsSuccess);
        Assert.True(called);
    }

    // Cancelled and Canceled are two names for one value, and that value is negative, which Enum.TryParse would accept
    // as -1.
    public enum Status
    {
        Pending,
        InProgress,
        Shipped,
        Cancelled = -1,
        Canceled = -1,
    }

    [Flags]
    public enum Access
    {
        None = 0,
        Read = 1,
        Write = 2,
        ReadWrite = Read | Write,
    }
}
