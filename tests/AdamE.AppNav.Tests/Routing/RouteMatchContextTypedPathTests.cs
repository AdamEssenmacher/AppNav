using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using AdamE.AppNav.Routing;
using Xunit;
using Custom = AdamE.AppNav.Tests.Routing.CustomValueTypes;

namespace AdamE.AppNav.Tests.Routing;

public sealed class RouteMatchContextTypedPathTests
{
    private const string NoValue = "no value for path parameter";
    private const string NotValid = "is not a valid";
    private const string PathValue = "path value";
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

    // A decimal keeps the scale and the sign of zero that the text gives, as the docs say, and so does its nullable
    // form. Decimal equality ignores both, so they are checked directly.
    [Theory]
    [InlineData("5", 0, false)]
    [InlineData("5.0", 1, false)]
    [InlineData("5.00", 2, false)]
    [InlineData("0.0", 1, false)]
    [InlineData("-0", 0, true)]
    [InlineData("-0.0", 1, true)]
    [InlineData("-0.00", 2, true)]
    public void ADecimalKeepsTheScaleAndTheSignOfZero(string text, int scale, bool isNegative)
    {
        decimal?[] values =
        [
            ReadValue(text, c => c.Path<decimal>("v")),
            ReadValue(text, c => c.Path<decimal?>("v")),
            ReadValue(text, c => c.TryGetPath("v", out decimal? value) ? value : null),
        ];

        foreach (decimal? read in values)
        {
            decimal value = Assert.NotNull(read);
            Assert.Equal(scale, value.Scale);
            Assert.Equal(isNegative, decimal.IsNegative(value));
            Assert.Equal(text.TrimStart('-'), value.ToString(Invariant));
        }
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

    // Over the corpus, an integer type accepts exactly the texts that long accepts whose value is in its range, to
    // the same value. The range of ulong goes beyond that of long, and there decimal, which accepts the same digits
    // and a point as well, gives the value of a text without a point. The expectation comes from the library's own
    // long and decimal reads, so this is not an independent check of what is valid: it checks that every integer type
    // agrees with the rule of long over a broad corpus. The independent expectations for each type are the literal
    // cases of TypedValueCorpus.IntegerCases.
    [Fact]
    public void AnIntegerTypeAcceptsExactlyTheTextsLongAcceptsWithinItsRange()
    {
        AssertWithinTheRuleOfLong<sbyte>();
        AssertWithinTheRuleOfLong<byte>();
        AssertWithinTheRuleOfLong<short>();
        AssertWithinTheRuleOfLong<ushort>();
        AssertWithinTheRuleOfLong<uint>();
        AssertWithinTheRuleOfLong<ulong>();
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

    // Over the corpus, restricted to texts below 10^28 in magnitude, which decimal, float and double all hold, float
    // and double accept exactly the texts that decimal accepts, and over the whole corpus every text it accepts. The
    // expectation comes from the library's own decimal read, so this checks agreement with the shape of decimal over a
    // broad corpus, not what is valid; the independent expectations for each type are the literal cases of
    // TypedValueCorpus.FloatingPointCases, and each value is checked against a framework parse.
    [Fact]
    public void FloatAndDoubleAcceptExactlyTheShapeThatDecimalAccepts()
    {
        AssertShapeOfDecimal<float>();
        AssertShapeOfDecimal<double>();
    }

    // Digits beyond the precision of the type round to the nearest value rather than being rejected, at any length,
    // and only a magnitude too large for the type is rejected, never read as infinity.
    [Fact]
    public void AFloatingPointValueIsRoundedAndOnlyTooLargeAMagnitudeIsRejected()
    {
        Assert.Equal(0.1, ReadDouble("0.1000000000000000055511151231257827021181583404541015625000001"));
        Assert.Equal(0.1f, ReadFloat("0.10000000149011611938476562500000001"));
        Assert.Equal(16777216f, ReadFloat("16777217"));
        Assert.Equal(0x3F800001, BitConverter.SingleToInt32Bits(ReadFloat(TypedValueCorpus.FloatAboveTie)));
        Assert.Equal(0x3F800001, BitConverter.SingleToInt32Bits(ReadFloat(TypedValueCorpus.FloatBelowTie)));
        Assert.Equal(float.Parse(TypedValueCorpus.FloatAboveTie, Invariant), ReadFloat(TypedValueCorpus.FloatAboveTie));
        Assert.Equal(1e29, ReadDouble("1" + new string('0', 29)));
        Assert.Equal(1e29f, ReadFloat("1" + new string('0', 29)));
        Assert.Equal(float.MaxValue, ReadFloat((new BigInteger(float.MaxValue) + 1).ToString(Invariant)));
        Assert.Equal(9007199254740992.0, ReadDouble("9007199254740993"));
        Assert.Equal(1e39, ReadDouble("1" + new string('0', 39)));
        Assert.Equal(1e308, ReadDouble("1" + new string('0', 308)));
        Assert.Equal(double.MaxValue, ReadDouble(new BigInteger(double.MaxValue).ToString(Invariant)));
        Assert.Equal(float.MaxValue, ReadFloat(new BigInteger(float.MaxValue).ToString(Invariant)));
        Assert.Equal(0.0, ReadDouble("0." + new string('0', 400) + "1"));
        Assert.True(double.IsNegative(ReadDouble("-0." + new string('0', 400) + "1")));
        Assert.True(float.IsNegative(ReadFloat("-0." + new string('0', 60) + "1")));

        Within("/{v}", "/1" + new string('0', 39), context =>
        {
            AssertInvalid(() => context.Path<float>("v"), "v", "float");
            AssertInvalid(() => context.TryGetPath("v", out float? _), "v", "float");
        });

        Within("/{v}", "/1" + new string('0', 399), context =>
        {
            AssertInvalid(() => context.Path<double>("v"), "v", "double");
            AssertInvalid(() => context.Path<double?>("v"), "v", "double");
            AssertInvalid(() => context.TryGetPath("v", out float _), "v", "float");
            AssertInvalid(() => context.TryGetPath("v", out decimal _), "v", "decimal");
        });

        Within("/{v}", "/-2" + new string('0', 308), context =>
        {
            AssertInvalid(() => context.Path<double>("v"), "v", "double");
        });

        Within("/{v}", "/1" + new string('0', 29), context =>
        {
            AssertInvalid(() => context.Path<decimal>("v"), "v", "decimal");
        });
    }

    // The sign of a zero is kept, as the framework parser gives it. Equality ignores it, so it is checked directly.
    [Theory]
    [InlineData("-0", true)]
    [InlineData("-0.0", true)]
    [InlineData("-.0", true)]
    [InlineData("0", false)]
    [InlineData("+0", false)]
    [InlineData("0.0", false)]
    public void AFloatingPointZeroKeepsItsSign(string text, bool isNegative)
    {
        double?[] doubles =
        [
            ReadDouble(text),
            ReadValue(text, c => c.Path<double?>("v")),
            ReadValue(text, c => c.TryGetPath("v", out double? value) ? value : null),
        ];
        float?[] floats =
        [
            ReadFloat(text),
            ReadValue(text, c => c.Path<float?>("v")),
            ReadValue(text, c => c.TryGetPath("v", out float value) ? value : (float?)null),
        ];

        foreach (double? read in doubles)
        {
            Assert.Equal(0.0, read);
            Assert.Equal(isNegative, double.IsNegative(read!.Value));
        }

        foreach (float? read in floats)
        {
            Assert.Equal(0f, read);
            Assert.Equal(isNegative, float.IsNegative(read!.Value));
        }
    }

    // A constraint is named after every number type, in any case.
    [Theory]
    [InlineData("sbyte")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("ulong")]
    [InlineData("float")]
    [InlineData("double")]
    public void AConstraintIsNamedAfterEachNumberType(string type)
    {
        foreach (string written in new[] { type, type.ToUpperInvariant() })
        {
            RouteTemplate template = RouteTemplate.Parse($"/products/{{x:{written}}}");
            Assert.Equal("+007", template.Match("/products/+007")?["x"]);
            Assert.Null(template.Match("/products/x"));
        }

        Assert.Equal(type, RouteConstraint.Find(type)?.Name);
        Assert.False(RouteConstraint.IsReserved(type));
    }

    // string and char have no constraint, a string because it would accept every text and a char because it has no
    // typed read, so a template naming one is invalid. The names are reserved, so a builder cannot add them either,
    // and Map rejects them at once.
    [Theory]
    [InlineData("string")]
    [InlineData("char")]
    public void NoConstraintIsNamedAfterStringOrChar(string type)
    {
        string template = $"/products/{{x:{type}}}";

        ArgumentException parse = Assert.Throws<ArgumentException>(() => RouteTemplate.Parse(template));
        ArgumentException map = Assert.Throws<ArgumentException>(
            () => new RouteTableBuilder().Map(template, _ => new SettingsRoute()));

        Assert.Equal("value", parse.ParamName);
        Assert.Contains($"unknown constraint '{type}'", parse.Message, StringComparison.Ordinal);
        ReservedConstraintNames.AssertExplained(type, parse.Message);
        Assert.Equal("template", map.ParamName);
        Assert.Contains($"unknown constraint '{type}'", map.Message, StringComparison.Ordinal);
        ReservedConstraintNames.AssertExplained(type, map.Message);
        Assert.Null(RouteConstraint.Find(type));
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
    [InlineData("sbyte")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("ulong")]
    [InlineData("float")]
    [InlineData("double")]
    public void ATextConvertsExactlyWhenItSatisfiesTheConstraintOfTheSameName(string constraint)
    {
        const NumberStyles Decimal = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        switch (constraint)
        {
            case "int":
                AssertEquivalent(constraint, t => int.Parse(t, NumberStyles.AllowLeadingSign, Invariant));
                break;
            case "long":
                AssertEquivalent(constraint, t => long.Parse(t, NumberStyles.AllowLeadingSign, Invariant));
                break;
            case "decimal":
                AssertEquivalent(constraint, t => decimal.Parse(t, Decimal, Invariant));
                break;
            case "bool":
                AssertEquivalent(constraint, bool.Parse);
                break;
            case "sbyte":
                AssertEquivalent(constraint, t => sbyte.Parse(t, NumberStyles.AllowLeadingSign, Invariant));
                break;
            case "byte":
                AssertEquivalent(constraint, t => byte.Parse(t, NumberStyles.AllowLeadingSign, Invariant));
                break;
            case "short":
                AssertEquivalent(constraint, t => short.Parse(t, NumberStyles.AllowLeadingSign, Invariant));
                break;
            case "ushort":
                AssertEquivalent(constraint, t => ushort.Parse(t, NumberStyles.AllowLeadingSign, Invariant));
                break;
            case "uint":
                AssertEquivalent(constraint, t => uint.Parse(t, NumberStyles.AllowLeadingSign, Invariant));
                break;
            case "ulong":
                AssertEquivalent(constraint, t => ulong.Parse(t, NumberStyles.AllowLeadingSign, Invariant));
                break;
            case "float":
                AssertEquivalent(constraint, t => float.Parse(t, Decimal, Invariant));
                break;
            case "double":
                AssertEquivalent(constraint, t => double.Parse(t, Decimal, Invariant));
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
            AssertInvalid(() => context.Path<sbyte>("id"), "id", "sbyte");
            AssertInvalid(() => context.TryGetPath("id", out byte _), "id", "byte");
            AssertInvalid(() => context.Path<short>("id"), "id", "short");
            AssertInvalid(() => context.TryGetPath("id", out ushort _), "id", "ushort");
            AssertInvalid(() => context.Path<uint>("id"), "id", "uint");
            AssertInvalid(() => context.TryGetPath("id", out ulong _), "id", "ulong");
            AssertInvalid(() => context.Path<float>("id"), "id", "float");
            AssertInvalid(() => context.TryGetPath("id", out double _), "id", "double");
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

    // A nullable type reads a value that is not there as null, in each way a value can be absent.
    [Theory]
    [InlineData("/products/{id}", "/products/42", "missing")]
    [InlineData("/orders/{id}/{page?}", "/orders/1", "page")]
    [InlineData("/files/{*rest}", "/files", "rest")]
    public void ANullableReadOfAnAbsentValueIsFalseAndNull(string template, string uri, string name)
    {
        Within(template, uri, context =>
        {
            AssertNullableAbsent<int>(context, name);
            AssertNullableAbsent<long>(context, name);
            AssertNullableAbsent<decimal>(context, name);
            AssertNullableAbsent<bool>(context, name);
            AssertNullableAbsent<Guid>(context, name);
            AssertNullableAbsent<sbyte>(context, name);
            AssertNullableAbsent<byte>(context, name);
            AssertNullableAbsent<short>(context, name);
            AssertNullableAbsent<ushort>(context, name);
            AssertNullableAbsent<uint>(context, name);
            AssertNullableAbsent<ulong>(context, name);
            AssertNullableAbsent<float>(context, name);
            AssertNullableAbsent<double>(context, name);
        });
    }

    [Fact]
    public void ANullableReadOfAPresentValueIsTheValue()
    {
        AssertNullablePresent("-42", -42);
        AssertNullablePresent("3000000000", 3000000000L);
        AssertNullablePresent("19.95", 19.95m);
        AssertNullablePresent("fAlSe", false);
        AssertNullablePresent(GuidText, new System.Guid(GuidText));
        AssertNullablePresent("-128", sbyte.MinValue);
        AssertNullablePresent("255", byte.MaxValue);
        AssertNullablePresent("-32768", short.MinValue);
        AssertNullablePresent("65535", ushort.MaxValue);
        AssertNullablePresent("4294967295", uint.MaxValue);
        AssertNullablePresent("18446744073709551615", ulong.MaxValue);
        AssertNullablePresent("-.5", -0.5f);
        AssertNullablePresent("-0", -0.0);
    }

    [Fact]
    public void ANullableReadOfAValueThatDoesNotConvertRejectsItAsTheUnderlyingTypeDoes()
    {
        Within("/products/{id}", "/products/abc", context =>
        {
            AssertInvalid(() => context.Path<int?>("id"), "id", "int");
            AssertInvalid(() => context.TryGetPath("id", out int? _), "id", "int");
            AssertInvalid(() => context.Path<long?>("id"), "id", "long");
            AssertInvalid(() => context.TryGetPath("id", out long? _), "id", "long");
            AssertInvalid(() => context.Path<decimal?>("id"), "id", "decimal");
            AssertInvalid(() => context.TryGetPath("id", out decimal? _), "id", "decimal");
            AssertInvalid(() => context.Path<bool?>("id"), "id", "bool");
            AssertInvalid(() => context.TryGetPath("id", out bool? _), "id", "bool");
            AssertInvalid(() => context.Path<Guid?>("id"), "id", "Guid");
            AssertInvalid(() => context.TryGetPath("id", out Guid? _), "id", "Guid");
            AssertInvalid(() => context.Path<sbyte?>("id"), "id", "sbyte");
            AssertInvalid(() => context.TryGetPath("id", out byte? _), "id", "byte");
            AssertInvalid(() => context.Path<short?>("id"), "id", "short");
            AssertInvalid(() => context.TryGetPath("id", out ushort? _), "id", "ushort");
            AssertInvalid(() => context.Path<uint?>("id"), "id", "uint");
            AssertInvalid(() => context.TryGetPath("id", out ulong? _), "id", "ulong");
            AssertInvalid(() => context.Path<float?>("id"), "id", "float");
            AssertInvalid(() => context.TryGetPath("id", out double? _), "id", "double");
        });
    }

    // Over the whole corpus, the nullable form converts exactly the texts its underlying type converts, to the same
    // value, and rejects the others with the same exception.
    [Fact]
    public void ANullableTypeReadsByExactlyTheRuleOfItsUnderlyingType()
    {
        AssertNullableEquivalent<int>();
        AssertNullableEquivalent<long>();
        AssertNullableEquivalent<decimal>();
        AssertNullableEquivalent<bool>();
        AssertNullableEquivalent<Guid>();
        AssertNullableEquivalent<sbyte>();
        AssertNullableEquivalent<byte>();
        AssertNullableEquivalent<short>();
        AssertNullableEquivalent<ushort>();
        AssertNullableEquivalent<uint>();
        AssertNullableEquivalent<ulong>();
        AssertNullableEquivalent<float>();
        AssertNullableEquivalent<double>();
    }

    // The natural way to read an optional parameter: the compiler infers int? from the out variable.
    [Fact]
    public void AnOptionalParameterReadsIntoANullableVariable()
    {
        int? inferred = null;
        Expression<Func<RouteMatchContext, bool>> read = c => c.TryGetPath("page", out inferred);
        Assert.Equal([typeof(int?)], ((MethodCallExpression)read.Body).Method.GetGenericArguments());

        var pages = new List<int?>();
        RouteTable table = new RouteTableBuilder()
            .Map("/list/{page?}", c =>
            {
                bool found = c.TryGetPath("page", out int? page);
                Assert.Equal(found, page.HasValue);
                pages.Add(page);
                return new SettingsRoute();
            })
            .Build();

        Assert.True(table.Match(new Uri("/list", UriKind.Relative)).IsSuccess);
        Assert.True(table.Match(new Uri("/list/3", UriKind.Relative)).IsSuccess);
        RouteMatchResult invalid = table.Match(new Uri("/list/x", UriKind.Relative));

        Assert.Equal([null, 3], pages);
        Assert.False(invalid.IsSuccess);
        RouteDiagnostic diagnostic = Assert.Single(invalid.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("page", diagnostic.Data["name"]);
        Assert.EndsWith(" int.", (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
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

    [Theory]
    [InlineData("v")]
    [InlineData("V")]
    public void ANullableReadLooksUpTheNameWithoutRegardToCase(string name)
    {
        Within("/{v}/{w?}", "/42", context =>
        {
            Assert.Equal(42, context.Path<int?>(name));
            Assert.True(context.TryGetPath(name, out long? value));
            Assert.Equal(42L, value);

            string absent = name == "v" ? "w" : "W";
            Assert.False(context.TryGetPath(absent, out Guid? guid));
            Assert.Null(guid);
            AssertAbsent(() => context.Path<bool?>(absent));
        });

        Within("/{v}", "/x", context =>
        {
            AssertInvalid(() => context.Path<decimal?>(name), name, "decimal");
            AssertInvalid(() => context.TryGetPath(name, out int? _), name, "int");
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
                AssertUnsupported(() => context.Path<Int128>(name), "'Int128'");
                AssertUnsupported(() => context.Path<UInt128?>(name), "'UInt128?'");
                AssertUnsupported(() => context.Path<Half>(name), "'Half'");
                AssertUnsupported(() => context.Path<Half?>(name), "'Half?'");
                AssertUnsupported(() => context.Path<char>(name), "'Char'");
                AssertUnsupported(() => context.Path<nint>(name), "'IntPtr'");
                AssertUnsupported(() => context.Path<DateTime>(name), "'DateTime'");
                AssertUnsupported(() => context.Path<DateTime?>(name), "'DateTime?'");
                AssertUnregistered(() => context.Path<DayOfWeek>(name), "'DayOfWeek'", "DayOfWeek");
                AssertUnregistered(() => context.Path<DayOfWeek?>(name), "'DayOfWeek?'", "DayOfWeek");
                AssertUnregistered(() => context.TryGetPath(name, out Status _), "'" + StatusName + "'", StatusName);
                AssertUnregistered(
                    () => context.TryGetPath(name, out Status? _),
                    "'" + StatusName + "?'",
                    StatusName);
                AssertUnsupported(() => context.Path<List<int>>(name), "'List<Int32>'");
                AssertUnsupported(() => context.Path<Dictionary<string, int?>>(name), "'Dictionary<String, Int32?>'");
                AssertUnsupported(() => context.Path<object>(name), "'Object'");
                AssertUnsupported(() => context.TryGetPath(name, out Half? _), "'Half?'");
                AssertUnsupported(() => context.TryGetPath(name, out Int128 _), "'Int128'");
                AssertUnsupported(() => context.TryGetPath(name, out char? _), "'Char?'");
                AssertUnsupported(() => context.TryGetPath(name, out nuint _), "'UIntPtr'");
                AssertUnsupported(() => context.TryGetPath(name, out List<int>? _), "'List<Int32>'");
                AssertUnsupported(() => context.TryGetPath(name, out object? _), "'Object'");
            }

            // The type is checked before anything else, the name included.
            AssertUnsupported(() => context.Path<Half>(null!), "'Half'");
            AssertUnsupported(() => context.Path<Int128?>(null!), "'Int128?'");
            AssertUnsupported(() => context.TryGetPath(null!, out nint _), "'IntPtr'");
            AssertUnregistered(() => context.Path<DayOfWeek>(null!), "'DayOfWeek'", "DayOfWeek");
            AssertUnregistered(() => context.TryGetPath(null!, out Status? _), "'" + StatusName + "?'", StatusName);
        });
    }

    // Enum and ValueType are not enums themselves, so they get the general message, not the advice to register them.
    [Fact]
    public void TheBaseTypesOfAnEnumAreUnsupportedRatherThanUnregistered()
    {
        WithinEnums("/products/{id}", "/products/42", context =>
        {
            foreach (string name in new[] { "id", "missing" })
            {
                AssertNotAnEnum(() => context.Path<Enum>(name), "'Enum'");
                AssertNotAnEnum(() => context.TryGetPath(name, out ValueType? _), "'ValueType'");
            }
        });
    }

    // An array has the name of its element type, and an array of arrays keeps the order of Type.Name. A nested type
    // has the types it is declared in, each with its own generic arguments.
    [Fact]
    public void AnUnsupportedArrayOrNestedTypeHasAReadableName()
    {
        const string OuterName = nameof(RouteMatchContextTypedPathTests) + ".Outer";
        Within("/products/{id}", "/products/42", context =>
        {
            AssertUnsupported(() => context.Path<int?[]>("id"), "'Int32?[]'");
            AssertUnsupported(() => context.Path<List<int>[]>("id"), "'List<Int32>[]'");
            AssertUnsupported(() => context.Path<int[,]>("id"), "'Int32[,]'");
            AssertUnsupported(() => context.Path<int[][]>("id"), "'Int32[][]'");
            AssertUnsupported(() => context.Path<int[][,]>("id"), $"'{typeof(int[][,]).Name}'");
            AssertUnsupported(() => context.Path<int[,][]>("missing"), $"'{typeof(int[,][]).Name}'");
            AssertUnsupported(() => context.Path<Outer<int>.Inner>("id"), $"'{OuterName}<Int32>.Inner'");
            AssertUnsupported(() => context.Path<Outer<int>.Inner?>("id"), $"'{OuterName}<Int32>.Inner?'");
            AssertUnsupported(
                () => context.TryGetPath("missing", out Outer<int>.Inner2<string>? _),
                $"'{OuterName}<Int32>.Inner2<String>'");
            AssertUnsupported(
                () => context.Path<Dictionary<string, Outer<long>.Inner?[]>>("id"),
                $"'Dictionary<String, {OuterName}<Int64>.Inner?[]>'");
        });

        Assert.Equal("Int32[,][]", typeof(int[][,]).Name);
    }

    [Fact]
    public void AnUnsupportedTypeEscapesMatch()
    {
        RouteTable present = new RouteTableBuilder()
            .Map("/products/{id}", c => new ProductRoute(c.Path<Int128>("id").ToString(Invariant)))
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
    [InlineData("/y/{id:long}", "/y/300", "byte")]
    [InlineData("/y/{id:int}", "/y/256", "byte")]
    [InlineData("/y/{id:short}", "/y/-1", "byte")]
    [InlineData("/u/{id:int}", "/u/-1", "uint")]
    [InlineData("/f/{id:double}", "/f/1000000000000000000000000000000000000000", "float")]
    public void ADifferentConstraintCanLetThroughAValueTheReadRejects(string template, string path, string type)
    {
        RouteTable table = new RouteTableBuilder()
            .Map(template, c => new ProductRoute(type switch
            {
                "int" => c.Path<int>("id").ToString(Invariant),
                "byte" => c.Path<byte>("id").ToString(Invariant),
                "uint" => c.Path<uint>("id").ToString(Invariant),
                "float" => c.Path<float>("id").ToString(Invariant),
                _ => c.Path<bool>("id").ToString(),
            }))
            .Build();

        RouteMatchResult result = table.Match(new Uri(path, UriKind.Relative));

        Assert.False(result.IsSuccess);
        Assert.Equal(template, result.Definition?.Template.Value);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("id", diagnostic.Data["name"]);
        Assert.EndsWith($" {type}.", (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
    }

    // A constraint narrower than the type always lets the read succeed: every text that byte accepts is an int, and
    // every text that float accepts is a double, so the path matches exactly when the constraint accepts the value.
    [Fact]
    public void AConstraintNarrowerThanTheTypeAlwaysConverts()
    {
        RouteConstraint byteRule = RouteConstraint.Find("byte")!;
        RouteConstraint floatRule = RouteConstraint.Find("float")!;
        RouteTable table = new RouteTableBuilder()
            .Map("/i/{n:byte}", c => new ProductRoute(c.Path<int>("n").ToString(Invariant)))
            .Map("/d/{n:float}", c => new ProductRoute(c.Path<double>("n").ToString(Invariant)))
            .Build();
        var converted = 0;

        foreach (string text in TypedValueCorpus.Texts())
        {
            string segment = Uri.EscapeDataString(text);
            RouteMatchResult integer = table.Match(new Uri("/i/" + segment, UriKind.Relative));
            RouteMatchResult floatingPoint = table.Match(new Uri("/d/" + segment, UriKind.Relative));

            Assert.True(byteRule.IsSatisfiedBy(text) == integer.IsSuccess, $"byte: '{text}'");
            Assert.True(floatRule.IsSatisfiedBy(text) == floatingPoint.IsSuccess, $"float: '{text}'");
            Assert.Equal(integer.IsSuccess, integer.Definition is not null);
            Assert.Equal(floatingPoint.IsSuccess, floatingPoint.Definition is not null);
            converted += integer.IsSuccess ? 1 : 0;
        }

        Assert.True(converted > 40, $"{converted}");
        Assert.Equal(new ProductRoute("7"), table.Match(new Uri("/i/+007", UriKind.Relative)).Route);
    }

    // long is neither narrower nor wider than ulong: it keeps a valid value above its range from the read, since the
    // template does not match at all, and lets a negative value through to fail the read.
    [Fact]
    public void AConstraintOfAnotherRangeKeepsValidValuesOutAndLetsInvalidOnesThrough()
    {
        const string AboveLong = "/n/9223372036854775808";
        RouteTable longRange = new RouteTableBuilder()
            .Map("/n/{n:long}", c => new ProductRoute(c.Path<ulong>("n").ToString(Invariant)))
            .Build();
        RouteTable wide = new RouteTableBuilder()
            .Map("/n/{n:decimal}", c => new ProductRoute(c.Path<ulong>("n").ToString(Invariant)))
            .Build();

        RouteMatchResult missed = longRange.Match(new Uri(AboveLong, UriKind.Relative));
        RouteMatchResult negative = longRange.Match(new Uri("/n/-1", UriKind.Relative));

        Assert.False(missed.IsSuccess);
        Assert.Null(missed.Definition);
        Assert.False(negative.IsSuccess);
        Assert.Equal("/n/{n:long}", negative.Definition?.Template.Value);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, Assert.Single(negative.Diagnostics).Code);
        Assert.Equal(
            new ProductRoute("9223372036854775808"),
            wide.Match(new Uri(AboveLong, UriKind.Relative)).Route);
        Assert.Equal(
            new ProductRoute(ulong.MaxValue.ToString(Invariant)),
            wide.Match(new Uri("/n/" + ulong.MaxValue.ToString(Invariant), UriKind.Relative)).Route);
    }

    // A wider constraint checks less than the type does, so what it lets through beyond the type fails the match.
    [Theory]
    [InlineData("/n/1.5")]
    [InlineData("/n/-1")]
    public void AConstraintWiderThanTheTypeLetsThroughValuesThatFailTheMatch(string path)
    {
        var laterCalled = false;
        RouteTable table = new RouteTableBuilder()
            .Map("/n/{n:decimal}", c => new ProductRoute(c.Path<ulong>("n").ToString(Invariant)))
            .Map("/n/{slug}", _ =>
            {
                laterCalled = true;
                return new SettingsRoute();
            })
            .Build();

        RouteMatchResult result = table.Match(new Uri(path, UriKind.Relative));

        Assert.False(result.IsSuccess);
        Assert.Equal("/n/{n:decimal}", result.Definition?.Template.Value);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, Assert.Single(result.Diagnostics).Code);
        Assert.False(laterCalled);
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
        Assert.Contains(PathValue, (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
        Assert.False(laterCalled);

        Assert.Equal(new ProductRoute("42"), table.Match(new Uri("/orders/+042", UriKind.Relative)).Route);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("decimal")]
    [InlineData("bool")]
    [InlineData("guid")]
    [InlineData("int?")]
    [InlineData("guid?")]
    [InlineData("ulong")]
    [InlineData("double?")]
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
                        "int?" => c.Path<int?>("id").ToString(),
                        "guid?" => c.TryGetPath("id", out Guid? n).ToString() + n,
                        "ulong" => c.Path<ulong>("id").ToString(Invariant),
                        "double?" => c.TryGetPath("id", out double? d).ToString() + d,
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
        Assert.Contains(PathValue, thrown.Message, StringComparison.Ordinal);
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
    [Theory]
    [InlineData("int", "42")]
    [InlineData("int?", "42")]
    [InlineData("try int?", "42")]
    [InlineData("long?", "42")]
    [InlineData("decimal?", "4.2")]
    [InlineData("bool?", "true")]
    [InlineData("guid?", GuidText)]
    [InlineData("byte", "42")]
    [InlineData("try ushort?", "42")]
    [InlineData("ulong?", "42")]
    [InlineData("double", "4.2")]
    [InlineData("try float?", "4.2")]
    public void ATypedReadAllocatesNoMoreThanReadingTheStringAndParsingIt(string type, string text)
    {
        const int Reads = 16;
        Func<RouteMatchContext, int> typedRead = type switch
        {
            "int" => c => c.Path<int>("id"),
            "int?" => c => c.Path<int?>("id")!.Value,
            "try int?" => c => c.TryGetPath("id", out int? value) ? value!.Value : 0,
            "long?" => c => (int)c.Path<long?>("id")!.Value,
            "decimal?" => c => (int)c.Path<decimal?>("id")!.Value,
            "bool?" => c => c.TryGetPath("id", out bool? value) && value!.Value ? 1 : 0,
            "byte" => c => c.Path<byte>("id"),
            "try ushort?" => c => c.TryGetPath("id", out ushort? value) ? value!.Value : 0,
            "ulong?" => c => (int)c.Path<ulong?>("id")!.Value,
            "double" => c => (int)c.Path<double>("id"),
            "try float?" => c => c.TryGetPath("id", out float? value) ? (int)value!.Value : 0,
            _ => c => c.TryGetPath("id", out Guid? value) ? value!.Value.GetHashCode() : 0,
        };
        Func<RouteMatchContext, int> parsedRead = type switch
        {
            "int" or "int?" or "try int?" => c => int.Parse(c.Path("id"), Invariant),
            "long?" => c => (int)long.Parse(c.Path("id"), Invariant),
            "decimal?" => c => (int)decimal.Parse(c.Path("id"), Invariant),
            "bool?" => c => bool.Parse(c.Path("id")) ? 1 : 0,
            "byte" => c => byte.Parse(c.Path("id"), Invariant),
            "try ushort?" => c => ushort.Parse(c.Path("id"), Invariant),
            "ulong?" => c => (int)ulong.Parse(c.Path("id"), Invariant),
            "double" => c => (int)double.Parse(c.Path("id"), Invariant),
            "try float?" => c => (int)float.Parse(c.Path("id"), Invariant),
            _ => c => System.Guid.Parse(c.Path("id")).GetHashCode(),
        };

        long typedBytes = AllocatedBytesPerMatch(ReadingTable(typedRead, Reads), text);
        long parsedBytes = AllocatedBytesPerMatch(ReadingTable(parsedRead, Reads), text);

        Assert.True(
            typedBytes - parsedBytes < Reads * IntPtr.Size,
            $"{type}: typed {typedBytes} bytes, parsed {parsedBytes} bytes");
    }

    // Matching a number constraint costs nothing beyond matching the same template without it: int and long call their
    // parsers directly, and the other types the generic parse methods that the typed reads use, none of which
    // allocates. A box on each check would make the difference at least one pointer per constrained segment, and the
    // bound allows less than that in all.
    [Theory]
    [InlineData("int", "42")]
    [InlineData("long", "-42")]
    [InlineData("decimal", "4.2")]
    [InlineData("sbyte", "-42")]
    [InlineData("byte", "+42")]
    [InlineData("short", "42")]
    [InlineData("ushort", "42")]
    [InlineData("uint", "42")]
    [InlineData("ulong", "18446744073709551615")]
    [InlineData("float", "4.2")]
    [InlineData("double", "-.5")]
    public void MatchingANumberConstraintAllocatesNoMoreThanMatchingWithoutIt(string constraint, string text)
    {
        const int Segments = 8;
        string Template(string suffix) =>
            "/orders" + string.Concat(Enumerable.Range(0, Segments).Select(i => $"/{{v{i}{suffix}}}"));
        string id = string.Join('/', Enumerable.Repeat(text, Segments));
        RouteTable constrained = new RouteTableBuilder()
            .Map(Template(":" + constraint), _ => new SettingsRoute())
            .Build();
        RouteTable unconstrained = new RouteTableBuilder()
            .Map(Template(""), _ => new SettingsRoute())
            .Build();

        long constrainedBytes = AllocatedBytesPerMatch(constrained, id);
        long unconstrainedBytes = AllocatedBytesPerMatch(unconstrained, id);

        Assert.True(
            constrainedBytes - unconstrainedBytes < Segments * IntPtr.Size,
            $"{constraint}: constrained {constrainedBytes} bytes, unconstrained {unconstrainedBytes} bytes");
    }

    [Theory]
    [InlineData("Pending", Status.Pending)]
    [InlineData("pending", Status.Pending)]
    [InlineData("PENDING", Status.Pending)]
    [InlineData("pEnDiNg", Status.Pending)]
    [InlineData("InProgress", Status.InProgress)]
    [InlineData("inprogress", Status.InProgress)]
    [InlineData("INPROGRESS", Status.InProgress)]
    [InlineData("Shipped", Status.Shipped)]
    [InlineData("Cancelled", Status.Cancelled)]
    public void AnEnumIsTheMemberItsNameNamesInAnyCase(string text, Status expected)
    {
        AssertEnumCase<Status>(text, expected, "Status");
    }

    // Enum.TryParse accepts the numbers, the lists of names and the texts with white space among these; a registered
    // enum accepts none of them.
    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("+2")]
    [InlineData("99")]
    [InlineData("0x1")]
    [InlineData("1.0")]
    [InlineData(" 2")]
    [InlineData("Pending,Shipped")]
    [InlineData("Pending, Shipped")]
    [InlineData("Pending,")]
    [InlineData(",Pending")]
    [InlineData(" Pending")]
    [InlineData("Pending ")]
    [InlineData("\tPending")]
    [InlineData("Pend ing")]
    [InlineData("Pendingx")]
    [InlineData("xPending")]
    [InlineData("In-Progress")]
    [InlineData("in_progress")]
    [InlineData("Status.Pending")]
    [InlineData("None")]
    [InlineData("Pend\u0131ng")]
    [InlineData("PEND\u0130NG")]
    [InlineData("\u0130nProgress")]
    [InlineData("\u0131nProgress")]
    public void AnEnumRejectsEveryTextButTheNameOfAMember(string text)
    {
        AssertEnumCase<Status>(text, null, "Status");
    }

    // Case is ignored ordinally, so a culture whose casing rules differ, such as Turkish, changes nothing.
    [Fact]
    public void AnEnumNameIsComparedTheSameWayInEveryCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            AssertEnumCase<Status>("PENDING", Status.Pending, "Status");
            AssertEnumCase<Status>("inprogress", Status.InProgress, "Status");
            AssertEnumCase<Status>("PEND\u0130NG", null, "Status");
            AssertEnumCase<Status>("\u0131nprogress", null, "Status");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Canceled")]
    [InlineData("CANCELED")]
    public void EveryNameOfAValueGivesThatValue(string text)
    {
        AssertEnumCase<Status>(text, Status.Cancelled, "Status");
    }

    [Theory]
    [InlineData("None", Access.None)]
    [InlineData("Read", Access.Read)]
    [InlineData("write", Access.Write)]
    [InlineData("READWRITE", Access.ReadWrite)]
    [InlineData("Admin", Access.Admin)]
    public void AFlagsEnumIsReadFromTheNameOfASingleMember(string text, Access expected)
    {
        AssertEnumCase<Access>(text, expected, "Access");
    }

    [Theory]
    [InlineData("Read,Write")]
    [InlineData("Read, Write")]
    [InlineData("Read|Write")]
    [InlineData("Read Write")]
    [InlineData("Read,Admin")]
    [InlineData("3")]
    [InlineData("5")]
    public void AFlagsEnumRejectsACombination(string text)
    {
        AssertEnumCase<Access>(text, null, "Access");
    }

    [Fact]
    public void AnEnumOfAnyUnderlyingTypeIsReadByTheNamesOfItsMembers()
    {
        AssertEnumCase<Level>("low", Level.Low, "Level");
        AssertEnumCase<Level>("High", Level.High, "Level");
        AssertEnumCase<Level>("255", null, "Level");
        AssertEnumCase<Offset>("Min", Offset.Min, "Offset");
        AssertEnumCase<Offset>("MAX", Offset.Max, "Offset");
        AssertEnumCase<Offset>("-9223372036854775808", null, "Offset");
        AssertEnumCase<Huge>("top", Huge.Top, "Huge");
        AssertEnumCase<Huge>("AboveLong", Huge.AboveLong, "Huge");
        AssertEnumCase<Huge>("18446744073709551615", null, "Huge");
        AssertEnumCase<Huge>("9223372036854775808", null, "Huge");
    }

    [Theory]
    [InlineData("Nothing")]
    [InlineData("None")]
    [InlineData("0")]
    [InlineData("x")]
    public void AnEnumWithNoMembersRejectsEveryText(string text)
    {
        AssertEnumCase<Nothing>(text, null, "Nothing");
    }

    [Fact]
    public void AnEnumThePathLeavesOutIsAbsent()
    {
        WithinEnums("/orders/{id}/{status?}", "/orders/1", context =>
        {
            AssertAbsent(() => context.Path<Status>("status"));
            AssertAbsent(() => context.Path<Status?>("status"));
            Assert.False(context.TryGetPath("status", out Status status));
            Assert.Equal(default, status);
            Assert.False(context.TryGetPath("status", out Status? nullable));
            Assert.Null(nullable);
        });

        WithinEnums("/orders/{id}/{status?}", "/orders/1/shipped", context =>
        {
            Assert.True(context.TryGetPath("status", out Status? status));
            Assert.Equal(Status.Shipped, status);
        });
    }

    [Fact]
    public void AnEnumReadRejectsANullNameOnceTheEnumIsRegistered()
    {
        WithinEnums("/orders/{status}", "/orders/pending", context =>
        {
            Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => context.Path<Status>(null!)).ParamName);
            Assert.Equal(
                "name",
                Assert.Throws<ArgumentNullException>(() => context.TryGetPath(null!, out Status? _)).ParamName);
        });
    }

    // A table knows only the enums registered on its own builder, whatever other enums that builder registered.
    [Fact]
    public void AnEnumThatIsNotRegisteredIsAProgrammingErrorThatEscapesMatch()
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Access>()
            .Map("/orders/{status}", c => new ProductRoute(c.Path<Status>("status").ToString()))
            .Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => table.Match(new Uri("/orders/pending", UriKind.Relative)));

        Assert.Contains("AddEnumValueCodec", exception.Message, StringComparison.Ordinal);
    }

    // The message for a value that is not valid names the enum by its simple name, as the built-in types are named by
    // their keywords; the message for an enum that is not registered names it as written in code, with the types it is
    // declared in, so that it can be found.
    [Fact]
    public void ANestedEnumIsNamedSimplyForAValueAndFullyForARegistration()
    {
        WithinEnums("/{v}", "/x", context =>
        {
            RouteValueException invalid = Assert.Throws<RouteValueException>(() => context.Path<Status>("v"));
            Assert.EndsWith(" Status.", invalid.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(RouteMatchContextTypedPathTests), invalid.Message, StringComparison.Ordinal);
        });

        Within("/{v}", "/x", context =>
        {
            AssertUnregistered(() => context.Path<Status>("v"), "'" + StatusName + "'", StatusName);
        });
    }

    [Fact]
    public void ANameThatIsNotAMemberFailsTheMatchAndNoLaterTemplateIsTried()
    {
        var laterCalled = false;
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .Map("/orders/{status}", c => new ProductRoute(c.Path<Status>("status").ToString()))
            .Map("/{*rest}", _ =>
            {
                laterCalled = true;
                return new SettingsRoute();
            })
            .Build();

        RouteMatchResult result = table.Match(new Uri("/orders/2", UriKind.Relative));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Route);
        Assert.Equal("/orders/{status}", result.Definition?.Template.Value);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("status", diagnostic.Data["name"]);
        Assert.Contains(PathValue, (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
        Assert.EndsWith(" Status.", (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
        Assert.False(laterCalled);

        Assert.Equal(new ProductRoute("Shipped"), table.Match(new Uri("/orders/SHIPPED", UriKind.Relative)).Route);
    }

    [Theory]
    [InlineData("enum")]
    [InlineData("enum?")]
    [InlineData("try enum")]
    [InlineData("try enum?")]
    public void AnEnumReadThatFailsNeverQuotesTheValue(string type)
    {
        RouteValueException? thrown = null;
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .Map("/orders/{id}", c =>
            {
                try
                {
                    _ = type switch
                    {
                        "enum" => c.Path<Status>("id").ToString(),
                        "enum?" => c.Path<Status?>("id").ToString(),
                        "try enum" => c.TryGetPath("id", out Status s).ToString() + s,
                        _ => c.TryGetPath("id", out Status? n).ToString() + n,
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
        Assert.Contains(PathValue, thrown.Message, StringComparison.Ordinal);
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

    [Fact]
    public void NoConstraintIsNamedAfterEnums()
    {
        RouteTableBuilder builder = new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .Map("/orders/{status:enum}", _ => new SettingsRoute());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("names the constraint 'enum'", exception.Message, StringComparison.Ordinal);
        Assert.Null(RouteConstraint.Find("enum"));
    }

    // An enum read must cost no more than the string read of the same value: the name table gives the value without a
    // box, which would be at least a pointer per read and is more than the bound allows in all.
    [Theory]
    [InlineData("enum", "Shipped")]
    [InlineData("enum?", "shipped")]
    [InlineData("try enum", "SHIPPED")]
    [InlineData("try enum?", "Shipped")]
    [InlineData("long enum", "max")]
    public void AnEnumReadAllocatesNoMoreThanTheStringRead(string type, string text)
    {
        const int Reads = 16;
        Func<RouteMatchContext, int> enumRead = type switch
        {
            "enum" => c => (int)c.Path<Status>("id"),
            "enum?" => c => (int)c.Path<Status?>("id")!.Value,
            "try enum" => c => c.TryGetPath("id", out Status value) ? (int)value : 0,
            "long enum" => c => (int)c.Path<Offset>("id"),
            _ => c => c.TryGetPath("id", out Status? value) ? (int)value!.Value : 0,
        };

        long enumBytes = AllocatedBytesPerMatch(ReadingTable(enumRead, Reads, EnumBuilder()), text);
        long stringBytes = AllocatedBytesPerMatch(ReadingTable(c => c.Path("id").Length, Reads, EnumBuilder()), text);

        Assert.True(
            enumBytes - stringBytes < Reads * IntPtr.Size,
            $"{type}: enum {enumBytes} bytes, string {stringBytes} bytes");
    }

    // A built-in type is decided by its typeof tests before the codecs are looked at, so registrations cost its reads
    // nothing.
    [Fact]
    public void ABuiltInReadAllocatesNoMoreOnATableWithRegistrations()
    {
        const int Reads = 16;
        Func<RouteMatchContext, int> read =
            c => c.Path<int>("id") + (c.TryGetPath("id", out long? n) ? (int)n!.Value : 0);

        long withBytes = AllocatedBytesPerMatch(ReadingTable(read, Reads, EnumBuilder()), "42");
        long withoutBytes = AllocatedBytesPerMatch(ReadingTable(read, Reads), "42");

        Assert.True(withBytes - withoutBytes < IntPtr.Size, $"with {withBytes} bytes, without {withoutBytes} bytes");
    }

    [Fact]
    public void ACustomTypeIsReadByItsCodec()
    {
        WithinCustom("/{v}", "/007", context =>
        {
            Assert.Equal(new Custom.ProductId(7), context.Path<Custom.ProductId>("v"));
            Assert.True(context.TryGetPath("v", out Custom.ProductId id));
            Assert.Equal(new Custom.ProductId(7), id);
            Assert.Equal(new Custom.Outer<int>.Inner(7), context.Path<Custom.Outer<int>.Inner>("v"));
            Assert.Equal(new Custom.Note("007"), context.Path<Custom.Note>("v"));
        });
        WithinCustom("/{v}", "/blue-42", context =>
        {
            Assert.Equal(new Custom.Slug("blue-42"), context.Path<Custom.Slug>("v"));
            Assert.True(context.TryGetPath("v", out Custom.Slug? slug));
            Assert.Equal(new Custom.Slug("blue-42"), slug);
        });
        WithinCustom("/{v}", "/-3x4", context =>
        {
            Custom.Point point = context.Path<Custom.Point>("v");
            Assert.Equal((-3, 4), (point.X, point.Y));
        });
        WithinCustom("/{v}", "/in-progress", context =>
        {
            Assert.Equal(Custom.Stage.InProgress, context.Path<Custom.Stage>("v"));
            Assert.Equal(Custom.Stage.InProgress, context.Path<Custom.Stage?>("v"));
        });
        WithinCustom("/{v}", "/circle", context =>
        {
            Assert.IsType<Custom.Circle>(context.Path<Custom.IShape>("v"));
        });
    }

    // A struct registered with AddValueCodec has no codec of its own for its nullable form, which is read through the
    // codec of the struct, to the same value.
    [Fact]
    public void TheNullableFormOfACustomStructIsReadByTheCodecOfTheStruct()
    {
        WithinCustom("/a/{v?}", "/a/42", context =>
        {
            Assert.Equal(new Custom.ProductId(42), context.Path<Custom.ProductId?>("v"));
            Assert.True(context.TryGetPath("v", out Custom.ProductId? id));
            Assert.Equal(new Custom.ProductId(42), id);
            Assert.True(context.TryGetPath("v", out Custom.Outer<int>.Inner? inner));
            Assert.Equal(new Custom.Outer<int>.Inner(42), inner);
        });
        WithinCustom("/a/{v?}", "/a/5x6", context =>
        {
            Assert.True(context.TryGetPath("v", out Custom.Point? point));
            Assert.Equal((5, 6), (point!.Value.X, point.Value.Y));
        });
        WithinCustom("/a/{v?}", "/a/done", context =>
        {
            Assert.True(context.TryGetPath("v", out Custom.Stage? stage));
            Assert.Equal(Custom.Stage.Done, stage);
        });
    }

    // Absent gives false with null for a nullable form and a reference type, never a default value of the type.
    [Fact]
    public void ACustomValueThePathLeavesOutIsAbsent()
    {
        WithinCustom("/a/{v?}", "/a", context =>
        {
            Assert.False(context.TryGetPath("v", out Custom.ProductId? id));
            Assert.Null(id);
            Assert.False(context.TryGetPath("v", out Custom.Stage? stage));
            Assert.Null(stage);
            Assert.False(context.TryGetPath("v", out Custom.Point? point));
            Assert.Null(point);
            Assert.False(context.TryGetPath("v", out Custom.Slug? slug));
            Assert.Null(slug);
            Assert.False(context.TryGetPath("v", out Custom.IShape? shape));
            Assert.Null(shape);
            Assert.False(context.TryGetPath("v", out Custom.ProductId plain));
            Assert.Equal(default, plain);
            AssertAbsent(() => context.Path<Custom.ProductId?>("v"));
            AssertAbsent(() => context.Path<Custom.Slug>("v"));
        });
    }

    // A text the parser rejects is not valid, named by the simple name of the type, even one nested in a generic type.
    [Theory]
    [InlineData("ProductId", "x")]
    [InlineData("ProductId", "-1")]
    [InlineData("Slug", "Blue")]
    [InlineData("Slug", "")]
    [InlineData("Point", "3")]
    [InlineData("Stage", "InProgress")]
    [InlineData("IShape", "triangle")]
    [InlineData("Inner", "x")]
    public void ATextTheParserRejectsIsNotValid(string type, string text)
    {
        WithinCustom("/a/{*v}", "/a/" + Uri.EscapeDataString(text), context =>
        {
            Action read = type switch
            {
                "ProductId" => () => context.Path<Custom.ProductId>("v"),
                "Slug" => () => context.Path<Custom.Slug>("v"),
                "Point" => () => context.Path<Custom.Point>("v"),
                "Stage" => () => context.Path<Custom.Stage>("v"),
                "IShape" => () => context.Path<Custom.IShape>("v"),
                _ => () => context.Path<Custom.Outer<int>.Inner>("v"),
            };
            Action nullable = type switch
            {
                "ProductId" => () => context.TryGetPath("v", out Custom.ProductId? _),
                "Point" => () => context.Path<Custom.Point?>("v"),
                "Stage" => () => context.TryGetPath("v", out Custom.Stage? _),
                "Inner" => () => context.Path<Custom.Outer<int>.Inner?>("v"),
                _ => read,
            };

            // The empty catch-all is left out, so it is absent rather than not valid.
            if (text.Length == 0)
            {
                AssertAbsent(read);
                return;
            }

            AssertInvalid(read, "v", type);
            AssertInvalid(nullable, "v", type);
            Assert.DoesNotContain(nameof(CustomValueTypes), Assert.Throws<RouteValueException>(read).Message);
        });
    }

    [Fact]
    public void ACustomValueThatIsNotValidFailsTheMatchWithoutQuotingItAndNoLaterTemplateIsTried()
    {
        var laterCalled = false;
        RouteValueException? thrown = null;
        RouteTable table = Custom.Builder()
            .Map("/orders/{id}", c =>
            {
                try
                {
                    return new ProductRoute(c.Path<Custom.ProductId>("id").ToString());
                }
                catch (RouteValueException exception)
                {
                    thrown = exception;
                    throw;
                }
            })
            .Map("/{*rest}", _ =>
            {
                laterCalled = true;
                return new SettingsRoute();
            })
            .Build();

        RouteMatchResult result = table.Match(new Uri($"/orders/{Secret}", UriKind.Relative));

        Assert.False(result.IsSuccess);
        Assert.False(laterCalled);
        RouteDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RouteDiagnosticCodes.ValueInvalid, diagnostic.Code);
        Assert.Equal("id", diagnostic.Data["name"]);
        Assert.EndsWith(" ProductId.", (string?)diagnostic.Data["reason"], StringComparison.Ordinal);
        Assert.NotNull(thrown);
        Assert.Null(thrown.InnerException);
        Assert.Empty(thrown.Data);
        Assert.DoesNotContain(Secret, thrown.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, diagnostic.Message, StringComparison.Ordinal);
        foreach ((string key, object? value) in diagnostic.Data)
        {
            if (key != "path")
                Assert.DoesNotContain(Secret, (string?)value, StringComparison.Ordinal);
        }
    }

    // A parser that accepts a text must give a value, so null from it is a mistake in the codec, not in the URI.
    [Fact]
    public void AParserThatAcceptsATextButGivesNullIsAMistakeThatEscapesMatch()
    {
        RouteTable table = new RouteTableBuilder()
            .AddValueCodec<Custom.Slug>(
                (string _, out Custom.Slug? slug) =>
                {
                    slug = null;
                    return true;
                },
                slug => slug.Value)
            .Map("/s/{v}", c => new ProductRoute(c.Path<Custom.Slug>("v").Value))
            .Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => table.Match(new Uri("/s/" + Secret, UriKind.Relative)));

        Assert.Contains("accepted a text but gave a null value", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{nameof(CustomValueTypes)}.Slug'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
    }

    // The parser's exception is the app's own, so it is neither caught nor wrapped, for the struct and its nullable
    // form alike.
    [Theory]
    [InlineData("struct")]
    [InlineData("nullable")]
    public void AnExceptionFromTheParserPropagatesOutOfMatchUnchanged(string type)
    {
        var thrown = new FormatException("From the parser.");
        RouteTable table = new RouteTableBuilder()
            .AddValueCodec<Custom.ProductId>((string _, out Custom.ProductId _) => throw thrown, _ => "1")
            .Map("/p/{v}", c => new ProductRoute(type == "struct"
                ? c.Path<Custom.ProductId>("v").ToString()
                : c.Path<Custom.ProductId?>("v").ToString()!))
            .Build();

        FormatException exception = Assert.Throws<FormatException>(
            () => table.Match(new Uri("/p/1", UriKind.Relative)));

        Assert.Same(thrown, exception);
    }

    // A codec is found by the exact type read: one for an interface does not read a class that implements it, nor one
    // for the class the interface.
    [Fact]
    public void ACodecReadsOnlyItsExactType()
    {
        const string Circle = nameof(CustomValueTypes) + ".Circle";
        const string Shape = nameof(CustomValueTypes) + ".IShape";
        WithinCustom("/{v}", "/circle", context =>
        {
            Assert.IsType<Custom.Circle>(context.Path<Custom.IShape>("v"));
            AssertNoCodec(() => context.Path<Custom.Circle>("v"), $"'{Circle}'", Circle);
            AssertNoCodec(() => context.TryGetPath("v", out object? _), "'Object'", "Object");
        });

        RouteTableBuilder circles = new RouteTableBuilder().AddValueCodec<Custom.Circle>(
            (string text, out Custom.Circle? circle) =>
            {
                circle = text == "circle" ? new Custom.Circle() : null;
                return circle is not null;
            },
            _ => "circle");
        Within("/{v}", "/circle", context =>
        {
            Assert.Equal(new Custom.Circle(), context.Path<Custom.Circle>("v"));
            AssertNoCodec(() => context.Path<Custom.IShape>("v"), $"'{Shape}'", Shape);
        }, circles);
    }

    // Nor does a codec for a class read a class derived from it, or one for the derived class read its base.
    [Fact]
    public void ACodecForAClassReadsNeitherItsBaseNorItsDerivedClasses()
    {
        const string Derived = nameof(RouteMatchContextTypedPathTests) + ".DerivedValue";
        const string Base = nameof(RouteMatchContextTypedPathTests) + ".BaseValue";
        RouteTableBuilder bases = new RouteTableBuilder().AddValueCodec<BaseValue>(
            (string text, out BaseValue? value) =>
            {
                value = new DerivedValue();
                return true;
            },
            _ => "b");
        RouteTableBuilder derived = new RouteTableBuilder().AddValueCodec<DerivedValue>(
            (string text, out DerivedValue? value) =>
            {
                value = new DerivedValue();
                return true;
            },
            _ => "d");

        Within("/{v}", "/x", context =>
        {
            Assert.NotNull(context.Path<BaseValue>("v"));
            AssertNoCodec(() => context.Path<DerivedValue>("v"), $"'{Derived}'", Derived);
        }, bases);
        Within("/{v}", "/x", context =>
        {
            Assert.NotNull(context.Path<DerivedValue>("v"));
            AssertNoCodec(() => context.Path<BaseValue>("v"), $"'{Base}'", Base);
        }, derived);
    }

    // A type that is not built in and has no codec is told which method registers one, by the type it would register,
    // except a collection, which cannot have one; an enum is told both methods.
    [Fact]
    public void AnUnsupportedTypeIsToldHowToRegisterACodec()
    {
        const string ProductId = nameof(CustomValueTypes) + ".ProductId";
        Within("/{v}", "/1", context =>
        {
            AssertNoCodec(() => context.Path<Half>("v"), "'Half'", "Half");
            AssertNoCodec(() => context.Path<Half?>("v"), "'Half?'", "Half");
            AssertNoCodec(() => context.TryGetPath("v", out DateTime? _), "'DateTime?'", "DateTime");
            AssertNoCodec(() => context.Path<Custom.ProductId>("v"), $"'{ProductId}'", ProductId);
            AssertNoCodec(() => context.TryGetPath("v", out Custom.ProductId? _), $"'{ProductId}?'", ProductId);
            const string Slug = nameof(CustomValueTypes) + ".Slug";
            AssertNoCodec(() => context.Path<Custom.Slug>("v"), $"'{Slug}'", Slug);

            foreach (Action read in new Action[]
                     {
                         () => context.Path<List<int>>("v"),
                         () => context.Path<char>("v"),
                         () => context.TryGetPath("v", out char? _),
                     })
            {
                InvalidOperationException cannot = Assert.Throws<InvalidOperationException>(read);
                Assert.DoesNotContain("AddValueCodec<", cannot.Message, StringComparison.Ordinal);
                Assert.Contains("read the value as a string", cannot.Message, StringComparison.Ordinal);
            }

            InvalidOperationException unregistered = Assert.Throws<InvalidOperationException>(
                () => context.Path<DayOfWeek>("v"));
            Assert.Contains("AddEnumValueCodec<DayOfWeek>()", unregistered.Message, StringComparison.Ordinal);
            Assert.Contains("AddValueCodec<DayOfWeek>", unregistered.Message, StringComparison.Ordinal);
        });
    }

    // The message for a value that is not valid names the type simply; the one for a type without a codec names it
    // with the types it is declared in, so that it can be found.
    [Fact]
    public void ACustomTypeNestedInAGenericTypeIsNamedSimplyForAValueAndFullyForARegistration()
    {
        const string Inner = nameof(CustomValueTypes) + ".Outer<Int32>.Inner";
        const string LongInner = nameof(CustomValueTypes) + ".Outer<Int64>.Inner";
        WithinCustom("/{v}", "/x", context =>
        {
            RouteValueException invalid = Assert.Throws<RouteValueException>(
                () => context.Path<Custom.Outer<int>.Inner>("v"));
            Assert.EndsWith(" Inner.", invalid.Message, StringComparison.Ordinal);
        });
        Within("/{v}", "/x", context =>
        {
            AssertNoCodec(() => context.Path<Custom.Outer<int>.Inner>("v"), $"'{Inner}'", Inner);
            AssertNoCodec(() => context.Path<Custom.Outer<long>.Inner?>("v"), $"'{LongInner}?'", LongInner);
        });
    }

    [Fact]
    public void NoConstraintIsNamedAfterACustomType()
    {
        RouteTableBuilder builder = Custom.Builder().Map("/p/{id:productid}", _ => new SettingsRoute());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("names the constraint 'productid'", exception.Message, StringComparison.Ordinal);
    }

    // A read of a struct with a codec passes the value through the codec as the struct itself, so it costs less than
    // one box per read more than reading the string and calling the same parse by hand; the smallest box is three
    // pointers.
    [Theory]
    [InlineData("struct")]
    [InlineData("try struct")]
    [InlineData("record")]
    public void ACustomStructReadAllocatesNoMoreThanReadingTheStringAndParsingIt(string type)
    {
        const int Reads = 16;
        Func<RouteMatchContext, int> customRead = type switch
        {
            "struct" => c => c.Path<Custom.ProductId>("id").Value,
            "try struct" => c => c.TryGetPath("id", out Custom.ProductId id) ? id.Value : 0,
            _ => c => c.Path<Custom.Outer<int>.Inner>("id").Value,
        };
        Func<RouteMatchContext, int> parsedRead = type == "record"
            ? c => int.TryParse(c.Path("id"), NumberStyles.None, Invariant, out int value)
                ? new Custom.Outer<int>.Inner(value).Value
                : 0
            : c => Custom.ProductId.TryParse(c.Path("id"), out Custom.ProductId id) ? id.Value : 0;

        long customBytes = AllocatedBytesPerMatch(ReadingTable(customRead, Reads, Custom.Builder()), "42");
        long parsedBytes = AllocatedBytesPerMatch(ReadingTable(parsedRead, Reads, Custom.Builder()), "42");

        Assert.True(
            customBytes - parsedBytes < Reads * 3 * IntPtr.Size,
            $"{type}: custom {customBytes} bytes, parsed {parsedBytes} bytes");
    }

    // The nullable form of a struct with a codec is read through the codec of the struct, boxed: one box of the struct
    // for each read and nothing else, so fewer than two of the smallest boxes, three pointers each, per read.
    [Theory]
    [InlineData("nullable")]
    [InlineData("try nullable")]
    public void TheNullableFormOfACustomStructCostsOneBoxPerRead(string type)
    {
        const int Reads = 16;
        Func<RouteMatchContext, int> nullableRead = type == "nullable"
            ? c => c.Path<Custom.ProductId?>("id")!.Value.Value
            : c => c.TryGetPath("id", out Custom.ProductId? id) ? id!.Value.Value : 0;
        Func<RouteMatchContext, int> plainRead = c => c.Path<Custom.ProductId>("id").Value;

        long nullableBytes = AllocatedBytesPerMatch(ReadingTable(nullableRead, Reads, Custom.Builder()), "42");
        long plainBytes = AllocatedBytesPerMatch(ReadingTable(plainRead, Reads, Custom.Builder()), "42");

        Assert.True(
            nullableBytes - plainBytes < Reads * 2 * 3 * IntPtr.Size,
            $"{type}: nullable {nullableBytes} bytes, plain {plainBytes} bytes");
    }

    // A built-in type is decided before the codecs are looked at, so codecs of the app's own cost its reads nothing.
    [Fact]
    public void ABuiltInReadAllocatesNoMoreOnATableWithCustomCodecs()
    {
        const int Reads = 16;
        Func<RouteMatchContext, int> read =
            c => c.Path<int>("id") + (c.TryGetPath("id", out long? n) ? (int)n!.Value : 0);

        long withBytes = AllocatedBytesPerMatch(ReadingTable(read, Reads, Custom.Builder()), "42");
        long withoutBytes = AllocatedBytesPerMatch(ReadingTable(read, Reads), "42");

        Assert.True(withBytes - withoutBytes < IntPtr.Size, $"with {withBytes} bytes, without {withoutBytes} bytes");
    }

    // The name of Status as the message for an enum that is not registered gives it.
    private const string StatusName = nameof(RouteMatchContextTypedPathTests) + ".Status";

    private static CultureInfo Invariant => CultureInfo.InvariantCulture;

    // A builder with every enum of these tests registered.
    private static RouteTableBuilder EnumBuilder()
    {
        return new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .AddEnumValueCodec<Access>()
            .AddEnumValueCodec<Level>()
            .AddEnumValueCodec<Offset>()
            .AddEnumValueCodec<Huge>()
            .AddEnumValueCodec<Nothing>();
    }

    // The text read as T and as T? by both methods, on a table that registers the enums: its value, or rejected
    // naming the type.
    private static void AssertEnumCase<T>(string text, T? expected, string typeName)
        where T : struct, Enum
    {
        WithinEnums("/{v}", "/" + Uri.EscapeDataString(text), context =>
        {
            if (expected is { } value)
            {
                Assert.Equal(value, context.Path<T>("v"));
                Assert.Equal(value, context.Path<T?>("v"));
                Assert.True(context.TryGetPath("v", out T read), text);
                Assert.Equal(value, read);
                Assert.True(context.TryGetPath("v", out T? nullable), text);
                Assert.Equal(value, nullable);
            }
            else
            {
                AssertInvalid(() => context.Path<T>("v"), "v", typeName);
                AssertInvalid(() => context.Path<T?>("v"), "v", typeName);
                AssertInvalid(() => context.TryGetPath("v", out T _), "v", typeName);
                AssertInvalid(() => context.TryGetPath("v", out T? _), "v", typeName);
            }
        });
    }

    private static void AssertUnregistered(Action read, string typeName, string enumName)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(read);

        Assert.Contains(PathValue, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Unsupported, exception.Message, StringComparison.Ordinal);
        Assert.Contains("not registered", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"AddEnumValueCodec<{enumName}>", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeName, exception.Message, StringComparison.Ordinal);
    }

    // The message for a type that is not built in and has no codec names it and the type to register a codec for.
    private static void AssertNoCodec(Action read, string typeName, string codecType)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(read);

        Assert.Contains(PathValue, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Unsupported, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"AddValueCodec<{codecType}>", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeName, exception.Message, StringComparison.Ordinal);
    }

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

        foreach (string text in TypedValueCorpus.Texts())
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

    // Each case read by both methods as T and as T?, which give its value or reject it naming the type.
    private static void AssertIntegerCases<T>(string typeName)
        where T : struct, IBinaryInteger<T>, IMinMaxValue<T>
    {
        foreach ((string text, T? expected) in TypedValueCorpus.IntegerCases<T>())
            AssertCase(text, expected, typeName);
    }

    private static void AssertFloatingPointCases<T>(
        string typeName,
        Func<(string Text, bool IsFloat, bool IsDouble), bool> isValid)
        where T : struct, IBinaryFloatingPointIeee754<T>
    {
        foreach ((string Text, bool IsFloat, bool IsDouble) item in TypedValueCorpus.FloatingPointCases())
        {
            T? expected = isValid(item) ? T.Parse(item.Text, Invariant) : null;
            Assert.True(expected is null || T.IsFinite(expected.Value), item.Text);
            AssertCase(item.Text, expected, typeName);
        }
    }

    private static void AssertCase<T>(string text, T? expected, string typeName)
        where T : struct
    {
        Within("/{v}", "/" + Uri.EscapeDataString(text), context =>
        {
            if (expected is { } value)
            {
                AssertSameValue(value, context.Path<T>("v"));
                AssertSameValue(value, context.Path<T?>("v"));
                Assert.True(context.TryGetPath("v", out T read), text);
                AssertSameValue(value, read);
                Assert.True(context.TryGetPath("v", out T? nullable), text);
                AssertSameValue(value, nullable);
            }
            else
            {
                AssertInvalid(() => context.Path<T>("v"), "v", typeName);
                AssertInvalid(() => context.Path<T?>("v"), "v", typeName);
                AssertInvalid(() => context.TryGetPath("v", out T _), "v", typeName);
                AssertInvalid(() => context.TryGetPath("v", out T? _), "v", typeName);
            }
        });
    }

    private static void AssertWithinTheRuleOfLong<T>()
        where T : struct, IBinaryInteger<T>, IMinMaxValue<T>
    {
        decimal min = decimal.CreateChecked(T.MinValue);
        decimal max = decimal.CreateChecked(T.MaxValue);
        var converted = 0;
        RouteTable table = new RouteTableBuilder()
            .Map("/v/{v}", c =>
            {
                string text = c.Path("v");
                decimal? number = TryConvert(c, out long asLong) ? asLong
                    : !text.Contains('.', StringComparison.Ordinal) && TryConvert(c, out decimal asDecimal) ? asDecimal
                    : null;
                bool expected = number >= min && number <= max;

                Assert.True(expected == TryConvert(c, out T value), $"{typeof(T).Name}: '{text}'");
                if (expected)
                {
                    converted++;
                    Assert.Equal(number, decimal.CreateChecked(value));
                }

                return new SettingsRoute();
            })
            .Build();

        foreach (string text in TypedValueCorpus.Texts())
            Assert.True(table.Match(new Uri("/v/" + Uri.EscapeDataString(text), UriKind.Relative)).IsSuccess, text);

        Assert.True(converted > 40, $"{typeof(T).Name}: {converted}");
    }

    private static void AssertShapeOfDecimal<T>()
        where T : struct, IBinaryFloatingPointIeee754<T>
    {
        var converted = 0;
        RouteTable table = new RouteTableBuilder()
            .Map("/v/{v}", c =>
            {
                string text = c.Path("v");
                bool expected = TryConvert(c, out decimal _);
                bool actual = TryConvert(c, out T value);

                // Beyond 10^28 only the converse holds: whatever decimal accepts, float and double accept too.
                Assert.True(
                    IsBelowTenToThe28(text) ? expected == actual : !expected || actual,
                    $"{typeof(T).Name}: '{text}'");
                if (expected)
                {
                    converted++;
                    AssertSameValue(T.Parse(text, Invariant), value);
                }

                return new SettingsRoute();
            })
            .Build();

        foreach (string text in TypedValueCorpus.Texts())
            Assert.True(table.Match(new Uri("/v/" + Uri.EscapeDataString(text), UriKind.Relative)).IsSuccess, text);

        Assert.True(converted > 40, $"{typeof(T).Name}: {converted}");
    }

    // Whether a text, if it is a number, has fewer than 29 digits before any point once its sign and leading zeros
    // are left out. Text that is not a number passes, since its shape is what is compared.
    private static bool IsBelowTenToThe28(string text)
    {
        int i = text.Length > 0 && text[0] is '+' or '-' ? 1 : 0;
        while (i < text.Length && text[i] == '0')
            i++;

        var digits = 0;
        for (; i < text.Length && char.IsAsciiDigit(text[i]); i++)
            digits++;

        return digits <= 28;
    }

    // Whether Path<T> converts the value of v; a rejection is a RouteValueException.
    private static bool TryConvert<T>(RouteMatchContext context, out T value)
    {
        try
        {
            value = context.Path<T>("v");
            return true;
        }
        catch (RouteValueException)
        {
            value = default!;
            return false;
        }
    }

    private static double ReadDouble(string text)
    {
        return ReadValue(text, c => c.Path<double>("v"));
    }

    private static float ReadFloat(string text)
    {
        return ReadValue(text, c => c.Path<float>("v"));
    }

    private static void AssertNullableAbsent<T>(RouteMatchContext context, string name)
        where T : struct
    {
        Assert.False(context.TryGetPath(name, out T? value));
        Assert.Null(value);
        AssertAbsent(() => context.Path<T?>(name));
    }

    private static void AssertNullablePresent<T>(string text, T expected)
        where T : struct
    {
        Within("/{v}", "/" + text, context =>
        {
            Assert.True(context.TryGetPath("v", out T? value));
            AssertSameValue(expected, value);
            AssertSameValue(expected, context.Path<T?>("v"));
        });
    }

    // For each text of the corpus, reads it as T and as T? by both methods, inside one create function.
    private static void AssertNullableEquivalent<T>()
        where T : struct
    {
        var converted = 0;
        RouteTable table = new RouteTableBuilder()
            .Map("/v/{v}", c =>
            {
                RouteValueException? rejected = null;
                T underlying = default;
                try
                {
                    underlying = c.Path<T>("v");
                }
                catch (RouteValueException exception)
                {
                    rejected = exception;
                }

                if (rejected is null)
                {
                    converted++;
                    Assert.True(c.TryGetPath("v", out T? value));
                    AssertSameValue(underlying, value);
                    AssertSameValue(underlying, c.Path<T?>("v"));
                }
                else
                {
                    AssertRejectedAlike(rejected, () => c.Path<T?>("v"));
                    AssertRejectedAlike(rejected, () => c.TryGetPath("v", out T? _));
                }

                return new SettingsRoute();
            })
            .Build();

        foreach (string text in TypedValueCorpus.Texts())
            table.Match(new Uri("/v/" + Uri.EscapeDataString(text), UriKind.Relative));

        Assert.True(converted > 40, $"{typeof(T).Name}: {converted}");
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

    // The same rejection: the same name and the same message, which names the underlying type.
    private static void AssertRejectedAlike(RouteValueException expected, Action read)
    {
        RouteValueException exception = Assert.Throws<RouteValueException>(read);

        Assert.Equal(expected.ValueName, exception.ValueName);
        Assert.Equal(expected.Message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    private static void AssertInvalid(Action read, string name, string? typeName = null)
    {
        RouteValueException exception = Assert.Throws<RouteValueException>(read);

        Assert.Equal(name, exception.ValueName);
        Assert.Null(exception.InnerException);
        Assert.Contains(PathValue, exception.Message, StringComparison.Ordinal);
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
        Assert.Contains("nullable", exception.Message, StringComparison.Ordinal);
        Assert.Contains("read the value as a string", exception.Message, StringComparison.Ordinal);
        Assert.Contains("any enum registered with", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeName, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertNotAnEnum(Action read, string typeName)
    {
        AssertUnsupported(read, typeName);
        Assert.DoesNotContain("not registered", Assert.Throws<InvalidOperationException>(read).Message);
    }

    // A table whose create function reads the value of id the given number of times.
    private static RouteTable ReadingTable(
        Func<RouteMatchContext, int> read,
        int reads,
        RouteTableBuilder? builder = null)
    {
        return (builder ?? new RouteTableBuilder())
            .Map("/orders/{id}", c =>
            {
                var sum = 0;
                for (var i = 0; i < reads; i++)
                    sum += read(c);

                return new ProductRoute(sum > 0 ? "p" : "n");
            })
            .Build();
    }

    // Matches the same URI each time, after enough matches for the code under test to be compiled.
    private static long AllocatedBytesPerMatch(RouteTable table, string id)
    {
        const int Calls = 50;
        var uri = new Uri("/orders/" + id, UriKind.Relative);
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

    private static void WithinCustom(string template, string uri, Action<RouteMatchContext> check)
    {
        Within(template, uri, check, Custom.Builder());
    }

    private static void WithinEnums(string template, string uri, Action<RouteMatchContext> check)
    {
        Within(template, uri, check, EnumBuilder());
    }

    // Runs the checks inside the create function, because a context is valid only during that call; an assertion
    // that fails there propagates out of Match unchanged.
    private static void Within(
        string template,
        string uri,
        Action<RouteMatchContext> check,
        RouteTableBuilder? builder = null)
    {
        var called = false;
        RouteTable table = (builder ?? new RouteTableBuilder())
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

    private class BaseValue;

    private sealed class DerivedValue : BaseValue;

    // Types nested in a generic type, for the names of unsupported types.
    private static class Outer<TOuter>
    {
        public readonly struct Inner;

        public sealed class Inner2<TInner>;
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
        Admin = 4,
    }

    private enum Level : byte
    {
        Low = 1,
        High = 255,
    }

    private enum Offset : long
    {
        Min = long.MinValue,
        Max = long.MaxValue,
    }

    // AboveLong is beyond the range of long, so a value that only ulong holds is read from its name.
    private enum Huge : ulong
    {
        Small = 1,
        AboveLong = 9223372036854775808,
        Top = ulong.MaxValue,
    }

    private enum Nothing;
}
