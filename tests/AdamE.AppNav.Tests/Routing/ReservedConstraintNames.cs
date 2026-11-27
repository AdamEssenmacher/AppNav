using Xunit;

namespace AdamE.AppNav.Tests.Routing;

/// <summary>
/// The phrases a message gives for a reserved constraint name: that it is reserved, and what to write instead.
/// </summary>
internal static class ReservedConstraintNames
{
    public static void AssertExplained(string name, string message)
    {
        string lower = name.ToLowerInvariant();
        Assert.Contains($"'{lower}' is reserved: ", message, StringComparison.Ordinal);
        Assert.Contains("Leave the parameter unconstrained", message, StringComparison.Ordinal);
        if (lower == "char")
            Assert.Contains("read the value as a string", message, StringComparison.Ordinal);
        else
            Assert.DoesNotContain("read the value as a string", message, StringComparison.Ordinal);
    }
}
