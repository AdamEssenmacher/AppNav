using System.Globalization;
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

    private static CultureInfo Invariant => CultureInfo.InvariantCulture;
}
