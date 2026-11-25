namespace AdamE.AppNav.Routing;

/// <summary>
/// Declares how a route of type <typeparamref name="TRoute"/> is written back as the path of the template it is
/// mapped to: for each parameter of the template, a function that reads the parameter's value from the route.
/// </summary>
/// <typeparam name="TRoute">
/// The type of route that the mapping produces, and that this builder declares how to write.
/// </typeparam>
/// <remarks>
/// <para>
/// A builder is passed to the format action given to <see cref="RouteTableBuilder.Map{TRoute}(string,
/// Func{RouteMatchContext, TRoute}, Action{RouteFormatBuilder{TRoute}})"/>, which calls the action once, while it maps
/// the route. The builder can be used only during that call.
/// </para>
/// <para>
/// Every parameter of the template, required, optional and catch-all alike, must be declared once with
/// <see cref="Path{TValue}(string, Func{TRoute, TValue})"/>. A template with no parameters is made formattable by an
/// action that declares nothing. <see cref="RouteTable.Format(AppRoute)"/> then calls each declared function at most
/// once, in template order, writes each value as text and builds the path with
/// <see cref="RouteTemplate.Format(IReadOnlyDictionary{string, string})"/>. After a value that cannot be written as
/// text, or an exception from a value function, the later functions are not called.
/// </para>
/// </remarks>
public sealed class RouteFormatBuilder<TRoute>
    where TRoute : AppRoute
{
    // Written for a value of a type that cannot be written; the list of supported types goes between.
    private const string Unsupported = "A path value cannot be written";

    private const string UnsupportedTail =
        RouteValueTypes.NullableForms + "; convert the value to a string and write that instead.";

    private readonly RouteTemplate _template;
    private readonly List<PathValueWriter<TRoute>> _writers = [];
    private bool _completed;

    internal RouteFormatBuilder(RouteTemplate template)
    {
        _template = template;
    }

    /// <summary>
    /// Declares the function that reads the value of a path parameter from the route.
    /// </summary>
    /// <typeparam name="TValue">
    /// The type of the value, which is written as the one text that the typed read of the same type, such as
    /// <see cref="RouteMatchContext.Path{T}(string)"/>, reads back to an equal value: <see cref="string"/>,
    /// <see cref="bool"/>, <see cref="Guid"/>, <see cref="decimal"/>, <see cref="double"/>, <see cref="float"/>, one
    /// of the integer types <see cref="sbyte"/>, <see cref="byte"/>, <see cref="short"/>, <see cref="ushort"/>,
    /// <see cref="int"/>, <see cref="uint"/>, <see cref="long"/> and <see cref="ulong"/>, an enum registered with
    /// <see cref="RouteTableBuilder.AddEnumValueCodec{TEnum}"/>, or the nullable form of any of them but
    /// <see cref="string"/>. A <see cref="string"/> is written as it is; <see cref="bool"/> as <c>true</c> or
    /// <c>false</c>; an integer as invariant digits with <c>-</c> for a negative value and no <c>+</c> or leading
    /// zeros; a <see cref="decimal"/> without trailing zeros after the point, without a point when it has no
    /// fraction, and as <c>0</c> for every zero; a <see cref="double"/> or <see cref="float"/> as the shortest digits
    /// that read back as the same value of its own type, never with an exponent, and as <c>0</c> for either zero; a
    /// <see cref="Guid"/> in lower case with hyphens, as <c>Guid.ToString()</c> writes it; and an enum as the name
    /// of the member with that value, as declared, or, when several members have it, the name that sorts first
    /// ordinally. A <see cref="double"/> or <see cref="float"/> that is NaN or infinite, and an enum value that is not
    /// the value of a member, such as an undefined number or a combination of flags with no name of its own, have no
    /// text, and <see cref="RouteTable.Format(AppRoute)"/> throws for them.
    /// </typeparam>
    /// <param name="name">
    /// The name of a parameter of the template, compared without regard to case, as the typed reads compare it.
    /// </param>
    /// <param name="value">
    /// Reads the value from the route. It is called at most once each time a route is formatted, in template order;
    /// after a value that cannot be written as text, or an exception from a value function, the later functions are
    /// not called. A <see langword="null"/>
    /// value, a null reference or a nullable value type without a value, leaves an optional or catch-all parameter
    /// out, and makes <see cref="RouteTable.Format(AppRoute)"/> throw for a required one. An empty string is not left
    /// out; the template rejects it.
    /// </param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TValue"/> is not one of the supported types, such as <see cref="Half"/> or
    /// <c>DateTime?</c>; the message lists the supported types. An enum that is not registered is not reported here,
    /// since it can be registered after the mapping, but by <see cref="RouteTableBuilder.Build"/>.
    /// <para>-or-</para>
    /// The call that maps the route has returned, so the builder can no longer be used.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The template has no parameter named <paramref name="name"/>, or the parameter has already been declared. The
    /// exception's <see cref="ArgumentException.ParamName"/> is <c>name</c>.
    /// </exception>
    public RouteFormatBuilder<TRoute> Path<TValue>(string name, Func<TRoute, TValue> value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        // A builder kept past the call to Map would declare nothing for the mapping, so its use then is a mistake.
        if (_completed)
        {
            throw new InvalidOperationException(
                "A route format builder can be used only during the call to the format action that receives it.");
        }

        // Checked before the name, as the typed reads check the type first, so that a type that can never be written
        // fails whatever the name.
        if (RouteValueTypes.SupportedTypeName<TValue>() is null && RouteValueTypes.EnumType<TValue>() is null)
            throw RouteValueTypes.UnsupportedType<TValue>(Unsupported, UnsupportedTail);

        string parameter = FindParameter(name) ?? throw new ArgumentException(
            $"The template '{_template.Value}' has no parameter named '{RouteText.EscapeForMessage(name)}'.",
            nameof(name));

        foreach (PathValueWriter<TRoute> writer in _writers)
        {
            if (StringComparer.Ordinal.Equals(writer.Name, parameter))
            {
                throw new ArgumentException(
                    $"Path parameter '{parameter}' of the template '{_template.Value}' is already declared; each " +
                    "parameter is declared once.",
                    nameof(name));
            }
        }

        _writers.Add(new TypedPathValueWriter<TRoute, TValue>(parameter, value));
        return this;
    }

    // Runs the format action and returns the formatter it declared, with a writer for every parameter in template
    // order. The builder cannot be used afterwards, whether or not the action succeeded.
    internal static RouteFormatter Build(RouteTemplate template, Action<RouteFormatBuilder<TRoute>> format)
    {
        var builder = new RouteFormatBuilder<TRoute>(template);
        try
        {
            format(builder);
        }
        finally
        {
            builder._completed = true;
        }

        return builder.Complete();
    }

    private TypedRouteFormatter<TRoute> Complete()
    {
        ReadOnlySpan<RouteSegment> segments = _template.Segments;
        var ordered = new PathValueWriter<TRoute>[_writers.Count];
        var count = 0;
        foreach (RouteSegment segment in segments)
        {
            if (!segment.IsParameter)
                continue;

            PathValueWriter<TRoute> writer = FindWriter(segment.Text) ?? throw new ArgumentException(
                $"The format function for the template '{_template.Value}' does not declare path parameter " +
                $"'{segment.Text}'. Every parameter of the template must be declared with Path, so that every value " +
                "the path needs is read from the route.",
                "format");
            ordered[count++] = writer;
        }

        return new TypedRouteFormatter<TRoute>(_template, ordered);
    }

    // Returns the parameter's name as written in the template. A template has no two names that differ only by case,
    // so once a name is found this way, writers can be compared by that name ordinally.
    private string? FindParameter(string name)
    {
        foreach (RouteSegment segment in _template.Segments)
        {
            if (segment.IsParameter && StringComparer.OrdinalIgnoreCase.Equals(segment.Text, name))
                return segment.Text;
        }

        return null;
    }

    private PathValueWriter<TRoute>? FindWriter(string parameter)
    {
        foreach (PathValueWriter<TRoute> writer in _writers)
        {
            if (StringComparer.Ordinal.Equals(writer.Name, parameter))
                return writer;
        }

        return null;
    }
}
