# Routing

## Route templates

A route template is the path pattern that a route answers to, such as `/settings/profile`. `RouteTemplate.Parse` turns the template text into a `RouteTemplate`, and `Match` tells you whether a path fits it and, when the template has [parameters](#path-parameters), what they captured. [`Format`](#formatting-a-path) builds a path from parameter values.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate template = RouteTemplate.Parse("/settings/profile");

Console.WriteLine(template.Match("/settings/profile") is not null);  // True
Console.WriteLine(template.Match("settings//profile/") is not null); // True: slashes are normalized
Console.WriteLine(template.Match("/Settings/profile") is not null);  // False: segments are case-sensitive
Console.WriteLine(template.Match("/settings") is not null);          // False: a segment is missing
```

A template must start with `/`. It is divided into segments at each `/`. A segment is either literal text or a [parameter](#path-parameters), which can carry a [constraint](#constraints) and can be [optional](#optional-parameters). A path matches when it has the same number of segments as the template, apart from [optional parameters](#optional-parameters), which it can leave out only from the end, and each literal equals the path segment in the same position. `Match` returns `null` when the path does not match, and a dictionary of captured values when it does; when nothing is captured, the dictionary is empty.

Templates and paths are divided and decoded the same way:

- Leading, trailing and repeated slashes do not create segments. `/a//b/` has the same segments as `/a/b`, and `/` is the root template, which has no segments.
- Each segment is percent-decoded once, after the text is divided. `/files/a%20b` matches `/files/a b`, and `%2F` stays inside its segment, so `/files/a%2Fb` does not match `/files/a/b`. A percent sequence that is not valid, or does not decode to valid UTF-8, such as `%zz` or `%FF`, is kept as written.
- Literals are compared with path segments ordinally and case-sensitively.

A path is otherwise taken as it is:

- The leading `/` is optional.
- White space is significant: `/settings ` does not match `/settings`.
- A query string or fragment is not removed, so remove it before calling `Match`.
- `+` is not a space, and `.` and `..` are ordinary segments.

## Path parameters

A segment written `{name}` is a parameter. It matches any one path segment and captures that segment's decoded text under its name.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate product = RouteTemplate.Parse("/stores/{storeId}/products/{productId}");

IReadOnlyDictionary<string, string>? values = product.Match("/stores/north%20wind/products/42");
Console.WriteLine(values?["storeId"]);   // north wind
Console.WriteLine(values?["PRODUCTID"]); // 42: names are not case-sensitive

Console.WriteLine(product.Match("/stores/northwind/products") is null); // True: a segment is missing
```

- A parameter is the whole segment. Its name starts with an ASCII letter or `_`, followed by ASCII letters, digits or `_`.
- Names are compared without regard to case. A template cannot use two names that differ only by case. The dictionary `Match` returns is keyed by the names as written in the template and looks them up without regard to case.
- A parameter matches exactly one segment, unless it is [optional](#optional-parameters). Its value is the segment after decoding, exactly as a literal would be compared: `%2F` becomes `/` inside the value, white space is kept, and an invalid percent sequence is kept as written. Unless the parameter has a [constraint](#constraints), values are not checked further, so a value can be any text, including a decoded control character.
- Each match that captures a value returns its own dictionary. Treat it as read-only.

## Constraints

A parameter written `{name:constraint}` matches a segment only when the segment's decoded text satisfies the constraint. A segment that does not satisfy it is not an error: the path does not match this template, and may match another.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate order = RouteTemplate.Parse("/stores/{store:alpha}/orders/{id:int}");

Console.WriteLine(order.Match("/stores/northwind/orders/42")?["id"]);     // 42
Console.WriteLine(order.Match("/stores/northwind/orders/+007")?["id"]);   // +007: the value is not normalized
Console.WriteLine(order.Match("/stores/northwind/orders/4.2") is null);   // True: 4.2 is not an int
Console.WriteLine(order.Match("/stores/northwind/orders/%2042") is null); // True: white space is not trimmed
Console.WriteLine(order.Match("/stores/north%20wind/orders/42") is null); // True: a space is not a letter
```

The built-in constraints are:

| Constraint | Satisfied by |
| --- | --- |
| `int` | An optional leading `+` or `-` followed by one or more ASCII digits, within the range of `int`, from -2147483648 to 2147483647. Leading zeros are allowed. |
| `long` | The same, within the range of `long`, from -9223372036854775808 to 9223372036854775807. |
| `decimal` | An optional leading `+` or `-` followed by ASCII digits with at most one `.`, and at least one digit, so `.5`, `5.` and `-.5` satisfy it. The value must lie within the range of `decimal`, ±79228162514264337593543950335, after rounding to its precision: digits beyond that are allowed, so `79228162514264337593543950335.4` satisfies it and `79228162514264337593543950335.5` does not. At the other end, a value too small to represent rounds to zero and still satisfies it, such as `0.` followed by 100 zeros and a `1`. |
| `bool` | `true` or `false`, in any ASCII case, such as `True` or `FALSE`. |
| `guid` | 32 hex digits, in either case, written either without separators, as in `6f9619ff8b86d011b42d00cf4fc964ff`, or with hyphens in groups of 8, 4, 4, 4 and 12, as in `6f9619ff-8b86-d011-b42d-00cf4fc964ff`. |
| `alpha` | One or more Unicode letters, including letters outside the Basic Multilingual Plane, such as `𝒜`. |

- Constraint names are compared without regard to case: `{id:INT}` is `{id:int}`.
- Every check is the same in every culture and applies to the decoded text exactly as it is captured. Nothing is trimmed, so a value with white space, such as `%2042`, never satisfies a number, `bool` or `guid`. Only the characters in the table are allowed: no thousands separators, exponents, currency symbols, trailing signs or digits other than ASCII `0` to `9`, such as `٣`, and no braces or parentheses around a guid.
- `alpha` is letters only. Digits, spaces, punctuation and combining marks are not letters, so decomposed text such as `e` followed by a combining acute accent (`e%CC%81`) does not satisfy it, though the single character `é` does.
- The captured value is the decoded text, unchanged: `+007` is captured as `+007`, not `7`.
- Several texts can denote one value, such as `7`, `007` and `+7`, `true` and `TRUE`, or a guid in upper and lower case. Each satisfies the constraint and is captured as written, and `Format` writes whatever text it is given, so producing one canonical link for each value is the job of the code that turns typed values into text.

## Optional parameters

A parameter written `{name?}` or `{name:constraint?}` is optional: a path can leave it out. The `?` must be the last character inside the braces.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate orders = RouteTemplate.Parse("/orders/{id:int?}/{tab?}");

Console.WriteLine(orders.Match("/orders/42/items")?["tab"]);        // items
Console.WriteLine(orders.Match("/orders/42")?.ContainsKey("tab"));  // False: tab is left out
Console.WriteLine(orders.Match("/orders/")?.Count);                 // 0
Console.WriteLine(orders.Match("/orders/latest") is null);          // True: latest is not an int
Console.WriteLine(orders.Format(new Dictionary<string, string>())); // /orders
```

- Optional parameters must be trailing: every segment after an optional parameter must also be an optional parameter. `/a/{b?}/{c?}` and `/{id?}` are valid; `/{id?}/details` and `/{a?}/{b}` are not.
- A path matches when it has at least the segments before the first optional parameter and no more segments than the template. The segments it has fill the template's positions from left to right, so a path can only leave out optional parameters from the end.
- A segment that is present must satisfy the parameter's constraint. If it does not, the path does not match: the segment is never treated as left out.
- An optional parameter that the path leaves out has no entry in the dictionary, not a `null` or empty value.

## Formatting a path

`Format` is the inverse of `Match`: it builds the path a template answers to from a value for each required parameter and for any optional parameters that are given.

```csharp
using AdamE.AppNav.Routing;

RouteTemplate file = RouteTemplate.Parse("/my files/{name}");

string path = file.Format(new Dictionary<string, string> { ["name"] = "notes/2026.txt" });
Console.WriteLine(path);                      // /my%20files/notes%2F2026.txt
Console.WriteLine(file.Match(path)?["name"]); // notes/2026.txt
```

- The path starts with `/`, separates segments with a single `/` and has no trailing slash. The root template formats as `/`.
- An [optional parameter](#optional-parameters) is left out when the dictionary has no entry for it, under its name as written or under a key that equals it ignoring case. Its segment and everything after it are not written, so `/products/{id?}` formats as `/products` without an `id`, and `/{id?}` formats as `/`.
- Each literal is written as its decoded text and each value as given, both percent-encoded with `Uri.EscapeDataString`. A `/` in a value becomes `%2F`, so it stays inside its segment.
- `.` and `..` are written as they are, as values or literals, and a URI parser or browser may remove them and the segment before them when the path is resolved.
- Values are keyed by parameter name without regard to case, as everywhere else in a template. Each name is looked up as written in the template first, and only if that finds nothing is the dictionary searched for a key that equals the name ignoring case. Entries for names the template does not use are ignored.
- `Format` throws `ArgumentNullException` for a `null` dictionary. It throws `ArgumentException` when a parameter that is not optional has no entry, when a parameter has no entry under its name as written and more than one whose key equals it ignoring case, or when its value is `null`, empty, contains a lone surrogate, which cannot be encoded and read back, or does not satisfy the parameter's [constraint](#constraints), because the path would not match. For a parameter without a constraint, any other value is allowed, including white space and control characters.
- An optional parameter that has an entry is checked the same way. A `null` or empty value does not leave it out; leave its entry out of the dictionary instead.
- `Format` also throws `ArgumentException` when an optional parameter has a value but an earlier optional parameter was left out, because its segment cannot be written without the earlier one. For `/a/{b?}/{c?}`, a value for `c` without one for `b` is rejected.
- The first parameter in template order that cannot be formatted is the one reported. A value that is not valid is reported before the fact that an earlier optional parameter was left out.

`Match` reads back what `Format` writes: for every template and every dictionary `Format` accepts, matching the formatted path returns exactly one entry for each required parameter and each optional parameter that was given, with the values that were formatted. In the other direction, formatting the values a match returned gives a path that matches again with the same values, unless the matched path contained a lone surrogate in a parameter's position, because `Match` does not check paths and `Format` rejects such a value. The new path need not have the same text as the one that was matched, because the output is canonical: the same values always format to the same path, whatever path they were matched from. Every segment is encoded the same way and repeated slashes are dropped. For example, `/my files/{name}` matches `//my files//%41/`, and formatting that match gives `/my%20files/A`.

## Invalid templates

`Parse` throws `ArgumentNullException` for `null`. It throws `ArgumentException` when the text is empty or only white space, or does not start with `/`. It also throws `ArgumentException` for:

- a segment that contains a control character (one for which `char.IsControl` is true), such as a tab, line feed or null character, anywhere in the segment, whether it is a parameter or a literal,
- a segment that contains a lone surrogate: a `char` that is half of a surrogate pair without its other half, which is not valid UTF-16 and cannot be percent-encoded,
- a parameter that contains `*`, in its name or its constraint, such as `{*rest}` or `{rest:*}`,
- a parameter that contains `?` anywhere but as its last character, such as `{id?:int}`, `{id??}` or `{id:in?t}`,
- a parameter whose name is not valid, such as `{}`, `{ id }`, `{1a}`, `{a-b}` or `{:int}`,
- a parameter that repeats the name of an earlier parameter, ignoring case, as in `/{id}/{ID}` or `/{id:int}/{ID}`,
- a parameter that contains more than one `:`, as in `{id:int:long}`, `{id::int}` or `{id:int:}`,
- a parameter with `:` but no constraint after it, as in `{id:}`,
- a parameter whose constraint is not one of the [built-in constraints](#constraints), such as `{id:slug}` or `{id: int}`; the message lists the supported names,
- a literal segment that starts or ends with white space, as in `/ {id}` (spaces inside a segment, as in `/my files`, are allowed),
- `{` or `}` anywhere except around a whole-segment parameter, as in `item{id}` or `{a}{b}`,
- a literal segment that contains `?` or `#`, which cannot appear in the path of a URI, and
- a literal or a parameter that is not optional after an optional parameter, as in `/{id?}/details`; the message names the optional parameter.

The rules for names and constraints apply to an optional parameter with its final `?` removed, so `{?}` has an invalid name and `{id:?}` has no constraint after `:`.

`Parse` reports the first invalid segment, reading left to right. Within a segment, a control character is reported first, then a lone surrogate, then any other problem with the segment's own text, then a segment that follows an optional parameter without being one, then a repeated name. Within a parameter, `*` is reported first, then a `?` that is not last, then an invalid name, then more than one `:`, then an empty constraint, then an unknown one. A control character is reported even when it is also white space, such as a tab at the start of a segment. The exception message shows a control character or a lone surrogate as a `\uXXXX` escape, such as `\u0009`, rather than printing it.

These checks apply to the text as written, before decoding. To match one of these characters literally, percent-encode it: `%09` for a tab, `%0A` for a line feed, `%00` for a null character (and the UTF-8 bytes of any other control character, such as `%C2%85` for U+0085), `%7B` for `{`, `%7D` for `}`, `%3F` for `?`, `%23` for `#` and `%20` for a space at the start or end of a segment. For example, `/what%3F` matches the path `/what%3F`, `/a%09b` matches a path whose segment is `a`, a tab and `b`, and `/%7Bid%7D` is a literal that matches the path `/{id}`. A lone surrogate has no encoded form: a surrogate has no UTF-8 bytes of its own, and the three bytes it would encode to, such as `%ED%A0%80`, are not valid UTF-8, so that text decodes to itself and the literal is that percent text.
