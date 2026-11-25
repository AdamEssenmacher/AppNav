namespace AdamE.AppNav.Routing;

// Which types the typed values are read and written as, and how messages name them. Both directions support the same
// types, so the list is written once here, for the messages of the reads of RouteMatchContext and of the declarations
// of RouteFormatBuilder alike.
internal static class RouteValueTypes
{
    // Written once, so that every message that lists the supported types lists the same ones; only the tail that
    // follows it differs, since a list has no nullable forms and a read and a write advise differently.
    public const string SupportedTypes =
        "string, bool, Guid, decimal, double, float, sbyte, byte, short, ushort, int, uint, long, ulong and any enum " +
        "registered with RouteTableBuilder.AddEnumValueCodec";

    public const string NullableForms = ", and the nullable forms of all but string";

    // The tests of typeof(T) are constants once the method is compiled for a value type, so each instantiation keeps
    // only its own branch, and no type is looked up by reflection, which trimming and AOT would not allow. A nullable
    // type has the name of its underlying type, because the text is what is not valid, by that type's rule.
    public static string? SupportedTypeName<T>()
    {
        if (typeof(T) == typeof(string))
            return "string";
        if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
            return "int";
        if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
            return "long";
        if (typeof(T) == typeof(sbyte) || typeof(T) == typeof(sbyte?))
            return "sbyte";
        if (typeof(T) == typeof(byte) || typeof(T) == typeof(byte?))
            return "byte";
        if (typeof(T) == typeof(short) || typeof(T) == typeof(short?))
            return "short";
        if (typeof(T) == typeof(ushort) || typeof(T) == typeof(ushort?))
            return "ushort";
        if (typeof(T) == typeof(uint) || typeof(T) == typeof(uint?))
            return "uint";
        if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?))
            return "ulong";
        if (typeof(T) == typeof(decimal) || typeof(T) == typeof(decimal?))
            return "decimal";
        if (typeof(T) == typeof(float) || typeof(T) == typeof(float?))
            return "float";
        if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
            return "double";
        if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
            return "bool";
        if (typeof(T) == typeof(Guid) || typeof(T) == typeof(Guid?))
            return "Guid";

        return null;
    }

    // The enum that T is, or that T is the nullable form of, or null for any other type.
    public static Type? EnumType<T>()
    {
        Type? enumType = typeof(T).IsEnum ? typeof(T) : Nullable.GetUnderlyingType(typeof(T));
        return enumType is { IsEnum: true } ? enumType : null;
    }

    // The subject says what cannot be done, as in "A path value cannot be read", and the tail what else is supported
    // and what to do instead. An enum, or its nullable form, is supported once registered, so for one the message says
    // only that, which is the fix.
    public static InvalidOperationException UnsupportedType<T>(string subject, string tail)
    {
        Type? enumType = EnumType<T>();
        if (enumType is not null)
        {
            return new InvalidOperationException(
                $"{subject} as type '{DisplayName(typeof(T))}', because the enum is not registered. Register it " +
                $"with RouteTableBuilder.AddEnumValueCodec<{DisplayName(enumType)}>() when building the table.");
        }

        return new InvalidOperationException(
            $"{subject} as type '{DisplayName(typeof(T))}'. The supported types are {SupportedTypes}{tail}");
    }

    // A short name for a message, as Int32? for a nullable type, List<Int32> for another generic one and Int32?[] for
    // an array, rather than the full name with its generic arity. An array of arrays keeps the order of Type.Name.
    public static string DisplayName(Type type)
    {
        Type? underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return DisplayName(underlying) + "?";
        if (type.IsArray)
        {
            string rank = type.IsSZArray ? "[]"
                : type.GetArrayRank() == 1 ? "[*]"
                : $"[{new string(',', type.GetArrayRank() - 1)}]";
            return DisplayName(type.GetElementType()!) + rank;
        }

        Type[] arguments = type.IsConstructedGenericType ? type.GenericTypeArguments : [];
        return NestedName(type, arguments, arguments.Length);
    }

    // A nested type has the generic arguments of the types it is declared in before its own, outermost first, so
    // each level shows only those after the ones of the level outside it, as Outer<Int32>.Inner<String>.
    private static string NestedName(Type type, Type[] arguments, int count)
    {
        Type? outer = type.DeclaringType;
        int start = outer is null ? 0 : Math.Min(outer.GetGenericArguments().Length, count);
        int arity = type.Name.IndexOf('`', StringComparison.Ordinal);
        string name = arity < 0 ? type.Name : type.Name[..arity];
        if (count > start)
            name += $"<{string.Join(", ", Array.ConvertAll(arguments[start..count], DisplayName))}>";

        return outer is null ? name : NestedName(outer, arguments, start) + "." + name;
    }
}
