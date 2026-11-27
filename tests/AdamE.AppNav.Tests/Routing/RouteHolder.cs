namespace AdamE.AppNav.Tests.Routing;

// Declares a nested route type, which Type.ToString() writes with '+' and its namespace and messages write as
// RouteHolder.NestedRoute.
internal static class RouteHolder
{
    internal sealed record NestedRoute : AppRoute;
}
