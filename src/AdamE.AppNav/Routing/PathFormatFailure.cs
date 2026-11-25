namespace AdamE.AppNav.Routing;

// The parameter that a template could not write and why. It never holds the value, which can be a secret, only the
// constraint's hint for it, which never repeats it.
internal readonly struct PathFormatFailure
{
    public PathFormatFailure(PathValueProblem problem, RouteSegment parameter, string? leftOut, string hint)
    {
        Problem = problem;
        Parameter = parameter;
        LeftOut = leftOut;
        Hint = hint;
    }

    public PathValueProblem Problem { get; }

    public RouteSegment Parameter { get; }

    // For AfterLeftOut, the name of the earlier optional parameter that was left out; otherwise null.
    public string? LeftOut { get; }

    // For ConstraintNotSatisfied, what the constraint adds to its message, which may be empty; otherwise empty.
    public string Hint { get; }
}
