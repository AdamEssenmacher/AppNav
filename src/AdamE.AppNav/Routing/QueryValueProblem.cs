namespace AdamE.AppNav.Routing;

// Why a query writer could not write what its value function returned. The route formatter turns it into the message,
// so that every message about a route that cannot be written is made in one place, in the same fixed words.
internal enum QueryValueProblem
{
    // Everything the value function returned was written, or nothing was, for a null value or an empty list.
    None,

    // A value, or an element of a list, has no text that its typed read reads back.
    NoText,

    // A value, or an element of a list, contains a lone surrogate, which cannot be percent-encoded and read back.
    LoneSurrogate,

    // An element of a list is null, which no typed read of a list returns.
    NullElement,
}
