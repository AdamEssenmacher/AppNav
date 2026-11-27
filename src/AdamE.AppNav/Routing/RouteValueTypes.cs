namespace AdamE.AppNav.Routing;

// Which types the typed values are read and written as, and how messages name them. Both directions support the same
// types, so the list is written once here, for the messages of the reads of RouteMatchContext and of the declarations
// of RouteFormatBuilder alike.
internal static class RouteValueTypes
{
    // Written once, so that every message that lists the supported types lists the same ones; only the tail that
    // follows it differs, since a list has no nullable forms and a read and a write advise differently.
    public const string SupportedTypes =
        "string, bool, Guid, decimal, double, float, sbyte, byte, short, ushort, int, uint, long, ulong, any enum " +
        "registered with RouteTableBuilder.AddEnumValueCodec and any type registered with " +
        "RouteTableBuilder.AddValueCodec";

    // A type of the application's own can be a class, which has no nullable form, so only the value types have one.
    public const string NullableForms = ", and the nullable forms of the value types among them";

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

    // Whether T, or the type T is the nullable form of, is a collection. A string is a collection of chars but one
    // value. A collection can never be given a codec, so a declaration of one can be rejected at once.
    public static bool IsCollection<T>()
    {
        Type type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }

    // Whether a codec could be registered for T, or for the type T is the nullable form of: not a collection, which a
    // codec cannot read as one value, and not char, which a list rejects. Only then does a message advise one.
    public static bool CanHaveCodec<T>()
    {
        return !IsCollection<T>() && (Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T)) != typeof(char);
    }

    // The type to give a codec so that T can be read and written: T itself, or the type T is the nullable form of.
    public static string CodecTypeName<T>()
    {
        return DisplayName(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    // The subject says what cannot be done, as in "A path value cannot be read", and the tail what else is supported
    // and what to do instead. An enum, or its nullable form, is supported once registered, so for one the message says
    // only that, which is the fix.
    public static InvalidOperationException UnsupportedType<T>(string subject, string tail)
    {
        Type? enumType = EnumType<T>();
        if (enumType is not null)
        {
            string name = DisplayName(enumType);
            return new InvalidOperationException(
                $"{subject} as type '{DisplayName(typeof(T))}', because the enum is not registered. Register it " +
                $"with RouteTableBuilder.AddEnumValueCodec<{name}>() when building the table, or give it a codec " +
                $"of its own with RouteTableBuilder.AddValueCodec<{name}>.");
        }

        return new InvalidOperationException(
            $"{subject} as type '{DisplayName(typeof(T))}'. The supported types are {SupportedTypes}{tail}");
    }

    // Ends the message for a type that is not supported with what to do instead: register a codec for it, unless it
    // cannot have one, or do what the caller advises. The forms are what follow the list.
    public static string RegisterOr<T>(string forms, string instead)
    {
        return CanHaveCodec<T>()
            ? $"{forms}. Register a codec with RouteTableBuilder.AddValueCodec<{CodecTypeName<T>()}> when building " +
              $"the table, or {instead}"
            : $"{forms}; {instead}";
    }

    // The type's own name without the types it is declared in, as in Status for an enum nested in another type, and
    // with its own generic arguments, as in Inner<String>.
    public static string SimpleName(Type type)
    {
        Type[] arguments = type.IsConstructedGenericType ? type.GenericTypeArguments : [];
        return OwnName(type, arguments, arguments.Length);
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

    // The names for a message that names two route types, as the same-template and ambiguity errors of a table do.
    // A message that names one type can rely on what surrounds it, but the reader of one that names two must be able
    // to tell the mappings apart, so each step below is taken only while the one before it writes two different types
    // alike. The same type twice, or two types of different short names, are written by their short names. Two
    // different types of the same short name are written with their namespace. Two types of the same namespace can
    // still share it, when they differ only in generic arguments whose own short names are the same, as
    // GenericRoute<X> closed over X of two namespaces, so both are then written as Type.ToString() writes them, which
    // names every namespace involved. Two types of the same full name declared in two assemblies are still alike
    // there, so each is then followed by the simple name of its assembly. Two assemblies can share that name too, as
    // two versions of one do, and the same generic definition can be closed over arguments of two assemblies; both
    // are then written as their assembly-qualified names, which carry the version, culture and public key token of
    // every assembly involved. When even those are alike, as for one assembly loaded twice, or there are none,
    // nothing is left to tell the types apart, so the text of the step before is kept: two different route types are
    // written identically only when not even their assembly-qualified names differ.
    public static (string First, string Second) DisplayNames(Type first, Type second)
    {
        string firstName = DisplayName(first);
        string secondName = DisplayName(second);
        if (first == second || !string.Equals(firstName, secondName, StringComparison.Ordinal))
            return (firstName, secondName);

        firstName = QualifiedName(first, firstName);
        secondName = QualifiedName(second, secondName);
        if (!string.Equals(firstName, secondName, StringComparison.Ordinal))
            return (firstName, secondName);

        firstName = first.ToString();
        secondName = second.ToString();
        if (!string.Equals(firstName, secondName, StringComparison.Ordinal))
            return (firstName, secondName);

        firstName = $"{firstName}, {first.Assembly.GetName().Name}";
        secondName = $"{secondName}, {second.Assembly.GetName().Name}";
        if (!string.Equals(firstName, secondName, StringComparison.Ordinal))
            return (firstName, secondName);

        string? firstFullName = first.AssemblyQualifiedName;
        string? secondFullName = second.AssemblyQualifiedName;
        return firstFullName is not null && secondFullName is not null &&
               !string.Equals(firstFullName, secondFullName, StringComparison.Ordinal)
            ? (firstFullName, secondFullName)
            : (firstName, secondName);
    }

    // The short name after the namespace, which for a nested type is that of the outermost type it is declared in. A
    // type in the global namespace has none, so its name is left as it is.
    private static string QualifiedName(Type type, string name)
    {
        return type.Namespace is null ? name : $"{type.Namespace}.{name}";
    }

    // A nested type has the generic arguments of the types it is declared in before its own, outermost first, so
    // each level shows only those after the ones of the level outside it, as Outer<Int32>.Inner<String>.
    private static string NestedName(Type type, Type[] arguments, int count)
    {
        Type? outer = type.DeclaringType;
        string name = OwnName(type, arguments, count);
        return outer is null ? name : NestedName(outer, arguments, OuterArity(type, count)) + "." + name;
    }

    // The name of one level, with the generic arguments that are its own: those after the ones of the type outside it.
    private static string OwnName(Type type, Type[] arguments, int count)
    {
        int start = OuterArity(type, count);
        int arity = type.Name.IndexOf('`', StringComparison.Ordinal);
        string name = arity < 0 ? type.Name : type.Name[..arity];
        if (count > start)
            name += $"<{string.Join(", ", Array.ConvertAll(arguments[start..count], DisplayName))}>";

        return name;
    }

    private static int OuterArity(Type type, int count)
    {
        Type? outer = type.DeclaringType;
        return outer is null ? 0 : Math.Min(outer.GetGenericArguments().Length, count);
    }
}
