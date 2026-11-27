using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

// The query writer of a declaration made with RouteFormatBuilder.QueryValues: each element of the list is written as
// its own pair, by exactly the rules a single value of type TValue is written by, which RouteFormatBuilder has checked
// is never a nullable value type, a collection or char.
internal sealed class ListQueryWriter<TRoute, TValue> : QueryWriter<TRoute>
    where TRoute : AppRoute
{
    private readonly Func<TRoute, IEnumerable<TValue>?> _values;

    // Null for a built-in type, as for TypedValueWriter.
    private readonly ValueCodec<TValue>? _codec;

    public ListQueryWriter(string name, string subject, string encodedName, Func<TRoute, IEnumerable<TValue>?> values)
        : this(name, subject, encodedName, values, null)
    {
    }

    private ListQueryWriter(
        string name,
        string subject,
        string encodedName,
        Func<TRoute, IEnumerable<TValue>?> values,
        ValueCodec<TValue>? codec)
        : base(name, subject, encodedName)
    {
        _values = values;
        _codec = codec;
    }

    public override string NoTextReason => TypedValueText<TValue>.NoTextReasonOf(_codec);

    public override QueryWriter<TRoute> Bind(FrozenDictionary<Type, object> codecs, RouteTemplate template)
    {
        ValueCodec<TValue>? codec = TypedValueWriter<TRoute, TValue>.FindCodec(codecs, template, Subject);
        return codec is null ? this : new ListQueryWriter<TRoute, TValue>(Name, Subject, EncodedName, _values, codec);
    }

    // The list is enumerated once, by the one foreach, which disposes its enumerator however it ends, and the elements
    // after one that cannot be written are not read. A null list and an empty one both write nothing. Each element is
    // passed on as TValue, so a value type is never boxed; only an enumerator that is a struct, such as List's, is
    // boxed, once for each list.
    public override QueryValueProblem Write(TRoute route, ref QueryBuilder query)
    {
        IEnumerable<TValue>? values = _values(route);
        if (values is null)
            return QueryValueProblem.None;

        foreach (TValue value in values)
        {
            // Possible only for a reference type, such as string: the element type is never a nullable value type, but
            // the list's own nullable annotations are not enforced at run time.
            if (value is null)
                return QueryValueProblem.NullElement;

            ValueTextProblem problem = TypedValueText<TValue>.Write(value, _codec, out string? text);
            QueryValueProblem appended = query.AppendValue(EncodedName, problem, text);
            if (appended != QueryValueProblem.None)
                return appended;
        }

        return QueryValueProblem.None;
    }
}
