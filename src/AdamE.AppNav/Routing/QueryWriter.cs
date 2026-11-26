using System.Collections.Frozen;
using System.Diagnostics;

namespace AdamE.AppNav.Routing;

// Writes the pairs of one query declaration, made with RouteFormatBuilder.Query or RouteFormatBuilder.QueryValues:
// none, one, or one for each element of a list, all under the declared name. The route formatter calls each declaration
// in the order it was declared, so the pairs of a list stay together where the list was declared.
internal abstract class QueryWriter<TRoute>
    where TRoute : AppRoute
{
    protected QueryWriter(string name, string subject, string encodedName)
    {
        Name = name;
        Subject = subject;
        EncodedName = encodedName;
    }

    // The name as declared, which the builder compares to find a name declared twice.
    public string Name { get; }

    // What a message calls the declaration, as in "query value 'tag'", with the name quoted so that it cannot break
    // the line it is printed on.
    public string Subject { get; }

    // The name percent-encoded, once, when it was declared, and written before every value.
    public string EncodedName { get; }

    // Ends the message for a value that has no text, without naming or describing the value.
    public abstract string NoTextReason { get; }

    // Returns the writer that a table built with these codecs uses, as ValueWriter.Bind does.
    public abstract QueryWriter<TRoute> Bind(FrozenDictionary<Type, object> codecs, RouteTemplate template);

    // Calls the app's value function once and appends its pairs. Stops at the first value that cannot be written and
    // says why, leaving what it appended before; the formatter then throws, so the query is never returned. An
    // exception that the value function, or a list it returns, throws propagates unchanged.
    public abstract QueryValueProblem Write(TRoute route, ref QueryBuilder query);

    // The problem of the query for a value that could not be written, which is None only when it was.
    protected static QueryValueProblem ProblemOf(ValueTextProblem problem)
    {
        return problem switch
        {
            ValueTextProblem.None => QueryValueProblem.None,
            ValueTextProblem.NoText => QueryValueProblem.NoText,
            ValueTextProblem.NotReadBack => QueryValueProblem.NotReadBack,
            _ => throw new UnreachableException("A value writer reported a problem that the query has no name for."),
        };
    }
}
