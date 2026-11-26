namespace AdamE.AppNav.Routing;

// What is known of the text that two constraints, or two templates of one shape, both accept. The built-in constraints
// are each known in full, so of two of them it is always known whether some text satisfies both. A constraint added
// with RouteTableBuilder.AddConstraint is a function that cannot be inspected, so of a pair that involves one, only a
// text both are found to accept is known; without one, the pair may still overlap, and is never taken to be disjoint.
internal enum ConstraintOverlap
{
    // No text satisfies both.
    Disjoint,

    // A text is known that satisfies both.
    CommonValue,

    // Some text may satisfy both, but none is known to.
    MayOverlap,
}
