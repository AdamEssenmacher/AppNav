namespace AdamE.AppNav.Tests.Routing.Orders;

// Shares its name with Catalog.Holder, so that a message naming both of their nested routes must tell them apart by
// namespace.
internal static class Holder
{
    internal sealed record Inner : AppRoute;
}
