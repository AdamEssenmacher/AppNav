using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

/// <summary>
/// Collects the mappings from route templates to route types and builds a <see cref="RouteTable"/> from them.
/// </summary>
/// <remarks>
/// A builder is not thread-safe. The tables it builds are, and do not change when the builder does.
/// </remarks>
public sealed class RouteTableBuilder
{
    private readonly List<RouteDefinition> _definitions = [];

    // The codecs registered so far, each under the type it reads: an enum of AddEnumValueCodec and its nullable form
    // side by side, and a type of AddValueCodec alone, since its nullable form cannot be named without reflection.
    private readonly Dictionary<Type, object> _codecs = [];

    // One constraint for each name that AddConstraint or a mapped template has named, not built in, ignoring case,
    // bound once AddConstraint is called for it. One instance for each name makes two templates that name it in
    // different case the same template, as for a built-in constraint. The names are ASCII, so ignoring case ordinally
    // is the same in every culture.
    private readonly Dictionary<string, RouteConstraint> _constraints = new(StringComparer.OrdinalIgnoreCase);

    // The pairs of constraint names that DeclareDisjointConstraints declared disjoint, in lower case, each once, in the
    // order they were first declared, which is the order Build checks them in. The names are kept, not constraints,
    // because a name may be added after it is declared, and a built-in constraint is shared by every builder.
    private readonly List<(string First, string Second)> _disjointPairs = [];

    // The same pairs, each with its names in ordinal order, so that declaring a pair again in either order is found.
    private readonly HashSet<(string, string)> _disjointKeys = [];

    // The resolver passed to the template parser, made once.
    private Func<string, RouteConstraint>? _resolveConstraint;

    /// <summary>
    /// Maps a route template to a route type.
    /// </summary>
    /// <typeparam name="TRoute">The type of route that a path matching the template produces.</typeparam>
    /// <param name="template">
    /// The route template, parsed as by <see cref="RouteTemplate.Parse(string)"/>.
    /// </param>
    /// <param name="createRoute">
    /// Creates the route from what the match captured. It is called once for each URI whose path the template
    /// matches, when this mapping is the one that wins, and must return a route, not <see langword="null"/>. To
    /// reject a value that it cannot use, it throws a <see cref="RouteValueException"/>, and
    /// <see cref="RouteTable.Match(Uri)"/> then fails for the URI instead of throwing.
    /// </param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <remarks>
    /// In this version, the same route type can be mapped to more than one template. The same template cannot be
    /// mapped twice, nor two templates that rank equally and can match the same path, but that is checked by
    /// <see cref="Build"/>, not here. The template can name a constraint added with
    /// <see cref="AddConstraint(string, Func{string, bool})"/>, before or after this call. The order of the calls sets
    /// the order of <see cref="RouteTable.Definitions"/>, but never which mapping wins when several match, which
    /// follows the precedence of their templates as <see cref="RouteTable.Match(Uri)"/> describes.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="template"/> or <paramref name="createRoute"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="template"/> is not a valid route template, for any of the reasons that
    /// <see cref="RouteTemplate.Parse(string)"/> gives, except that a constraint whose name is not built in, is not
    /// reserved and has the form that <see cref="AddConstraint(string, Func{string, bool})"/> accepts is left for
    /// <see cref="Build"/> to check. A reserved name, which can never be added, is rejected here.
    /// The exception's <see cref="ArgumentException.ParamName"/> is <c>template</c>.
    /// </exception>
    public RouteTableBuilder Map<TRoute>(string template, Func<RouteMatchContext, TRoute> createRoute)
        where TRoute : AppRoute
    {
        RouteTemplate parsed = RouteTemplate.Parse(template, nameof(template), ResolveConstraint());
        ArgumentNullException.ThrowIfNull(createRoute);

        // Func is covariant in its result, so the function is stored as it is, without a wrapper.
        _definitions.Add(new RouteDefinition(typeof(TRoute), parsed, createRoute, null));
        return this;
    }

    /// <summary>
    /// Maps a route template to a route type, and declares how a route of that type is written back as a path of the
    /// template and a query, which <see cref="RouteTable.Format(AppRoute)"/> does.
    /// </summary>
    /// <typeparam name="TRoute">The type of route that a path matching the template produces.</typeparam>
    /// <param name="template">
    /// The route template, parsed as by <see cref="RouteTemplate.Parse(string)"/>.
    /// </param>
    /// <param name="createRoute">
    /// Creates the route from what the match captured, exactly as for
    /// <see cref="Map{TRoute}(string, Func{RouteMatchContext, TRoute})"/>.
    /// </param>
    /// <param name="format">
    /// Declares, on the <see cref="RouteFormatBuilder{TRoute}"/> it receives, a function that reads the value of each
    /// parameter of the template from a route, as in <c>format =&gt; format.Path("id", r =&gt; r.Id)</c>, and one for
    /// each query value the route is written with, as in <c>.Query("page", r =&gt; r.Page)</c>, or list of values of
    /// one name, as in <c>.QueryValues("tag", r =&gt; r.Tags)</c>. It is called once, here, and must declare every
    /// parameter of the template: required, optional and catch-all. Query values are optional, one declaration for
    /// each name. For a template with no parameters and a route with no query values, pass an action that declares
    /// nothing, such as <c>_ =&gt; { }</c>. An exception it throws propagates unchanged.
    /// </param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// The mapping matches exactly as one made by <see cref="Map{TRoute}(string, Func{RouteMatchContext, TRoute})"/>;
    /// only this overload can also write the route. Of the mappings of one route type, at most one can have a format
    /// function, which <see cref="Build"/> checks, so that writing a route never has to choose between templates.
    /// Other mappings of the same type without one are allowed, so <c>/</c> and <c>/home</c> can both create the same
    /// route while only one of them says how to write it.
    /// </para>
    /// <para>
    /// A value of a type that is not built in must have a codec, registered with <see cref="AddEnumValueCodec{TEnum}"/>
    /// for an enum or with <see cref="AddValueCodec{TValue}"/> for any type, before <see cref="Build"/>, before or
    /// after this call; <see cref="Build"/> reports one that has none.
    /// </para>
    /// <para>
    /// A route is written by the mapping of its exact type, so <typeparamref name="TRoute"/> must be a type that can be
    /// instantiated, not an abstract base type of the routes the mapping creates.
    /// </para>
    /// <para>
    /// When this method throws, it adds no mapping.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="template"/>, <paramref name="createRoute"/> or <paramref name="format"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="template"/> is not a valid route template, for any of the reasons that
    /// <see cref="RouteTemplate.Parse(string)"/> gives, except that a constraint whose name is not built in, is not
    /// reserved and has the form that <see cref="AddConstraint(string, Func{string, bool})"/> accepts is left for
    /// <see cref="Build"/> to check, while a reserved name, which can never be added, is rejected here; the
    /// exception's <see cref="ArgumentException.ParamName"/> is <c>template</c>.
    /// <para>-or-</para>
    /// <paramref name="format"/> did not declare every parameter of the template. The message names the template and
    /// the first parameter, in template order, that is not declared, and the exception's
    /// <see cref="ArgumentException.ParamName"/> is <c>format</c>.
    /// <para>-or-</para>
    /// <paramref name="format"/> declared a name that is not a parameter of the template, or declared a parameter
    /// twice, as <see cref="RouteFormatBuilder{TRoute}.Path{TValue}(string, Func{TRoute, TValue})"/> describes, or
    /// declared a query value name twice or one with a lone surrogate, as
    /// <see cref="RouteFormatBuilder{TRoute}.Query{TValue}(string, Func{TRoute, TValue})"/> and
    /// <see cref="RouteFormatBuilder{TRoute}.QueryValues{TValue}(string, Func{TRoute, IEnumerable{TValue}})"/>
    /// describe; the exception's <see cref="ArgumentException.ParamName"/> is <c>name</c>.
    /// <para>-or-</para>
    /// <typeparamref name="TRoute"/> is abstract, which happens when it is inferred from a create function whose
    /// result is a base type. The exception's <see cref="ArgumentException.ParamName"/> is <c>TRoute</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="format"/> declared a value of a type that can never be written, whatever is registered, such as
    /// a collection, as <see cref="RouteFormatBuilder{TRoute}.Path{TValue}(string, Func{TRoute, TValue})"/>,
    /// <see cref="RouteFormatBuilder{TRoute}.Query{TValue}(string, Func{TRoute, TValue})"/> and
    /// <see cref="RouteFormatBuilder{TRoute}.QueryValues{TValue}(string, Func{TRoute, IEnumerable{TValue}})"/>
    /// describe.
    /// </exception>
    public RouteTableBuilder Map<TRoute>(
        string template,
        Func<RouteMatchContext, TRoute> createRoute,
        Action<RouteFormatBuilder<TRoute>> format)
        where TRoute : AppRoute
    {
        RouteTemplate parsed = RouteTemplate.Parse(template, nameof(template), ResolveConstraint());
        ArgumentNullException.ThrowIfNull(createRoute);
        ArgumentNullException.ThrowIfNull(format);

        // Format looks a route up by its exact type, which is never abstract, so a format function for an abstract
        // type could never be used. Checked before the format action runs, so that it runs only for a usable mapping.
        if (typeof(TRoute).IsAbstract)
        {
            throw new ArgumentException(
                $"The route type '{RouteValueTypes.DisplayName(typeof(TRoute))}' is abstract, so it cannot be given " +
                "a format function: a route is written by the mapping of its exact type, which must be one that can " +
                "be instantiated. Give the type argument of Map explicitly, or map each concrete route type.",
                nameof(TRoute));
        }

        // The definition is added only once the format action has declared everything, so that a mapping that fails
        // leaves the builder as it was.
        RouteFormatter formatter = RouteFormatBuilder<TRoute>.Build(parsed, format);
        _definitions.Add(new RouteDefinition(typeof(TRoute), parsed, createRoute, formatter));
        return this;
    }

    /// <summary>
    /// Lets the typed reads of every table this builder builds from then on read values as the enum
    /// <typeparamref name="TEnum"/>.
    /// </summary>
    /// <typeparam name="TEnum">The enum type to read values as.</typeparam>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// Once registered, <typeparamref name="TEnum"/> and its nullable form can be read by
    /// <see cref="RouteMatchContext.Path{T}(string)"/>, <see cref="RouteMatchContext.TryGetPath{T}(string, out T)"/>
    /// and <see cref="RouteMatchContext.TryGetQuery{T}(string, out T)"/>, and <typeparamref name="TEnum"/> itself by
    /// <see cref="RouteMatchContext.GetQueryValues{T}(string)"/>. A value converts exactly when its text is the name
    /// of a member, compared ordinally without regard to case, and gives the value of that member. Nothing else
    /// converts: not a number, such as <c>2</c>, which would tie links to the order the members are declared in; not
    /// a list of names separated by commas; not a name with white space around it, which is not trimmed; and not the
    /// empty text. Two names for the same value both convert, to that value. An enum with
    /// <see cref="FlagsAttribute"/> can be registered, but only the name of a single member converts, never a
    /// combination. An enum with no members can be registered, and then no text converts. Renaming a member changes
    /// which links work, so treat the names of a registered enum as part of the links of the app. For other spellings,
    /// such as <c>in-progress</c>, register a codec of your own for the enum with <see cref="AddValueCodec{TValue}"/>
    /// instead.
    /// </para>
    /// <para>
    /// A format function given to
    /// <see cref="Map{TRoute}(string, Func{RouteMatchContext, TRoute}, Action{RouteFormatBuilder{TRoute}})"/> writes
    /// a value of <typeparamref name="TEnum"/>, or of its nullable form, as the name of its member, as declared. Of
    /// several members with the value, the name that sorts first ordinally is written. A value that no member has,
    /// such as an undefined number or a combination of flags with no name of its own, cannot be written.
    /// </para>
    /// <para>
    /// Registering applies to the whole table, so the order of this call and of the <c>Map</c> calls does not matter,
    /// and registering the same enum again does nothing. A table already built does not change; <see cref="Build"/>
    /// builds one that includes the registration. No template constraint is named after an enum.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Two members of <typeparamref name="TEnum"/> have names that differ only by case, such as <c>A</c> and
    /// <c>a</c>, so a text that names one would name both. The message names the enum and both members.
    /// <para>-or-</para>
    /// <typeparamref name="TEnum"/> already has a codec registered with <see cref="AddValueCodec{TValue}"/> on this
    /// builder, and a type has one codec. The message names the enum.
    /// <para/>
    /// The exception's <see cref="ArgumentException.ParamName"/> is <c>TEnum</c>, and the builder is not changed.
    /// </exception>
    public RouteTableBuilder AddEnumValueCodec<TEnum>()
        where TEnum : struct, Enum
    {
        if (_codecs.TryGetValue(typeof(TEnum), out object? existing))
        {
            return existing is DelegateValueCodec<TEnum>
                ? throw AlreadyHasCodec(typeof(TEnum), "AddValueCodec", nameof(TEnum))
                : this;
        }

        // Made before either is added, so that an enum that cannot be registered leaves the builder as it was.
        var codec = new EnumValueCodec<TEnum>();
        _codecs.Add(typeof(TEnum), codec);
        _codecs.Add(typeof(TEnum?), new NullableValueCodec<TEnum>(codec));
        return this;
    }

    /// <summary>
    /// Lets the typed reads and the format functions of every table this builder builds from then on read and write
    /// values of the application's own type <typeparamref name="TValue"/>, by a parser and a writer of its own.
    /// </summary>
    /// <typeparam name="TValue">
    /// The type to read and write values as: a class, a struct, a record, an interface, an abstract class or an enum,
    /// but not one of the built-in types, <see cref="string"/>, <see cref="bool"/>, <see cref="Guid"/>,
    /// <see cref="decimal"/>, <see cref="double"/>, <see cref="float"/>, <see cref="sbyte"/>, <see cref="byte"/>,
    /// <see cref="short"/>, <see cref="ushort"/>, <see cref="int"/>, <see cref="uint"/>, <see cref="long"/> and
    /// <see cref="ulong"/>, not <see cref="char"/>, which is one character of a string, not a nullable value type and
    /// not a collection. A value is found by its codec by exactly
    /// this type: a codec for an interface or a base class does not read or write a type that implements or derives
    /// from it, nor the other way round.
    /// </typeparam>
    /// <param name="tryParse">
    /// Reads a text as a value. It returns <see langword="true"/> with the value, which must not be
    /// <see langword="null"/>, for a text that is a valid value, and <see langword="false"/> for one that is not.
    /// </param>
    /// <param name="format">
    /// Writes a value as its text, or returns <see langword="null"/> for a value that cannot be written. It is never
    /// called with <see langword="null"/>, which a format function leaves out before asking for text.
    /// </param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// Once registered, <typeparamref name="TValue"/> can be read by <see cref="RouteMatchContext.Path{T}(string)"/>,
    /// <see cref="RouteMatchContext.TryGetPath{T}(string, out T)"/>,
    /// <see cref="RouteMatchContext.TryGetQuery{T}(string, out T)"/> and
    /// <see cref="RouteMatchContext.GetQueryValues{T}(string)"/>, and written by a format function given to
    /// <see cref="Map{TRoute}(string, Func{RouteMatchContext, TRoute}, Action{RouteFormatBuilder{TRoute}})"/> with
    /// <see cref="RouteFormatBuilder{TRoute}.Path{TValue}(string, Func{TRoute, TValue})"/>,
    /// <see cref="RouteFormatBuilder{TRoute}.Query{TValue}(string, Func{TRoute, TValue})"/> and
    /// <see cref="RouteFormatBuilder{TRoute}.QueryValues{TValue}(string, Func{TRoute, IEnumerable{TValue}})"/>. For a
    /// value type, its nullable form can then be read and written wherever the nullable form of a built-in type can,
    /// by the same codec; each value read or written in that form costs one allocation, a box around the value. As for
    /// the built-in types, a list of values cannot have a nullable element type.
    /// </para>
    /// <para>
    /// The two functions are a contract that the library relies on but cannot check in full.
    /// <paramref name="tryParse"/> must accept every text that <paramref name="format"/> writes, and
    /// <paramref name="format"/> should give equal values the same text, so that one value has one link. Both must be
    /// safe to call from several threads at once, since a built table is, and should have no side effects: either can
    /// be called more than once for one value, because <see cref="RouteTable.Format(AppRoute)"/> reads back each text
    /// <paramref name="format"/> writes with <paramref name="tryParse"/>, and throws <see cref="ArgumentException"/>
    /// for one it does not accept. That check does not compare the value read back with the one written, so it does not
    /// catch a <paramref name="format"/> that gives two different values the same text, nor a
    /// <paramref name="tryParse"/> that reads a written text back as a different value: the round trip is the codec's
    /// promise, and a value reads back equal exactly when the codec makes it so. A parser that accepts several
    /// spellings of one value is fine: each spelling matches, and <see cref="RouteTable.Format(AppRoute)"/> writes the
    /// one <paramref name="format"/> gives. A path value must also satisfy the constraint of its parameter, if the
    /// template gives one. A <c>TryParse</c> method of the base class library, such as
    /// <c>DateOnly.TryParse(string, out DateOnly)</c>, reads by the current culture, so pass a lambda that gives
    /// <see cref="System.Globalization.CultureInfo.InvariantCulture"/> and an exact format instead, and write the
    /// value by the same format.
    /// </para>
    /// <para>
    /// A text that <paramref name="tryParse"/> does not accept makes the typed read throw
    /// <see cref="RouteValueException"/>, as a text that is not a valid <see cref="int"/> does, so that
    /// <see cref="RouteTable.Match(Uri)"/> fails for the URI; its message names the type by its simple name and never
    /// quotes the value. A <see langword="null"/> from <paramref name="format"/> makes
    /// <see cref="RouteTable.Format(AppRoute)"/> throw <see cref="ArgumentException"/>, as a <see cref="double"/> that
    /// is NaN does. An empty text is a text: as a query value it is written as <c>name=</c>, and as a path value the
    /// template rejects it, as it rejects any empty path value. An exception that either function throws propagates
    /// unchanged, out of the read and <see cref="RouteTable.Match(Uri)"/>, or out of
    /// <see cref="RouteTable.Format(AppRoute)"/>, except a <see cref="RouteValueException"/> that
    /// <paramref name="tryParse"/> throws during a read, which <see cref="RouteTable.Match(Uri)"/> takes as a value
    /// that is not valid, as it takes one thrown by a create function. Return <see langword="false"/> or
    /// <see langword="null"/> for a value that is not valid, rather than throw.
    /// </para>
    /// <para>
    /// The built-in types cannot be given a codec, because their text is fixed by the library so that it always agrees
    /// with template constraints such as <c>{id:int}</c>; for another text, use a type of your own. An enum can be
    /// registered here instead of with <see cref="AddEnumValueCodec{TEnum}"/>, which is how it gets spellings of its
    /// own, such as <c>in-progress</c>. No template constraint is named after a type registered here.
    /// </para>
    /// <para>
    /// Registering applies to the whole table, so the order of this call and of the <c>Map</c> calls does not matter.
    /// A table already built does not change; <see cref="Build"/> builds one that includes the registration, and keeps
    /// the two functions given here, whatever this builder registers later.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tryParse"/> or <paramref name="format"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TValue"/> is one of the built-in types, or the nullable form of one; or it is a collection,
    /// which a type of your own can wrap, or the nullable form of one; or it is <see cref="char"/> or its nullable
    /// form; or it is the nullable form of another value type, whose codec is registered for that type instead; or it
    /// already has a codec registered on this builder, with this method or with
    /// <see cref="AddEnumValueCodec{TEnum}"/>, and a type has one codec. The message names the type, and the
    /// exception's <see cref="ArgumentException.ParamName"/> is <c>TValue</c>. The builder is not changed.
    /// </exception>
    public RouteTableBuilder AddValueCodec<TValue>(RouteValueParser<TValue> tryParse, Func<TValue, string?> format)
    {
        ArgumentNullException.ThrowIfNull(tryParse);
        ArgumentNullException.ThrowIfNull(format);

        string type = RouteValueTypes.DisplayName(typeof(TValue));
        // A type that can never have a codec is reported before the nullable form of a type, so that the advice to
        // register the underlying type is given only when that would work. Each test looks through a nullable form.
        Type? underlying = Nullable.GetUnderlyingType(typeof(TValue));
        if (RouteValueTypes.SupportedTypeName<TValue>() is not null)
        {
            string what = underlying is null ? "one of the built-in types" : "the nullable form of a built-in type";
            throw new ArgumentException(
                $"The type '{type}' cannot be given a codec, because it is {what}, whose text is fixed by the " +
                "library so that it always agrees with template constraints such as {id:int}. For another text, " +
                "use a type of your own and register a codec for it.",
                nameof(TValue));
        }

        if (RouteValueTypes.IsCollection<TValue>())
        {
            throw new ArgumentException(
                $"The type '{type}' cannot be given a codec, because it is a collection, and a codec reads and " +
                "writes one value. Wrap the collection in a type of your own and register a codec for that type, or " +
                "read and write its elements as a list of query values.",
                nameof(TValue));
        }

        // QueryValues rejects char, which a string given by mistake makes the element type, so a codec for it could
        // not work everywhere a codec works.
        if ((underlying ?? typeof(TValue)) == typeof(char))
        {
            throw new ArgumentException(
                $"The type '{type}' cannot be given a codec, because a char is one character of a string. Read and " +
                "write it as a string, or use a type of your own.",
                nameof(TValue));
        }

        if (underlying is not null)
        {
            string name = RouteValueTypes.DisplayName(underlying);
            throw new ArgumentException(
                $"The type '{type}' cannot be given a codec, because it is the nullable form of '{name}'. Register " +
                $"the codec for '{name}' instead; its nullable form is then read and written by that codec.",
                nameof(TValue));
        }

        if (_codecs.TryGetValue(typeof(TValue), out object? existing))
        {
            throw AlreadyHasCodec(
                typeof(TValue),
                existing is DelegateValueCodec<TValue> ? "AddValueCodec" : "AddEnumValueCodec",
                nameof(TValue));
        }

        _codecs.Add(typeof(TValue), new DelegateValueCodec<TValue>(tryParse, format));
        return this;
    }

    /// <summary>
    /// Adds a constraint of the application's own, which the templates mapped on this builder can then name, as in
    /// <c>{value:name}</c> or <c>{value:name?}</c>, as they name a built-in constraint.
    /// </summary>
    /// <param name="name">
    /// The name of the constraint: an ASCII letter followed by ASCII letters, digits, <c>-</c> or <c>_</c>, such as
    /// <c>slug</c> or <c>order-code</c>. Names are compared without regard to ASCII case, so a template can write
    /// <c>{x:Slug}</c> for <c>slug</c>, and messages write the name in lower case.
    /// </param>
    /// <param name="isSatisfiedBy">
    /// Returns whether a parameter's decoded segment text satisfies the constraint. It receives the text exactly as a
    /// built-in constraint does, decoded and never trimmed, and must return the same answer for the same text. It is
    /// called by <see cref="RouteTable.Match(Uri)"/> for the segment in the parameter's position, unless the segment is
    /// <c>.</c> or <c>..</c> once decoded; by <see cref="RouteTable.Format(AppRoute)"/> and
    /// <see cref="RouteTable.FormatUri(AppRoute, Uri)"/>, to check the value written, unless it is <c>.</c> or
    /// <c>..</c>, and when checking whether another template would match the path written first; by
    /// <see cref="RouteTemplate.Match(string)"/> and
    /// <see cref="RouteTemplate.Format(IReadOnlyDictionary{string, string})"/> of a template mapped on this builder,
    /// such as one read from <see cref="RouteTable.Definitions"/>, as the table's own methods call it; and by
    /// <see cref="Build"/>, with sample texts, when it checks that templates can be told apart and when it checks a
    /// pair declared with <see cref="DeclareDisjointConstraints(ReadOnlySpan{string})"/>. It must be safe to call
    /// from several threads at once, since a built table is. An exception it throws, a
    /// <see cref="RouteValueException"/> included, propagates unchanged from each of them.
    /// </param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// Adding applies to the whole table, so the order of this call and of the <c>Map</c> calls does not matter: a
    /// template can name the constraint before it is added. A template that names a constraint that is neither built
    /// in nor added by the time <see cref="Build"/> is called is reported by <see cref="Build"/>; one that names a
    /// reserved name, which can never be added, is rejected by <c>Map</c>. A table already built
    /// does not change; <see cref="Build"/> builds one that includes the constraint.
    /// </para>
    /// <para>
    /// The constraint belongs to this builder: <see cref="RouteTemplate.Parse(string)"/> and the templates mapped on
    /// another builder do not know it, so the <see cref="RouteTemplate.Value"/> of a template that names it cannot be
    /// given to <see cref="RouteTemplate.Parse(string)"/>.
    /// </para>
    /// <para>
    /// A constraint only decides whether a segment matches; the value captured is the decoded text, unchanged, as for
    /// a built-in constraint. To read it as a type of the application's own, pair the constraint with a codec
    /// registered with <see cref="AddValueCodec{TValue}"/> and a typed read.
    /// </para>
    /// <para>
    /// A parameter with an added constraint ranks as one with a built-in constraint does. Which text the function
    /// accepts cannot be known, so <see cref="Build"/> treats an added constraint as overlapping every constraint,
    /// itself included: it cannot keep apart two templates that otherwise rank equally, and such a pair is rejected
    /// as templates that cannot be told apart. Only a pair declared with
    /// <see cref="DeclareDisjointConstraints(ReadOnlySpan{string})"/> is taken to accept no text in common, and a
    /// constraint can never be declared disjoint from itself. Keep such templates apart with a different literal
    /// instead, or, when the two constraints accept no text in common, declare that.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="isSatisfiedBy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not an ASCII letter followed by ASCII letters, digits, <c>-</c> or <c>_</c>; it is
    /// the name of a built-in constraint, <c>int</c>, <c>long</c>, <c>decimal</c>, <c>bool</c>, <c>guid</c> or
    /// <c>alpha</c>; it is reserved for a later built-in constraint, as the C# keywords of the other built-in value
    /// types are: <c>string</c>, <c>char</c>, <c>sbyte</c>, <c>byte</c>, <c>short</c>, <c>ushort</c>, <c>uint</c>,
    /// <c>ulong</c>, <c>float</c> and <c>double</c>; or a constraint with that name, ignoring case, is already added on
    /// this builder. The exception's <see cref="ArgumentException.ParamName"/> is <c>name</c>, and the builder is not
    /// changed.
    /// </exception>
    public RouteTableBuilder AddConstraint(string name, Func<string, bool> isSatisfiedBy)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(isSatisfiedBy);

        if (!RouteConstraint.IsAddableName(name))
        {
            throw new ArgumentException(
                $"The constraint name '{RouteText.EscapeForMessage(name)}' is not valid; the name of a constraint is " +
                "an ASCII letter followed by ASCII letters, digits, '-' or '_'.",
                nameof(name));
        }

        string lower = name.ToLowerInvariant();
        if (RouteConstraint.Find(name) is not null)
        {
            throw new ArgumentException(
                $"The constraint name '{lower}' is the name of a built-in constraint, which cannot be replaced; the " +
                $"built-in constraints are {RouteConstraint.SupportedNames}, ignoring case.",
                nameof(name));
        }

        if (RouteConstraint.IsReserved(name))
        {
            throw new ArgumentException(
                $"The constraint name '{lower}' is reserved, so that a later built-in constraint can have it.",
                nameof(name));
        }

        if (_constraints.TryGetValue(name, out RouteConstraint? constraint) && constraint.IsBound)
        {
            throw new ArgumentException(
                $"The constraint name '{lower}' is already added on this builder, and a name has one constraint; " +
                "names are compared without regard to case.",
                nameof(name));
        }

        constraint ??= AddUnbound(name);
        constraint.Bind(isSatisfiedBy);
        return this;
    }

    /// <summary>
    /// Declares that no text satisfies any two of the named constraints together, so that <see cref="Build"/> can
    /// keep apart templates that differ only in them, such as <c>/articles/{slug:slug}</c> and
    /// <c>/articles/{id:int}</c>.
    /// </summary>
    /// <param name="names">
    /// The names of two or more different constraints, built in, such as <c>int</c>, or added with
    /// <see cref="AddConstraint(string, Func{string, bool})"/>, such as <c>slug</c>, compared without regard to ASCII
    /// case. Every two of them are declared disjoint: <c>("slug", "int", "guid")</c> declares <c>slug</c> and
    /// <c>int</c>, <c>slug</c> and <c>guid</c>, and <c>int</c> and <c>guid</c>, and nothing about any other pair.
    /// </param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// The library cannot know which text the function of an added constraint accepts, so without a declaration
    /// <see cref="Build"/> treats an added constraint as overlapping every constraint and rejects two templates that
    /// rank equally and differ only in such constraints as templates that cannot be told apart. A declared pair is
    /// taken to accept no text in common instead, so a required position where the two templates have its two
    /// constraints keeps them apart. A constraint always overlaps itself, which is why a name cannot be given twice.
    /// </para>
    /// <para>
    /// Declaring applies to the whole table, so the order of this call and of the
    /// <see cref="AddConstraint(string, Func{string, bool})"/> and <c>Map</c> calls does not matter: a name can be
    /// declared before it is added. Declaring a pair again, with an identical call, with the names in the other order,
    /// or as part of another list, is allowed and changes nothing. A table already built does not change;
    /// <see cref="Build"/> builds one that includes the declaration.
    /// </para>
    /// <para>
    /// <see cref="Build"/> checks each declared pair against the sample texts <c>0</c>, <c>a</c>, <c>true</c> and
    /// <c>00000000-0000-0000-0000-000000000000</c>: it passes each sample text to the function of each constraint of
    /// a declared pair, once for that check however many pairs name the constraint, and throws when both
    /// constraints of a pair accept one of them. Comparing the templates can then pass a function the same texts
    /// again. That check catches a declaration that is false for one of
    /// those texts, and so every false declaration about two built-in constraints, such as <c>int</c> and
    /// <c>long</c>, which both accept <c>0</c>; a declaration about two built-in constraints that accept no text in
    /// common, such as <c>int</c> and <c>guid</c>, is true and changes nothing. Beyond that check the declaration is
    /// trusted. If it is false, some path is matched by both templates, and for such a path the one mapped first wins,
    /// so the order the routes were mapped in decides the route. <see cref="RouteTable.Format(AppRoute)"/> and
    /// <see cref="RouteTable.FormatUri(AppRoute, Uri)"/> then refuse to write a route of the template mapped later
    /// whose path the earlier one matches, because <see cref="RouteTable.Match(Uri)"/> of it would select the earlier,
    /// and the message says that the earlier template ranks equally and was mapped first.
    /// </para>
    /// <para>
    /// When this method throws, the builder is not changed.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// One of <paramref name="names"/> is <see langword="null"/>, wherever it is in the list and however many names
    /// there are; this is checked before anything else. The exception's <see cref="ArgumentException.ParamName"/> is
    /// <c>names</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="names"/> has fewer than two names, which includes an array that is <see langword="null"/>; or
    /// one of them is not an ASCII letter followed by ASCII letters, digits, <c>-</c> or <c>_</c>; or one is reserved
    /// for a later built-in constraint, as <see cref="AddConstraint(string, Func{string, bool})"/> describes, and so
    /// can never be a constraint; or one is the same as an earlier one, ignoring case. The number of names is checked
    /// first; then each name in turn, its form, then whether it is reserved, then whether it repeats an earlier one,
    /// and the first failure is reported, naming the name. The exception's <see cref="ArgumentException.ParamName"/>
    /// is <c>names</c>.
    /// </exception>
    public RouteTableBuilder DeclareDisjointConstraints(params ReadOnlySpan<string> names)
    {
        // A null name is a mistake in the call rather than in a name, so it is reported before anything else.
        foreach (string name in names)
            ArgumentNullException.ThrowIfNull(name, nameof(names));

        if (names.Length < 2)
        {
            throw new ArgumentException(
                "DeclareDisjointConstraints takes two or more constraint names, every two of which are declared to " +
                "accept no text in common.",
                nameof(names));
        }

        // Every name is checked before any pair is kept, so that a call that fails leaves the builder as it was.
        var lower = new string[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            string name = names[i];
            if (!RouteConstraint.IsAddableName(name))
            {
                throw new ArgumentException(
                    $"The constraint name '{RouteText.EscapeForMessage(name)}' is not valid; the name of a " +
                    "constraint is an ASCII letter followed by ASCII letters, digits, '-' or '_'.",
                    nameof(names));
            }

            lower[i] = name.ToLowerInvariant();
            if (RouteConstraint.IsReserved(name))
            {
                throw new ArgumentException(
                    $"The constraint name '{lower[i]}' is reserved, so that a later built-in constraint can have it, " +
                    "and no constraint has it yet.",
                    nameof(names));
            }

            // A constraint always accepts what it accepts, so it can never be disjoint from itself.
            if (Array.IndexOf(lower, lower[i], 0, i) >= 0)
            {
                throw new ArgumentException(
                    $"The constraint name '{lower[i]}' is given twice, ignoring case, but a constraint always " +
                    "overlaps itself.",
                    nameof(names));
            }
        }

        for (var i = 0; i < lower.Length; i++)
        {
            for (int j = i + 1; j < lower.Length; j++)
            {
                (string, string) key = string.CompareOrdinal(lower[i], lower[j]) < 0
                    ? (lower[i], lower[j])
                    : (lower[j], lower[i]);
                if (_disjointKeys.Add(key))
                    _disjointPairs.Add((lower[i], lower[j]));
            }
        }

        return this;
    }

    /// <summary>
    /// Builds a route table from the mappings made so far.
    /// </summary>
    /// <returns>
    /// A new, immutable table. Mappings and registrations made on this builder afterwards do not change it, and this
    /// method can be called again to build another table that includes them. A table with no mappings is valid and
    /// matches nothing. Its <see cref="RouteTable.Definitions"/> are in the order they were mapped, and the order in
    /// which <see cref="RouteTable.Match(Uri)"/> tries them, by precedence, is decided here, once.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// A template names a constraint that is neither built in nor added with
    /// <see cref="AddConstraint(string, Func{string, bool})"/>. The message names the template, the route type and
    /// the constraint, and lists the built-in constraints. A misspelled built-in name, such as <c>itn</c>, is
    /// reported here too, and the message then names the built-in constraint it resembles. The first such
    /// constraint, in the order the routes were mapped and then in template order, is reported.
    /// <para>-or-</para>
    /// Two mappings have the same template: the same number of segments and, at every position, the same kind of
    /// segment, the same decoded literal text, compared ordinally and case-sensitively, and the same constraint or
    /// none. Parameter names do not count, so <c>/a/{x}</c> and <c>/a/{y}</c> are the same. The message names both
    /// templates and both route types, the earlier mapping first: the first mapping, in the order the routes were
    /// mapped, whose template repeats an earlier one is reported, with the mapping it repeats. When two different route
    /// types have the same name, the message writes both in full: with their namespace where they have one, or, when
    /// that still leaves them alike, as for generic types whose arguments share a name, as
    /// <see cref="Type.ToString()"/> writes them, and with the name of their assembly when even that leaves them
    /// alike, or, when that still leaves them alike, as their assembly-qualified names.
    /// <para>-or-</para>
    /// Two mappings have templates that are not the same but rank equally at every position and both match some
    /// path, such as <c>/products/{id:int}</c> and <c>/products/{n:long}</c>, which both match <c>/products/0</c>, or
    /// <c>/a/{x:int?}</c> and <c>/a/{y:alpha?}</c>, which both match <c>/a</c>. The route that won for such a path
    /// would depend on the order of the mappings. The message names both templates and both route types, the earlier
    /// mapping first, and one path that both match: the first mapping, in the order the routes were mapped, that
    /// conflicts with an earlier one is reported, with the earliest it conflicts with. As for the same template, two
    /// different route types of the same name are written in full: with their namespace where they have one, or as
    /// <see cref="Type.ToString()"/> writes them when that still leaves them alike, and with the name of their
    /// assembly when even that leaves them alike, or, when that still leaves them alike, as their assembly-qualified
    /// names. A constraint added with
    /// <see cref="AddConstraint(string, Func{string, bool})"/> is treated as overlapping every constraint, so two such
    /// templates are reported even when no text is known that both match, unless the two constraints are declared
    /// disjoint with <see cref="DeclareDisjointConstraints(ReadOnlySpan{string})"/>; the message then names no path,
    /// but the constraints at the first required position where no common text is known. The same template twice is
    /// looked for first, across the whole table, so it is reported even when two other mappings that conflict in this
    /// way come earlier. Templates that rank differently can match the same path, because precedence decides between
    /// them.
    /// <para>-or-</para>
    /// Two mappings of the same route type both have a format function, given by
    /// <see cref="Map{TRoute}(string, Func{RouteMatchContext, TRoute}, Action{RouteFormatBuilder{TRoute}})"/>, so
    /// which template writes a route of that type would be ambiguous. The message names the route type and both
    /// templates, the earlier mapping first. Only the exact type counts, so a base type and a type derived from it
    /// can each have one.
    /// <para>-or-</para>
    /// A format function writes a value as a type that is not built in and has no codec: an enum, or the nullable form
    /// of one, that is not registered with <see cref="AddEnumValueCodec{TEnum}"/> or
    /// <see cref="AddValueCodec{TValue}"/>, or any other type, or the nullable form of one, that is not registered with
    /// <see cref="AddValueCodec{TValue}"/>. The message names the route type, the template, the path parameter or query
    /// value, the type and the method that registers it: <see cref="AddEnumValueCodec{TEnum}"/> for an enum, which it
    /// says can also be given a codec of its own, and <see cref="AddValueCodec{TValue}"/> for any other type.
    /// <para>-or-</para>
    /// A name given to <see cref="DeclareDisjointConstraints(ReadOnlySpan{string})"/> is neither built in nor added
    /// with <see cref="AddConstraint(string, Func{string, bool})"/>. The message names the constraint, says that
    /// <see cref="DeclareDisjointConstraints(ReadOnlySpan{string})"/> named it, and, for a misspelled built-in name,
    /// names the built-in constraint it resembles. The first such name, in the order the names were first declared, is
    /// reported.
    /// <para>-or-</para>
    /// Both constraints of a pair declared disjoint with <see cref="DeclareDisjointConstraints(ReadOnlySpan{string})"/>
    /// accept one of the sample texts <c>0</c>, <c>a</c>, <c>true</c> and
    /// <c>00000000-0000-0000-0000-000000000000</c>, so the declaration is false. The message names both constraints
    /// and the sample text. Build passes each sample text to the function of each constraint of a declared pair
    /// before it reports any pair, so which texts a function receives does not depend on the order of the names or of
    /// the declarations. The first such pair, in the order the pairs were first declared, is reported, with the first
    /// sample, in that order, that both accept.
    /// <para/>
    /// The constraints the templates name are checked first, then the names declared disjoint, then the declared pairs
    /// against the samples, then the templates, then that no route type has two format functions, then the types the
    /// format functions write, each in the order the routes were mapped or the names declared. In every case, the
    /// builder is not changed.
    /// </exception>
    /// <exception cref="Exception">
    /// Any exception that the function of a constraint added with
    /// <see cref="AddConstraint(string, Func{string, bool})"/> throws when it is called with a sample text, to check a
    /// declared pair or to compare two templates, unchanged.
    /// </exception>
    public RouteTable Build()
    {
        ThrowIfConstraintNotAdded();
        DisjointConstraints disjoint = ResolveDisjointConstraints();

        // The codecs themselves never change, so tables can share them; only the dictionary is copied.
        return new RouteTable(_definitions.ToArray(), _codecs.ToFrozenDictionary(), disjoint);
    }

    // The resolver that lets the parser leave a name that is not built in to this builder, made on first use so that
    // a builder that maps nothing allocates none.
    private Func<string, RouteConstraint> ResolveConstraint()
    {
        return _resolveConstraint ??= name => _constraints.TryGetValue(name, out RouteConstraint? constraint)
            ? constraint
            : AddUnbound(name);
    }

    private RouteConstraint AddUnbound(string name)
    {
        RouteConstraint constraint = RouteConstraint.Unbound(name);
        _constraints.Add(name, constraint);
        return constraint;
    }

    // A table must never hold a constraint without its function, so a template whose constraint has not been added
    // yet is reported before anything calls one. Only the templates of the mappings count: a name that a template
    // which then failed to map named stays in _constraints unbound, and is no error.
    private void ThrowIfConstraintNotAdded()
    {
        foreach (RouteDefinition definition in _definitions)
        {
            foreach (RouteSegment segment in definition.Template.Segments)
            {
                if (segment.Constraint is not { IsBound: false } constraint)
                    continue;

                // The parser has already rejected a reserved name, which could never be added.
                // A misspelled built-in name has the form of an added one, so it arrives here and not at Map, and the
                // likeliest fix is then to correct the template, which the message says first.
                string name = constraint.Name;
                string? similar = RouteConstraint.FindSimilarBuiltIn(name);
                throw new InvalidOperationException(
                    $"The template '{definition.Template.Value}', mapped to route type " +
                    $"'{RouteValueTypes.DisplayName(definition.RouteType)}', names the constraint '{name}', which is " +
                    $"neither built in nor added on this builder. The built-in constraints are " +
                    $"{RouteConstraint.SupportedNames}, ignoring case. " +
                    (similar is null
                        ? "If one of them was meant, correct the template. Otherwise add "
                        : $"If '{similar}' was meant, correct the template. Otherwise add ") +
                    $"it with AddConstraint(\"{name}\", ...) on this builder, before or after the Map call.");
            }
        }
    }

    // Resolves the declared pairs to the constraints of this builder, made anew for each table so that a later
    // declaration cannot reach a table already built. Every name is resolved before any function is called, as for the
    // templates. Then each constraint of a declared pair is asked about every sample, once, in the order the
    // constraints were first declared and with no answer skipped, so that which texts a function receives, and so
    // whether an exception it throws for one propagates, depends neither on the order of the names in a declaration
    // nor on which declaration came first. Only then is each pair checked, in the order declared: the samples can
    // prove a declaration false but never true, and past that it is trusted.
    private DisjointConstraints ResolveDisjointConstraints()
    {
        if (_disjointPairs.Count == 0)
            return DisjointConstraints.None;

        var resolved = new (RouteConstraint First, RouteConstraint Second)[_disjointPairs.Count];
        for (var i = 0; i < resolved.Length; i++)
        {
            (string first, string second) = _disjointPairs[i];
            resolved[i] = (ResolveDeclared(first), ResolveDeclared(second));
        }

        var accepted = new Dictionary<RouteConstraint, bool[]>();
        foreach ((RouteConstraint first, RouteConstraint second) in resolved)
        {
            AskAboutSamples(first, accepted);
            AskAboutSamples(second, accepted);
        }

        foreach ((RouteConstraint first, RouteConstraint second) in resolved)
        {
            bool[] byFirst = accepted[first];
            bool[] bySecond = accepted[second];
            for (var i = 0; i < byFirst.Length; i++)
            {
                if (!(byFirst[i] && bySecond[i]))
                    continue;

                // The sample is the library's own text, not a value from a route, so it can be quoted.
                throw new InvalidOperationException(
                    $"The constraints '{first.Name}' and '{second.Name}' are declared disjoint by " +
                    $"DeclareDisjointConstraints, but both accept the text '{RouteConstraint.Samples[i]}', so the " +
                    "declaration is false. Remove the declaration, or change a constraint so that the two accept no " +
                    "text in common.");
            }
        }

        return new DisjointConstraints(resolved);
    }

    // Records which samples a constraint accepts, asking its function about each of them unless an earlier pair
    // already has.
    private static void AskAboutSamples(RouteConstraint constraint, Dictionary<RouteConstraint, bool[]> accepted)
    {
        if (accepted.ContainsKey(constraint))
            return;

        ReadOnlySpan<string> samples = RouteConstraint.Samples;
        var answers = new bool[samples.Length];
        for (var i = 0; i < samples.Length; i++)
            answers[i] = constraint.IsSatisfiedBy(samples[i]);

        accepted.Add(constraint, answers);
    }

    // The constraint a declared name names: a built-in one, or one added on this builder, which a template that failed
    // to map may have left unbound. The name is already in lower case.
    private RouteConstraint ResolveDeclared(string name)
    {
        if (RouteConstraint.Find(name) is { } builtIn)
            return builtIn;

        if (_constraints.TryGetValue(name, out RouteConstraint? constraint) && constraint.IsBound)
            return constraint;

        string? similar = RouteConstraint.FindSimilarBuiltIn(name);
        throw new InvalidOperationException(
            $"The constraint '{name}', named by DeclareDisjointConstraints, is neither built in nor added on this " +
            $"builder. The built-in constraints are {RouteConstraint.SupportedNames}, ignoring case. " +
            (similar is null
                ? "If one of them was meant, correct the call. Otherwise add "
                : $"If '{similar}' was meant, correct the call. Otherwise add ") +
            $"it with AddConstraint(\"{name}\", ...) on this builder, before or after the DeclareDisjointConstraints " +
            "call.");
    }

    // A second codec for a type would make which one a table reads and writes it with depend on the order of the
    // calls, so the type keeps the one it has, whichever method registered it.
    private static ArgumentException AlreadyHasCodec(Type type, string registeredBy, string paramName)
    {
        return new ArgumentException(
            $"The type '{RouteValueTypes.DisplayName(type)}' already has a codec on this builder, registered with " +
            $"{registeredBy}, and a type has one codec.",
            paramName);
    }
}
