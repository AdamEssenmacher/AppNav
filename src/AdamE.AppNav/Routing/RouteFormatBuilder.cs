namespace AdamE.AppNav.Routing;

/// <summary>
/// Declares how a route of type <typeparamref name="TRoute"/> is written back as a relative URI of the template it is
/// mapped to: for each parameter of the template, a function that reads the parameter's value from the route, and for
/// each query value, or list of query values of one name, a function that reads it from the route.
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
/// action that declares nothing. A query value is declared with
/// <see cref="Query{TValue}(string, Func{TRoute, TValue})"/>, and a list of values for one name with
/// <see cref="QueryValues{TValue}(string, Func{TRoute, IEnumerable{TValue}})"/>, any number of them, none included,
/// before or after the path values, each name once.
/// </para>
/// <para>
/// <see cref="RouteTable.Format(AppRoute)"/> calls each declared function at most once: the path functions in template
/// order, then the query functions in the order they were declared. It writes each value as text, builds the path with
/// <see cref="RouteTemplate.Format(IReadOnlyDictionary{string, string})"/>, and then writes each query value that is
/// not <see langword="null"/>, and each element of each list, as <c>name=value</c>, joined by <c>&amp;</c> after a
/// <c>?</c>. After a value that cannot be written, or an exception from a value function, the later functions are not
/// called.
/// </para>
/// </remarks>
public sealed class RouteFormatBuilder<TRoute>
    where TRoute : AppRoute
{
    // Written for a value of a type that cannot be written; the list of supported types goes between.
    private const string Unsupported = "A path value cannot be written";

    private const string UnsupportedTail =
        RouteValueTypes.NullableForms + "; convert the value to a string and write that instead.";

    private const string UnsupportedQuery = "A query value cannot be written";

    // Converting a collection to a string, as the other tail advises, would write one value that the typed read of the
    // item type rejects, so a collection is told what is not supported instead.
    private const string UnsupportedCollectionTail =
        RouteValueTypes.NullableForms + ". A collection is not one of them: a value declared with Query is written " +
        "as one value for its name; declare several values for one name with QueryValues.";

    private const string UnsupportedList = "A list of query values cannot be written";

    // A list has no nullable forms, since its elements are never absent, as for the read of a list.
    private const string UnsupportedListTail = "; convert the values to strings and write those instead.";

    // Converting an element that is a collection to a string would hide a second list inside each value, so a nested
    // collection is told what to do instead.
    private const string NestedCollectionTail =
        ". An element that is itself a collection is not one of them: write one list of values for a name, or " +
        "declare each list under its own name.";

    // A string given to QueryValues is a list of chars, which is what makes the element type char; the advice to
    // convert to strings would point back at the mistake.
    private const string CharElement =
        ". A string is one value, not a list of characters: declare it with Query, or return a list of strings.";

    private readonly RouteTemplate _template;
    private readonly List<ValueWriter<TRoute>> _writers = [];
    private readonly List<QueryWriter<TRoute>> _queryWriters = [];
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
    /// Reads the value from the route. It is called at most once each time a route is formatted, in template order,
    /// before every query value function; after a value that cannot be written as text, or an exception from a value
    /// function, the later functions are not called. A <see langword="null"/> value, a null reference or a nullable
    /// value type without a value, leaves an optional or catch-all parameter out, and makes
    /// <see cref="RouteTable.Format(AppRoute)"/> throw for a required one. An empty string is not left out; the
    /// template rejects it.
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
        ThrowIfCompleted();

        // Checked before the name, as the typed reads check the type first, so that a type that can never be written
        // fails whatever the name.
        if (RouteValueTypes.SupportedTypeName<TValue>() is null && RouteValueTypes.EnumType<TValue>() is null)
            throw RouteValueTypes.UnsupportedType<TValue>(Unsupported, UnsupportedTail);

        string parameter = FindParameter(name) ?? throw new ArgumentException(
            $"The template '{_template.Value}' has no parameter named '{RouteText.EscapeForMessage(name)}'.",
            nameof(name));

        foreach (ValueWriter<TRoute> writer in _writers)
        {
            if (StringComparer.Ordinal.Equals(writer.Name, parameter))
            {
                throw new ArgumentException(
                    $"Path parameter '{parameter}' of the template '{_template.Value}' is already declared; each " +
                    "parameter is declared once.",
                    nameof(name));
            }
        }

        _writers.Add(new TypedValueWriter<TRoute, TValue>(parameter, $"path parameter '{parameter}'", value));
        return this;
    }

    /// <summary>
    /// Declares a query value: the function that reads it from the route, which
    /// <see cref="RouteTable.Format(AppRoute)"/> writes after the path as <c>name=value</c>.
    /// </summary>
    /// <typeparam name="TValue">
    /// The type of the value, one of those listed for <see cref="Path{TValue}(string, Func{TRoute, TValue})"/>, and
    /// written as the same canonical text, which the typed read of the same type, such as
    /// <see cref="RouteMatchContext.TryGetQuery{T}(string, out T)"/>, reads back to an equal value. A collection, such
    /// as <c>string[]</c> or <c>List&lt;int&gt;</c>, is not one of them: this method writes one value for the name.
    /// Declare several values for one name with
    /// <see cref="QueryValues{TValue}(string, Func{TRoute, IEnumerable{TValue}})"/>.
    /// </typeparam>
    /// <param name="name">
    /// The name of the query value. Any text is a name, including the empty string, as for the query reads; it is
    /// percent-encoded when it is written. A query value can have the name of a path parameter, since the two are read
    /// separately.
    /// </param>
    /// <param name="value">
    /// Reads the value from the route. It is called at most once each time a route is formatted, after every path
    /// value function, in the order the query values were declared; after a value that cannot be written, or an
    /// exception from a value function, the later functions are not called. A <see langword="null"/> value, a null
    /// reference or a nullable value type without a value, leaves the query value out. An empty string is a value,
    /// and is written as <c>name=</c>.
    /// </param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TValue"/> is not one of the supported types, such as <see cref="Half"/>,
    /// <c>DateTime?</c> or <c>string[]</c>; the message lists the supported types, and for a collection names
    /// <see cref="QueryValues{TValue}(string, Func{TRoute, IEnumerable{TValue}})"/>. An enum that is not registered
    /// is not reported here, since it can be registered after the mapping, but by
    /// <see cref="RouteTableBuilder.Build"/>.
    /// <para>-or-</para>
    /// The call that maps the route has returned, so the builder can no longer be used.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A query value of the name has already been declared, with this method or with
    /// <see cref="QueryValues{TValue}(string, Func{TRoute, IEnumerable{TValue}})"/>, the names being compared ordinally
    /// without regard to case, as the query reads compare them; or the name contains a lone surrogate, which cannot be
    /// percent-encoded and read back unchanged. The exception's <see cref="ArgumentException.ParamName"/> is
    /// <c>name</c>.
    /// </exception>
    public RouteFormatBuilder<TRoute> Query<TValue>(string name, Func<TRoute, TValue> value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfCompleted();

        // Checked before the name, as for a path value. String is a collection of chars but one value, and is
        // supported, so only another type can be reported as a collection.
        if (RouteValueTypes.SupportedTypeName<TValue>() is null && RouteValueTypes.EnumType<TValue>() is null)
        {
            throw RouteValueTypes.UnsupportedType<TValue>(
                UnsupportedQuery,
                typeof(System.Collections.IEnumerable).IsAssignableFrom(
                    Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue))
                    ? UnsupportedCollectionTail
                    : UnsupportedTail);
        }

        string subject = QuerySubject(name);
        _queryWriters.Add(
            new ValueQueryWriter<TRoute>(
                new TypedValueWriter<TRoute, TValue>(name, subject, value),
                Uri.EscapeDataString(name)));
        return this;
    }

    /// <summary>
    /// Declares a list of query values for one name: the function that reads the list from the route, each element of
    /// which <see cref="RouteTable.Format(AppRoute)"/> writes after the path as its own <c>name=value</c> pair.
    /// </summary>
    /// <typeparam name="TValue">
    /// The type of the elements, one of those that <see cref="RouteMatchContext.GetQueryValues{T}(string)"/> reads:
    /// <see cref="string"/>, <see cref="bool"/>, <see cref="Guid"/>, <see cref="decimal"/>, <see cref="double"/>,
    /// <see cref="float"/>, one of the integer types <see cref="sbyte"/>, <see cref="byte"/>, <see cref="short"/>,
    /// <see cref="ushort"/>, <see cref="int"/>, <see cref="uint"/>, <see cref="long"/> and <see cref="ulong"/>, or an
    /// enum registered with <see cref="RouteTableBuilder.AddEnumValueCodec{TEnum}"/>. Each element is written as the
    /// canonical text described for <see cref="Path{TValue}(string, Func{TRoute, TValue})"/>. A nullable type, such as
    /// <c>int?</c>, is not one of them, since an element of a list is never absent, and neither is a collection, so
    /// a list of lists cannot be written, nor is <see cref="char"/>, which a function that returns a string gives,
    /// since a string is one value. It is inferred from the function, as <see cref="int"/> for a function that
    /// returns <c>int[]</c>, <c>List&lt;int&gt;</c> or <c>ImmutableArray&lt;int&gt;</c>.
    /// </typeparam>
    /// <param name="name">
    /// The name of the query values, by the rules of <see cref="Query{TValue}(string, Func{TRoute, TValue})"/>: any
    /// text, including the empty string, percent-encoded when it is written, and possibly the name of a path
    /// parameter.
    /// </param>
    /// <param name="values">
    /// Reads the list from the route. It is called at most once each time a route is formatted, after every path value
    /// function, in the order the query values were declared, and the list it returns is enumerated once, its
    /// enumerator disposed. A <see langword="null"/> list and an empty one both write nothing. A default
    /// <c>ImmutableArray&lt;T&gt;</c> is neither: enumerating it throws its own exception, which propagates unchanged.
    /// </param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="values"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TValue"/> is not one of the supported types, such as <see cref="Half"/>, <c>DateTime?</c> or
    /// <c>string[]</c>, and the message lists the supported types, or, for <see cref="char"/>, which a function that
    /// returns a string gives, says to declare the string with Query; or it is the nullable form of a supported type or
    /// of an enum, such as <c>int?</c>, and the message names the type to declare instead. An enum that is not
    /// registered is not reported here, since it can be registered after the mapping, but by
    /// <see cref="RouteTableBuilder.Build"/>.
    /// <para>-or-</para>
    /// The call that maps the route has returned, so the builder can no longer be used.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A query value of the name has already been declared, with this method or with
    /// <see cref="Query{TValue}(string, Func{TRoute, TValue})"/>, the names being compared ordinally without regard
    /// to case, as the query reads compare them; or the name contains a lone surrogate, which cannot be
    /// percent-encoded and read back unchanged. The exception's <see cref="ArgumentException.ParamName"/> is
    /// <c>name</c>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The elements are written in the order the list gives them, repeated and empty ones included, as
    /// <c>name=value</c> pairs joined by <c>&amp;</c>, where the list was declared among the other query values, so
    /// <c>.Query("page", r =&gt; r.Page).QueryValues("tag", r =&gt; r.Tags)</c> writes a page of 2 and the tags
    /// <c>a</c> and <c>b</c> as <c>?page=2&amp;tag=a&amp;tag=b</c>.
    /// <see cref="RouteMatchContext.GetQueryValues{T}(string)"/> of the name reads the elements back, equal and in
    /// order; a route type that is a record still compares a list member by reference. An empty string is a value,
    /// written as <c>name=</c>.
    /// </para>
    /// <para>
    /// An element that cannot be written makes <see cref="RouteTable.Format(AppRoute)"/> throw: a
    /// <see langword="null"/> element, which only a list of strings can have, since nullable element types are not
    /// supported; an element with no text, such as a <see cref="double"/> that is NaN or an enum value that is not the
    /// value of a member; and a string that contains a lone surrogate. The elements after it are not read, and the
    /// later value functions are not called. An exception thrown while the list is enumerated propagates unchanged.
    /// </para>
    /// <para>
    /// This is a separate method because <see cref="Query{TValue}(string, Func{TRoute, TValue})"/> writes one value
    /// for its name, and rejects a collection rather than guess what writing it means.
    /// </para>
    /// </remarks>
    public RouteFormatBuilder<TRoute> QueryValues<TValue>(string name, Func<TRoute, IEnumerable<TValue>?> values)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(values);
        ThrowIfCompleted();

        // Checked before the name, as for a single value. The types are those of the read of a list: the nullable form
        // of a supported type or of an enum is rejected with the type to use instead, and any other type that is
        // neither built in nor an enum is rejected outright, with advice that depends on what it is. String is a
        // collection of chars, but one value.
        Type? enumType = RouteValueTypes.EnumType<TValue>();
        Type? underlying = Nullable.GetUnderlyingType(typeof(TValue));
        if (underlying is not null && (RouteValueTypes.SupportedTypeName<TValue>() is not null || enumType is not null))
        {
            string register = enumType is not null
                ? ", with the enum registered by RouteTableBuilder.AddEnumValueCodec" +
                  $"<{RouteValueTypes.DisplayName(enumType)}>() when building the table"
                : "";
            throw new InvalidOperationException(
                $"{UnsupportedList} as type '{RouteValueTypes.DisplayName(typeof(TValue))}': an element of the list " +
                $"is never absent, so write it as type '{RouteValueTypes.DisplayName(underlying)}' instead{register}.");
        }

        if (RouteValueTypes.SupportedTypeName<TValue>() is null && enumType is null)
        {
            if (typeof(TValue) == typeof(char))
            {
                throw new InvalidOperationException(
                    $"{UnsupportedList} as type '{RouteValueTypes.DisplayName(typeof(TValue))}'{CharElement}");
            }

            throw RouteValueTypes.UnsupportedType<TValue>(
                UnsupportedList,
                typeof(System.Collections.IEnumerable).IsAssignableFrom(underlying ?? typeof(TValue))
                    ? NestedCollectionTail
                    : UnsupportedListTail);
        }

        _queryWriters.Add(
            new ListQueryWriter<TRoute, TValue>(name, QuerySubject(name), Uri.EscapeDataString(name), values));
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
        var ordered = new ValueWriter<TRoute>[_writers.Count];
        var count = 0;
        foreach (RouteSegment segment in segments)
        {
            if (!segment.IsParameter)
                continue;

            ValueWriter<TRoute> writer = FindWriter(segment.Text) ?? throw new ArgumentException(
                $"The format function for the template '{_template.Value}' does not declare path parameter " +
                $"'{segment.Text}'. Every parameter of the template must be declared with Path, so that every value " +
                "the path needs is read from the route.",
                "format");
            ordered[count++] = writer;
        }

        return new TypedRouteFormatter<TRoute>(_template, ordered, _queryWriters.ToArray());
    }

    // A builder kept past the call to Map would declare nothing for the mapping, so its use then is a mistake.
    private void ThrowIfCompleted()
    {
        if (_completed)
        {
            throw new InvalidOperationException(
                "A route format builder can be used only during the call to the format action that receives it.");
        }
    }

    // Checks a query name against the rules that Query and QueryValues share, and returns the subject that messages
    // call its declaration. One name has one declaration, whichever method made it: the single-value reads take the
    // first value of a name, ignoring case, and the list reads take all of them, so a second declaration would write
    // values that its read never returns, or that the other declaration's read would return as its own.
    private string QuerySubject(string name)
    {
        string quoted = RouteText.EscapeForMessage(name);
        if (RouteText.ContainsLoneSurrogate(name))
        {
            throw new ArgumentException(
                $"The query value name '{quoted}' is not valid UTF-16: it contains a lone surrogate, which cannot be " +
                "percent-encoded and read back unchanged.",
                nameof(name));
        }

        foreach (QueryWriter<TRoute> writer in _queryWriters)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(writer.Name, name))
            {
                string spelling = StringComparer.Ordinal.Equals(writer.Name, name)
                    ? ""
                    : $", as '{RouteText.EscapeForMessage(writer.Name)}'";
                throw new ArgumentException(
                    $"Query value '{quoted}' is already declared{spelling}; each name is declared once, and names " +
                    "that differ only by case are the same name.",
                    nameof(name));
            }
        }

        return $"query value '{quoted}'";
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

    private ValueWriter<TRoute>? FindWriter(string parameter)
    {
        foreach (ValueWriter<TRoute> writer in _writers)
        {
            if (StringComparer.Ordinal.Equals(writer.Name, parameter))
                return writer;
        }

        return null;
    }
}
