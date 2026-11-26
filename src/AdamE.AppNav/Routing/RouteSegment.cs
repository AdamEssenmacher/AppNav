namespace AdamE.AppNav.Routing;

// One segment of a parsed route template, a literal or a parameter, with what matching and formatting need to handle
// it. A readonly struct, stored in an array, so that reading segments on the hot path neither allocates nor dispatches.
internal readonly struct RouteSegment
{
    private RouteSegment(string text, string encodedText, RouteSegmentKind kind, RouteConstraint? constraint)
    {
        Text = text;
        EncodedText = encodedText;
        Kind = kind;
        Constraint = constraint;
    }

    // A literal's decoded text, which path segments are compared with, or a parameter's name.
    public string Text { get; }

    // A literal's text percent-encoded as Format writes it; empty for a parameter.
    public string EncodedText { get; }

    public RouteSegmentKind Kind { get; }

    // A parameter's constraint, built in or added on a builder; null for a literal, a catch-all parameter or a
    // parameter without one.
    public RouteConstraint? Constraint { get; }

    public bool IsParameter => Kind != RouteSegmentKind.Literal;

    public bool IsCatchAll => Kind == RouteSegmentKind.CatchAllParameter;

    // Whether Format leaves this parameter out when it has no entry, and a path may leave out its position; the first
    // such segment is where the segments that every matching path has end.
    public bool CanBeLeftOut => Kind is RouteSegmentKind.OptionalParameter or RouteSegmentKind.CatchAllParameter;

    public static RouteSegment Literal(string text)
    {
        return new RouteSegment(text, Uri.EscapeDataString(text), RouteSegmentKind.Literal, null);
    }

    public static RouteSegment Parameter(string name, RouteConstraint? constraint)
    {
        return new RouteSegment(name, string.Empty, RouteSegmentKind.Parameter, constraint);
    }

    public static RouteSegment OptionalParameter(string name, RouteConstraint? constraint)
    {
        return new RouteSegment(name, string.Empty, RouteSegmentKind.OptionalParameter, constraint);
    }

    // A catch-all parameter takes no constraint, because the parser rejects a catch-all parameter that has one.
    public static RouteSegment CatchAllParameter(string name)
    {
        return new RouteSegment(name, string.Empty, RouteSegmentKind.CatchAllParameter, null);
    }

    // Whether a decoded path segment can stand in this segment's position. A parameter never takes a dot segment, so
    // that every value a match returns can be formatted; a literal is never one, because the parser rejects it. The
    // dot segment is checked first, so that a constraint is never asked about one here, which matters only for an
    // added constraint, since no built-in one accepts a dot segment.
    public bool Accepts(string decoded)
    {
        return IsParameter
            ? !RouteText.IsDotSegment(decoded) && (Constraint is null || Constraint.IsSatisfiedBy(decoded))
            : StringComparer.Ordinal.Equals(Text, decoded);
    }
}
