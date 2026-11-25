using System.Globalization;
using System.Text;

namespace AdamE.AppNav.Routing;

// A built-in parameter constraint: a condition that a parameter's decoded segment text must satisfy. Every check is
// culture-invariant and applies to the text exactly as given, without trimming. The shape of a number is checked
// before its range, because the BCL parsers used for the range also accept text that a constraint does not, such as
// trailing null characters.
internal sealed class RouteConstraint
{
    private static readonly RouteConstraint[] BuiltIn =
    [
        new("int", IsInt),
        new("long", IsLong),
        new("decimal", IsDecimal),
        new("bool", IsBool),
        new("guid", IsGuid, HintForGuid),
        new("alpha", IsAlpha),
    ];

    // Whenever two built-in constraints both accept some text, they both accept one of these, tried simplest first:
    // '0' is an int, a long and a decimal, 'a' letters, 'true' a bool and letters, and the zero guid a guid, which
    // overlaps only itself: a number has a hyphen only as its first character, and letters have none. So two built-in
    // constraints that accept none of them together accept no text together, which the tests check against a corpus.
    // A new built-in constraint needs a sample here for every overlap it creates, and an entry in the tests' tables of
    // overlapping pairs, disjoint pairs and own values, and values in their corpus that satisfy it.
    private static readonly string[] SampleValues = ["0", "a", "true", "00000000-0000-0000-0000-000000000000"];

    private readonly Func<string, bool> _isSatisfiedBy;
    private readonly Func<string, string>? _hintFor;

    private RouteConstraint(string name, Func<string, bool> isSatisfiedBy, Func<string, string>? hintFor = null)
    {
        Name = name;
        _isSatisfiedBy = isSatisfiedBy;
        _hintFor = hintFor;
    }

    // The supported names, quoted and listed in order, for the message that rejects an unknown one.
    public static string SupportedNames { get; } = ListNames();

    // The name in lowercase, as messages write it, whatever case the template used.
    public string Name { get; }

    // Returns the built-in constraint with this name, ignoring case, or null when there is none. Each built-in
    // constraint is one instance, which SameTemplateComparer relies on to compare the constraints of templates by
    // reference.
    public static RouteConstraint? Find(string name)
    {
        foreach (RouteConstraint constraint in BuiltIn)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(constraint.Name, name))
                return constraint;
        }

        return null;
    }

    public bool IsSatisfiedBy(string value)
    {
        return _isSatisfiedBy(value);
    }

    // Text to append to the message that rejects a value that does not satisfy this constraint, or an empty string.
    // It never repeats the value.
    public string HintFor(string value)
    {
        return _hintFor?.Invoke(value) ?? "";
    }

    // Returns text that both constraints accept, or null when no text satisfies both. The same pair gives the same
    // value in either order, so that a message naming it does not depend on which template was mapped first.
    public static string? FindCommonValue(RouteConstraint x, RouteConstraint y)
    {
        foreach (string value in SampleValues)
        {
            if (x.IsSatisfiedBy(value) && y.IsSatisfiedBy(value))
                return value;
        }

        return null;
    }

    private static string ListNames()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < BuiltIn.Length; i++)
        {
            if (i > 0)
                builder.Append(i == BuiltIn.Length - 1 ? " and " : ", ");

            builder.Append('\'').Append(BuiltIn[i].Name).Append('\'');
        }

        return builder.ToString();
    }

    private static bool IsInt(string value)
    {
        return IsSignedDigits(value) &&
               int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsLong(string value)
    {
        return IsSignedDigits(value) &&
               long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    }

    // An optional sign followed by ASCII digits with at most one '.', and at least one digit, so '.5', '5.' and '-.5'
    // are numbers. Digits beyond the precision of decimal are rounded by the range check, not rejected.
    private static bool IsDecimal(string value)
    {
        int start = HasSign(value) ? 1 : 0;
        var digits = 0;
        var points = 0;
        for (int i = start; i < value.Length; i++)
        {
            if (char.IsAsciiDigit(value[i]))
                digits++;
            else if (value[i] != '.' || ++points > 1)
                return false;
        }

        return digits > 0 &&
               decimal.TryParse(
                   value,
                   NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture,
                   out _);
    }

    private static bool IsBool(string value)
    {
        return Ascii.EqualsIgnoreCase(value, "true") || Ascii.EqualsIgnoreCase(value, "false");
    }

    // 32 hex digits with a hyphen after the 8th, 12th, 16th and 20th, 36 characters in all. Only this form, so that a
    // guid is never a number or letters and overlaps no other constraint. Every text of this shape parses as a guid,
    // and the framework's "D" parser also accepts text that is not of it, such as a '+' or a "0x" at the start of a
    // group, and trims white space, so it is not used.
    private static bool IsGuid(string value)
    {
        if (value.Length != 36)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            bool isHyphenPosition = i is 8 or 13 or 18 or 23;
            if (isHyphenPosition ? value[i] != '-' : !char.IsAsciiHexDigit(value[i]))
                return false;
        }

        return true;
    }

    // A guid written without hyphens, as Guid.ToString("N") writes it, is the likeliest mistake, so it is named.
    private static string HintForGuid(string value)
    {
        if (value.Length != 32)
            return "";

        foreach (char c in value)
        {
            if (!char.IsAsciiHexDigit(c))
                return "";
        }

        return " A guid must have hyphens, in the 8-4-4-4-12 form that Guid.ToString() writes.";
    }

    // One or more Unicode letters, tested per scalar value so that a letter outside the Basic Multilingual Plane, which
    // takes two chars, counts. A lone surrogate enumerates as U+FFFD, which is not a letter.
    private static bool IsAlpha(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (Rune rune in value.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
                return false;
        }

        return true;
    }

    // An optional sign followed by one or more ASCII digits.
    private static bool IsSignedDigits(string value)
    {
        int start = HasSign(value) ? 1 : 0;
        return value.Length > start && !value.AsSpan(start).ContainsAnyExceptInRange('0', '9');
    }

    private static bool HasSign(string value)
    {
        return value.Length > 0 && value[0] is '+' or '-';
    }
}
