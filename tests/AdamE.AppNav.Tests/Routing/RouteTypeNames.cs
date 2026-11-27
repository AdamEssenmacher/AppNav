using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// The names messages are expected to give the route types of these tests, written out rather than computed, so that a
// test fails when the form a message writes changes.
internal static class RouteTypeNames
{
    public const string Nested = "RouteHolder.NestedRoute";
    public const string Generic = "GenericRoute<Int32>";

    // Orders.DetailRoute and Catalog.DetailRoute share a short name, so a message naming both writes their namespaces.
    public const string OrdersDetail = "AdamE.AppNav.Tests.Routing.Orders.DetailRoute";
    public const string CatalogDetail = "AdamE.AppNav.Tests.Routing.Catalog.DetailRoute";

    // So do Orders.Holder.Inner and Catalog.Holder.Inner, whose namespace comes before the type they are nested in.
    public const string OrdersInner = "AdamE.AppNav.Tests.Routing.Orders.Holder.Inner";
    public const string CatalogInner = "AdamE.AppNav.Tests.Routing.Catalog.Holder.Inner";

    // The global DetailRoute has no namespace, so beside Orders.DetailRoute it is written as it is.
    public const string GlobalDetail = "DetailRoute";

    // GenericRoute over each of them shares the namespace as well, so only the Type.ToString() form tells them apart.
    public const string GenericOfOrdersDetail =
        "AdamE.AppNav.Tests.Routing.GenericRoute`1[AdamE.AppNav.Tests.Routing.Orders.DetailRoute]";
    public const string GenericOfCatalogDetail =
        "AdamE.AppNav.Tests.Routing.GenericRoute`1[AdamE.AppNav.Tests.Routing.Catalog.DetailRoute]";

    // Orders.DetailRoute of this assembly and that of the other test assembly share the full name as well, so only the
    // simple names of their assemblies tell them apart.
    public const string OrdersDetailOfTests = "AdamE.AppNav.Tests.Routing.Orders.DetailRoute, AdamE.AppNav.Tests";
    public const string OrdersDetailOfOtherAssembly =
        "AdamE.AppNav.Tests.Routing.Orders.DetailRoute, AdamE.AppNav.Tests.OtherAssembly";

    // The error for a constraint that is not added is the reference for how a message writes a route type, so a
    // message that names the same type is checked to write it the same way, and never in the Type.ToString() form.
    public static void AssertWrittenAsInTheUnboundConstraintError<TRoute>(string message, string name)
        where TRoute : AppRoute, new()
    {
        RouteTableBuilder builder = new RouteTableBuilder().Map("/unbound/{value:notadded}", _ => new TRoute());
        InvalidOperationException reference = Assert.Throws<InvalidOperationException>(builder.Build);
        Assert.Contains($"'{name}'", reference.Message, StringComparison.Ordinal);

        Assert.Contains($"'{name}'", message, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(TRoute).ToString(), message, StringComparison.Ordinal);
        Assert.DoesNotContain("+", message, StringComparison.Ordinal);
        Assert.DoesNotContain("`", message, StringComparison.Ordinal);
    }
}
