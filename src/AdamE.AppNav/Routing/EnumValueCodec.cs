using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// Reads and writes an enum by the names of its members only. Enum.TryParse is not used, because it also accepts
// numbers, which would tie links to the order of declaration, and lists of names separated by commas, and it trims
// white space. The names are read once, when the enum is registered, with the generic Enum methods, which need no
// reflection that trimming or AOT would forbid.
internal sealed class EnumValueCodec<TEnum> : ValueCodec<TEnum>
    where TEnum : struct, Enum
{
    // Compared ordinally, so that no culture's casing rules, such as Turkish dotted and dotless I, change which text
    // names which member.
    private readonly FrozenDictionary<string, TEnum> _values;

    // The name written for each value that has one. Keyed by the enum itself, so that a write boxes nothing.
    private readonly FrozenDictionary<TEnum, string> _names;

    // The enum is named by its simple name, as the built-in types are by their keywords, so that the message for an
    // enum nested in another type reads as "is not a valid Status" rather than naming every type it is declared in.
    public EnumValueCodec()
        : base(typeof(TEnum).Name)
    {
        // Both arrays come in the order of the members' values, with one entry for each member, aliases included, so
        // each name is at the index of its own value. Names of equal value may come in either order among themselves
        // and still each meet that value, since both arrays are sorted by value.
        string[] names = Enum.GetNames<TEnum>();
        TEnum[] values = Enum.GetValues<TEnum>();
        if (names.Length != values.Length)
            throw new UnreachableException("Enum.GetNames and Enum.GetValues returned arrays of different lengths.");

        var byName = new Dictionary<string, TEnum>(names.Length, StringComparer.OrdinalIgnoreCase);
        var byValue = new Dictionary<TEnum, string>(names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            // Names that differ only by case would make the text that names one of them name both, so which member it
            // read would be arbitrary.
            if (!byName.TryAdd(names[i], values[i]))
                throw NamesDifferOnlyByCase(names, i);

            // Of several names for one value, the one that sorts first ordinally is written, so that the choice is
            // the same on every runtime; which one Enum.GetName returns among them is not specified.
            if (!byValue.TryGetValue(values[i], out string? written) || string.CompareOrdinal(names[i], written) < 0)
                byValue[values[i]] = names[i];
        }

        _values = byName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _names = byValue.ToFrozenDictionary();
    }

    // Says nothing about the value, not even whether it is a combination of flags, since it can be a secret.
    public override string NoTextReason =>
        "only the value of a member of the enum can be written, as the name of that member";

    public override bool TryParse(string text, [MaybeNullWhen(false)] out TEnum value)
    {
        return _values.TryGetValue(text, out value);
    }

    // Writes the name as declared; the read ignores case, so it reads back as the same member. A value that no member
    // has, such as an undefined number or a combination of flags with no name of its own, has no text, because the
    // read accepts only names.
    public override bool TryFormat(TEnum value, [NotNullWhen(true)] out string? text)
    {
        return _names.TryGetValue(value, out text);
    }

    // Names the member at the index and the earlier one whose name differs from it only by case.
    private static ArgumentException NamesDifferOnlyByCase(string[] names, int index)
    {
        string second = names[index];
        string first = Array.Find(names, n => string.Equals(n, second, StringComparison.OrdinalIgnoreCase))!;
        return new ArgumentException(
            $"The enum type '{RouteValueTypes.DisplayName(typeof(TEnum))}' cannot be registered, because its " +
            $"members '{RouteText.EscapeForMessage(first)}' and '{RouteText.EscapeForMessage(second)}' have names " +
            "that differ only by case, and a name is read without regard to case.",
            nameof(TEnum));
    }
}
