# Routing

## Route templates

A route template is the path pattern that a route answers to, such as `/settings/profile`. `RouteTemplate.Parse` turns the template text into a `RouteTemplate`, and `Match` tells you whether a path fits it.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate template = RouteTemplate.Parse("/settings/profile");

Console.WriteLine(template.Match("/settings/profile") is not null);  // True
Console.WriteLine(template.Match("settings//profile/") is not null); // True: slashes are normalized
Console.WriteLine(template.Match("/Settings/profile") is not null);  // False: segments are case-sensitive
Console.WriteLine(template.Match("/settings") is not null);          // False: a segment is missing
```

A template must start with `/`. It is divided into segments at each `/`, and every segment is literal text. A path matches when it has the same number of segments as the template and each segment equals the template's segment in the same position. `Match` returns an empty, read-only dictionary for a match and `null` otherwise.

Templates and paths are divided and decoded the same way:

- Leading, trailing and repeated slashes do not create segments. `/a//b/` has the same segments as `/a/b`, and `/` is the root template, which has no segments.
- Each segment is percent-decoded once, after the text is divided. `/files/a%20b` matches `/files/a b`, and `%2F` stays inside its segment, so `/files/a%2Fb` does not match `/files/a/b`. A percent sequence that is not valid, or does not decode to valid UTF-8, such as `%zz` or `%FF`, is kept as written.
- Segments are compared ordinally and case-sensitively.

A path is otherwise taken as it is:

- The leading `/` is optional.
- White space is significant: `/settings ` does not match `/settings`.
- A query string or fragment is not removed, so remove it before calling `Match`.
- `+` is not a space, and `.` and `..` are ordinary segments.

## Invalid templates

`Parse` throws `ArgumentNullException` for `null`. It throws `ArgumentException` when the text is empty or only white space, does not start with `/`, or has a segment that:

- contains a control character (one for which `char.IsControl` is true), such as a tab, line feed or null character, anywhere in the segment,
- contains `{` or `}`,
- contains `?` or `#`, which cannot appear in the path of a URI, or
- starts or ends with white space. Spaces inside a segment, as in `/my files`, are allowed.

A control character is reported as a control character even when it is also white space, as a tab or line feed at the start of a segment is, and the exception message shows it as a `\uXXXX` escape, such as `\u0009`, rather than printing it.

These checks apply to the text as written, before decoding. To match one of these characters, percent-encode it: `%09` for a tab, `%0A` for a line feed, `%00` for a null character (and the UTF-8 bytes of any other control character, such as `%C2%85` for U+0085), `%7B` for `{`, `%7D` for `}`, `%3F` for `?`, `%23` for `#` and `%20` for a space at the start or end of a segment. For example, `/what%3F` matches the path `/what%3F`, and `/a%09b` matches a path whose segment is `a`, a tab and `b`.
