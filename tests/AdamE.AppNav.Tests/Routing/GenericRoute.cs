namespace AdamE.AppNav.Tests.Routing;

// A generic route type, which Type.ToString() writes with its arity after a backtick and messages write with its
// arguments, as GenericRoute<Int32>.
internal sealed record GenericRoute<T> : AppRoute;
