using AdamE.AppNav.Routing;
using Xunit;

namespace AdamE.AppNav.Tests.Routing;

// Which part of a URI the route table reads as the path. An empty table reports the path it read in its diagnostic,
// so most of these tests read it from there.
public sealed class RouteTableUriTests
{
    private const string NotAnAbsoluteUriHere =
        "On this platform a string that starts with '/' is not an absolute URI, as it is on Unix-like platforms.";

    [Theory]
    [InlineData("https://example.com/settings/profile", "/settings/profile")]
    [InlineData("http://example.com/settings/profile", "/settings/profile")]
    [InlineData("https://example.com", "/")]
    [InlineData("https://example.com:8443/settings/profile?tab=1#top", "/settings/profile")]
    [InlineData("https://user:secret@example.com/settings/profile", "/settings/profile")]
    [InlineData("https://example.com/a?b#c", "/a")]
    [InlineData("https://example.com/a#b?c", "/a")]
    [InlineData("myapp:///settings/profile", "/settings/profile")]
    [InlineData("myapp:///settings/profile?tab=1#top", "/settings/profile")]
    [InlineData("myapp://host/settings/profile", "/settings/profile")]
    [InlineData("app:/settings/profile", "/settings/profile")]
    [InlineData("app:/settings/profile?tab=1#top", "/settings/profile")]
    [InlineData("app:x/y", "x/y")]
    [InlineData("mailto:a@b.c", "")]
    [InlineData("myapp:", "")]
    public void AnAbsoluteUriIsReadAsItsAbsolutePath(string uri, string path)
    {
        Assert.Equal(path, ReadPath(new Uri(uri)));
    }

    [Theory]
    [InlineData("myapp://settings/profile", "settings", "/profile")]
    [InlineData("myapp://open?tab=1", "open", "/")]
    public void ACustomSchemeWithTwoSlashesLosesItsFirstWordToTheHost(string uri, string host, string path)
    {
        var parsed = new Uri(uri);

        Assert.Equal(host, parsed.Host);
        Assert.Equal(path, ReadPath(parsed));
    }

    [Fact]
    public void ACustomSchemeWithTwoSlashesMatchesTheWrongRoute()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/", _ => new SettingsRoute())
            .Map("/products/{id}", c => new ProductRoute(c.Path("id")))
            .Map("/{slug}", c => new ProductByNameRoute(c.Path("slug")))
            .Build();

        Assert.Equal(new ProductByNameRoute("42"), table.Match(new Uri("myapp://products/42")).Route);
        Assert.Equal(new SettingsRoute(), table.Match(new Uri("myapp://open?tab=1")).Route);
        Assert.Equal(new ProductRoute("42"), table.Match(new Uri("myapp:///products/42")).Route);
    }

    [Fact]
    public void AUriWithAnEmptyPathMatchesTheRoot()
    {
        RouteTable table = new RouteTableBuilder().Map("/", _ => new SettingsRoute()).Build();

        Assert.Equal("", new Uri("mailto:a@b.c").AbsolutePath);
        Assert.True(table.Match(new Uri("mailto:a@b.c")).IsSuccess);
    }

    [Fact]
    public void NeitherTheSchemeNorTheHostIsChecked()
    {
        RouteTable table = new RouteTableBuilder().Map("/account", _ => new SettingsRoute()).Build();

        Assert.True(table.Match(new Uri("myapp:///account")).IsSuccess);
        Assert.True(table.Match(new Uri("https://other.example/account")).IsSuccess);
        Assert.True(table.Match(new Uri("file:///account")).IsSuccess);
    }

    [Fact]
    public void ACustomSchemeWithThreeSlashesKeepsItsWholePath()
    {
        RouteTable table = new RouteTableBuilder().Map("/settings/profile", _ => new SettingsRoute()).Build();

        Assert.False(table.Match(new Uri("myapp://settings/profile")).IsSuccess);
        Assert.True(table.Match(new Uri("myapp:///settings/profile")).IsSuccess);
        Assert.Equal("", new Uri("myapp:///settings/profile").Host);
    }

    [Fact]
    public void TheHostIsIgnored()
    {
        RouteTable table = new RouteTableBuilder().Map("/settings", _ => new SettingsRoute()).Build();

        Assert.True(table.Match(new Uri("https://one.example/settings")).IsSuccess);
        Assert.True(table.Match(new Uri("https://two.example:8080/settings")).IsSuccess);
        Assert.True(table.Match(new Uri("other:///settings")).IsSuccess);
    }

    [Theory]
    [InlineData("https://example.com/a/../b", "/b")]
    [InlineData("https://example.com/a/./b", "/a/b")]
    [InlineData("https://example.com/a/%2E%2E/b", "/b")]
    [InlineData("app://host/a/../b", "/b")]
    [InlineData("myapp:///a/../b", "/b")]
    [InlineData("myapp:///a/%2E%2E/b", "/b")]
    [InlineData("app:/%41", "/A")]
    [InlineData("app:/a b", "/a%20b")]
    [InlineData("https://example.com/%41", "/A")]
    [InlineData("https://example.com/a b/é", "/a%20b/%C3%A9")]
    [InlineData("https://example.com/a//b", "/a//b")]
    [InlineData("https://example.com/a%2Fb", "/a%2Fb")]
    public void AUriWithAnAuthorityIsReadAsSystemUriNormalizesIt(string uri, string path)
    {
        Assert.Equal(path, ReadPath(new Uri(uri)));
    }

    [Fact]
    public void AUriWithAnAuthorityMatchesAfterSystemUriNormalizesIt()
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/files/{name}", c => new FileRoute(c.Path("name")))
            .Build();

        Assert.Equal(new FileRoute("A b"), table.Match(new Uri("https://example.com/x/../files/%41 b")).Route);
        Assert.Equal(new FileRoute("é"), table.Match(new Uri("https://example.com/files/é")).Route);
    }

    [Theory]
    [InlineData("app:/a/../b", "/a/../b")]
    [InlineData("app:/a/%2E%2E/b", "/a/../b")]
    public void AUriWithoutAnAuthorityKeepsDotSegmentsSoItDoesNotMatch(string uri, string path)
    {
        RouteTable table = new RouteTableBuilder()
            .Map("/b", _ => new SettingsRoute())
            .Map("/{*rest}", c => new FileRoute(c.TryGetPath("rest", out string? rest) ? rest : null))
            .Build();

        RouteMatchResult result = table.Match(new Uri(uri));

        Assert.False(result.IsSuccess);
        Assert.Equal(path, Assert.Single(result.Diagnostics).Data["path"]);
    }

    [Theory]
    [InlineData("https://example.com/%C3%A9%20", "/%C3%A9")]
    [InlineData("https://example.com/%C3%A9%20?q=1", "/%C3%A9%20")]
    [InlineData("https://example.com/%C3%A9%20#top", "/%C3%A9%20")]
    [InlineData("https://example.com/%C3%A9%20?", "/%C3%A9%20")]
    [InlineData("https://example.com/%C3%A9%20#", "/%C3%A9%20")]
    [InlineData("https://example.com/\u00E9%20%20", "/%C3%A9")]
    [InlineData("https://example.com/%FF%20", "/%FF")]
    [InlineData("https://b\u00FCcher.example/a%20", "/a")]
    [InlineData("https://\u00E9@example.com/a%20", "/a")]
    [InlineData("https://example.com/a%20", "/a%20")]
    [InlineData("https://example.com/a%20%20", "/a%20%20")]
    public void SystemUriDropsTrailingEncodedSpacesAtTheEndOfAUriWithNonAsciiText(string uri, string path)
    {
        Assert.Equal(path, ReadPath(new Uri(uri)));
    }

    [Theory]
    [InlineData("/settings/profile", "/settings/profile")]
    [InlineData("settings/profile", "settings/profile")]
    [InlineData("/settings?tab=1", "/settings")]
    [InlineData("/settings#top", "/settings")]
    [InlineData("/settings?tab=1#top", "/settings")]
    [InlineData("/settings#top?tab=1", "/settings")]
    [InlineData("/settings?a#b?c", "/settings")]
    [InlineData("", "")]
    [InlineData("?tab=1", "")]
    [InlineData("#top", "")]
    [InlineData(" /settings ", " /settings ")]
    [InlineData("/a%20b/%41", "/a%20b/%41")]
    [InlineData("/a/../b", "/a/../b")]
    [InlineData("//a//b/", "//a//b/")]
    public void ARelativeUriIsReadFromItsOriginalStringUpToTheFirstQuestionMarkOrHash(string uri, string path)
    {
        Assert.Equal(path, ReadPath(new Uri(uri, UriKind.Relative)));
    }

    [Theory]
    [InlineData("/settings/profile")]
    [InlineData("settings/profile")]
    [InlineData("/settings/profile?tab=1")]
    [InlineData("settings/profile#top")]
    [InlineData("//settings//profile/")]
    public void ARelativeUriMatchesWithOrWithoutALeadingSlash(string uri)
    {
        RouteTable table = new RouteTableBuilder().Map("/settings/profile", _ => new SettingsRoute()).Build();

        Assert.True(table.Match(new Uri(uri, UriKind.Relative)).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?tab=1")]
    [InlineData("#top")]
    public void AnEmptyOrQueryOnlyRelativeUriMatchesTheRoot(string uri)
    {
        RouteTable table = new RouteTableBuilder().Map("/", _ => new SettingsRoute()).Build();

        Assert.True(table.Match(new Uri(uri, UriKind.Relative)).IsSuccess);
    }

    [Fact]
    public void ARelativeUriHasNoAbsolutePath()
    {
        var uri = new Uri("/settings", UriKind.Relative);

        Assert.Throws<InvalidOperationException>(() => uri.AbsolutePath);
    }

    [Fact]
    public void ARelativeUriIsNotTrimmed()
    {
        RouteTable table = new RouteTableBuilder().Map("/settings", _ => new SettingsRoute()).Build();

        Assert.False(table.Match(new Uri("/settings ", UriKind.Relative)).IsSuccess);
    }

    [Theory]
    [InlineData("/settings?tab=1")]
    [InlineData("/settings#x")]
    [InlineData("/a b/c")]
    [InlineData("//host/path")]
    public void AnImplicitFileUriIsReadAsTheRelativeUriTheCallerWrote(string text)
    {
        // On Unix-like platforms these are absolute file URIs; on Windows they cannot be created without a UriKind,
        // so there is nothing to test.
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? implicitFile))
            Assert.Skip(NotAnAbsoluteUriHere);

        Assert.True(implicitFile.IsFile);
        Assert.Equal(ReadPath(new Uri(text, UriKind.RelativeOrAbsolute)), ReadPath(implicitFile));
        Assert.Equal(text.Split('?', '#')[0], ReadPath(implicitFile));
    }

    [Fact]
    public void AnImplicitFileUriMatchesAsTheRelativeUriTheCallerWrote()
    {
        if (!Uri.TryCreate("/settings?tab=1", UriKind.Absolute, out Uri? implicitFile))
            Assert.Skip(NotAnAbsoluteUriHere);

        RouteTable table = new RouteTableBuilder().Map("/settings", _ => new SettingsRoute()).Build();

        Assert.True(table.Match(implicitFile).IsSuccess);
        Assert.True(table.Match(new Uri("/settings?tab=1", UriKind.RelativeOrAbsolute)).IsSuccess);
    }

    [Theory]
    [InlineData("file:///settings", "/settings")]
    [InlineData("file:///settings?tab=1", "/settings")]
    [InlineData("file:///a%20b", "/a%20b")]
    public void AnExplicitFileUriIsReadAsItsAbsolutePath(string uri, string path)
    {
        Assert.Equal(path, ReadPath(new Uri(uri)));
    }

    [Theory]
    [InlineData(" /settings?tab=1")]
    [InlineData("\t/settings?tab=1")]
    public void AnImplicitFileUriWhoseTextDoesNotStartWithASlashKeepsItsAbsolutePath(string text)
    {
        // Nothing is trimmed, so white space before the '/' means the text is not read as an app-relative link.
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? implicitFile))
            Assert.Skip(NotAnAbsoluteUriHere);

        Assert.True(implicitFile.IsFile);
        Assert.Equal(implicitFile.AbsolutePath, ReadPath(implicitFile));
    }

    [Theory]
    [InlineData(@"\\server\share\x")]
    [InlineData(@"C:\x")]
    public void AWindowsStylePathKeepsItsAbsolutePath(string text)
    {
        // Only text that starts with '/' is read as an app-relative link; a UNC or drive path is a file URI.
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? file))
            Assert.Skip(NotAnAbsoluteUriHere);

        Assert.True(file.IsFile);
        Assert.Equal(file.AbsolutePath, ReadPath(file));
    }

    private static string? ReadPath(Uri uri)
    {
        RouteTable empty = new RouteTableBuilder().Build();
        return (string?)Assert.Single(empty.Match(uri).Diagnostics).Data["path"];
    }
}
