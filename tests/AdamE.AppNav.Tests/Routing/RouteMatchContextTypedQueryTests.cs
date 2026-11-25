using System.Globalization;
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
    public void AFailedConversionNeverQuotesTheValue(string type)
    {
        RouteValueException? thrown = null;
        RouteTable table = new RouteTableBuilder()
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
                    AssertUnsupported(() => context.TryGetQuery(name!, out double _), "'Double'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out double? _), "'Double?'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out DateTime _), "'DateTime'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out DayOfWeek? _), "'DayOfWeek?'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out short _), "'Int16'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out List<int>? _), "'List<Int32>'");
                    AssertUnsupported(() => context.TryGetQuery(name!, out object? _), "'Object'");
                    AssertListUnsupported(() => context.GetQueryValues<double>(name!), "'Double'");
                    AssertListUnsupported(() => context.GetQueryValues<double?>(name!), "'Double?'");
                    AssertListUnsupported(() => context.GetQueryValues<DayOfWeek>(name!), "'DayOfWeek'");
                    AssertListUnsupported(() => context.GetQueryValues<int[]>(name!), "'Int32[]'");
                    AssertListUnsupported(() => context.GetQueryValues<object>(name!), "'Object'");

                    AssertNullableList(() => context.GetQueryValues<int?>(name!), "'Int32?'", "'Int32'");
                    AssertNullableList(() => context.GetQueryValues<long?>(name!), "'Int64?'", "'Int64'");
                    AssertNullableList(() => context.GetQueryValues<decimal?>(name!), "'Decimal?'", "'Decimal'");
                    AssertNullableList(() => context.GetQueryValues<bool?>(name!), "'Boolean?'", "'Boolean'");
                    AssertNullableList(() => context.GetQueryValues<Guid?>(name!), "'Guid?'", "'Guid'");
                }
            });
        }
    }

    [Fact]
    public void AnUnsupportedTypeEscapesMatch()
    {
        RouteTable single = new RouteTableBuilder()
            .Map("/q", c => new ProductRoute(c.TryGetQuery("v", out double _) ? "x" : "y"))
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
    public void ATypedReadAllocatesNoMoreThanTheStringReadOfTheSameName(string type, string text)
    {
        const int Reads = 16;
        Func<RouteMatchContext, int> typedRead = type switch
        {
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

    private static CultureInfo Invariant => CultureInfo.InvariantCulture;

    // Every rule, for one value type and its nullable form. The two valid texts are different values.
    private static void AssertRules<T>(string first, T firstValue, string second, T secondValue, string typeName)
        where T : struct
    {
        // Present and valid.
        Within($"/q?v={first}", context =>
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
            Within(uri, context =>
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
            Within(uri, context =>
            {
                AssertInvalid(() => context.TryGetQuery("v", out T _), "v", typeName);
                AssertInvalid(() => context.TryGetQuery("v", out T? _), "v", typeName);
                AssertInvalid(() => context.GetQueryValues<T>("v"), "v", typeName);
            });
        }

        // The first value decides, in both orders.
        Within($"/q?v=x&v={first}", context =>
        {
            AssertInvalid(() => context.TryGetQuery("v", out T _), "v", typeName);
            AssertInvalid(() => context.TryGetQuery("v", out T? _), "v", typeName);
        });

        Within($"/q?v={first}&v=x", context =>
        {
            Assert.True(context.TryGetQuery("v", out T value));
            AssertSameValue(firstValue, value);
            Assert.True(context.TryGetQuery("v", out T? nullable));
            AssertSameValue(firstValue, nullable);
        });

        // Every value in order, repeated ones included, or none.
        Within($"/q?v={second}&w=x&v={first}&v={second}", context =>
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
            Within(uri, context =>
            {
                AssertInvalid(() => context.GetQueryValues<T>("v"), "v", typeName);
                Assert.True(context.TryGetQuery("v", out T value));
                AssertSameValue(firstValue, value);
            });
        }

        // Names are compared without regard to case, and the exception has the name the caller gave.
        Within($"/q?Value={first}&Bad=x", context =>
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
            Within(uri, context =>
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

    // Equal, and for a decimal also of the same scale and sign of zero, which decimal equality ignores.
    private static void AssertSameValue<T>(T expected, T? actual)
        where T : struct
    {
        Assert.Equal(expected, actual);
        if (expected is decimal number)
            Assert.Equal(decimal.GetBits(number), decimal.GetBits((decimal)(object)actual!.Value));
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
        Assert.Contains(typeName, exception.Message, StringComparison.Ordinal);
    }

    // A list has no nullable forms and several values, so its message must not offer either.
    private static void AssertListUnsupported(Action read, string typeName)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(read);

        Assert.Contains(ListOfQueryValues, exception.Message, StringComparison.Ordinal);
        Assert.Contains(Unsupported, exception.Message, StringComparison.Ordinal);
        Assert.Contains("read the values as strings", exception.Message, StringComparison.Ordinal);
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
    private static RouteTable ReadingTable(Func<RouteMatchContext, int> read, int reads)
    {
        return new RouteTableBuilder()
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

    // Runs the checks inside the create function, because a context is valid only during that call; an assertion
    // that fails there propagates out of Match unchanged.
    private static void Within(
        string template,
        string uri,
        Action<RouteMatchContext> check,
        UriKind kind = UriKind.Relative)
    {
        var called = false;
        RouteTable table = new RouteTableBuilder()
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
}
