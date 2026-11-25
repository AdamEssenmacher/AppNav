using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using static AdamE.AppNav.Routing.RouteValueTypes;

namespace AdamE.AppNav.Routing;

/// <summary>
/// What a route's create function receives when its template matches a URI: the URI, the values the template
/// captured from its path, and the values of its query.
/// </summary>
/// <remarks>
/// A context is valid only during the call to the create function that receives it. Do not store it; build the route
/// from it and let it go.
/// </remarks>
public sealed class RouteMatchContext
{
    // Where the route table found the query, -1 for Uri.Query, and its values once a create function first asks for
    // one, so that a match whose create function never asks pays nothing for the query.
    private readonly int _queryStart;
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? _queryValues;

    // The codecs of the table that matched, each a ValueCodec<T> kept under typeof(T).
    private readonly FrozenDictionary<Type, object> _codecs;

    internal RouteMatchContext(
        Uri sourceUri,
        IReadOnlyDictionary<string, string> pathValues,
        int queryStart,
        FrozenDictionary<Type, object> codecs)
    {
        SourceUri = sourceUri;
        PathValues = pathValues;
        _queryStart = queryStart;
        _codecs = codecs;
    }

    /// <summary>
    /// Gets the URI that was passed to <see cref="RouteTable.Match(Uri)"/>.
    /// </summary>
    /// <remarks>
    /// A route carries application identity only, so build it from the captured values rather than from parts of the
    /// URI that the table does not read, such as its host.
    /// </remarks>
    public Uri SourceUri { get; }

    /// <summary>
    /// Gets the values the template's parameters captured from the path, as returned by
    /// <see cref="RouteTemplate.Match(string)"/>: keyed by parameter name as written in the template and looked up
    /// without regard to case, each the decoded path text. An optional parameter that the path leaves out has no
    /// entry, and neither has a catch-all parameter when nothing is left of the path. Treat it as read-only.
    /// </summary>
    public IReadOnlyDictionary<string, string> PathValues { get; }

    /// <summary>
    /// Gets the value a path parameter captured.
    /// </summary>
    /// <param name="name">The parameter name, compared without regard to case.</param>
    /// <returns>The captured value, decoded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">
    /// The template has no parameter named <paramref name="name"/>, or it is an optional or catch-all parameter that
    /// the path left out. Use <see cref="TryGetPath(string, out string)"/> for a parameter that can be left out.
    /// </exception>
    public string Path(string name)
    {
        return TryGetPath(name, out string? value) ? value : throw NoPathValue(name);
    }

    /// <summary>
    /// Gets the value a path parameter captured, converted to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The type to convert to: <see cref="string"/>, <see cref="bool"/>, <see cref="Guid"/>, <see cref="decimal"/>,
    /// <see cref="double"/>, <see cref="float"/>, one of the integer types <see cref="sbyte"/>, <see cref="byte"/>,
    /// <see cref="short"/>, <see cref="ushort"/>, <see cref="int"/>, <see cref="uint"/>, <see cref="long"/> and
    /// <see cref="ulong"/>, an enum registered with <see cref="RouteTableBuilder.AddEnumValueCodec{TEnum}"/> on the
    /// builder of the table, or the nullable form of any of them but <see cref="string"/>, such as <c>int?</c>. A
    /// value converts to a type a constraint is named after, such as <see cref="int"/>, exactly when it satisfies that
    /// constraint, such as <c>{id:int}</c>, so a value that the constraint of the same name accepted in the template
    /// always converts. A different constraint can let through a value that does not convert, such as
    /// <c>3000000000</c> for <c>{id:long}</c> read as <see cref="int"/>. The other integer types follow the rule of
    /// <see cref="int"/> within their own range. <see cref="double"/> and <see cref="float"/> accept text of the
    /// same shape as <see cref="decimal"/>, not limited to the range of <see cref="decimal"/>, and read the nearest
    /// value of the type; a value that, after rounding, is beyond the largest finite value of the type does not
    /// convert, rather than being read as infinity. No constraint is named after those types. An enum converts from
    /// the name of one of its members only, compared ordinally without regard to case, as
    /// <see cref="RouteTableBuilder.AddEnumValueCodec{TEnum}"/> describes. A nullable type converts by the rule of
    /// its underlying type, to the same value. A <see cref="string"/> is the captured value unchanged.
    /// </typeparam>
    /// <param name="name">The parameter name, compared without regard to case.</param>
    /// <returns>
    /// The captured value, decoded and converted. It is never <see langword="null"/>, even for a nullable
    /// <typeparamref name="T"/>, which reads a value that must be present, as its underlying type does.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not one of the supported types, such as <see cref="Half"/>, <c>DateTime?</c> or
    /// an enum that is not registered. This is thrown whether or not the path has a value, before the name is checked,
    /// and propagates out of <see cref="RouteTable.Match(Uri)"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">
    /// The template has no parameter named <paramref name="name"/>, or it is an optional or catch-all parameter that
    /// the path left out, whether or not <typeparamref name="T"/> is nullable. Use
    /// <see cref="TryGetPath{T}(string, out T)"/> for a parameter that can be left out.
    /// </exception>
    /// <exception cref="RouteValueException">
    /// The path has a value that does not convert to <typeparamref name="T"/>. Thrown from the create function,
    /// it makes <see cref="RouteTable.Match(Uri)"/> return a failed result with one
    /// <see cref="RouteDiagnosticCodes.ValueInvalid"/> diagnostic, and no later template is tried. Its message
    /// names the parameter and the type but never quotes the value.
    /// </exception>
    public T Path<T>(string name)
    {
        // A true result means the value was set, which the compiler cannot see for an unconstrained T.
        return TryGetPath(name, out T? value) ? value! : throw NoPathValue(name);
    }

    /// <summary>
    /// Gets the value a path parameter captured, if the matched path has one.
    /// </summary>
    /// <param name="name">The parameter name, compared without regard to case.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, the captured value, decoded; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the matched path has a value for the parameter; <see langword="false"/> when the
    /// template has no parameter named <paramref name="name"/>, or it is an optional or catch-all parameter that the
    /// path left out.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetPath(string name, [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        return PathValues.TryGetValue(name, out value);
    }

    /// <summary>
    /// Gets the value a path parameter captured, converted to <typeparamref name="T"/>, if the matched path has one.
    /// A value that is present but does not convert throws <see cref="RouteValueException"/>; this method never
    /// returns <see langword="false"/> for one.
    /// </summary>
    /// <typeparam name="T">
    /// The type to convert to, one of those listed for <see cref="Path{T}(string)"/>, by the same rules. A nullable
    /// type, such as <c>int?</c>, reads an optional parameter into a nullable variable, as in
    /// <c>TryGetPath("page", out int? page)</c>.
    /// </typeparam>
    /// <param name="name">The parameter name, compared without regard to case.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, the captured value, decoded and converted; otherwise the
    /// default value of <typeparamref name="T"/>, which is <see langword="null"/> for a nullable type.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the matched path has a value for the parameter, which is then converted;
    /// <see langword="false"/> when it has none: the template has no parameter named <paramref name="name"/>, or it
    /// is an optional or catch-all parameter that the path left out.
    /// </returns>
    /// <remarks>
    /// Unlike most <c>Try</c> methods, this one does not return <see langword="false"/> for a value that does not
    /// convert: a value that is missing and a value that is not valid are different, and treating a bad value as a
    /// missing one would hide a bad link. For a lenient read, use <see cref="TryGetPath(string, out string)"/> and
    /// convert the text yourself.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not one of the supported types, such as an enum that is not registered. This is
    /// thrown whether or not the path has a value, before the name is checked, and propagates out of
    /// <see cref="RouteTable.Match(Uri)"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="RouteValueException">
    /// The path has a value that does not convert to <typeparamref name="T"/>. Thrown from the create function,
    /// it makes <see cref="RouteTable.Match(Uri)"/> return a failed result with one
    /// <see cref="RouteDiagnosticCodes.ValueInvalid"/> diagnostic, and no later template is tried. Its message
    /// names the parameter and the type but never quotes the value.
    /// </exception>
    public bool TryGetPath<T>(string name, [MaybeNullWhen(false)] out T value)
    {
        // Checked before the name and the value, so that using a type that can never work fails on every path, not
        // only on one that has the value.
        ValueCodec<T>? codec = FindCodec<T>("A path value cannot be read", ValueTypesTail, out string typeName);

        if (!TryGetPath(name, out string? text))
        {
            value = default;
            return false;
        }

        return TryConvert(text, codec, out value) ? true : throw NotValid(name, "The path value of", typeName);
    }

    /// <summary>
    /// Gets the first value the query of the URI gives for a name, if it gives any.
    /// </summary>
    /// <param name="name">The name, compared ordinally without regard to case; the empty name is a valid name.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, the first value given for <paramref name="name"/>, decoded,
    /// which is empty for a name written without a value, as in <c>?flag</c> or <c>?flag=</c>; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the query has the name; <see langword="false"/> when it does not, or there is no
    /// query.
    /// </returns>
    /// <remarks>
    /// The query is <see cref="Uri.Query"/> as <see cref="Uri"/> reports it for an absolute URI, and the original
    /// text for a relative one or for a file URI that the table reads as relative, as described for
    /// <see cref="RouteTable.Match(Uri)"/>. It is split into pairs on <c>&amp;</c> and decoded as form data, so
    /// <c>+</c> is a space. Use <see cref="GetQueryValues(string)"/> for every value of a name that can repeat.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetQuery(string name, [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (QueryValues.TryGetValue(name, out IReadOnlyList<string>? values))
        {
            value = values[0];
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Gets every value the query of the URI gives for a name, in the order they appear.
    /// </summary>
    /// <param name="name">The name, compared ordinally without regard to case; the empty name is a valid name.</param>
    /// <returns>
    /// The values, decoded, including repeated and empty ones; an empty list when the query does not have the name, or
    /// there is no query. The list cannot be changed.
    /// </returns>
    /// <remarks>The query is read and decoded as described for <see cref="TryGetQuery(string, out string)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<string> GetQueryValues(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!QueryValues.TryGetValue(name, out IReadOnlyList<string>? values))
            return ReadOnlyCollection<string>.Empty;

        // The parsed lists are arrays or lists that later calls share, so each call wraps its list rather than letting
        // a cast change what the next call returns. Any other list would be a change to QueryString.Parse that this
        // must not hand out unwrapped.
        return values switch
        {
            string[] array => Array.AsReadOnly(array),
            List<string> list => list.AsReadOnly(),
            _ => throw new UnreachableException("QueryString.Parse returned a list of an unexpected type."),
        };
    }

    /// <summary>
    /// Gets the first value the query of the URI gives for a name, converted to <typeparamref name="T"/>, if the
    /// query gives any. A value that is present but does not convert throws <see cref="RouteValueException"/>; this
    /// method never returns <see langword="false"/> for one.
    /// </summary>
    /// <typeparam name="T">
    /// The type to convert to, one of those listed for <see cref="Path{T}(string)"/>, by the same rules, so a value
    /// converts to a type a constraint is named after, such as <see cref="int"/>, exactly when it satisfies that
    /// constraint, and a <see cref="string"/> is the value unchanged. A nullable type, such as <c>int?</c>, reads a
    /// value that may be absent into a nullable variable, as in <c>TryGetQuery("page", out int? page)</c>. An enum
    /// must be registered with <see cref="RouteTableBuilder.AddEnumValueCodec{TEnum}"/>, as for a path value.
    /// </typeparam>
    /// <param name="name">The name, compared ordinally without regard to case; the empty name is a valid name.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, the first value given for <paramref name="name"/>, decoded
    /// and converted; otherwise the default value of <typeparamref name="T"/>, which is <see langword="null"/> for a
    /// nullable type.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the query has the name, whose first value is then converted;
    /// <see langword="false"/> when it does not, or there is no query.
    /// </returns>
    /// <remarks>
    /// The query is read and decoded as described for <see cref="TryGetQuery(string, out string)"/>, so <c>+</c> is
    /// a space: <c>?n=+5</c> gives a space followed by <c>5</c>, which is not a valid <see cref="int"/>, and
    /// <c>?n=%2B5</c> gives <c>+5</c>, which is. A name written without a value, as in <c>?page</c> or
    /// <c>?page=</c>, has the empty value, which is present and converts only to <see cref="string"/>. Only the first
    /// value counts: <c>?page=x&amp;page=2</c> read as <see cref="int"/> is not valid, although a later value would
    /// be. For a lenient read, use <see cref="TryGetQuery(string, out string)"/> and convert the text yourself.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not one of the supported types, such as an enum that is not registered. This is
    /// thrown whether or not the query has the name, before the name is checked, and propagates out of
    /// <see cref="RouteTable.Match(Uri)"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="RouteValueException">
    /// The first value of <paramref name="name"/> does not convert to <typeparamref name="T"/>. Thrown from the
    /// create function, it makes <see cref="RouteTable.Match(Uri)"/> return a failed result with one
    /// <see cref="RouteDiagnosticCodes.ValueInvalid"/> diagnostic, and no later template is tried. Its message names
    /// the query value and the type but never quotes the value.
    /// </exception>
    public bool TryGetQuery<T>(string name, [MaybeNullWhen(false)] out T value)
    {
        // Checked first, as for a path value, so that a type that can never work fails on every URI.
        ValueCodec<T>? codec = FindCodec<T>("A query value cannot be read", ValueTypesTail, out string typeName);

        if (!TryGetQuery(name, out string? text))
        {
            value = default;
            return false;
        }

        return TryConvert(text, codec, out value) ? true : throw NotValid(name, "The query value of", typeName);
    }

    /// <summary>
    /// Gets every value the query of the URI gives for a name, in the order they appear, each converted to
    /// <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The type to convert to: <see cref="string"/>, <see cref="bool"/>, <see cref="Guid"/>, <see cref="decimal"/>,
    /// <see cref="double"/>, <see cref="float"/>, one of the integer types <see cref="sbyte"/>, <see cref="byte"/>,
    /// <see cref="short"/>, <see cref="ushort"/>, <see cref="int"/>, <see cref="uint"/>, <see cref="long"/> and
    /// <see cref="ulong"/>, or an enum registered with <see cref="RouteTableBuilder.AddEnumValueCodec{TEnum}"/>, by
    /// the rules described for <see cref="Path{T}(string)"/>, so a value converts to a type a constraint is named
    /// after, such as <see cref="int"/>, exactly when it satisfies that constraint, and a <see cref="string"/> is the
    /// value unchanged. A nullable type is not supported, since an element of the list is never absent, and that
    /// includes the nullable form of an enum, registered or not.
    /// </typeparam>
    /// <param name="name">The name, compared ordinally without regard to case; the empty name is a valid name.</param>
    /// <returns>
    /// The values, decoded and converted, including repeated ones; an empty list when the query does not have the
    /// name, or there is no query. The list cannot be changed. For <see cref="string"/>, the values are those of
    /// <see cref="GetQueryValues(string)"/>.
    /// </returns>
    /// <remarks>
    /// The query is read and decoded as described for <see cref="TryGetQuery(string, out string)"/>. Either every
    /// value converts or the read fails: a name written without a value has the empty value, which converts only to
    /// <see cref="string"/>, so <c>?tag=1&amp;tag</c> read as <see cref="int"/> is not valid.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not one of the supported types, such as an enum that is not registered, or is a
    /// nullable type. This is thrown whether or not the query has the name, before the name is checked, and propagates
    /// out of <see cref="RouteTable.Match(Uri)"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="RouteValueException">
    /// A value of <paramref name="name"/> does not convert to <typeparamref name="T"/>. Thrown from the create
    /// function, it makes <see cref="RouteTable.Match(Uri)"/> return a failed result with one
    /// <see cref="RouteDiagnosticCodes.ValueInvalid"/> diagnostic, and no later template is tried. Its message names
    /// the query value and the type but never quotes the value.
    /// </exception>
    public IReadOnlyList<T> GetQueryValues<T>(string name)
    {
        // Of the supported types, only string and the nullable ones have a null default, and the test is a constant
        // once the method is compiled for a value type, so a read of a value type never reaches the rest of it. The
        // nullable form of an enum is rejected for being nullable whether or not the enum is registered, since
        // registering it would not help. When it is not registered, the message says that too, so that following
        // its advice does not lead straight to a second failure.
        if (default(T) is null && typeof(T) != typeof(string)
            && (SupportedTypeName<T>() is not null || Nullable.GetUnderlyingType(typeof(T)) is { IsEnum: true }))
        {
            Type underlying = Nullable.GetUnderlyingType(typeof(T))!;
            string register = underlying.IsEnum && !_codecs.ContainsKey(underlying)
                ? ", after registering the enum with " +
                  $"RouteTableBuilder.AddEnumValueCodec<{DisplayName(underlying)}>() when building the table"
                : "";
            throw new InvalidOperationException(
                $"A list of query values cannot be read as type '{DisplayName(typeof(T))}': an element of the list " +
                $"is never absent, so read it as type '{DisplayName(underlying)}' instead{register}.");
        }

        ValueCodec<T>? codec =
            FindCodec<T>("A list of query values cannot be read", ListTypesTail, out string typeName);

        // A string needs no conversion, and the reference cast keeps its values and wrapping exactly those of the
        // string member.
        if (typeof(T) == typeof(string))
            return (IReadOnlyList<T>)(object)GetQueryValues(name);

        ArgumentNullException.ThrowIfNull(name);

        if (!QueryValues.TryGetValue(name, out IReadOnlyList<string>? values))
            return ReadOnlyCollection<T>.Empty;

        // A new array that only the wrapper sees, so the caller cannot change it through a cast.
        var converted = new T[values.Count];
        for (var i = 0; i < converted.Length; i++)
        {
            if (!TryConvert(values[i], codec, out converted[i]!))
                throw NotValid(name, "The query value of", typeName);
        }

        return Array.AsReadOnly(converted);
    }

    // What follows the list of supported types in the message for an unsupported type; a list has no nullable forms.
    private const string ValueTypesTail = NullableForms + "; read the value as a string and convert it instead.";

    private const string ListTypesTail = "; read the values as strings and convert them instead.";

    private static KeyNotFoundException NoPathValue(string name)
    {
        return new KeyNotFoundException(
            $"The matched path has no value for path parameter '{RouteText.EscapeForMessage(name)}': the template " +
            "has no parameter of that name, or the path left it out.");
    }

    // A path or query value can carry a secret, such as a sign-in token, and the message ends up in a diagnostic that
    // tends to be logged, so it names the parameter or query name and the type only, never the value. It is never
    // another exception's message and has no inner exception, since parse exceptions quote their input. The subject
    // tells a path value from a query value of the same name.
    private static RouteValueException NotValid(string name, string subject, string typeName)
    {
        return new RouteValueException(
            name,
            $"{subject} '{RouteText.EscapeForMessage(name)}' is not a valid {typeName}.");
    }

    private static bool TryConvert<T>(string text, ValueCodec<T>? codec, [MaybeNullWhen(false)] out T value)
    {
        return codec is null ? TryConvert(text, out value) : codec.TryParse(text, out value);
    }

    // Null for a built-in type, whose name SupportedTypeName gives, so that a read of one is decided by its typeof
    // tests alone and never looks in the codecs. Any other type needs a codec registered on the builder, or the read
    // fails as for an unsupported type. The cast cannot fail, since each codec is kept under the type it reads.
    private ValueCodec<T>? FindCodec<T>(string subject, string tail, out string typeName)
    {
        string? builtIn = SupportedTypeName<T>();
        if (builtIn is not null)
        {
            typeName = builtIn;
            return null;
        }

        if (!_codecs.TryGetValue(typeof(T), out object? found))
            throw UnsupportedType<T>(subject, tail);

        var codec = (ValueCodec<T>)found;
        typeName = codec.TypeName;
        return codec;
    }

    // Each type uses the parse method of the constraint of the same name, so that conversion and constraint cannot
    // disagree, or, for a type that no constraint is named after, the method of the rule it follows. A nullable type
    // shares the branch of its underlying type, so it reads by exactly that rule. On CoreCLR the JIT removes the cast
    // through object at every tier when T is that value type or its nullable form, as measured, and a test checks
    // that a typed read allocates nothing more; another runtime still gives the right value and could differ only in
    // that allocation. A branch whose test and local ever disagreed would throw InvalidCastException rather than
    // misread memory.
    private static bool TryConvert<T>(string text, [MaybeNullWhen(false)] out T value)
    {
        bool converted;
        if (typeof(T) == typeof(string))
        {
            value = (T)(object)text;
            return true;
        }

        if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
        {
            converted = RouteConstraint.TryParseInt(text, out int result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
        {
            converted = RouteConstraint.TryParseLong(text, out long result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(sbyte) || typeof(T) == typeof(sbyte?))
        {
            converted = RouteConstraint.TryParseInteger(text, out sbyte result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(byte) || typeof(T) == typeof(byte?))
        {
            converted = RouteConstraint.TryParseInteger(text, out byte result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(short) || typeof(T) == typeof(short?))
        {
            converted = RouteConstraint.TryParseInteger(text, out short result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(ushort) || typeof(T) == typeof(ushort?))
        {
            converted = RouteConstraint.TryParseInteger(text, out ushort result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(uint) || typeof(T) == typeof(uint?))
        {
            converted = RouteConstraint.TryParseInteger(text, out uint result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?))
        {
            converted = RouteConstraint.TryParseInteger(text, out ulong result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(decimal) || typeof(T) == typeof(decimal?))
        {
            converted = RouteConstraint.TryParseDecimal(text, out decimal result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(float) || typeof(T) == typeof(float?))
        {
            converted = RouteConstraint.TryParseFloatingPoint(text, out float result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
        {
            converted = RouteConstraint.TryParseFloatingPoint(text, out double result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
        {
            converted = RouteConstraint.TryParseBool(text, out bool result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(Guid) || typeof(T) == typeof(Guid?))
        {
            converted = RouteConstraint.TryParseGuid(text, out Guid result);
            value = (T)(object)result;
        }
        else
        {
            throw new UnreachableException("TryConvert was called for a type that SupportedTypeName rejects.");
        }

        return converted;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> QueryValues => _queryValues ??= ParseQuery();

    private IReadOnlyDictionary<string, IReadOnlyList<string>> ParseQuery()
    {
        if (_queryStart < 0)
            return QueryString.Parse(SourceUri.Query);

        // The query of a relative URI ends at the first '#' after its '?', and is parsed from the text in place.
        ReadOnlySpan<char> query = SourceUri.OriginalString.AsSpan(_queryStart);
        int fragment = query.IndexOf('#');
        return QueryString.Parse(fragment < 0 ? query : query[..fragment]);
    }
}
