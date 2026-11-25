using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace AdamE.AppNav.Routing;

// Reads an enum by the names of its members only. Enum.TryParse is not used, because it also accepts numbers, which
// would tie links to the order of declaration, and lists of names separated by commas, and it trims white space. The
// names are read once, when the enum is registered, with the generic Enum methods, which need no reflection that
// trimming or AOT would forbid.
internal sealed class EnumValueCodec<TEnum> : ValueCodec<TEnum>
    where TEnum : struct, Enum
{
    // Compared ordinally, so that no culture's casing rules, such as Turkish dotted and dotless I, change which text
    // names which member.
    private readonly FrozenDictionary<string, TEnum> _values;

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
        for (var i = 0; i < names.Length; i++)
        {
            // Names that differ only by case would make the text that names one of them name both, so which member it
            // read would be arbitrary.
            if (!byName.TryAdd(names[i], values[i]))
                throw NamesDifferOnlyByCase(names, i);
        }

        _values = byName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public override bool TryParse(string text, [MaybeNullWhen(false)] out TEnum value)
    {
        return _values.TryGetValue(text, out value);
    }

    // Names the member at the index and the earlier one whose name differs from it only by case.
    private static ArgumentException NamesDifferOnlyByCase(string[] names, int index)
    {
        string second = names[index];
        string first = Array.Find(names, n => string.Equals(n, second, StringComparison.OrdinalIgnoreCase))!;
        return new ArgumentException(
            $"The enum type '{RouteMatchContext.DisplayName(typeof(TEnum))}' cannot be registered, because its " +
            $"members '{RouteText.EscapeForMessage(first)}' and '{RouteText.EscapeForMessage(second)}' have names " +
            "that differ only by case, and a name is read without regard to case.",
            nameof(TEnum));
    }
}
