// In the global namespace on purpose: it shares its name with Orders.DetailRoute, and a message naming both writes
// this one without a namespace, since it has none.
internal sealed record DetailRoute : AdamE.AppNav.AppRoute;
