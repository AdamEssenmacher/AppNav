using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace AdamE.AppNav.Routing;

// A parameter constraint: a condition that a parameter's decoded segment text must satisfy. Most of this type is the
// built-in constraints. Every check is culture-invariant and applies to the text exactly as given, without trimming.
// The shape of a number is checked before its range, because the BCL parsers used for the range also accept text that
// a constraint does not, such as trailing null characters. Each constraint that names a type is its TryParse method,
// which RouteMatchContext also uses to read a value as that type, so a value converts exactly when it satisfies the
// constraint of the same name. TryParseInteger reads the integer types other than int and long, and
// TryParseFloatingPoint reads float and double, each sharing the shape check of int or decimal. Beside each parse
// method is the method that writes a value of its type as the one canonical text that it reads back to an equal value,
// and TryFormat chooses among them by type.
//
// The other constraints are the application's own, added with RouteTableBuilder.AddConstraint. Such a constraint is
// made unbound, when a template mapped on a builder or AddConstraint first names it, and bound to its function once,
// by AddConstraint; it is never bound again. Build refuses a table with a template whose constraint is still unbound,
// so a table only ever holds bound constraints, and nothing a builder does later changes what one of them accepts.
internal sealed class RouteConstraint
{
    private const NumberStyles DecimalStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    private static readonly RouteConstraint[] BuiltIn =
    [
        new("int", static value => TryParseInt(value, out _)),
        new("long", static value => TryParseLong(value, out _)),
        new("decimal", static value => TryParseDecimal(value, out _)),
        new("bool", static value => TryParseBool(value, out _)),
        new("guid", static value => TryParseGuid(value, out _), HintForGuid),
        new("alpha", IsAlpha),

        // The other number types, after the first six, so that messages list the commonest constraints first.
        new("sbyte", static value => TryParseInteger<sbyte>(value, out _)),
        new("byte", static value => TryParseInteger<byte>(value, out _)),
        new("short", static value => TryParseInteger<short>(value, out _)),
        new("ushort", static value => TryParseInteger<ushort>(value, out _)),
        new("uint", static value => TryParseInteger<uint>(value, out _)),
        new("ulong", static value => TryParseInteger<ulong>(value, out _)),
        new("float", static value => TryParseFloatingPoint<float>(value, out _)),
        new("double", static value => TryParseFloatingPoint<double>(value, out _)),
    ];

    // Names kept from the application's own constraints, because a template that named one would look like a
    // constraint of a type whose values a route reads or writes as text, which neither is: a string constraint would
    // accept every text, which is no constraint at all, and a char is deliberately not a value type of the library.
    private static readonly string[] Reserved = ["string", "char"];

    private const string StringReservedReason =
        "a string constraint would accept every text, which is no constraint at all. Leave the parameter " +
        "unconstrained";

    private const string CharReservedReason =
        "a char is not a type a route value is read as. Leave the parameter unconstrained and read the value as " +
        "a string";

    // Whenever two built-in constraints both accept some text, they both accept one of these, tried simplest first:
    // '0' is a value of every number type, 'a' letters, 'true' a bool and letters, and the zero guid a guid, which
    // overlaps only itself: a number has a hyphen only as its first character, and letters have none. So two built-in
    // constraints that accept none of them together accept no text together, which the tests check against a corpus.
    // A new built-in constraint needs a sample here for every overlap it creates, and an entry in the tests' tables of
    // overlapping pairs, disjoint pairs and own values, and values in their corpus that satisfy it. A pair with an
    // added constraint is tried against the same samples, which can only find a text in common, never rule one out,
    // and so is a pair declared disjoint, which a sample both accept proves false; that check asks about every sample.
    private static readonly string[] SampleValues = ["0", "a", "true", "00000000-0000-0000-0000-000000000000"];

    // The samples, in the order they are tried, for the check of a declared pair, which asks about each of them.
    public static ReadOnlySpan<string> Samples => SampleValues;

    private readonly Func<string, string>? _hintFor;

    // Null only for an added constraint that is not bound yet. Written once, by Bind, before any table holds it.
    private Func<string, bool>? _isSatisfiedBy;

    private RouteConstraint(
        string name,
        Func<string, bool>? isSatisfiedBy,
        Func<string, string>? hintFor = null,
        bool isBuiltIn = true)
    {
        Name = name;
        _isSatisfiedBy = isSatisfiedBy;
        _hintFor = hintFor;
        IsBuiltIn = isBuiltIn;
    }

    // The built-in names, quoted and listed in order, for the messages that reject an unknown one.
    public static string SupportedNames { get; } = ListNames();

    // The name in lowercase, as messages write it, whatever case the template used.
    public string Name { get; }

    // Whether this is one of the built-in constraints, whose overlaps with each other are known in full.
    public bool IsBuiltIn { get; }

    // Whether the constraint has its function: always for a built-in one, and for an added one once AddConstraint has
    // bound it.
    public bool IsBound => _isSatisfiedBy is not null;

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

    // Makes an added constraint that is not bound yet, for a name that IsAddableName accepts and that is neither built
    // in nor reserved. Its name is the given one
    // in lowercase, which for ASCII is the same in every culture.
    public static RouteConstraint Unbound(string name)
    {
        Debug.Assert(
            IsAddableName(name) && !IsReserved(name) && Find(name) is null,
            "Only a name an added constraint can have is made into one.");
        return new RouteConstraint(name.ToLowerInvariant(), null, isBuiltIn: false);
    }

    // Gives an added constraint its function, once.
    public void Bind(Func<string, bool> isSatisfiedBy)
    {
        Debug.Assert(!IsBuiltIn && !IsBound, "Only an added constraint is bound, and only once.");
        _isSatisfiedBy = isSatisfiedBy;
    }

    // Whether a name has the form of an added constraint's: an ASCII letter followed by ASCII letters, digits, '-' or
    // '_'. Every such name is constraint text that the template parser accepts, so the parser can leave a name of this
    // form for the builder to resolve and reject every other one at once. The built-in and reserved names also have
    // this form; Find and IsReserved tell them apart.
    public static bool IsAddableName(string name)
    {
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]))
            return false;

        for (var i = 1; i < name.Length; i++)
        {
            if (!(char.IsAsciiLetterOrDigit(name[i]) || name[i] is '-' or '_'))
                return false;
        }

        return true;
    }

    // The built-in name that a name not added was most likely meant to be, or null when it resembles none: a name that
    // differs from a built-in one by a single character added, left out or changed, or by two adjacent characters
    // swapped, as 'itn' does from 'int', or the name another system gives the same type, as 'int32' is. A name is
    // compared in lower case, as an unbound constraint's already is. The first match in the order of the built-ins
    // is given, so the hint does not change from one build to the next; 'unt', one edit from both 'int' and 'uint',
    // gives 'int'. Several built-in names are one edit from another, as 'byte' is from 'sbyte', so a built-in name
    // itself, which never reaches the messages that give the hint, gives null rather than another name.
    public static string? FindSimilarBuiltIn(string name)
    {
        if (Find(name) is not null)
            return null;

        string? alias = name switch
        {
            "int32" or "integer" => "int",
            "int64" => "long",
            "int8" => "sbyte",
            "uint8" or "ubyte" => "byte",
            "int16" => "short",
            "uint16" => "ushort",
            "uint32" => "uint",
            "uint64" => "ulong",
            "single" or "float32" => "float",
            "float64" => "double",
            "boolean" => "bool",
            "uuid" => "guid",
            _ => null,
        };
        if (alias is not null)
            return alias;

        foreach (RouteConstraint constraint in BuiltIn)
        {
            if (IsOneEditApart(name, constraint.Name))
                return constraint.Name;
        }

        return null;
    }

    // Whether two different texts are one insertion, deletion, substitution or swap of adjacent characters apart.
    private static bool IsOneEditApart(string x, string y)
    {
        if (x.Length < y.Length)
            (x, y) = (y, x);

        if (x.Length - y.Length > 1)
            return false;

        var start = 0;
        while (start < y.Length && x[start] == y[start])
            start++;

        // The texts agree up to start. One edit there leaves the rest equal: after skipping a character of the
        // longer text, after skipping one of each, or after two characters that are each other's in reverse.
        if (x.Length > y.Length)
            return x.AsSpan(start + 1).SequenceEqual(y.AsSpan(start));

        if (start == y.Length)
            return false;

        if (x.AsSpan(start + 1).SequenceEqual(y.AsSpan(start + 1)))
            return true;

        return start + 1 < y.Length && x[start] == y[start + 1] && x[start + 1] == y[start] &&
               x.AsSpan(start + 2).SequenceEqual(y.AsSpan(start + 2));
    }

    // Why a reserved name is refused and what to write instead, in the same words wherever it is refused: by the
    // template parser, AddConstraint and DeclareDisjointConstraints. It follows "The name 'string' is reserved: " and
    // has no full stop of its own.
    public static string ReservedReason(string name)
    {
        Debug.Assert(IsReserved(name), "Only a reserved name has a reason.");
        return StringComparer.OrdinalIgnoreCase.Equals(name, "string") ? StringReservedReason : CharReservedReason;
    }

    // Whether a name is one of the reserved ones, ignoring case.
    public static bool IsReserved(string name)
    {
        foreach (string reserved in Reserved)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(reserved, name))
                return true;
        }

        return false;
    }

    public bool IsSatisfiedBy(string value)
    {
        return _isSatisfiedBy!(value);
    }

    // Text to append to the message that rejects a value that does not satisfy this constraint, or an empty string.
    // It never repeats the value.
    public string HintFor(string value)
    {
        return _hintFor?.Invoke(value) ?? "";
    }

    // Says whether some text satisfies both constraints and, when one is known, returns it. Two built-in constraints
    // are disjoint exactly when they accept no sample together. A pair that involves an added constraint is never
    // disjoint here, since what such a constraint accepts cannot be known; it has a common value when both accept a
    // sample, and may overlap otherwise, even an added constraint with itself, which may accept no text at all. Only a
    // declaration of RouteTableBuilder.DeclareDisjointConstraints, which Build tests with the samples, makes such a
    // pair disjoint, and it is kept by the builder, not here, because a built-in constraint is shared by every
    // builder. The same pair gives the same answer and value in either order, so that a message naming it does not
    // depend on which template was mapped first. The samples are passed to an added constraint's function, stopping at
    // the first both accept, and an exception it throws propagates.
    public static ConstraintOverlap FindCommonValue(RouteConstraint x, RouteConstraint y, out string? value)
    {
        foreach (string sample in SampleValues)
        {
            if (x.IsSatisfiedBy(sample) && y.IsSatisfiedBy(sample))
            {
                value = sample;
                return ConstraintOverlap.CommonValue;
            }
        }

        value = null;
        return x.IsBuiltIn && y.IsBuiltIn ? ConstraintOverlap.Disjoint : ConstraintOverlap.MayOverlap;
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

    // int and long, the commonest constraints, call their parsers directly rather than through TryParseInteger: on the
    // path that matches a constraint, the generic call costs time until the JIT optimizes it.
    public static bool TryParseInt(string value, out int result)
    {
        result = 0;
        return IsSignedDigits(value) &&
               int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
    }

    public static bool TryParseLong(string value, out long result)
    {
        result = 0;
        return IsSignedDigits(value) &&
               long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
    }

    // The canonical text of every integer type: invariant digits, '-' for a negative value, and no '+' or leading
    // zeros, which the rule of int would also accept.
    public static string FormatInteger<T>(T value)
        where T : struct, IBinaryInteger<T>
    {
        return value.ToString(null, CultureInfo.InvariantCulture);
    }

    // The rule of int, within the range of T: an optional sign followed by ASCII digits, so '-0' and '+5' are also
    // values of an unsigned type. Each value-type instantiation is compiled separately, so the static calls through T
    // are direct calls and nothing is boxed.
    public static bool TryParseInteger<T>(string value, out T result)
        where T : struct, IBinaryInteger<T>
    {
        result = T.Zero;
        return IsSignedDigits(value) &&
               T.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
    }

    // Digits beyond the precision of decimal are rounded by the range check, not rejected.
    public static bool TryParseDecimal(string value, out decimal result)
    {
        result = 0;
        return IsDecimalText(value) &&
               decimal.TryParse(value, DecimalStyles, CultureInfo.InvariantCulture, out result);
    }

    // Equal decimals can differ in scale, as 1.5 and 1.50 do, so the text is normalised to have no trailing zeros
    // after the point and no point without a fraction, and every zero, whatever its sign or scale, is 0. A decimal is
    // never written with an exponent.
    public static string FormatDecimal(decimal value)
    {
        if (value == 0)
            return "0";

        string text = value.ToString(CultureInfo.InvariantCulture);
        if (!text.Contains('.', StringComparison.Ordinal))
            return text;

        text = text.TrimEnd('0');
        return text[^1] == '.' ? text[..^1] : text;
    }

    // The shape of decimal, rounded to the nearest value of T, which is float or double. With no exponent, every text
    // of that shape is a finite number, so there is no NaN or infinity to accept, but the parser gives infinity
    // rather than failing for a magnitude too large for T, which is rejected as decimal rejects one too large for it.
    // A negative zero stays negative, as the parser gives it.
    public static bool TryParseFloatingPoint<T>(string value, out T result)
        where T : struct, IBinaryFloatingPointIeee754<T>
    {
        result = T.Zero;
        return IsDecimalText(value) &&
               T.TryParse(value, DecimalStyles, CultureInfo.InvariantCulture, out result) &&
               T.IsFinite(result);
    }

    // The shortest digits that read back as the same value, which the default formatting of T gives, written out in
    // full where it would use an exponent, which the shape of decimal does not allow: 1E+21 is written with 21 zeros
    // and 1E-07 as 0.0000001. A negative zero equals zero, so it is written 0. NaN and the infinities have no text,
    // because TryParseFloatingPoint reads no text as them. Formatting T itself, not a double for a float, keeps the
    // digits the shortest for T.
    public static bool TryFormatFloatingPoint<T>(T value, [NotNullWhen(true)] out string? text)
        where T : struct, IBinaryFloatingPointIeee754<T>
    {
        if (!T.IsFinite(value))
        {
            text = null;
            return false;
        }

        text = T.IsZero(value) ? "0" : WithoutExponent(value.ToString(null, CultureInfo.InvariantCulture));
        return true;
    }

    // Moves the point of shortest round-trip text such as -1.25E-07 by its exponent. That text has one nonzero digit
    // before its point, if it has a point, and no trailing zeros after it, so the digits are only moved, never
    // changed, and zeros are added only between the point and the digits or after the digits.
    private static string WithoutExponent(string text)
    {
        int e = text.IndexOf('E', StringComparison.Ordinal);
        if (e < 0)
            return text;

        int exponent = int.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        ReadOnlySpan<char> mantissa = text.AsSpan(0, e);
        bool negative = mantissa[0] == '-';
        if (negative)
            mantissa = mantissa[1..];

        int point = mantissa.IndexOf('.');
        string digits = point < 0 ? mantissa.ToString() : string.Concat(mantissa[..point], mantissa[(point + 1)..]);
        int integerDigits = (point < 0 ? mantissa.Length : point) + exponent;

        var builder = new StringBuilder(digits.Length + Math.Abs(exponent) + 3);
        if (negative)
            builder.Append('-');

        if (integerDigits <= 0)
            builder.Append("0.").Append('0', -integerDigits).Append(digits);
        else if (integerDigits >= digits.Length)
            builder.Append(digits).Append('0', integerDigits - digits.Length);
        else
            builder.Append(digits.AsSpan(0, integerDigits)).Append('.').Append(digits.AsSpan(integerDigits));

        return builder.ToString();
    }

    public static bool TryParseBool(string value, out bool result)
    {
        result = Ascii.EqualsIgnoreCase(value, "true");
        return result || Ascii.EqualsIgnoreCase(value, "false");
    }

    // Lower case, which the read accepts as well as upper case.
    public static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    // 32 hex digits with a hyphen after the 8th, 12th, 16th and 20th, 36 characters in all. Only this form, so that a
    // guid is never a number or letters and overlaps no other constraint. The framework's "D" parser also accepts text
    // that is not of this shape, such as a '+' or a "0x" at the start of a group, and trims white space, so it is used
    // only for the value, after the shape is checked; it parses every text of this shape.
    public static bool TryParseGuid(string value, out Guid result)
    {
        result = Guid.Empty;
        if (value.Length != 36)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            bool isHyphenPosition = i is 8 or 13 or 18 or 23;
            if (isHyphenPosition ? value[i] != '-' : !char.IsAsciiHexDigit(value[i]))
                return false;
        }

        return Guid.TryParseExact(value, "D", out result);
    }

    // The hyphenated form in lower case, the only form whose shape TryParseGuid accepts, in the case Guid writes.
    public static string FormatGuid(Guid value)
    {
        return value.ToString("D");
    }

    // Writes a value of a built-in type as its canonical text, by the method beside the parse method of its type, so
    // that the typed read of the same type reads the text back to an equal value. A nullable type shares the branch of
    // its underlying type and is never null here, since a null value is left out before any text is asked for. The
    // tests of typeof(T) and the casts through object are those of RouteMatchContext.TryConvert. On CoreCLR the JIT
    // removes the casts, as measured: 0 bytes per call for a value type and for its nullable form. Another runtime
    // still writes the right text and could differ only in that allocation. Returns false only for a float or double
    // that is not finite.
    public static bool TryFormat<T>(T value, [NotNullWhen(true)] out string? text)
    {
        if (typeof(T) == typeof(string))
            text = (string)(object)value!;
        else if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
            text = FormatInteger((int)(object)value!);
        else if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
            text = FormatInteger((long)(object)value!);
        else if (typeof(T) == typeof(sbyte) || typeof(T) == typeof(sbyte?))
            text = FormatInteger((sbyte)(object)value!);
        else if (typeof(T) == typeof(byte) || typeof(T) == typeof(byte?))
            text = FormatInteger((byte)(object)value!);
        else if (typeof(T) == typeof(short) || typeof(T) == typeof(short?))
            text = FormatInteger((short)(object)value!);
        else if (typeof(T) == typeof(ushort) || typeof(T) == typeof(ushort?))
            text = FormatInteger((ushort)(object)value!);
        else if (typeof(T) == typeof(uint) || typeof(T) == typeof(uint?))
            text = FormatInteger((uint)(object)value!);
        else if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?))
            text = FormatInteger((ulong)(object)value!);
        else if (typeof(T) == typeof(decimal) || typeof(T) == typeof(decimal?))
            text = FormatDecimal((decimal)(object)value!);
        else if (typeof(T) == typeof(float) || typeof(T) == typeof(float?))
            return TryFormatFloatingPoint((float)(object)value!, out text);
        else if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
            return TryFormatFloatingPoint((double)(object)value!, out text);
        else if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
            text = FormatBool((bool)(object)value!);
        else if (typeof(T) == typeof(Guid) || typeof(T) == typeof(Guid?))
            text = FormatGuid((Guid)(object)value!);
        else
            throw new UnreachableException("TryFormat was called for a type that SupportedTypeName rejects.");

        return true;
    }

    // Ends the message for a built-in value that TryFormat cannot write, which is a float or double that is not
    // finite. Which of NaN and the infinities it is goes unsaid, as the value itself does.
    public const string NoTextReason = "no text reads back as a float or double that is NaN or infinite";

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

    // An optional sign followed by one or more ASCII digits. A plain loop, because until the JIT optimizes it the
    // vectorized span search allocates on every call, and a typed read of a path value must not.
    private static bool IsSignedDigits(string value)
    {
        int start = HasSign(value) ? 1 : 0;
        for (int i = start; i < value.Length; i++)
        {
            if (!char.IsAsciiDigit(value[i]))
                return false;
        }

        return value.Length > start;
    }

    // An optional sign followed by ASCII digits with at most one '.', and at least one digit, so '.5', '5.' and '-.5'
    // are numbers.
    private static bool IsDecimalText(string value)
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

        return digits > 0;
    }

    private static bool HasSign(string value)
    {
        return value.Length > 0 && value[0] is '+' or '-';
    }
}
