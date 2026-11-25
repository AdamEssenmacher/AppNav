namespace AdamE.AppNav.Routing;

// Whether two templates are the same template: the same number of segments and, at every position, the same kind of
// segment, the same decoded literal text, compared ordinally, and the same constraint. Parameter names and how the
// text was written do not count, so two templates that are the same match exactly the same paths and rank equally,
// and a route table could never reach the one mapped second. It compares parsed segments rather than canonical
// strings, because a decoded literal can contain any character, including any separator such a string would use.
//
// SameShape compares everything but which constraint a parameter has, only whether it has one. Templates of the same
// shape rank equally at every position, and only they can rank equally and match the same path, because literals are
// always required and different literals never accept the same segment; so a route table needs to look for such a
// pair only among templates of the same shape.
internal sealed class SameTemplateComparer : IEqualityComparer<RouteTemplate>
{
    private readonly bool _compareConstraints;

    private SameTemplateComparer(bool compareConstraints)
    {
        _compareConstraints = compareConstraints;
    }

    public static SameTemplateComparer Instance { get; } = new(compareConstraints: true);

    public static SameTemplateComparer SameShape { get; } = new(compareConstraints: false);

    public bool Equals(RouteTemplate? x, RouteTemplate? y)
    {
        if (ReferenceEquals(x, y))
            return true;

        if (x is null || y is null || x.Segments.Length != y.Segments.Length)
            return false;

        for (var i = 0; i < x.Segments.Length; i++)
        {
            if (!IsSameSegment(x.Segments[i], y.Segments[i]))
                return false;
        }

        return true;
    }

    // Hashes exactly what Equals compares, so that equal templates hash equally whatever their names and spelling.
    public int GetHashCode(RouteTemplate obj)
    {
        var hash = new HashCode();
        hash.Add(obj.Segments.Length);
        foreach (RouteSegment segment in obj.Segments)
        {
            hash.Add(segment.Kind);
            if (!segment.IsParameter)
                hash.Add(segment.Text, StringComparer.Ordinal);
            else if (_compareConstraints)
                hash.Add(segment.Constraint);
            else
                hash.Add(segment.Constraint is null);
        }

        return hash.ToHashCode();
    }

    // Whether two segments in the same position are the same. Constraints compare by reference, because each built-in
    // constraint is one instance, whatever case its name was written in.
    private bool IsSameSegment(in RouteSegment x, in RouteSegment y)
    {
        if (x.Kind != y.Kind)
            return false;

        if (!x.IsParameter)
            return StringComparer.Ordinal.Equals(x.Text, y.Text);

        return _compareConstraints
            ? ReferenceEquals(x.Constraint, y.Constraint)
            : x.Constraint is null == y.Constraint is null;
    }
}
