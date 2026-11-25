using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

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

    internal RouteMatchContext(Uri sourceUri, IReadOnlyDictionary<string, string> pathValues, int queryStart)
    {
        SourceUri = sourceUri;
        PathValues = pathValues;
        _queryStart = queryStart;
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
    /// The type to convert to: <see cref="string"/>, <see cref="int"/>, <see cref="long"/>, <see cref="decimal"/>,
    /// <see cref="bool"/> or <see cref="Guid"/>. A value converts to a type other than <see cref="string"/> exactly
    /// when it satisfies the built-in constraint of the same name, such as <c>{id:int}</c>, so a value that the
    /// constraint of the same name accepted in the template always converts. A different constraint can let through
    /// a value that does not convert, such as <c>3000000000</c> for <c>{id:long}</c> read as <see cref="int"/>. A
    /// <see cref="string"/> is the captured value unchanged.
    /// </typeparam>
    /// <param name="name">The parameter name, compared without regard to case.</param>
    /// <returns>The captured value, decoded and converted.</returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="T"/> is not one of the supported types, such as <c>int?</c>, <see cref="double"/> or an
    /// enum. This is thrown whether or not the path has a value, and propagates out of
    /// <see cref="RouteTable.Match(Uri)"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">
    /// The template has no parameter named <paramref name="name"/>, or it is an optional or catch-all parameter that
    /// the path left out. Use <see cref="TryGetPath{T}(string, out T)"/> for a parameter that can be left out.
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
    /// The type to convert to, one of those listed for <see cref="Path{T}(string)"/>, by the same rules.
    /// </typeparam>
    /// <param name="name">The parameter name, compared without regard to case.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, the captured value, decoded and converted; otherwise the
    /// default value of <typeparamref name="T"/>.
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
    /// <typeparamref name="T"/> is not one of the supported types. This is thrown whether or not the path has a
    /// value, and propagates out of <see cref="RouteTable.Match(Uri)"/>.
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
        string typeName = SupportedTypeName<T>() ?? throw new InvalidOperationException(
            $"A path value cannot be read as type '{DisplayName(typeof(T))}'. The supported types are string, int, " +
            "long, decimal, bool and Guid; read the value as a string and convert it instead.");

        if (!TryGetPath(name, out string? text))
        {
            value = default;
            return false;
        }

        if (TryConvert(text, out value))
            return true;

        // A path value can carry a secret, such as a sign-in token, and the message ends up in a diagnostic that
        // tends to be logged, so it names the parameter and the type only. It is never another exception's
        // message and has no inner exception, since parse exceptions quote their input.
        throw new RouteValueException(
            name,
            $"The value of '{RouteText.EscapeForMessage(name)}' is not a valid {typeName}.");
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

    private static KeyNotFoundException NoPathValue(string name)
    {
        return new KeyNotFoundException(
            $"The matched path has no value for path parameter '{RouteText.EscapeForMessage(name)}': the template " +
            "has no parameter of that name, or the path left it out.");
    }

    // A short name for a message, as Int32? for a nullable type, rather than the full name with its generic arity.
    private static string DisplayName(Type type)
    {
        Type? underlying = Nullable.GetUnderlyingType(type);
        return underlying is null ? type.Name : underlying.Name + "?";
    }

    // The tests of typeof(T) are constants once the method is compiled for a value type, so each instantiation keeps
    // only its own branch, and no type is looked up by reflection, which trimming and AOT would not allow.
    private static string? SupportedTypeName<T>()
    {
        if (typeof(T) == typeof(string))
            return "string";
        if (typeof(T) == typeof(int))
            return "int";
        if (typeof(T) == typeof(long))
            return "long";
        if (typeof(T) == typeof(decimal))
            return "decimal";
        if (typeof(T) == typeof(bool))
            return "bool";
        if (typeof(T) == typeof(Guid))
            return "Guid";

        return null;
    }

    // Each type uses the parse method of the constraint of the same name, so that conversion and constraint cannot
    // disagree. The cast through object is removed by the JIT when T is that value type, so nothing is boxed, and a
    // branch whose test and local ever disagreed would throw InvalidCastException rather than misread memory.
    private static bool TryConvert<T>(string text, [MaybeNullWhen(false)] out T value)
    {
        bool converted;
        if (typeof(T) == typeof(string))
        {
            value = (T)(object)text;
            return true;
        }

        if (typeof(T) == typeof(int))
        {
            converted = RouteConstraint.TryParseInt(text, out int result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(long))
        {
            converted = RouteConstraint.TryParseLong(text, out long result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(decimal))
        {
            converted = RouteConstraint.TryParseDecimal(text, out decimal result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(bool))
        {
            converted = RouteConstraint.TryParseBool(text, out bool result);
            value = (T)(object)result;
        }
        else if (typeof(T) == typeof(Guid))
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
