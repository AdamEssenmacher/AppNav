namespace AdamE.AppNav.Routing;

// Why one value, a path value, a single query value or an element of a list, could not be written as text. The route
// formatter turns it into the message, so that the words stay in one place.
internal enum ValueTextProblem
{
    // The value was written, or, for a path or single query value, was null and is left out.
    None,

    // The value has no text, as its codec or the built-in rule of its type decides.
    NoText,

    // The codec registered by the application wrote a text that its own parser does not accept, so a link with it
    // could not be read back.
    NotReadBack,
}
