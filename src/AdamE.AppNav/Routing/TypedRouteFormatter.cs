using System.Collections.Frozen;
using System.Diagnostics;

namespace AdamE.AppNav.Routing;

// The formatter for routes of type TRoute: a writer for every parameter of the template, in template order, and a
// writer for every query declaration, in the order they were declared, each with its name already percent-encoded.
internal sealed class TypedRouteFormatter<TRoute> : RouteFormatter
    where TRoute : AppRoute
{
    // Ends the message for a value with a lone surrogate, in the path or the query alike.
    private const string LoneSurrogate =
        "has a value that is not valid UTF-16: it contains a lone surrogate, which cannot be percent-encoded and " +
        "read back unchanged";

    private readonly RouteTemplate _template;
    private readonly ValueWriter<TRoute>[] _pathWriters;
    private readonly QueryWriter<TRoute>[] _queryWriters;

    public TypedRouteFormatter(
        RouteTemplate template,
        ValueWriter<TRoute>[] pathWriters,
        QueryWriter<TRoute>[] queryWriters)
    {
        _template = template;
        _pathWriters = pathWriters;
        _queryWriters = queryWriters;
    }

    public override RouteFormatter Bind(FrozenDictionary<Type, object> codecs)
    {
        var pathWriters = new ValueWriter<TRoute>[_pathWriters.Length];
        for (var i = 0; i < pathWriters.Length; i++)
            pathWriters[i] = _pathWriters[i].Bind(codecs, _template);

        var queryWriters = new QueryWriter<TRoute>[_queryWriters.Length];
        for (var i = 0; i < queryWriters.Length; i++)
            queryWriters[i] = _queryWriters[i].Bind(codecs, _template);

        return new TypedRouteFormatter<TRoute>(_template, pathWriters, queryWriters);
    }

    // Each value function is called at most once: the path functions in template order, then the query functions in
    // the order they were declared. A value that has no text is reported as soon as it is read, before later value
    // functions are called. The path texts go through the template's own checks before any query function is called,
    // so that a path this writes is one the template writes, and matches it, and a path that cannot be written stops
    // the calls as a value without text does. The dictionary compares keys as a template does, so the template's
    // lookup of each name never has to search it.
    public override string Format(AppRoute route, out string path)
    {
        var typed = (TRoute)route;
        var texts = new Dictionary<string, string>(_pathWriters.Length, StringComparer.OrdinalIgnoreCase);
        foreach (ValueWriter<TRoute> writer in _pathWriters)
        {
            if (!writer.TryWrite(typed, out string? text))
                throw NotWritten(writer.Subject, $"has a value that cannot be written: {writer.NoTextReason}");

            if (text is not null)
                texts.Add(writer.Name, text);
        }

        if (!_template.TryFormat(texts, out string? written, out PathFormatFailure failure))
            throw NotWritten($"path parameter '{failure.Parameter.Text}'", Describe(failure));

        path = written;
        return _queryWriters.Length == 0 ? written : AppendQuery(typed, written);
    }

    // Each declaration appends its own pairs, so the pairs of a list stay where the list was declared among the
    // others. A declaration that cannot write its value stops the calls, as a path value does.
    private string AppendQuery(TRoute route, string path)
    {
        var query = new QueryBuilder(path);
        foreach (QueryWriter<TRoute> writer in _queryWriters)
        {
            QueryValueProblem problem = writer.Write(route, ref query);
            if (problem != QueryValueProblem.None)
                throw NotWritten(writer.Subject, Describe(problem, writer.NoTextReason));
        }

        return query.ToString();
    }

    // Route values can be secrets, such as a sign-in token, and messages tend to be logged, so every message here
    // names the route type, the template and the path parameter or query value, and says in fixed words what is
    // wrong, but never quotes or describes the value, and the exception has no inner exception that could.
    private ArgumentException NotWritten(string subject, string problem)
    {
        return new ArgumentException(
            $"The route type '{RouteValueTypes.DisplayName(typeof(TRoute))}' cannot be written by the template " +
            $"'{_template.Value}': {subject} {problem}.",
            "route");
    }

    // Which element of a list failed goes unsaid: the kind of failure tells the developer what to look for, and the
    // position would say how many values come before it, which is something about the route's data.
    private static string Describe(QueryValueProblem problem, string noTextReason)
    {
        return problem switch
        {
            QueryValueProblem.NoText => $"has a value that cannot be written: {noTextReason}",
            QueryValueProblem.LoneSurrogate => LoneSurrogate,
            QueryValueProblem.NullElement =>
                "has a null element, which cannot be written, because an element of a list is never absent; leave " +
                "it out of the list instead",
            _ => throw new UnreachableException("A query writer reported a problem that has no message."),
        };
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
            PathValueProblem.LoneSurrogate => LoneSurrogate,
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
