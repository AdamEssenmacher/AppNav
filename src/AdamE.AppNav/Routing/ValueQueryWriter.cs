using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

// The query writer of a declaration made with RouteFormatBuilder.Query: the value writer that a path value would have,
// whose one value, unless it is null, is written as one pair.
internal sealed class ValueQueryWriter<TRoute> : QueryWriter<TRoute>
    where TRoute : AppRoute
{
    private readonly ValueWriter<TRoute> _writer;

    public ValueQueryWriter(ValueWriter<TRoute> writer, string encodedName)
        : base(writer.Name, writer.Subject, encodedName)
    {
        _writer = writer;
    }

    public override string NoTextReason => _writer.NoTextReason;

    public override QueryWriter<TRoute> Bind(FrozenDictionary<Type, object> codecs, RouteTemplate template)
    {
        ValueWriter<TRoute> bound = _writer.Bind(codecs, template);
        return ReferenceEquals(bound, _writer) ? this : new ValueQueryWriter<TRoute>(bound, EncodedName);
    }

    public override QueryValueProblem Write(TRoute route, ref QueryBuilder query)
    {
        ValueTextProblem problem = _writer.Write(route, out string? text);
        if (problem != ValueTextProblem.None || text is null)
            return ProblemOf(problem);

        return query.TryAppend(EncodedName, text) ? QueryValueProblem.None : QueryValueProblem.LoneSurrogate;
    }
}
