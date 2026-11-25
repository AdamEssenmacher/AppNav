using System.Globalization;
using System.Text;
using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

public sealed class QueryStringTests
{
    [Fact]
    public void ParseReadsOnlyTheSpanItIsGiven()
    {
        Assert.Equal("a:1", Describe(QueryString.Parse("/p?a=1#b=2".AsSpan(2, 4))));
        Assert.Same(QueryString.Parse(""), QueryString.Parse("/p?a=1".AsSpan(6)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("&")]
    [InlineData("&&")]
    [InlineData("?&")]
    [InlineData("?&&&")]
    public void ParseReturnsTheSharedEmptyDictionaryWhenThereAreNoPairs(string query)
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> values = QueryString.Parse(query);

        Assert.Empty(values);
        Assert.Same(QueryString.Parse(""), values);
    }

    // Entries are written 'name:value|value;name:value', names sorted ordinally and values in the order parsed.
    [Theory]
    [InlineData("a=1", "a:1")]
    [InlineData("?a=1", "a:1")]
    [InlineData("??a=1", "?a:1")]
    [InlineData("a=1?b=2", "a:1?b=2")]
    [InlineData("a", "a:")]
    [InlineData("a=", "a:")]
    [InlineData("flag&flag=", "flag:|")]
    [InlineData("=", ":")]
    [InlineData("=x", ":x")]
    [InlineData("=x&=&a=1", ":x|;a:1")]
    [InlineData("a=b=c", "a:b=c")]
    [InlineData("a==", "a:=")]
    [InlineData("a=1&&b=2", "a:1;b:2")]
    [InlineData("&a=1&", "a:1")]
    [InlineData("?&a=1&&", "a:1")]
    [InlineData("a=1;b=2", "a:1;b=2")]
    [InlineData("a=1#frag", "a:1#frag")]
    [InlineData("a#=1", "a#:1")]
    [InlineData("a+b=c+d", "a b:c d")]
    [InlineData("a=1+2+3", "a:1 2 3")]
    [InlineData("a+b+c=1", "a b c:1")]
    [InlineData("a=%2B", "a:+")]
    [InlineData("a=a+%2541", "a:a %41")]
    [InlineData("a=%2b+%2B", "a:+ +")]
    [InlineData("a=%26&b=2", "a:&;b:2")]
    [InlineData("a%3Db=c", "a=b:c")]
    [InlineData("a=b%3Dc", "a:b=c")]
    [InlineData("a=%20+", "a:  ")]
    [InlineData("%", "%:")]
    [InlineData("a=%", "a:%")]
    [InlineData("a=%zz", "a:%zz")]
    [InlineData("a=%FF", "a:%FF")]
    [InlineData("a=%E2%82", "a:%E2%82")]
    [InlineData("a=%E2%82%AC", "a:€")]
    [InlineData("a=%25", "a:%")]
    [InlineData("a=%2525", "a:%25")]
    [InlineData("caf%C3%A9=%E2%82%AC", "café:€")]
    [InlineData("café=€", "café:€")]
    [InlineData("caf%C3%A9=1&café=2", "café:1|2")]
    [InlineData(" a = b ", " a : b ")]
    [InlineData("a=%09&b=%0A", "a:\t;b:\n")]
    [InlineData("a=\t", "a:\t")]
    public void ParseSplitsAndDecodesPairs(string query, string expected)
    {
        Assert.Equal(expected, Describe(QueryString.Parse(query)));
    }

    [Fact]
    public void ParseTreatsAnInvalidPercentSequenceAsTheTextItDecodesTo()
    {
        // '%' alone is kept as written, so the name '%' and the name '%25' are the same name.
        Assert.Equal("%:1|2", Describe(QueryString.Parse("%=1&%25=2")));
    }

    [Fact]
    public void ParseMergesNamesThatDifferByCaseUnderTheFirstSpelling()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> values = QueryString.Parse("Tab=a&tab=b&TAB=c&x=1");

        Assert.Equal(["Tab", "x"], SortedKeys(values));
        Assert.Equal(["a", "b", "c"], values["tab"]);
        Assert.Equal(["a", "b", "c"], values["TAB"]);
        Assert.True(values.ContainsKey("tAB"));
        Assert.True(values.TryGetValue("X", out IReadOnlyList<string>? x));
        Assert.Equal(["1"], x);
        Assert.False(values.ContainsKey("ta"));
    }

    [Fact]
    public void ParseComparesNamesOrdinally()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> values =
            QueryString.Parse("I=1&i=2&%C4%B0=3&%C3%89=4&%C3%A9=5");

        Assert.Equal("I:1|2;É:4|5;İ:3", Describe(values));
    }

    [Fact]
    public void ParseDoesNotMergeNamesThatOnlyACultureTreatsAsEqual()
    {
        // The Kelvin sign and 'k' are equal under culture-aware comparison ignoring case, but not ordinally.
        IReadOnlyDictionary<string, IReadOnlyList<string>> values = QueryString.Parse("%E2%84%AA=1&k=2&K=3");

        Assert.Equal("k:2|3;\u212A:1", Describe(values));
    }

    [Fact]
    public void ParseKeepsEveryValueOfAnInterleavedName()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> values = QueryString.Parse("b=1&a=2&b=&a=3&b=1&c&a=2");

        Assert.Equal("a:2|3|2;b:1||1;c:", Describe(values));
    }

    [Fact]
    public void ParseKeepsManyRepeatedValuesInOrder()
    {
        var query = new StringBuilder();
        var expected = new List<string>();
        for (var i = 0; i < 1000; i++)
        {
            string value = i % 7 == 0 ? "" : i.ToString(CultureInfo.InvariantCulture);
            query.Append(i % 2 == 0 ? "&v=" : "&V=").Append(value);
            expected.Add(value);
        }

        IReadOnlyDictionary<string, IReadOnlyList<string>> values = QueryString.Parse(query.ToString());

        Assert.Equal("v", Assert.Single(values).Key);
        Assert.Equal(expected, values["v"]);
    }

    [Fact]
    public void ParsePassesALoneSurrogateThrough()
    {
        string surrogate = ((char)0xD800).ToString();

        Assert.Equal($"a{surrogate}:{surrogate}b", Describe(QueryString.Parse($"a{surrogate}={surrogate}b")));
        Assert.Equal($"a:{surrogate} %", Describe(QueryString.Parse($"a={surrogate}+%25")));
        Assert.Equal("a:%ED%A0%80", Describe(QueryString.Parse("a=%ED%A0%80")));
    }

    [Fact]
    public void GeneratedQueriesGroupTheirEncodedPairs()
    {
        string[] names =
            ["a", "A", "b", "tab", "TAB", "", " ", "a=b", "&", "+", "%", "?", "#", "é", "É", "\U0001F600"];
        string[] values = ["", "1", " ", "a b", "=", "&", "+", "%", "%2B", "?", "#", ";", "é", "\U0001F600", "\0"];
        var random = new Random(20261129);

        for (var i = 0; i < 500; i++)
        {
            var query = new StringBuilder(random.Next(2) == 0 ? "?" : "");
            var expected = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            int pairCount = random.Next(0, 8);
            for (var pair = 0; pair < pairCount; pair++)
            {
                string name = names[random.Next(names.Length)];
                string value = values[random.Next(values.Length)];
                if (pair > 0)
                    query.Append('&', random.Next(1, 3));

                query.Append(Uri.EscapeDataString(name));
                // A pair with an empty name and no '=' would be empty, and skipped.
                if (value.Length > 0 || name.Length == 0 || random.Next(2) == 0)
                    query.Append('=').Append(Uri.EscapeDataString(value));

                // The expected key is the first spelling, because later spellings find the entry it added.
                if (!expected.TryGetValue(name, out List<string>? list))
                    expected.Add(name, list = []);

                list.Add(value);
            }

            IReadOnlyDictionary<string, IReadOnlyList<string>> parsed = QueryString.Parse(query.ToString());

            Assert.Equal(SortedKeys(expected), SortedKeys(parsed));
            foreach ((string name, List<string> expectedValues) in expected)
                Assert.Equal(expectedValues, parsed[name]);
        }
    }

    // The parser makes no promise about the order of names, so names are compared sorted.
    private static string Describe(IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        var entries = new List<string>();
        foreach (string name in SortedKeys(values))
            entries.Add(name + ":" + string.Join('|', values[name]));

        return string.Join(';', entries);
    }

    private static List<string> SortedKeys<TValue>(IReadOnlyDictionary<string, TValue> values)
    {
        var keys = new List<string>(values.Keys);
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }
}
