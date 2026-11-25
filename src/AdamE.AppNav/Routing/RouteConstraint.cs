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
        new("guid", IsGuid),
        new("alpha", IsAlpha),
    ];

    private readonly Func<string, bool> _isSatisfiedBy;

    private RouteConstraint(string name, Func<string, bool> isSatisfiedBy)
    {
        Name = name;
        _isSatisfiedBy = isSatisfiedBy;
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

    // 32 hex digits, or 36 characters with a hyphen after the 8th, 12th, 16th and 20th hex digits.
    private static bool IsGuid(string value)
    {
        if (value.Length is not (32 or 36))
            return false;

        bool hyphenated = value.Length == 36;
        for (var i = 0; i < value.Length; i++)
        {
            bool isHyphenPosition = hyphenated && i is 8 or 13 or 18 or 23;
            if (isHyphenPosition ? value[i] != '-' : !char.IsAsciiHexDigit(value[i]))
                return false;
        }

        return true;
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
