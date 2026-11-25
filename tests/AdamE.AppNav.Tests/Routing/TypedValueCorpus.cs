using System.Globalization;
using System.Numerics;
using System.Text;

namespace AdamE.AppNav.Tests.Routing;

// The texts against which typed path and query reads are checked, shared so that both are held to the same cases.
internal static class TypedValueCorpus
{
    // Deterministic texts of every shape the rules distinguish: numbers of every length with and without signs and
    // points, guids in each form and case, true and false in every case, letters, non-ASCII digits, white space,
    // signs in odd places and other junk. Only texts that a path segment and a query value can carry exactly.
    public static List<string> Texts()
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
            "127", "128", "-128", "-129", "255", "256", "32767", "32768", "-32768", "-32769", "65535", "65536",
            "4294967295", "4294967296", "18446744073709551615", "18446744073709551616", "-18446744073709551615",
            "NaN", "nan", "Infinity", "-Infinity", "+Infinity", "\u221E", "-\u221E", "1e-5", "1.5e5", "0x1p3",
            "1.5f", "1.5d", "1.5m", "1" + new string('0', 308), "2" + new string('0', 308), "-" + new string('9', 400),
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

    // Texts at the edges of the range of the integer type T and of the rule of int, each with the value it denotes as
    // a T, or null when it is not one. Every text is one that a path segment can carry, so the empty text, which only
    // a query value can, is not among them.
    public static List<(string Text, T? Value)> IntegerCases<T>()
        where T : struct, IBinaryInteger<T>, IMinMaxValue<T>
    {
        string min = T.MinValue.ToString(null, Invariant);
        string max = T.MaxValue.ToString(null, Invariant);
        bool signed = T.MinValue < T.Zero;
        var cases = new List<(string Text, T? Value)>
        {
            (min, T.MinValue), (max, T.MaxValue), ("+" + max, T.MaxValue), ("000" + max, T.MaxValue),
            ("+000" + max, T.MaxValue), ((BigInteger.Parse(min, Invariant) - 1).ToString(Invariant), null),
            ((BigInteger.Parse(max, Invariant) + 1).ToString(Invariant), null), (max + "0", null),
            ("0", T.Zero), ("-0", T.Zero), ("+0", T.Zero), ("-000", T.Zero), ("+5", T.CreateChecked(5)),
            ("007", T.CreateChecked(7)), ("-1", signed ? -T.One : null),
            ("-007", signed ? T.CreateChecked(-7) : null),
            (" 1", null), ("1 ", null), ("\t1", null), ("1\n", null), ("\u00A01", null), ("1\u0000", null),
            ("1,0", null), ("1_0", null), ("1.0", null), ("1.", null), (".1", null), ("1e1", null), ("0x1", null),
            ("\u0661", null), ("\u06F1", null), ("\uFF11", null), ("\u0967", null), ("\u22121", null), ("-", null),
            ("+", null), ("--1", null), ("+-1", null), ("1-", null), ("1+", null), ("a", null), ("true", null),
        };

        if (signed)
        {
            cases.Add(("-000" + min[1..], T.MinValue));
            cases.Add(("-" + max, -T.MaxValue));
        }

        return cases;
    }

    // Just above 1 + 2^-24, halfway between the floats 1 and 1 + 2^-23, so the float is 1 + 2^-23; a double holds the
    // halfway value exactly, so a parse to double and a narrowing to float would give the tie, which rounds to 1.
    public const string FloatAboveTie = "1.0000000596046447753906250000000000000000000000001";

    // Just below 1 + 3 * 2^-24, halfway between the floats 1 + 2^-23 and 1 + 2^-22, so the float is 1 + 2^-23, where
    // a parse to double and a narrowing would give the tie, which rounds to 1 + 2^-22.
    public const string FloatBelowTie = "1.0000001788139343261718749999999999999999999999999";

    // Texts that float and double accept or reject, each with whether it is a valid float and whether it is a valid
    // double. Every text is one that a path segment can carry, so '.', a dot segment, is not among them.
    public static List<(string Text, bool IsFloat, bool IsDouble)> FloatingPointCases()
    {
        string fortyDigits = "1" + new string('0', 39);
        string floatMax = new BigInteger(float.MaxValue).ToString(Invariant);
        string doubleMax = new BigInteger(double.MaxValue).ToString(Invariant);

        // Half the gap between the largest finite value and the next power of two: below it the text rounds to the
        // largest finite value, and from it, a tie that rounds to even, beyond it.
        string belowFloatTie = (new BigInteger(float.MaxValue) + BigInteger.Pow(2, 103) - 1).ToString(Invariant);
        string floatTie = (new BigInteger(float.MaxValue) + BigInteger.Pow(2, 103)).ToString(Invariant);
        string belowDoubleTie = (new BigInteger(double.MaxValue) + BigInteger.Pow(2, 970) - 1).ToString(Invariant);
        string doubleTie = (new BigInteger(double.MaxValue) + BigInteger.Pow(2, 970)).ToString(Invariant);
        const string LongFraction = "0.1000000000000000055511151231257827021181583404541015625000001";
        const string Pi = "3.14159265358979323846264338327950288419716939937510582097494459230781640628620899862803";
        return
        [
            ("0", true, true), ("-0", true, true), ("+0", true, true), ("-0.0", true, true), ("1.5", true, true),
            ("-19.95", true, true), ("+007.25", true, true), (".5", true, true), ("5.", true, true),
            ("-.5", true, true), ("+.5", true, true), ("0.1", true, true), ("16777217", true, true),
            ("9007199254740993", true, true), (LongFraction, true, true), (Pi, true, true),
            (FloatAboveTie, true, true), (FloatBelowTie, true, true), ("1" + new string('0', 29), true, true),
            (belowFloatTie, true, true), (floatTie, false, true), (belowDoubleTie, false, true),
            (doubleTie, false, false),
            ("0." + new string('0', 400) + "1", true, true), ("-0." + new string('0', 400) + "1", true, true),
            (floatMax, true, true), ("-" + floatMax, true, true), (fortyDigits, false, true),
            ("-" + fortyDigits, false, true), ("79228162514264337593543950336", true, true),
            ("1" + new string('0', 308), false, true), (doubleMax, false, true), ("-" + doubleMax, false, true),
            ("2" + new string('0', 308), false, false), ("1" + new string('0', 399), false, false),
            ("-1" + new string('0', 399), false, false), (new string('9', 400) + ".5", false, false),
            ("1e5", false, false), ("1E5", false, false), ("1e-5", false, false), ("1.5e+3", false, false),
            ("NaN", false, false), ("nan", false, false), ("Infinity", false, false), ("-Infinity", false, false),
            ("+Infinity", false, false), ("\u221E", false, false), ("-\u221E", false, false), ("0x10", false, false),
            ("0x1p3", false, false), ("1.5f", false, false), ("1.5d", false, false),
            ("-.", false, false), ("+.", false, false), ("1.2.3", false, false), ("1..2", false, false),
            ("1,000", false, false), ("1,5", false, false), ("1_000", false, false), (" 1", false, false),
            ("1 ", false, false), ("\t1.5", false, false), ("1.5\n", false, false), ("1\u0000", false, false),
            ("\u0661.5", false, false), ("\uFF11.\uFF15", false, false), ("\u22121.5", false, false),
            ("-", false, false), ("+", false, false), ("--1", false, false), ("1-", false, false),
        ];
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

    private static CultureInfo Invariant => CultureInfo.InvariantCulture;
}
