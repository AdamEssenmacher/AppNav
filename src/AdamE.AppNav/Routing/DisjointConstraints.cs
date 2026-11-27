namespace AdamE.AppNav.Routing;

// The pairs of constraints that RouteTableBuilder.DeclareDisjointConstraints declared to accept no text in common,
// resolved to the constraints of one builder when it builds a table, and used only while that table checks that its
// templates can be told apart. The declarations are kept here, per build, rather than on the constraints, because a
// built-in constraint is one instance shared by every builder, and a declaration on one builder must not change what
// another one's tables accept. Constraints compare by reference, as SameTemplateComparer compares them, since a builder
// has one instance for each name. Each pair is held in both orders, so a lookup is one hash probe whatever the order.
internal sealed class DisjointConstraints
{
    private readonly HashSet<(RouteConstraint, RouteConstraint)> _pairs = [];

    // No pair declared, for a builder that declared none.
    public static DisjointConstraints None { get; } = new();

    public void Add(RouteConstraint x, RouteConstraint y)
    {
        _pairs.Add((x, y));
        _pairs.Add((y, x));
    }

    public bool Contains(RouteConstraint x, RouteConstraint y)
    {
        return _pairs.Count > 0 && _pairs.Contains((x, y));
    }
}
