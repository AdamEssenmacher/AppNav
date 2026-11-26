using System.Collections.Frozen;

namespace AdamE.AppNav.Routing;

// Reads one value from a route and writes it as text, for a format function declared with RouteFormatBuilder.Path or
// RouteFormatBuilder.Query, which ValueQueryWriter wraps. The type of the value is known only to the derived class, so
// that the route formatter that calls it needs no type argument but the route's.
internal abstract class ValueWriter<TRoute>
    where TRoute : AppRoute
{
    protected ValueWriter(string name, string subject)
    {
        Name = name;
        Subject = subject;
    }

    // For a path value, the parameter's name as written in the template, under which RouteTemplate.TryFormat looks the
    // text up; for a query value, the name as declared.
    public string Name { get; }

    // What a message calls the value, as in "path parameter 'id'" or "query value 'page'", with the name quoted so that
    // it cannot break the line it is printed on.
    public string Subject { get; }

    // Ends the message for a value that TryWrite cannot write, without naming or describing the value.
    public abstract string NoTextReason { get; }

    // Returns the writer that a table built with these codecs uses. A writer for a type that is not built in takes its
    // codec now, once, so that writing never looks it up; one whose codec is missing throws, so that Build reports an
    // enum that was never registered rather than the first Format that writes it.
    public abstract ValueWriter<TRoute> Bind(FrozenDictionary<Type, object> codecs, RouteTemplate template);

    // Calls the app's value function once. Returns true with the text, or with null when the value is null, which
    // leaves the value out; returns false when the value has no text. An exception that the value function throws
    // propagates unchanged.
    public abstract bool TryWrite(TRoute route, out string? text);
}
