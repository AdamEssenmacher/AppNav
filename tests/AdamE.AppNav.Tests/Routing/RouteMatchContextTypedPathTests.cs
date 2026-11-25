using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteMatchContextTypedPathTests
{
    private const string NoValue = "no value for path parameter";
    private const string NotValid = "is not a valid";
    private const string Unsupported = "cannot be read as type";
    private const string GuidText = "6f9619ff-8b86-d011-b42d-00cf4fc964ff";

    // Text that stands for a secret a deep link can carry, distinctive enough that finding it in a message or in Data
    // is not a coincidence.
    private const string Secret = "tok-Vn3Jq8Rw-9Wq4Lm7Tz";

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    [InlineData("+0", 0)]
    [InlineData("42", 42)]
    [InlineData("+007", 7)]
    [InlineData("-007", -7)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("+2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    [InlineData("0000000000000000000000000000002147483647", int.MaxValue)]
    public void AnIntIsTheNumberTheTextDenotes(string text, int expected)
    {
        Assert.Equal(expected, ReadValue(text, c => c.Path<int>("v")));
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("-0", 0L)]
    [InlineData("+007", 7L)]
    [InlineData("2147483648", 2147483648L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    [InlineData("-00000000000000000000000000000000000000009223372036854775808", long.MinValue)]
    public void ALongIsTheNumberTheTextDenotes(string text, long expected)
    {
        Assert.Equal(expected, ReadValue(text, c => c.Path<long>("v")));
    }

    public static TheoryData<string, decimal> Decimals => new()
    {
        { "0", 0m },
        { "-0", 0m },
        { "19.95", 19.95m },
        { "-19.95", -19.95m },
        { "+007.50", 7.5m },
        { ".5", 0.5m },
        { "5.", 5m },
        { "-.5", -0.5m },
        { "79228162514264337593543950335", decimal.MaxValue },
        { "-79228162514264337593543950335", decimal.MinValue },
        { "79228162514264337593543950335.4", decimal.MaxValue },
        { "0.1000000000000000000000000000000000001", 0.1m },
        { "0." + new string('0', 100) + "1", 0m },
    };

    // A decimal keeps the scale and the sign of zero that the text gives, as the docs say.
    [Theory]
    [InlineData("5", 0, false)]
    [InlineData("5.00", 2, false)]
    [InlineData("0.0", 1, false)]
    [InlineData("-0", 0, true)]
    [InlineData("-0.00", 2, true)]
    public void ADecimalKeepsTheScaleAndTheSignOfZero(string text, int scale, bool isNegative)
    {
        decimal value = ReadValue(text, c => c.Path<decimal>("v"));

        Assert.Equal(scale, value.Scale);
        Assert.Equal(isNegative, decimal.IsNegative(value));
        Assert.Equal(text.TrimStart('-'), value.ToString(Invariant));
    }

    [Theory]
    [MemberData(nameof(Decimals))]
    public void ADecimalIsTheValueAfterTheRoundingTheConstraintTolerates(string text, decimal expected)
    {
        Assert.Equal(expected, ReadValue(text, c => c.Path<decimal>("v")));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("tRuE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    [InlineData("fAlSe", false)]
    public void ABoolIsTrueOrFalseInAnyAsciiCase(string text, bool expected)
    {
        Assert.Equal(expected, ReadValue(text, c => c.Path<bool>("v")));
    }

    [Theory]
    [InlineData(GuidText)]
    [InlineData("6F9619FF-8B86-D011-B42D-00CF4FC964FF")]
    [InlineData("6f9619FF-8b86-D011-b42d-00CF4fc964ff")]
    public void AGuidInEitherCaseIsTheGuid(string text)
    {
        Assert.Equal(new System.Guid(GuidText), ReadValue(text, c => c.Path<Guid>("v")));
        Assert.Equal(System.Guid.Empty, ReadValue(System.Guid.Empty.ToString(), c => c.Path<Guid>("v")));
    }

    [Theory]
    [InlineData("+007")]
    [InlineData("north wind")]
    [InlineData("caf\u00E9")]
    [InlineData(GuidText)]
    public void AStringIsTheCapturedTextUnchanged(string text)
    {
        Assert.Equal(text, ReadValue(text, c => c.Path<string>("v")));
        Assert.Equal(text, ReadValue(text, c => c.TryGetPath("v", out string? value) ? value : null));
    }

    // One rule per type. Over a large corpus, a text converts exactly when the constraint of the same name accepts
    // it, read through a template without the constraint, and a template with the constraint never matches a path
    // whose value then fails to convert. Acceptance is the same code on both sides by design, so this part guards
    // the wiring; what each constraint accepts is anchored by the independent examples in the constraint tests. The
    // independent part of this test is the value, which is checked against a framework parse of the same text.
    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("guid")]
    public void ATextConvertsExactlyWhenItSatisfiesTheConstraintOfTheSameName(string constraint)
    {
        switch (constraint)
        {
            case "int":
                AssertEquivalent(constraint, t => int.Parse(t, NumberStyles.AllowLeadingSign, Invariant));
                break;
            case "long":
                AssertEquivalent(constraint, t => long.Parse(t, NumberStyles.AllowLeadingSign, Invariant));
                break;
            case "decimal":
                AssertEquivalent(
                    constraint,
                    t => decimal.Parse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant));
                break;
            case "bool":
                AssertEquivalent(constraint, bool.Parse);
                break;
            default:
                AssertEquivalent(constraint, t => System.Guid.ParseExact(t, "D"));
                break;
        }
    }

    [Fact]
    public void AValueThatDoesNotConvertRejectsTheMatchFromBothMethods()
    {
        Within("/products/{id}", "/products/abc", context =>
        {
            AssertInvalid(() => context.Path<int>("id"), "id", "int");
            AssertInvalid(() => context.TryGetPath("id", out int _), "id", "int");
            AssertInvalid(() => context.Path<long>("id"), "id", "long");
            AssertInvalid(() => context.Path<decimal>("id"), "id", "decimal");
            AssertInvalid(() => context.Path<bool>("id"), "id", "bool");
            AssertInvalid(() => context.TryGetPath("id", out Guid _), "id", "Guid");
            Assert.Equal("abc", context.Path<string>("id"));
        });
    }

    [Fact]
    public void AnOptionalParameterThePathLeftOutIsAbsentAndOneItHasIsConverted()
    {
        Within("/orders/{id}/{page?}", "/orders/1", context =>
        {
            AssertAbsent(() => context.Path<int>("page"));
            Assert.False(context.TryGetPath("page", out int page));
            Assert.Equal(0, page);
            Assert.False(context.TryGetPath("page", out string? text));
            Assert.Null(text);
        });

        Within("/orders/{id}/{page?}", "/orders/1/3", context =>
        {
            Assert.True(context.TryGetPath("page", out int page));
            Assert.Equal(3, page);
        });

        Within("/orders/{id}/{page?}", "/orders/1/x", context =>
        {
            AssertInvalid(() => context.TryGetPath("page", out int _), "page", "int");
        });
    }

    [Fact]
    public void ACatchAllParameterIsTheJoinedTextConvertedByTheSameRules()
    {
        Within("/files/{*rest}", "/files", context =>
        {
            AssertAbsent(() => context.Path<string>("rest"));
            Assert.False(context.TryGetPath("rest", out Guid guid));
            Assert.Equal(System.Guid.Empty, guid);
        });

        Within("/files/{*rest}", "/files/a/b", context =>
        {
            Assert.Equal("a/b", context.Path<string>("rest"));
            AssertInvalid(() => context.Path<int>("rest"), "rest", "int");
            AssertInvalid(() => context.TryGetPath("rest", out decimal _), "rest", "decimal");
        });

        Within("/files/{*rest}", "/files/42", context =>
        {
            Assert.Equal(42, context.Path<int>("rest"));
        });
    }

    [Fact]
    public void ANameTheTemplateDoesNotHaveIsAbsent()
    {
        Within("/products/{id}", "/products/42", context =>
        {
            AssertAbsent(() => context.Path<int>("name"));
            Assert.False(context.TryGetPath("name", out long value));
            Assert.Equal(0, value);
        });
    }

    [Theory]
    [InlineData("id")]
    [InlineData("ID")]
    [InlineData("iD")]
    public void NamesAreLookedUpWithoutRegardToCaseAndTheExceptionHasTheNameGiven(string name)
    {
        Within("/products/{Id}", "/products/42", context =>
        {
            Assert.Equal(42, context.Path<int>(name));
            Assert.True(context.TryGetPath(name, out int value));
            Assert.Equal(42, value);
        });

        Within("/products/{Id}", "/products/x", context =>
        {
            AssertInvalid(() => context.Path<int>(name), name);
            AssertInvalid(() => context.TryGetPath(name, out bool _), name);
        });
    }

    [Fact]
    public void ANullNameIsRejected()
    {
        Within("/products/{id}", "/products/42", context =>
        {
            Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => context.Path<int>(null!)).ParamName);
            Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => context.Path<string>(null!)).ParamName);
            Assert.Equal(
                "name",
                Assert.Throws<ArgumentNullException>(() => context.TryGetPath(null!, out Guid _)).ParamName);
        });
    }

    [Fact]
    public void AnUnsupportedTypeIsAProgrammingErrorWhetherOrNotTheValueIsPresent()
    {
        Within("/products/{id}", "/products/42", context =>
        {
            foreach (string name in new[] { "id", "missing" })
            {
                AssertUnsupported(() => context.Path<int?>(name), "'Int32?'");
                AssertUnsupported(() => context.Path<double>(name), "'Double'");
                AssertUnsupported(() => context.Path<DateTime>(name), "'DateTime'");
                AssertUnsupported(() => context.Path<DayOfWeek>(name), "'DayOfWeek'");
                AssertUnsupported(() => context.Path<object>(name), "'Object'");
                AssertUnsupported(() => context.TryGetPath(name, out int? _), "'Int32?'");
                AssertUnsupported(() => context.TryGetPath(name, out short _), "'Int16'");
                AssertUnsupported(() => context.TryGetPath(name, out object? _), "'Object'");
            }

            // The type is checked before anything else, the name included.
            AssertUnsupported(() => context.Path<double>(null!), "'Double'");
        });
    }

    [Fact]
    public void AnUnsupportedTypeEscapesMatch()
    {
        RouteTable present = new RouteTableBuilder()
            .Map("/products/{id}", c => new ProductRoute(c.Path<double>("id").ToString(Invariant)))
            .Build();
        RouteTable absent = new RouteTableBuilder()
            .Map("/products/{id}", c => new ProductRoute(c.TryGetPath("tab", out DayOfWeek day) ? "x" : "y"))
            .Build();

        Assert.Throws<InvalidOperationException>(() => present.Match(new Uri("/products/42", UriKind.Relative)));
        Assert.Throws<InvalidOperationException>(() => absent.Match(new Uri("/products/42", UriKind.Relative)));
    }

    // Only the constraint of the same name guarantees the conversion: another one can let through a value that the
    // typed read rejects.
    [Theory]
    [InlineData("/i/{id:long}", "/i/3000000000", "int")]
    [InlineData("/b/{id:int}", "/b/1", "bool")]
    public void ADifferentConstraintCanLetThroughAValueTheReadRejects(string template, string path, string type)
    {
        RouteTable table = new RouteTableBuilder()
            .Map(template, c => new ProductRoute(
                type == "int" ? c.Path<int>("id").ToString(Invariant) : c.Path<bool>("id").ToString()))
            .Build();

        RouteMatchResult result = table.Match(new Uri(path, UriKind.Relative));

        Assert.False(result.IsSuccess);
        Assert.Equal(template, result.Definition?.Template.Value);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("id", diagnostic.Data["name"]);
        Assert.EndsWith($" {type}.", (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedConversionFailsTheMatchAndNoLaterTemplateIsTried()
    {
        var laterCalled = false;
        RouteTable table = new RouteTableBuilder()
            .Map("/orders/{id}", c => new ProductRoute(c.Path<int>("id").ToString(Invariant)))
            .Map("/{*rest}", _ =>
            {
                laterCalled = true;
                return new SettingsRoute();
            })
            .Build();

        RouteMatchResult result = table.Match(new Uri("/orders/abc", UriKind.Relative));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Route);
        Assert.NotNull(result.Definition);
        Assert.Equal("/orders/{id}", result.Definition.Template.Value);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("id", diagnostic.Data["name"]);
        Assert.Contains(NotValid, (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
        Assert.False(laterCalled);

        Assert.Equal(new ProductRoute("42"), table.Match(new Uri("/orders/+042", UriKind.Relative)).Route);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("guid")]
    public void AFailedConversionNeverQuotesTheValue(string type)
    {
        RouteValueException? thrown = null;
        RouteTable table = new RouteTableBuilder()
            .Map("/orders/{id}", c =>
            {
                try
                {
                    _ = type switch
                    {
                        "int" => c.Path<int>("id").ToString(Invariant),
                        "long" => c.Path<long>("id").ToString(Invariant),
                        "decimal" => c.Path<decimal>("id").ToString(Invariant),
                        "bool" => c.Path<bool>("id").ToString(),
                        _ => c.TryGetPath("id", out Guid g).ToString() + g,
                    };
                }
                catch (RouteValueException exception)
                {
                    thrown = exception;
                    throw;
                }

                return new SettingsRoute();
            })
            .Build();

        RouteMatchResult result = table.Match(new Uri($"/orders/{Secret}?k=1", UriKind.Relative));

        Assert.NotNull(thrown);
        Assert.Equal("id", thrown.ValueName);
        Assert.Null(thrown.InnerException);
        Assert.Contains(NotValid, thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, thrown.ToString(), StringComparison.Ordinal);

        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.DoesNotContain(Secret, diagnostic.Message, StringComparison.Ordinal);
        foreach ((string key, object? value) in diagnostic.Data)
        {
            if (key != "path")
                Assert.DoesNotContain(Secret, (string?)value, StringComparison.Ordinal);
        }

        Assert.Contains(Secret, (string?)diagnostic.Data["path"], StringComparison.Ordinal);
    }

    // Path("id") and TryGetPath("tab", out var tab) can bind only to the string members, since T cannot be inferred
    // from them, and for out string? the non-generic member is the better candidate. The expression trees record the
    // member the compiler chose.
    [Fact]
    public void TheStringReadsStillBindToTheNonGenericMembers()
    {
        string? tab = null;
        Expression<Func<RouteMatchContext, string>> path = c => c.Path("id");
        Expression<Func<RouteMatchContext, bool>> tryGetPath = c => c.TryGetPath("tab", out tab);

        Assert.False(((MethodCallExpression)path.Body).Method.IsGenericMethod);
        Assert.False(((MethodCallExpression)tryGetPath.Body).Method.IsGenericMethod);

        Within("/orders/{id}/{tab?}", "/orders/42/items", context =>
        {
            string id = context.Path("id");
            bool found = context.TryGetPath("tab", out string? typed);
            bool inferred = context.TryGetPath("tab", out var implicitlyTyped);
            string? sameType = implicitlyTyped;

            Assert.Equal("42", id);
            Assert.True(found && inferred);
            Assert.Equal("items", typed);
            Assert.Equal("items", sameType);
        });
    }

    // A typed read must cost no more than reading the string and parsing it. A boxed value is larger than a pointer
    // for every supported value type, so a box on each read would make the difference at least one pointer per read,
    // and the bound allows less than that in all.
    [Fact]
    public void ATypedReadAllocatesNoMoreThanReadingTheStringAndParsingIt()
    {
        const int Reads = 16;
        RouteTable typed = new RouteTableBuilder()
            .Map("/orders/{id}", c =>
            {
                var sum = 0;
                for (var i = 0; i < Reads; i++)
                    sum += c.Path<int>("id");

                return new ProductRoute(sum > 0 ? "p" : "n");
            })
            .Build();
        RouteTable parsed = new RouteTableBuilder()
            .Map("/orders/{id}", c =>
            {
                var sum = 0;
                for (var i = 0; i < Reads; i++)
                    sum += int.Parse(c.Path("id"), Invariant);

                return new ProductRoute(sum > 0 ? "p" : "n");
            })
            .Build();

        long typedBytes = AllocatedBytesPerMatch(typed);
        long parsedBytes = AllocatedBytesPerMatch(parsed);

        Assert.True(
            typedBytes - parsedBytes < Reads * IntPtr.Size,
            $"typed {typedBytes} bytes, parsed {parsedBytes} bytes");
    }

    private static CultureInfo Invariant => CultureInfo.InvariantCulture;

    private static void AssertEquivalent<T>(string constraint, Func<string, T> parse)
    {
        RouteConstraint rule = RouteConstraint.Find(constraint)!;
        var satisfied = 0;
        T converted = default!;
        RouteTable unconstrained = new RouteTableBuilder()
            .Map("/v/{v}", c =>
            {
                converted = c.Path<T>("v");
                return new SettingsRoute();
            })
            .Build();
        RouteTable constrained = new RouteTableBuilder()
            .Map($"/v/{{v:{constraint}}}", c =>
            {
                converted = c.Path<T>("v");
                return new SettingsRoute();
            })
            .Build();

        foreach (string text in Corpus())
        {
            var uri = new Uri("/v/" + Uri.EscapeDataString(text), UriKind.Relative);
            bool expected = rule.IsSatisfiedBy(text);
            Assert.True(expected == unconstrained.Match(uri).IsSuccess, $"{constraint}: '{text}'");
            if (expected)
            {
                satisfied++;
                Assert.Equal(parse(text), converted);
            }

            Assert.True(expected == constrained.Match(uri).IsSuccess, $"{constraint} constrained: '{text}'");
            Assert.True(
                expected == TryRead(text, c => c.TryGetPath("v", out T? _)),
                $"{constraint} TryGetPath: '{text}'");
        }

        Assert.True(satisfied > 40, $"{constraint}: {satisfied}");
    }

    // Whether TryGetPath converted the text; a text that does not convert must throw, never return false.
    private static bool TryRead(string text, Func<RouteMatchContext, bool> read)
    {
        bool? converted = null;
        RouteTable table = new RouteTableBuilder()
            .Map("/v/{v}", c =>
            {
                converted = read(c);
                return new SettingsRoute();
            })
            .Build();

        bool success = table.Match(new Uri("/v/" + Uri.EscapeDataString(text), UriKind.Relative)).IsSuccess;
        Assert.Equal(success ? true : null, converted);
        return success;
    }

    // Deterministic texts of every shape the rules distinguish: numbers of every length with and without signs and
    // points, guids in each form and case, true and false in every case, letters, non-ASCII digits, white space,
    // signs in odd places and other junk. Only texts that a path segment can carry exactly.
    private static List<string> Corpus()
    {
        var random = new Random(31);
        var corpus = new List<string>
        {
            "-", "+", "-.", "+.", ".5", "5.", "-.5", "+.5", "1..2", "1.2.3", " 1", "1 ", "\t1", "1\n", "1\u0000",
            "1e5", "1E5", "0x10", "1,000", "1_000", "\u0663", "\uFF14\uFF12", "\u221242", "42-", "4-2", "--1",
            "+-1", "-+1", "++1", "1+", "+ 1", "\u00A01", "\u20001", "1\u200B",
            int.MinValue.ToString(Invariant), int.MaxValue.ToString(Invariant), "2147483648", "-2147483649",
            long.MinValue.ToString(Invariant), long.MaxValue.ToString(Invariant), "9223372036854775808",
            "-9223372036854775809", "79228162514264337593543950335", "79228162514264337593543950335.4",
            "79228162514264337593543950335.5", "79228162514264337593543950336", "0." + new string('0', 100) + "1",
            "1" + new string('0', 1000), "a1", "1a", "true1", "1true", "-true", "+true", " true", "true ", "t",
            "f", "tru", "fals", "truee", "yes", "no", "on", "1", "0", "tr\u00FCe", "\u0442rue",
            "\u00E9", "\u03B1\u03B2", "\U0001D49C", "e\u0301", "\u65E5\u672C", "%", "%41", "a b", "{}", "()",
        };

        for (var bits = 0; bits < 32; bits++)
        {
            corpus.Add(Cased("true", bits));
            corpus.Add(Cased("false", bits));
        }

        for (var length = 1; length <= 40; length++)
        {
            foreach (string sign in new[] { "", "+", "-" })
            {
                corpus.Add(sign + new string('0', length));
                corpus.Add(sign + new string('9', length));
                corpus.Add(sign + "1" + new string('0', length - 1));
                corpus.Add(sign + RandomText(random, "0123456789", length));
                corpus.Add(sign + "." + RandomText(random, "0123456789", length));
                corpus.Add(sign + RandomText(random, "0123456789", length) + ".");
                int point = random.Next(length + 1);
                string digits = RandomText(random, "0123456789", length);
                corpus.Add(sign + digits[..point] + "." + digits[point..]);
            }

            corpus.Add(RandomText(random, "0123456789+-.", length));
            corpus.Add(RandomText(random, "0123456789abcdefABCDEF-", length));
            corpus.Add(RandomText(random, "abcdefghijklmnopqrstuvwxyzTRUEFALS", length));
            corpus.Add(RandomText(random, "\u0660\u0661\u0662\u0663\u06F0\u0966\uFF10", length));
            corpus.Add(RandomText(random, " \t0123456789", length));
        }

        var bytes = new byte[16];
        for (var i = 0; i < 40; i++)
        {
            random.NextBytes(bytes);
            var guid = new System.Guid(bytes);
            string hyphenated = guid.ToString("D");
            corpus.Add(hyphenated);
            corpus.Add(hyphenated.ToUpperInvariant());
            corpus.Add(Cased(hyphenated, random.Next()));
            corpus.Add(guid.ToString("N"));
            corpus.Add(guid.ToString("B"));
            corpus.Add(guid.ToString("P"));
            corpus.Add(" " + hyphenated);
            corpus.Add(hyphenated + " ");
            corpus.Add(hyphenated[..35]);
            corpus.Add(hyphenated + "0");
            corpus.Add(hyphenated.Replace('-', '_'));
            corpus.Add("+" + hyphenated[1..]);
            corpus.Add(hyphenated[..9] + "+" + hyphenated[10..]);
            corpus.Add(hyphenated[..8] + "\u2011" + hyphenated[9..]);
            corpus.Add(hyphenated[..20] + "g" + hyphenated[21..]);
        }

        corpus.Add(System.Guid.Empty.ToString());
        corpus.Add("ffffffff-ffff-ffff-ffff-ffffffffffff");
        corpus.Add("0x000000-0000-0000-0000-000000000000");
        return corpus;
    }

    // The text with each ASCII letter upper-cased where the matching bit of the mask is set.
    private static string Cased(string text, int mask)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
            builder.Append((mask >> (i % 31) & 1) == 1 ? char.ToUpperInvariant(text[i]) : text[i]);

        return builder.ToString();
    }

    private static string RandomText(Random random, string alphabet, int length)
    {
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            builder.Append(alphabet[random.Next(alphabet.Length)]);

        return builder.ToString();
    }

    private static void AssertInvalid(Action read, string name, string? typeName = null)
    {
        RouteValueException exception = Assert.Throws<RouteValueException>(read);

        Assert.Equal(name, exception.ValueName);
        Assert.Null(exception.InnerException);
        Assert.Contains(NotValid, exception.Message, StringComparison.Ordinal);
        if (typeName is not null)
            Assert.EndsWith($" {typeName}.", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertAbsent(Action read)
    {
        KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(read);

        Assert.Contains(NoValue, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertUnsupported(Action read, string typeName)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(read);

        Assert.Contains(Unsupported, exception.Message, StringComparison.Ordinal);
        Assert.Contains("read the value as a string", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeName, exception.Message, StringComparison.Ordinal);
    }

    // Matches the same URI each time, after enough matches for the code under test to be compiled.
    private static long AllocatedBytesPerMatch(RouteTable table)
    {
        const int Calls = 50;
        var uri = new Uri("/orders/42", UriKind.Relative);
        for (var i = 0; i < 200; i++)
            Assert.True(table.Match(uri).IsSuccess);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Calls; i++)
            table.Match(uri);

        return (GC.GetAllocatedBytesForCurrentThread() - before) / Calls;
    }

    private static T ReadValue<T>(string text, Func<RouteMatchContext, T> read)
    {
        var called = false;
        T result = default!;
        RouteTable table = new RouteTableBuilder()
            .Map("/{v}", c =>
            {
                result = read(c);
                called = true;
                return new SettingsRoute();
            })
            .Build();

        Assert.True(table.Match(new Uri("/" + Uri.EscapeDataString(text), UriKind.Relative)).IsSuccess);
        Assert.True(called);
        return result;
    }

    // Runs the checks inside the create function, because a context is valid only during that call; an assertion
    // that fails there propagates out of Match unchanged.
    private static void Within(string template, string uri, Action<RouteMatchContext> check)
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

        Assert.True(table.Match(new Uri(uri, UriKind.Relative)).IsSuccess);
        Assert.True(called);
    }
}
