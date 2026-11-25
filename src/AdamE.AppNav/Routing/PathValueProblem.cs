namespace AdamE.AppNav.Routing;

// Why a template cannot write a parameter's value, so that RouteTemplate.Format and RouteTable.Format apply the same
// checks and each words its own message, for the dictionary it was given or for the route it was asked to write.
internal enum PathValueProblem
{
    // A parameter that is not optional or catch-all has no value.
    Missing,

    // The dictionary has an entry for the parameter whose value is null.
    Null,

    Empty,

    // The value contains a lone surrogate, which cannot be percent-encoded and read back unchanged.
    LoneSurrogate,

    // A catch-all value starts or ends with '/' or contains '//'.
    EmptyPart,

    ConstraintNotSatisfied,

    // The value, or for a catch-all a part of it between slashes, is '.' or '..'.
    DotSegment,

    // An optional parameter has a value, but an earlier optional parameter is left out.
    AfterLeftOut,
}
