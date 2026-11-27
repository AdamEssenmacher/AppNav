using System.Globalization;
using System.Numerics;
using AdamE.AppNav.Routing;
using Xunit;
using Custom = AdamE.AppNav.Tests.Routing.CustomValueTypes;

namespace AdamE.AppNav.Tests.Routing;

// The canonical text of each type a format function can write, as a path value and as a query value, and the round
// trips between Format and Match: a route written and matched again is equal to the one written, and text matched and
// written again is the canonical text, which writing once more does not change. A query value is checked through
// relative and absolute URIs alike.
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
        RouteTable query = QueryTable<double>();
        foreach (double value in samples)
        {
            if (!double.IsFinite(value))
                continue;

            string path = table.Format(new ValueRoute<double>(value));
            Assert.DoesNotContain('E', path);
            ValueRoute<double> read = Assert.IsType<ValueRoute<double>>(Match(table, path).Route);
            Assert.Equal(value, read.Value);

            string text = query.Format(new QueryRoute<double>(value));
            Assert.Equal("/q?v=" + path["/v/".Length..], text);
            Assert.Equal(new QueryRoute<double>(value), query.Match(new Uri("https://h" + text)).Route);
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
        RouteTable query = QueryTable<float>();
        foreach (float value in samples)
        {
            if (!float.IsFinite(value))
                continue;

            string path = table.Format(new ValueRoute<float>(value));
            string text = query.Format(new QueryRoute<float>(value));
            Assert.Equal("/q?v=" + path["/v/".Length..], text);
            Assert.Equal(new QueryRoute<float>(value), query.Match(new Uri("myapp://" + text)).Route);
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

    // The canonical text of a number, a bool or a guid always satisfies the constraint named after its type, so a
    // template with that constraint writes every value of the type, as the same text a template without it writes:
    // the edges of each range, random values, and for float and double the largest and smallest magnitudes, written
    // in full without an exponent.
    [Fact]
    public void TheTextOfAValueSatisfiesTheConstraintNamedAfterItsType()
    {
        AssertIntegersSatisfyTheirConstraint<sbyte>("sbyte");
        AssertIntegersSatisfyTheirConstraint<byte>("byte");
        AssertIntegersSatisfyTheirConstraint<short>("short");
        AssertIntegersSatisfyTheirConstraint<ushort>("ushort");
        AssertIntegersSatisfyTheirConstraint<int>("int");
        AssertIntegersSatisfyTheirConstraint<uint>("uint");
        AssertIntegersSatisfyTheirConstraint<long>("long");
        AssertIntegersSatisfyTheirConstraint<ulong>("ulong");

        var random = new Random(29);
        var floats = new List<float>
        {
            float.MaxValue, float.MinValue, float.Epsilon, -float.Epsilon, 1E21f, 1E-7f, 0.1f, -0f, 16777217f,
        };
        var doubles = new List<double>
        {
            double.MaxValue, double.MinValue, double.Epsilon, -double.Epsilon, 1E21, 1E-7, 0.1, -0.0, 1E308,
        };
        while (floats.Count < 2000)
        {
            float value = BitConverter.Int32BitsToSingle(random.Next(int.MinValue, int.MaxValue));
            if (float.IsFinite(value))
                floats.Add(value);
        }

        while (doubles.Count < 2000)
        {
            double value = BitConverter.Int64BitsToDouble(random.NextInt64(long.MinValue, long.MaxValue));
            if (double.IsFinite(value))
                doubles.Add(value);
        }

        AssertSatisfiesItsConstraint("float", floats);
        AssertSatisfiesItsConstraint("double", doubles);
        AssertSatisfiesItsConstraint(
            "decimal",
            [decimal.MaxValue, decimal.MinValue, 0.0000000000000000000000000001m, -0.5m, 1.50m, -0.0m]);
        AssertSatisfiesItsConstraint("bool", [true, false]);
        AssertSatisfiesItsConstraint(
            "guid",
            [System.Guid.Empty, new System.Guid("6F9619FF-8B86-D011-B42D-00CF4FC964FF")]);
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

    // Query names and values of any text read back exactly, through the reader's rules: '+' is a space there, so a
    // space is written %20 and '+' is written %2B, and '&', '=', '#', '?' and '%' are encoded so that they are data.
    // A name is any text, the empty name included, and a value can be empty or a dot segment, which a path value
    // cannot.
    [Fact]
    public void QueryNamesAndValuesOfAnyTextReadBackExactly()
    {
        var random = new Random(29);
        const string Alphabet =
            "aZ09 +%/?#&=.-_~;,:@!$'()*[]\\\"<>^`{|}\u00E9\u65E5\t\n\r\u0001\u007F\u00A0\u200B";
        string[] whole = ["", ".", "..", " ", "+", "&", "=", "#", "?", "%", "%20", "%2B", "a+b", "a b", "\U0001F600"];
        for (var i = 0; i < 3000; i++)
        {
            string name = i < whole.Length ? whole[i] : RandomText(random, Alphabet, random.Next(0, 8));
            string value = i < whole.Length ? whole[^(i + 1)] : RandomText(random, Alphabet, random.Next(0, 12));
            if (random.Next(10) == 0)
                value += "\U0001F600";

            RouteTable table = new RouteTableBuilder()
                .Map("/p", c => new QueryRoute<string?>(c.TryGetQuery(name, out string? v) ? v : null),
                    f => f.Query(name, r => r.Value))
                .Build();
            var route = new QueryRoute<string?>(value);
            string text = table.Format(route);

            Assert.StartsWith("/p?", text, StringComparison.Ordinal);
            Assert.DoesNotContain('+', text);
            Assert.DoesNotContain(' ', text);
            Assert.Equal(1, text.Count(c => c == '?'));
            Assert.Equal(1, text.Count(c => c == '='));
            Assert.DoesNotContain('#', text);
            Assert.DoesNotContain('&', text);
            Assert.True(text.All(char.IsAscii), text);

            IReadOnlyDictionary<string, IReadOnlyList<string>> parsed = QueryString.Parse(text.AsSpan(2));
            Assert.Equal(name, Assert.Single(parsed).Key);
            Assert.Equal(value, Assert.Single(Assert.Single(parsed).Value));

            bool dropsTrailingSpaces = value.EndsWith(' ') && HasEncodedNonAscii(text);
            foreach (string prefix in Prefixes)
            {
                var uri = prefix.Length == 0 ? new Uri(text, UriKind.Relative) : new Uri(prefix + text);
                string expected = prefix.Length > 0 && dropsTrailingSpaces ? value.TrimEnd(' ') : value;
                Assert.Equal(new QueryRoute<string?>(expected), table.Match(uri).Route);
            }
        }
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

    // A list of every element type is written and read back through GetQueryValues<T> as an equal list, in order,
    // with its repeated and empty values, between single values before and after it, under names that need encoding,
    // from a relative URI and an absolute one alike. A null list and an empty one read back as an empty list.
    [Fact]
    public void AListOfEveryTypeReadsBackEqualAndInOrder()
    {
        var random = new Random(53);
        AssertListRoundTrips(random, r => RandomText(r, ListAlphabet, r.Next(0, 6)));
        AssertListRoundTrips(random, r => r.Next(2) == 0);
        AssertListRoundTrips(random, r => new Guid(RandomBytes(r, 16)));
        AssertListRoundTrips(random, r => new decimal(r.Next(), r.Next(), r.Next(), r.Next(2) == 0, (byte)r.Next(29)));
        AssertListRoundTrips(random, r => FiniteDouble(r));
        AssertListRoundTrips(random, r => (float)FiniteDouble(r) is var f && float.IsFinite(f) ? f : 1.5f);
        AssertListRoundTrips(random, r => (sbyte)RandomBytes(r, 1)[0]);
        AssertListRoundTrips(random, r => RandomBytes(r, 1)[0]);
        AssertListRoundTrips(random, r => BitConverter.ToInt16(RandomBytes(r, 2)));
        AssertListRoundTrips(random, r => BitConverter.ToUInt16(RandomBytes(r, 2)));
        AssertListRoundTrips(random, r => BitConverter.ToInt32(RandomBytes(r, 4)));
        AssertListRoundTrips(random, r => BitConverter.ToUInt32(RandomBytes(r, 4)));
        AssertListRoundTrips(random, r => BitConverter.ToInt64(RandomBytes(r, 8)));
        AssertListRoundTrips(random, r => BitConverter.ToUInt64(RandomBytes(r, 8)));
        AssertListRoundTrips(random, r => r.Next(2) == 0 ? Status.Pending : Status.Shipped);
        AssertListRoundTrips(random, r => Access.Read | (Access)(r.Next(2) * 6));
    }

    // Two lists of one route are each read back as written, in the order of their declarations around a single value.
    [Fact]
    public void TwoListsInterleavedWithASingleValueReadBackEach()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/two",
                c => new TwoListsRoute(c.GetQueryValues<int>("a"), c.TryGetQuery("x", out string? x) ? x : null,
                    c.GetQueryValues("b")),
                f => f.QueryValues("a", r => r.A).Query("x", r => r.X).QueryValues("b", r => r.B))
            .Build();
        var route = new TwoListsRoute([3, 1, 3], "m", ["", "z", ""]);

        string text = table.Format(route);
        Assert.Equal("/two?a=3&a=1&a=3&x=m&b=&b=z&b=", text);
        foreach (string prefix in Prefixes)
        {
            var read = Assert.IsType<TwoListsRoute>(MatchText(table, prefix + text).Route);
            Assert.Equal(route.A, read.A);
            Assert.Equal(route.X, read.X);
            Assert.Equal(route.B, read.B);
        }
    }

    // A value of each custom type, its nullable form and a list of it, written and matched again from a relative URI
    // and an absolute one, reads back equal by the type's own equality, the struct with none of its own included.
    [Fact]
    public void ValuesOfCustomTypesReadBackEqual()
    {
        RouteTable table = CustomTypesTable();
        CustomTypesRoute[] routes =
        [
            new(new(42), new("a-b"), new Custom.Point { X = -1, Y = 2 }, Custom.Stage.InProgress, new Custom.Square(),
                new("a b&=+\u00E9"), new Custom.Outer<int>.Inner(9), [new(3), new(1), new(3)],
                [Custom.Stage.Done, Custom.Stage.NotStarted]),
            new(new(0), new("z"), null, null, null, null, null, [], []),
            new(new(int.MaxValue), new("0"), new Custom.Point(), Custom.Stage.NotStarted, new Custom.Circle(),
                new(""), new Custom.Outer<int>.Inner(0), [new(0)], [Custom.Stage.InProgress]),
        ];

        foreach (CustomTypesRoute route in routes)
        {
            string text = table.Format(route);
            foreach (string prefix in Prefixes)
            {
                var read = Assert.IsType<CustomTypesRoute>(MatchText(table, prefix + text).Route);
                Assert.Equal(route with { Ids = read.Ids, Stages = read.Stages }, read);
                Assert.Equal(route.Ids, read.Ids);
                Assert.Equal(route.Stages, read.Stages);
            }
        }
    }

    // Text that a codec accepts is written back as the text the codec writes for the value, which writing again does
    // not change: for a custom type, what is canonical is the codec's choice.
    [Fact]
    public void AcceptedTextOfACustomTypeIsWrittenBackAsItsCodecWritesIt()
    {
        RouteTable table = CustomTypesTable();
        var read = Assert.IsType<CustomTypesRoute>(Match(table, "/c/007/a-b/+1x-0?ids=01&ids=2").Route);

        string text = table.Format(read);

        Assert.Equal("/c/7/a-b/1x0?ids=1&ids=2", text);
        Assert.Equal(text, table.Format(Assert.IsType<CustomTypesRoute>(Match(table, text).Route)));
    }

    private static RouteTable CustomTypesTable()
    {
        return Custom.Builder()
            .Map("/c/{id}/{slug}/{point?}",
                c => new CustomTypesRoute(
                    c.Path<Custom.ProductId>("id"),
                    c.Path<Custom.Slug>("slug"),
                    c.TryGetPath("point", out Custom.Point? point) ? point : null,
                    c.TryGetQuery("stage", out Custom.Stage? stage) ? stage : null,
                    c.TryGetQuery("shape", out Custom.IShape? shape) ? shape : null,
                    c.TryGetQuery("note", out Custom.Note? note) ? note : null,
                    c.TryGetQuery("inner", out Custom.Outer<int>.Inner? inner) ? inner : null,
                    c.GetQueryValues<Custom.ProductId>("ids"),
                    c.GetQueryValues<Custom.Stage>("stages")),
                f => f
                    .Path("id", r => r.Id)
                    .Path("slug", r => r.Slug)
                    .Path("point", r => r.Point)
                    .Query("stage", r => r.Stage)
                    .Query("shape", r => r.Shape)
                    .Query("note", r => r.Note)
                    .Query("inner", r => r.Inner)
                    .QueryValues("ids", r => r.Ids)
                    .QueryValues("stages", r => r.Stages))
            .Build();
    }

    private static void AssertIntegersSatisfyTheirConstraint<T>(string constraint)
        where T : struct, IBinaryInteger<T>, IMinMaxValue<T>
    {
        var values = new List<T> { T.MinValue, T.MaxValue, T.Zero, T.One };
        var random = new Random(37);
        var bytes = new byte[16];
        for (var i = 0; i < 500; i++)
        {
            random.NextBytes(bytes);
            values.Add(T.ReadLittleEndian(bytes.AsSpan(0, T.Zero.GetByteCount()), T.MinValue == T.Zero));
        }

        AssertSatisfiesItsConstraint(constraint, values);
    }

    // Each value written by a template with the constraint gives the same text as one without it, which the
    // constraint accepts and which reads back as an equal value.
    private static void AssertSatisfiesItsConstraint<T>(string constraint, IEnumerable<T> values)
    {
        RouteConstraint rule = RouteConstraint.Find(constraint)!;
        RouteTable table = new RouteTableBuilder()
            .Map($"/v/{{v:{constraint}}}", c => new ValueRoute<T>(c.Path<T>("v")), f => f.Path("v", r => r.Value))
            .Build();

        foreach (T value in values)
        {
            string path = table.Format(new ValueRoute<T>(value));
            string text = Write(value);
            Assert.Equal("/v/" + text, path);
            Assert.True(rule.IsSatisfiedBy(Uri.UnescapeDataString(text)), $"{constraint}: {text}");
            Assert.Equal(new ValueRoute<T>(value), Match(table, path).Route);
        }
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

    // The written text is matched as a relative URI, and after a scheme with a host, with an empty host, and with an
    // empty host and an extra slash, which makes a path that starts with an empty segment.
    private static readonly string[] Prefixes = ["", "https://h", "myapp://", "myapp:///"];

    private static void AssertNullIsLeftOut<T>()
    {
        RouteTable query = QueryTable<T>();
        Assert.Equal("/q", query.Format(new QueryRoute<T>(default!)));
        Assert.Equal(new QueryRoute<T>(default!), Match(query, "/q").Route);

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
            AssertQueryCanonical<T>(text, canonical["/v/".Length..]);
        }

        return accepted;
    }

    // The text is the same in a query as in a path segment: both are the canonical text encoded with
    // Uri.EscapeDataString.
    private static void AssertWrites<T>(T value, string text)
    {
        Assert.Equal(text, Write(value));
        Assert.Equal("/q?v=" + text, QueryTable<T>().Format(new QueryRoute<T>(value)));
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
        AssertQueryRoundTrip(value);
    }

    // The value is written last in the query, so an absolute URI drops a trailing %20 from it when the URI also
    // contains a non-ASCII character, which the text then has as a percent-encoded byte from %80 to %FF. That is the
    // one change System.Uri makes, and it is asserted exactly; the relative text always reads back unchanged.
    private static void AssertQueryRoundTrip<T>(T value)
    {
        RouteTable table = QueryTable<T>();
        var route = new QueryRoute<T>(value);
        string text = table.Format(route);
        bool dropsTrailingSpaces = value is string s && s.EndsWith(' ') && HasEncodedNonAscii(text);

        foreach (string prefix in Prefixes)
        {
            RouteMatchResult result = MatchText(table, prefix + text);
            Assert.True(result.IsSuccess, prefix + text);
            QueryRoute<T> read = Assert.IsType<QueryRoute<T>>(result.Route);
            if (prefix.Length > 0 && dropsTrailingSpaces)
                Assert.Equal(((string)(object)value!).TrimEnd(' '), (string?)(object?)read.Value);
            else
                Assert.Equal(route, read);
        }

        Assert.Equal(text, table.Format(MatchText(table, text).Route!));
    }

    private static void AssertQueryCanonical<T>(string text, string canonical)
    {
        RouteTable table = QueryTable<T>();

        RouteMatchResult first = Match(table, "/q?v=" + Uri.EscapeDataString(text));
        Assert.True(first.IsSuccess, text);
        string written = table.Format(first.Route!);
        Assert.Equal("/q?v=" + canonical, written);
        Assert.Equal(first.Route, Match(table, written).Route);
    }

    private static bool HasEncodedNonAscii(string text)
    {
        for (var i = 0; i + 2 < text.Length; i++)
        {
            if (text[i] == '%' && text[i + 1] is >= '8' and <= '9' or >= 'A' and <= 'F')
                return true;
        }

        return false;
    }

    private static void AssertCanonical<T>(string text, string canonical)
    {
        AssertQueryCanonical<T>(text, canonical);

        RouteTable table = Table<T>();

        RouteMatchResult first = Match(table, "/v/" + Uri.EscapeDataString(text));
        Assert.True(first.IsSuccess, text);
        string path = table.Format(first.Route!);
        Assert.Equal("/v/" + canonical, path);

        RouteMatchResult second = Match(table, path);
        Assert.Equal(first.Route, second.Route);
        Assert.Equal(path, table.Format(second.Route!));
    }

    private static void AssertNoText<T>(RouteTable table, ValueRoute<T> route)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => table.Format(route));

        Assert.Equal("route", exception.ParamName);
        Assert.Contains(NoText, exception.Message, StringComparison.Ordinal);
        Assert.Contains("path parameter 'v'", exception.Message, StringComparison.Ordinal);

        // The same value has no text in a query either, and the message says which query value it is.
        exception = Assert.Throws<ArgumentException>(() => QueryTable<T>().Format(new QueryRoute<T>(route.Value)));
        Assert.Equal("route", exception.ParamName);
        Assert.Contains(NoText, exception.Message, StringComparison.Ordinal);
        Assert.Contains("query value 'v'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("path parameter", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    private const string ListAlphabet = "aZ09 +%/?#&=.-_~;\u00E9\u65E5\t";

    private static readonly string[] ListNames = ["v", "", "a b&=+", "\u00E9", "V%20"];

    // Lists of 0, 1, 2 and 1,000 elements, the longer ones with repeats from a few values, written between a single
    // value before and one after, and a null list, each under every name.
    private static void AssertListRoundTrips<T>(Random random, Func<Random, T> next)
    {
        foreach (string name in ListNames)
        {
            RouteTable table = ListTable<T>(name);
            foreach (int count in new[] { 0, 1, 2, 1000 })
            {
                T[] pool = Enumerable.Range(0, 4).Select(_ => next(random)).ToArray();
                var values = new T[count];
                for (var i = 0; i < count; i++)
                    values[i] = random.Next(3) == 0 ? pool[random.Next(pool.Length)] : next(random);

                AssertListRoundTrip(table, new ListRoute<T>(random.Next(2) == 0 ? null : "b", values, "z"), name);
            }

            AssertListRoundTrip(table, new ListRoute<T>("b", null, "z"), name);
        }
    }

    // The single value after the list is never null, so the list is never the end of the text, whose trailing %20 an
    // absolute URI with non-ASCII text drops.
    private static void AssertListRoundTrip<T>(RouteTable table, ListRoute<T> route, string name)
    {
        string text = table.Format(route);
        IReadOnlyList<T> expected = route.Values ?? [];
        int pairs = expected.Count + (route.Before is null ? 0 : 1) + 1;

        Assert.Equal(pairs, text.Split('&').Length);
        Assert.Equal(1, text.Count(c => c == '?'));
        Assert.EndsWith("after=z", text, StringComparison.Ordinal);
        foreach (string prefix in Prefixes)
        {
            RouteMatchResult result = MatchText(table, prefix + text);
            Assert.True(result.IsSuccess, prefix + text);
            ListRoute<T> read = Assert.IsType<ListRoute<T>>(result.Route);
            Assert.Equal(route.Before, read.Before);
            Assert.Equal(expected, read.Values);
            Assert.Equal(route.After, read.After);
            Assert.Equal(expected.Count, read.Texts.Count);
            if (typeof(T) == typeof(string))
                Assert.Equal((IEnumerable<string>)expected, read.Texts);
        }

        Assert.Equal(text, table.Format(MatchText(table, text).Route!));
        string pair = Uri.EscapeDataString(name) + "=";
        Assert.Equal(expected.Count, text[(text.IndexOf('?') + 1)..].Split('&').Count(p => p.StartsWith(pair,
            StringComparison.Ordinal) && p.IndexOf('=') == pair.Length - 1));
    }

    private static RouteTable ListTable<T>(string name)
    {
        return new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .AddEnumValueCodec<Access>()
            .Map("/l",
                c => new ListRoute<T>(
                    c.TryGetQuery("before", out string? before) ? before : null,
                    c.GetQueryValues<T>(name),
                    c.TryGetQuery("after", out string? after) ? after : null)
                {
                    Texts = c.GetQueryValues(name),
                },
                f => f.Query("before", r => r.Before).QueryValues(name, r => r.Values).Query("after", r => r.After))
            .Build();
    }

    private static byte[] RandomBytes(Random random, int count)
    {
        var bytes = new byte[count];
        random.NextBytes(bytes);
        return bytes;
    }

    private static double FiniteDouble(Random random)
    {
        double value = BitConverter.Int64BitsToDouble(random.NextInt64());
        return double.IsFinite(value) ? value : random.NextDouble();
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

    private static string RandomText(Random random, string alphabet, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = alphabet[random.Next(alphabet.Length)];

        return new string(chars);
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

    // Writes the value as the one query value of /q, and reads it with the typed read of the same type.
    private static RouteTable QueryTable<T>()
    {
        return new RouteTableBuilder()
            .AddEnumValueCodec<Status>()
            .AddEnumValueCodec<Aliased>()
            .AddEnumValueCodec<CaseSorted>()
            .AddEnumValueCodec<AlphaFirst>()
            .AddEnumValueCodec<AlphaMiddle>()
            .AddEnumValueCodec<Access>()
            .AddEnumValueCodec<Holder.Nested>()
            .Map("/q", c => new QueryRoute<T>(c.TryGetQuery("v", out T? value) ? value! : default!),
                f => f.Query("v", r => r.Value))
            .Build();
    }

    private static RouteMatchResult Match(RouteTable table, string path)
    {
        return table.Match(new Uri(path, UriKind.Relative));
    }

    // A relative URI for text with no scheme, and an absolute one otherwise.
    private static RouteMatchResult MatchText(RouteTable table, string text)
    {
        return table.Match(text.StartsWith('/') ? new Uri(text, UriKind.Relative) : new Uri(text));
    }

    private sealed record ValueRoute<T>(T Value) : AppRoute;

    private sealed record QueryRoute<T>(T Value) : AppRoute;

    private sealed record RequiredRoute<T>(T Value) : AppRoute;

    private sealed record ListRoute<T>(string? Before, IReadOnlyList<T>? Values, string? After) : AppRoute
    {
        // The values as strings, as the untyped read gives them, which the format function does not write.
        public IReadOnlyList<string> Texts { get; init; } = [];
    }

    private sealed record CustomTypesRoute(
        Custom.ProductId Id,
        Custom.Slug Slug,
        Custom.Point? Point,
        Custom.Stage? Stage,
        Custom.IShape? Shape,
        Custom.Note? Note,
        Custom.Outer<int>.Inner? Inner,
        IReadOnlyList<Custom.ProductId> Ids,
        IReadOnlyList<Custom.Stage> Stages) : AppRoute;

    private sealed record TwoListsRoute(IReadOnlyList<int> A, string? X, IReadOnlyList<string> B) : AppRoute;

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
