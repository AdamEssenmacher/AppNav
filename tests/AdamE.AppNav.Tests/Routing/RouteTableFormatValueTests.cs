using System.Globalization;
using System.Numerics;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// The canonical text of each type a format function can write, and the round trips between Format and Match: a route
// written and matched again is equal to the one written, and text matched and written again is the canonical text,
// which writing once more does not change.
public sealed class RouteTableFormatValueTests
{
    private const string NoText = "has a value that cannot be written:";

    [Fact]
    public void IntegersAreWrittenAsPlainInvariantDigits()
    {
        AssertWrites(0, "0");
        AssertWrites(7, "7");
        AssertWrites(-7, "-7");
        AssertIntegerEdges<sbyte>();
        AssertIntegerEdges<byte>();
        AssertIntegerEdges<short>();
        AssertIntegerEdges<ushort>();
        AssertIntegerEdges<int>();
        AssertIntegerEdges<uint>();
        AssertIntegerEdges<long>();
        AssertIntegerEdges<ulong>();
    }

    // A sign, leading zeros and a negative zero all read as the same value, which is written one way.
    [Theory]
    [InlineData("+007", "7")]
    [InlineData("-0", "0")]
    [InlineData("+0", "0")]
    [InlineData("-000", "0")]
    [InlineData("-007", "-7")]
    [InlineData("0000000000000000000000000000002147483647", "2147483647")]
    public void IntegerTextIsWrittenBackCanonically(string text, string canonical)
    {
        AssertCanonical<int>(text, canonical);
        AssertCanonical<long>(text, canonical);
        AssertCanonical<int?>(text, canonical);
        if (!canonical.StartsWith('-'))
            AssertCanonical<ulong>(text, canonical);
    }

    [Fact]
    public void BoolIsWrittenInLowerCase()
    {
        AssertWrites(true, "true");
        AssertWrites(false, "false");
        AssertCanonical<bool>("TRUE", "true");
        AssertCanonical<bool>("False", "false");
        AssertCanonical<bool?>("tRuE", "true");
    }

    public static TheoryData<decimal, string> Decimals => new()
    {
        { 0m, "0" },
        { 1.50m, "1.5" },
        { 2.0m, "2" },
        { -0.0m, "0" },
        { -0.00m, "0" },
        { 0.000m, "0" },
        { 100m, "100" },
        { 100.00m, "100" },
        { -19.950m, "-19.95" },
        { 0.0000000000000000000000000001m, "0.0000000000000000000000000001" },
        { decimal.MaxValue, "79228162514264337593543950335" },
        { decimal.MinValue, "-79228162514264337593543950335" },
        { 1E28m, "10000000000000000000000000000" },
    };

    [Theory]
    [MemberData(nameof(Decimals))]
    public void ADecimalIsWrittenNormalised(decimal value, string text)
    {
        AssertWrites(value, text);
        AssertWrites<decimal?>(value, text);
    }

    [Theory]
    [InlineData("+007.50", "7.5")]
    [InlineData("5.", "5")]
    [InlineData(".5", "0.5")]
    [InlineData("-.5", "-0.5")]
    [InlineData("-0.00", "0")]
    [InlineData("1.00000000000000000000000000001", "1")]
    [InlineData("79228162514264337593543950335.4", "79228162514264337593543950335")]
    public void DecimalTextIsWrittenBackCanonically(string text, string canonical)
    {
        AssertCanonical<decimal>(text, canonical);
    }

    // Equal decimals of different scale write the same text, which the template's escaping leaves alone.
    [Fact]
    public void EqualDecimalsWriteEqualText()
    {
        var random = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            var value = new decimal(random.Next(), random.Next(), random.Next(), random.Next(2) == 0,
                (byte)random.Next(29));
            string text = Write(value);
            Assert.DoesNotContain('E', text);
            Assert.False(text.Contains('.', StringComparison.Ordinal) && text.EndsWith('0'), text);
            AssertRoundTrip(value);

            // Scaling by a power of ten that keeps the value adds trailing zeros, when the scale allows.
            decimal scaled = value * 1.00m;
            Assert.Equal(value, scaled);
            Assert.Equal(text, Write(scaled));
        }
    }

    public static TheoryData<double, string> Doubles => new()
    {
        { 0.0, "0" },
        { -0.0, "0" },
        { 1.0, "1" },
        { -1.5, "-1.5" },
        { 0.1, "0.1" },
        { 1E21, "1000000000000000000000" },
        { 1E-7, "0.0000001" },
        { -1.25E-7, "-0.000000125" },
        { 1E15, "1000000000000000" },
        { 1E16, "10000000000000000" },
        { 1.2345678901234568E+17, "123456789012345680" },
        { 1E-5, "0.00001" },
        { 0.0001, "0.0001" },
        { double.Epsilon, "0." + new string('0', 323) + "5" },
        { double.MaxValue, "17976931348623157" + new string('0', 292) },
        { double.MinValue, "-17976931348623157" + new string('0', 292) },
    };

    [Theory]
    [MemberData(nameof(Doubles))]
    public void ADoubleIsWrittenAsItsShortestDigitsWithoutAnExponent(double value, string text)
    {
        AssertWrites(value, text);
        AssertWrites<double?>(value, text);
    }

    // A float is written by its own shortest digits, which a double's would not be: 0.1f as a double is
    // 0.100000001490116119384765625.
    public static TheoryData<float, string> Floats => new()
    {
        { 0f, "0" },
        { -0f, "0" },
        { 0.1f, "0.1" },
        { 1.1f, "1.1" },
        { 16777216f, "16777216" },
        { 1E21f, "1000000000000000000000" },
        { 1E-7f, "0.0000001" },
        { float.Epsilon, "0." + new string('0', 44) + "1" },
        { float.MaxValue, "34028235" + new string('0', 31) },
        { float.MinValue, "-34028235" + new string('0', 31) },
    };

    [Theory]
    [MemberData(nameof(Floats))]
    public void AFloatIsWrittenAsItsOwnShortestDigitsWithoutAnExponent(float value, string text)
    {
        AssertWrites(value, text);
        AssertWrites<float?>(value, text);
    }

    // Every finite double reads back as itself: random bit patterns cover every exponent, and the edges cover the
    // subnormals, the largest values and the powers of ten around which the default formatting switches to an
    // exponent.
    [Fact]
    public void EveryDoubleReadsBackAsItself()
    {
        var samples = new List<double>
        {
            double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue, 2.2250738585072014E-308,
            2.2250738585072009E-308, BitConverter.Int64BitsToDouble(1), BitConverter.Int64BitsToDouble(0xFFFFFFFFFFFFF),
            1.0 / 3, 2.0 / 3, Math.PI, Math.E, 9007199254740993, 0.30000000000000004,
        };

        for (int exponent = -324; exponent <= 308; exponent++)
        {
            double power = double.Parse(PowerOfTen(exponent), CultureInfo.InvariantCulture);
            samples.Add(power);
            samples.Add(-power);
            samples.Add(Math.BitIncrement(power));
            samples.Add(Math.BitDecrement(power));
            samples.Add(power * 1.5);
        }

        var random = new Random(11);
        for (var i = 0; i < 20_000; i++)
            samples.Add(BitConverter.Int64BitsToDouble(random.NextInt64(long.MinValue, long.MaxValue)));

        RouteTable table = Table<double>();
        foreach (double value in samples)
        {
            if (!double.IsFinite(value))
                continue;

            string path = table.Format(new ValueRoute<double>(value));
            Assert.DoesNotContain('E', path);
            ValueRoute<double> read = Assert.IsType<ValueRoute<double>>(Match(table, path).Route);
            Assert.Equal(value, read.Value);
        }
    }

    [Fact]
    public void EveryFloatReadsBackAsItself()
    {
        var samples = new List<float>
        {
            float.Epsilon, -float.Epsilon, float.MaxValue, float.MinValue, BitConverter.Int32BitsToSingle(0x7FFFFF),
            BitConverter.Int32BitsToSingle(0x800000), 1f / 3, 16777217f, 0.1f, 0.2f, 0.3f,
        };

        for (int exponent = -45; exponent <= 38; exponent++)
        {
            float power = float.Parse(PowerOfTen(exponent), CultureInfo.InvariantCulture);
            samples.Add(power);
            samples.Add(-power);
            samples.Add(MathF.BitIncrement(power));
            samples.Add(MathF.BitDecrement(power));
        }

        var random = new Random(13);
        for (var i = 0; i < 20_000; i++)
            samples.Add(BitConverter.Int32BitsToSingle(random.Next(int.MinValue, int.MaxValue)));

        RouteTable table = Table<float>();
        foreach (float value in samples)
        {
            if (!float.IsFinite(value))
                continue;

            string path = table.Format(new ValueRoute<float>(value));
            Assert.DoesNotContain('E', path);
            Assert.Equal(value, Assert.IsType<ValueRoute<float>>(Match(table, path).Route).Value);

            // The digits are the float's own shortest ones, not those of the double it widens to.
            Assert.Equal(SignificantDigits(value.ToString(CultureInfo.InvariantCulture)), SignificantDigits(path[3..]));
        }
    }

    [Theory]
    [InlineData("-0", "0")]
    [InlineData("-0.0", "0")]
    [InlineData("+007.25", "7.25")]
    [InlineData(".5", "0.5")]
    [InlineData("0.1000000000000000055511151231257827021181583404541015625000001", "0.1")]
    public void FloatingPointTextIsWrittenBackCanonically(string text, string canonical)
    {
        AssertCanonical<double>(text, canonical);
        AssertCanonical<float>(text, canonical);
        AssertCanonical<double?>(text, canonical);
    }

    [Fact]
    public void AGuidIsWrittenInLowerCaseWithHyphens()
    {
        var guid = new Guid("6F9619FF-8B86-D011-B42D-00CF4FC964FF");

        AssertWrites(guid, "6f9619ff-8b86-d011-b42d-00cf4fc964ff");
        AssertWrites<Guid?>(guid, "6f9619ff-8b86-d011-b42d-00cf4fc964ff");
        AssertWrites(Guid.Empty, "00000000-0000-0000-0000-000000000000");
        AssertCanonical<Guid>("6F9619FF-8B86-D011-B42D-00CF4FC964FF", "6f9619ff-8b86-d011-b42d-00cf4fc964ff");

        var random = new Random(17);
        var bytes = new byte[16];
        for (var i = 0; i < 200; i++)
        {
            random.NextBytes(bytes);
            AssertRoundTrip(new Guid(bytes));
        }
    }

    [Fact]
    public void AStringIsWrittenAsItIs()
    {
        AssertWrites("abc", "abc");
        AssertWrites("a b", "a%20b");
        AssertCanonical<string>("a b", "a%20b");

        // The path the text was matched from is not reproduced: the value is, encoded the one way the template writes.
        RouteTable table = Table<string>();
        Assert.Equal("/v/A%20b", table.Format(Match(table, "/v/%41 b").Route!));

        var random = new Random(19);
        const string Alphabet = "aZ09 +%/?#&=.-_~\u00E9\u65E5\t";
        for (var i = 0; i < 2000; i++)
        {
            var chars = new char[random.Next(1, 12)];
            for (var j = 0; j < chars.Length; j++)
                chars[j] = Alphabet[random.Next(Alphabet.Length)];

            string value = new(chars);
            if (value is "." or "..")
                continue;

            AssertRoundTrip(value);
        }

        AssertRoundTrip("\U0001D49C");
    }

    [Fact]
    public void AnEnumIsWrittenAsTheNameOfItsMember()
    {
        AssertWrites(Status.Shipped, "Shipped");
        AssertWrites<Status?>(Status.Pending, "Pending");
        AssertCanonical<Status>("shipped", "Shipped");
        AssertCanonical<Status>("PENDING", "Pending");
        AssertWrites(Holder.Nested.Second, "Second");
        AssertCanonical<Holder.Nested>("second", "Second");
    }

    // Of several names for one value, the one that sorts first ordinally is written, whichever name was read, so
    // upper case comes before lower case.
    [Fact]
    public void AnAliasedValueIsWrittenAsTheNameThatSortsFirstOrdinally()
    {
        AssertWrites(Aliased.Primary, "Alpha");
        AssertCanonical<Aliased>("primary", "Alpha");
        AssertCanonical<Aliased>("first", "Alpha");
        AssertWrites(Aliased.Second, "Second");
        AssertWrites(CaseSorted.alpha, "Zulu");
        AssertCanonical<CaseSorted>("ALPHA", "Zulu");
    }

    // The choice does not depend on where the name is declared: first, in the middle or last among the aliases.
    [Fact]
    public void TheAliasWrittenDoesNotDependOnTheOrderOfDeclaration()
    {
        AssertWrites(AlphaFirst.Primary, "Alpha");
        AssertCanonical<AlphaFirst>("primary", "Alpha");
        AssertWrites(AlphaMiddle.Zeta, "Alpha");
        AssertCanonical<AlphaMiddle>("mid", "Alpha");
        AssertCanonical<AlphaMiddle>("zeta", "Alpha");
    }

    [Fact]
    public void AnEnumValueThatIsNotTheValueOfAMemberIsRejected()
    {
        RouteTable access = Table<Access>();
        RouteTable status = Table<Status?>();

        Assert.Equal("/v/All", access.Format(new ValueRoute<Access>(Access.Read | Access.Write | Access.Delete)));
        Assert.Equal("/v/Write", access.Format(new ValueRoute<Access>(Access.Write)));
        AssertNoText(access, new ValueRoute<Access>(Access.Read | Access.Write));
        AssertNoText(access, new ValueRoute<Access>(0));
        AssertNoText(status, new ValueRoute<Status?>((Status)17));
        AssertNoText(Table<Status>(), new ValueRoute<Status>((Status)(-1)));
    }

    [Fact]
    public void FloatingPointValuesThatAreNotFiniteAreRejected()
    {
        AssertNoText(Table<double>(), new ValueRoute<double>(double.NaN));
        AssertNoText(Table<double?>(), new ValueRoute<double?>(double.NegativeInfinity));
        AssertNoText(Table<float>(), new ValueRoute<float>(float.PositiveInfinity));
        AssertNoText(Table<float?>(), new ValueRoute<float?>(float.NaN));
    }

    // For every supported type, text from the corpus that the typed read accepts is written back canonically, the
    // canonical text reads as the same value, and writing that value again changes nothing.
    [Fact]
    public void AcceptedTextOfEveryTypeIsWrittenBackCanonicallyAndIdempotently()
    {
        List<string> texts = TypedValueCorpus.Texts();
        texts.AddRange(["False", "shipped", "PENDING", "a b", "\u00E9", "6F9619FF-8B86-D011-B42D-00CF4FC964FF"]);

        int accepted = AssertAcceptedTextIsCanonical<string>(texts)
            + AssertAcceptedTextIsCanonical<bool>(texts) + AssertAcceptedTextIsCanonical<bool?>(texts)
            + AssertAcceptedTextIsCanonical<Guid>(texts) + AssertAcceptedTextIsCanonical<Guid?>(texts)
            + AssertAcceptedTextIsCanonical<decimal>(texts) + AssertAcceptedTextIsCanonical<decimal?>(texts)
            + AssertAcceptedTextIsCanonical<double>(texts) + AssertAcceptedTextIsCanonical<double?>(texts)
            + AssertAcceptedTextIsCanonical<float>(texts) + AssertAcceptedTextIsCanonical<float?>(texts)
            + AssertAcceptedTextIsCanonical<sbyte>(texts) + AssertAcceptedTextIsCanonical<sbyte?>(texts)
            + AssertAcceptedTextIsCanonical<byte>(texts) + AssertAcceptedTextIsCanonical<byte?>(texts)
            + AssertAcceptedTextIsCanonical<short>(texts) + AssertAcceptedTextIsCanonical<short?>(texts)
            + AssertAcceptedTextIsCanonical<ushort>(texts) + AssertAcceptedTextIsCanonical<ushort?>(texts)
            + AssertAcceptedTextIsCanonical<int>(texts) + AssertAcceptedTextIsCanonical<int?>(texts)
            + AssertAcceptedTextIsCanonical<uint>(texts) + AssertAcceptedTextIsCanonical<uint?>(texts)
            + AssertAcceptedTextIsCanonical<long>(texts) + AssertAcceptedTextIsCanonical<long?>(texts)
            + AssertAcceptedTextIsCanonical<ulong>(texts) + AssertAcceptedTextIsCanonical<ulong?>(texts)
            + AssertAcceptedTextIsCanonical<Status>(texts) + AssertAcceptedTextIsCanonical<Status?>(texts);

        Assert.True(accepted > 3000, accepted.ToString(CultureInfo.InvariantCulture));
    }

    // A null value of a nullable type leaves an optional parameter out and is an error for a required one.
    [Fact]
    public void NullOfEveryNullableTypeIsLeftOutOfAnOptionalParameter()
    {
        AssertNullIsLeftOut<string>();
        AssertNullIsLeftOut<bool?>();
        AssertNullIsLeftOut<Guid?>();
        AssertNullIsLeftOut<decimal?>();
        AssertNullIsLeftOut<double?>();
        AssertNullIsLeftOut<float?>();
        AssertNullIsLeftOut<sbyte?>();
        AssertNullIsLeftOut<byte?>();
        AssertNullIsLeftOut<short?>();
        AssertNullIsLeftOut<ushort?>();
        AssertNullIsLeftOut<int?>();
        AssertNullIsLeftOut<uint?>();
        AssertNullIsLeftOut<long?>();
        AssertNullIsLeftOut<ulong?>();
        AssertNullIsLeftOut<Status?>();
    }

    private static void AssertIntegerEdges<T>()
        where T : struct, IBinaryInteger<T>, IMinMaxValue<T>
    {
        AssertWrites(T.MinValue, T.MinValue.ToString(null, CultureInfo.InvariantCulture));
        AssertWrites(T.MaxValue, T.MaxValue.ToString(null, CultureInfo.InvariantCulture));
        AssertWrites<T?>(T.MaxValue, T.MaxValue.ToString(null, CultureInfo.InvariantCulture));
        AssertWrites(T.Zero, "0");

        var random = new Random(23);
        var bytes = new byte[16];
        for (var i = 0; i < 500; i++)
        {
            random.NextBytes(bytes);
            T value = T.ReadLittleEndian(bytes.AsSpan(0, T.Zero.GetByteCount()), T.MinValue == T.Zero);
            string text = Write(value);
            Assert.DoesNotContain('+', text);
            Assert.True(text == "0" || !text.TrimStart('-').StartsWith('0'), text);
            AssertRoundTrip(value);
        }
    }

    private static void AssertNullIsLeftOut<T>()
    {
        RouteTable table = new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .Map("/o/{v?}", c => new ValueRoute<T>(c.TryGetPath("v", out T? value) ? value! : default!),
                f => f.Path("v", r => r.Value))
            .Map("/r/{v}", c => new RequiredRoute<T>(c.Path<T>("v")), f => f.Path("v", r => r.Value))
            .Build();

        Assert.Equal("/o", table.Format(new ValueRoute<T>(default!)));
        Assert.Equal(new ValueRoute<T>(default!), Match(table, "/o").Route);

        ArgumentException exception =
            Assert.Throws<ArgumentException>(() => table.Format(new RequiredRoute<T>(default!)));
        Assert.Equal("route", exception.ParamName);
        Assert.Contains("is required", exception.Message, StringComparison.Ordinal);
    }

    // Returns how many texts the type accepted.
    private static int AssertAcceptedTextIsCanonical<T>(List<string> texts)
    {
        RouteTable table = Table<T>();
        var accepted = 0;
        foreach (string text in texts)
        {
            if (text.Length == 0 || text is "." or "..")
                continue;

            RouteMatchResult first = Match(table, "/v/" + Uri.EscapeDataString(text));
            if (!first.IsSuccess)
                continue;

            accepted++;
            string canonical = table.Format(first.Route!);
            RouteMatchResult second = Match(table, canonical);
            Assert.Equal(first.Route, second.Route);
            Assert.Equal(canonical, table.Format(second.Route!));
        }

        return accepted;
    }

    private static void AssertWrites<T>(T value, string text)
    {
        Assert.Equal(text, Write(value));
        AssertRoundTrip(value);
    }

    private static string Write<T>(T value)
    {
        string path = Table<T>().Format(new ValueRoute<T>(value));
        Assert.StartsWith("/v/", path, StringComparison.Ordinal);
        return path["/v/".Length..];
    }

    private static void AssertRoundTrip<T>(T value)
    {
        RouteTable table = Table<T>();
        var route = new ValueRoute<T>(value);

        string path = table.Format(route);
        RouteMatchResult result = Match(table, path);

        Assert.True(result.IsSuccess, path);
        Assert.Equal(route, result.Route);
        Assert.Equal(path, table.Format(result.Route!));
    }

    private static void AssertCanonical<T>(string text, string canonical)
    {
        RouteTable table = Table<T>();

        RouteMatchResult first = Match(table, "/v/" + Uri.EscapeDataString(text));
        Assert.True(first.IsSuccess, text);
        string path = table.Format(first.Route!);
        Assert.Equal("/v/" + canonical, path);

        RouteMatchResult second = Match(table, path);
        Assert.Equal(first.Route, second.Route);
        Assert.Equal(path, table.Format(second.Route!));
    }

    private static void AssertNoText(RouteTable table, AppRoute route)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => table.Format(route));

        Assert.Equal("route", exception.ParamName);
        Assert.Contains(NoText, exception.Message, StringComparison.Ordinal);
    }

    // The digits of a number's text without its sign, point, exponent, or leading and trailing zeros, which moving
    // the point by the exponent adds.
    private static string SignificantDigits(string text)
    {
        int exponent = text.IndexOf('E', StringComparison.Ordinal);
        string mantissa = exponent < 0 ? text : text[..exponent];
        return mantissa.Replace("-", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal)
            .Trim('0');
    }

    private static string PowerOfTen(int exponent)
    {
        return "1E" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    private static RouteTable Table<T>()
    {
        return new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .AddEnumValueCodec<Aliased>()
            .AddEnumValueCodec<CaseSorted>()
            .AddEnumValueCodec<AlphaFirst>()
            .AddEnumValueCodec<AlphaMiddle>()
            .AddEnumValueCodec<Access>()
            .AddEnumValueCodec<Holder.Nested>()
            .Map("/v/{v}", c => new ValueRoute<T>(c.Path<T>("v")), f => f.Path("v", r => r.Value))
            .Build();
    }

    private static RouteMatchResult Match(RouteTable table, string path)
    {
        return table.Match(new Uri(path, UriKind.Relative));
    }

    private sealed record ValueRoute<T>(T Value) : AppRoute;

    private sealed record RequiredRoute<T>(T Value) : AppRoute;

    private enum Status
    {
        Pending,
        Shipped,
    }

    private enum Aliased
    {
        Primary = 1,
        First = 1,
        Alpha = 1,
        Second = 2,
    }

    // Upper case sorts before lower case ordinally, so Zulu comes before alpha.
    private enum CaseSorted
    {
        alpha = 1,
        Zulu = 1,
    }

    // The name written is declared first here, and in the middle of three in AlphaMiddle.
    private enum AlphaFirst
    {
        Alpha = 1,
        Primary = 1,
    }

    private enum AlphaMiddle
    {
        Zeta = 1,
        Alpha = 1,
        Mid = 1,
    }

    [Flags]
    private enum Access
    {
        Read = 1,
        Write = 2,
        Delete = 4,
        All = Read | Write | Delete,
    }

    private static class Holder
    {
        public enum Nested
        {
            First,
            Second,
        }
    }
}
