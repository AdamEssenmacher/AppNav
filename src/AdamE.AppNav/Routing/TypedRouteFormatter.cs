using System.Collections.Frozen;
using System.Diagnostics;

namespace AdamE.AppNav.Routing;

// The formatter for routes of type TRoute: a writer for every parameter of the template, in template order.
internal sealed class TypedRouteFormatter<TRoute> : RouteFormatter
    where TRoute : AppRoute
{
    private readonly RouteTemplate _template;
    private readonly PathValueWriter<TRoute>[] _writers;

    public TypedRouteFormatter(RouteTemplate template, PathValueWriter<TRoute>[] writers)
    {
        _template = template;
        _writers = writers;
    }

    public override RouteFormatter Bind(FrozenDictionary<Type, object> codecs)
    {
        var bound = new PathValueWriter<TRoute>[_writers.Length];
        for (var i = 0; i < bound.Length; i++)
            bound[i] = _writers[i].Bind(codecs, _template);

        return new TypedRouteFormatter<TRoute>(_template, bound);
    }

    // Each value function is called at most once, in template order, and a value that has no text is reported as
    // soon as it is read, before later value functions are called. The texts then go through the template's own
    // checks, so that a path this writes is one the template writes, and matches it. The dictionary compares keys as
    // a template does, so the template's lookup of each name never has to search it.
    public override string Format(AppRoute route)
    {
        var typed = (TRoute)route;
        var texts = new Dictionary<string, string>(_writers.Length, StringComparer.OrdinalIgnoreCase);
        foreach (PathValueWriter<TRoute> writer in _writers)
        {
            if (!writer.TryWrite(typed, out string? text))
                throw NotWritten(writer.Name, $"has a value that cannot be written: {writer.NoTextReason}");

            if (text is not null)
                texts.Add(writer.Name, text);
        }

        return _template.TryFormat(texts, out string? path, out PathFormatFailure failure)
            ? path
            : throw NotWritten(failure.Parameter.Text, Describe(failure));
    }

    // Route values can be secrets, such as a sign-in token, and messages tend to be logged, so every message here
    // names the route type, the template and the parameter, and says in fixed words what is wrong, but never quotes
    // or describes the value, and the exception has no inner exception that could.
    private ArgumentException NotWritten(string name, string problem)
    {
        return new ArgumentException(
            $"The route type '{RouteValueTypes.DisplayName(typeof(TRoute))}' cannot be written as a path by the " +
            $"template '{_template.Value}': path parameter '{name}' {problem}.",
            "route");
    }

    // These messages say what is structurally wrong, such as an empty or null value or a dot segment, which the
    // developer needs to fix the route, but never what the value contains. The guid constraint's hint is left out:
    // it says the value has the format of a guid without hyphens, and for a token the format is nearly the value.
    private static string Describe(PathFormatFailure failure)
    {
        RouteSegment parameter = failure.Parameter;
        return failure.Problem switch
        {
            PathValueProblem.Missing => "is required, but its value is null",
            PathValueProblem.Empty => parameter.CanBeLeftOut
                ? "has an empty value; to leave the parameter out, its value must be null"
                : "has an empty value, but a parameter always matches exactly one path segment",
            PathValueProblem.LoneSurrogate =>
                "has a value that is not valid UTF-16: it contains a lone surrogate, which cannot be percent-encoded " +
                "and read back unchanged",
            PathValueProblem.EmptyPart =>
                "has a value with an empty part; a catch-all value cannot start or end with '/' or contain '//', " +
                "because a match drops empty segments and would not return it",
            PathValueProblem.ConstraintNotSatisfied =>
                $"has a value that does not satisfy its constraint '{parameter.Constraint!.Name}'",
            PathValueProblem.DotSegment =>
                (parameter.IsCatchAll
                    ? "has a value with a dot segment part ('.' or '..' between slashes), "
                    : "has a value that is a dot segment ('.' or '..'), ") + RouteText.DotSegmentReason,
            PathValueProblem.AfterLeftOut =>
                $"has a value, but it cannot be written because the earlier optional parameter '{failure.LeftOut}' " +
                "is left out, its value being null",

            // Only a value function's null leaves a parameter out, and that never reaches the dictionary.
            _ => throw new UnreachableException("A route formatter put a value in the dictionary that it rejects."),
        };
    }
}
