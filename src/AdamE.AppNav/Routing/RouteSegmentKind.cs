namespace AdamE.AppNav.Routing;

// What a template segment is, as one value rather than separate flags, so that a segment cannot be, for example, both
// a literal and optional, or both optional and a catch-all.
internal enum RouteSegmentKind
{
    // Text that a path segment must equal.
    Literal,

    // A parameter written '{name}' or '{name:constraint}', which matches exactly one path segment.
    Parameter,

    // A parameter written with a final '?', which a path may leave out.
    OptionalParameter,

    // A parameter written with a first '*', which takes the rest of the path.
    CatchAllParameter,
}
