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

    // The codecs registered so far, each under the type it reads, an enum and its nullable form side by side.
    private readonly Dictionary<Type, object> _codecs = [];

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
    /// <see cref="Build"/>, not here. The order of the calls sets the order of <see cref="RouteTable.Definitions"/>,
    /// but never which mapping wins when several match, which follows the precedence of their templates as
    /// <see cref="RouteTable.Match(Uri)"/> describes.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="template"/> or <paramref name="createRoute"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="template"/> is not a valid route template, for any of the reasons that
    /// <see cref="RouteTemplate.Parse(string)"/> gives. The exception's <see cref="ArgumentException.ParamName"/> is
    /// <c>template</c>.
    /// </exception>
    public RouteTableBuilder Map<TRoute>(string template, Func<RouteMatchContext, TRoute> createRoute)
        where TRoute : AppRoute
    {
        RouteTemplate parsed = RouteTemplate.Parse(template, nameof(template));
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
    /// each query value the route is written with, as in <c>.Query("page", r =&gt; r.Page)</c>. It is called once,
    /// here, and must declare every parameter of the template: required, optional and catch-all. Query values are
    /// optional, one value for each name. For a template with no parameters and a route with no query values, pass an
    /// action that declares nothing, such as <c>_ =&gt; { }</c>. An exception it throws propagates unchanged.
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
    /// A value declared as an enum must be registered with <see cref="AddEnumValueCodec{TEnum}"/> before
    /// <see cref="Build"/>, before or after this call; <see cref="Build"/> reports one that is not.
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
    /// <see cref="RouteTemplate.Parse(string)"/> gives; the exception's <see cref="ArgumentException.ParamName"/> is
    /// <c>template</c>.
    /// <para>-or-</para>
    /// <paramref name="format"/> did not declare every parameter of the template. The message names the template and
    /// the first parameter, in template order, that is not declared, and the exception's
    /// <see cref="ArgumentException.ParamName"/> is <c>format</c>.
    /// <para>-or-</para>
    /// <paramref name="format"/> declared a name that is not a parameter of the template, or declared a parameter
    /// twice, as <see cref="RouteFormatBuilder{TRoute}.Path{TValue}(string, Func{TRoute, TValue})"/> describes, or
    /// declared a query value name twice or one with a lone surrogate, as
    /// <see cref="RouteFormatBuilder{TRoute}.Query{TValue}(string, Func{TRoute, TValue})"/> describes; the
    /// exception's <see cref="ArgumentException.ParamName"/> is <c>name</c>.
    /// <para>-or-</para>
    /// <typeparamref name="TRoute"/> is abstract, which happens when it is inferred from a create function whose
    /// result is a base type. The exception's <see cref="ArgumentException.ParamName"/> is <c>TRoute</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="format"/> declared a value of a type that cannot be written, as
    /// <see cref="RouteFormatBuilder{TRoute}.Path{TValue}(string, Func{TRoute, TValue})"/> and
    /// <see cref="RouteFormatBuilder{TRoute}.Query{TValue}(string, Func{TRoute, TValue})"/> describe.
    /// </exception>
    public RouteTableBuilder Map<TRoute>(
        string template,
        Func<RouteMatchContext, TRoute> createRoute,
        Action<RouteFormatBuilder<TRoute>> format)
        where TRoute : AppRoute
    {
        RouteTemplate parsed = RouteTemplate.Parse(template, nameof(template));
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
    /// which links work, so treat the names of a registered enum as part of the links of the app. For another
    /// spelling, such as <c>in-progress</c>, read the value as a string and map it yourself.
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
    /// <c>a</c>, so a text that names one would name both. The message names the enum and both members. The builder
    /// is not changed.
    /// </exception>
    public RouteTableBuilder AddEnumValueCodec<TEnum>()
        where TEnum : struct, Enum
    {
        if (_codecs.ContainsKey(typeof(TEnum)))
            return this;

        // Made before either is added, so that an enum that cannot be registered leaves the builder as it was.
        var codec = new EnumValueCodec<TEnum>();
        _codecs.Add(typeof(TEnum), codec);
        _codecs.Add(typeof(TEnum?), new NullableValueCodec<TEnum>(codec));
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
    /// Two mappings have the same template: the same number of segments and, at every position, the same kind of
    /// segment, the same decoded literal text, compared ordinally and case-sensitively, and the same constraint or
    /// none. Parameter names do not count, so <c>/a/{x}</c> and <c>/a/{y}</c> are the same. The message names both
    /// templates and both route types, the earlier mapping first: the first mapping, in the order the routes were
    /// mapped, whose template repeats an earlier one is reported, with the mapping it repeats.
    /// <para>-or-</para>
    /// Two mappings have templates that are not the same but rank equally at every position and both match some
    /// path, such as <c>/products/{id:int}</c> and <c>/products/{n:long}</c>, which both match <c>/products/0</c>, or
    /// <c>/a/{x:int?}</c> and <c>/a/{y:alpha?}</c>, which both match <c>/a</c>. The route that won for such a path
    /// would depend on the order of the mappings. The message names both templates and both route types, the earlier
    /// mapping first, and one path that both match: the first mapping, in the order the routes were mapped, that
    /// conflicts with an earlier one is reported, with the earliest it conflicts with. The same template twice is
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
    /// A format function writes a value as an enum, or the nullable form of one, that is not registered with
    /// <see cref="AddEnumValueCodec{TEnum}"/>. The message names the enum, the route type, the template and
    /// <see cref="AddEnumValueCodec{TEnum}"/>.
    /// <para/>
    /// The templates are checked first, then that no route type has two format functions, then the enums the format
    /// functions write, each in the order the routes were mapped. In every case, the builder is not changed.
    /// </exception>
    public RouteTable Build()
    {
        // The codecs themselves never change, so tables can share them; only the dictionary is copied.
        return new RouteTable(_definitions.ToArray(), _codecs.ToFrozenDictionary());
    }
}
