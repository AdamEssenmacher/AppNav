extern alias OtherAssembly;

using System.Reflection;
using System.Reflection.Emit;
using AdamE.AppNav.Routing;
using Xunit;
using OtherOrders = OtherAssembly::AdamE.AppNav.Tests.Routing.Orders;

namespace AdamE.AppNav.Tests.Routing;

// The names written for a pair of route types, step by step: each step is taken only when the one before it writes two
// different types alike, so a pair is written by the first step that tells it apart.
public sealed class RouteValueTypesDisplayNamesTests
{
    private const string DynamicAssembly = "AdamE.AppNav.Tests.Dynamic";
    private const string DynamicType = "Dynamic.SameRoute";

    [Fact]
    public void TypesOfDifferentShortNamesAreWrittenByThem()
    {
        Assert.Equal(
            ("SettingsRoute", "ProductRoute"),
            RouteValueTypes.DisplayNames(typeof(SettingsRoute), typeof(ProductRoute)));
    }

    [Fact]
    public void TheSameTypeTwiceIsWrittenByItsShortName()
    {
        Assert.Equal(
            ("DetailRoute", "DetailRoute"),
            RouteValueTypes.DisplayNames(typeof(Orders.DetailRoute), typeof(Orders.DetailRoute)));
    }

    [Fact]
    public void TypesOfOneShortNameAreWrittenWithTheirNamespaces()
    {
        Assert.Equal(
            (RouteTypeNames.OrdersDetail, RouteTypeNames.CatalogDetail),
            RouteValueTypes.DisplayNames(typeof(Orders.DetailRoute), typeof(Catalog.DetailRoute)));
    }

    [Fact]
    public void TypesOfOneQualifiedNameAreWrittenAsTypeToStringWritesThem()
    {
        Assert.Equal(
            (RouteTypeNames.GenericOfOrdersDetail, RouteTypeNames.GenericOfCatalogDetail),
            RouteValueTypes.DisplayNames(
                typeof(GenericRoute<Orders.DetailRoute>),
                typeof(GenericRoute<Catalog.DetailRoute>)));
    }

    [Fact]
    public void TypesOfOneFullNameAreWrittenWithTheSimpleNamesOfTheirAssemblies()
    {
        Assert.Equal(
            (RouteTypeNames.OrdersDetailOfTests, RouteTypeNames.OrdersDetailOfOtherAssembly),
            RouteValueTypes.DisplayNames(typeof(Orders.DetailRoute), typeof(OtherOrders.DetailRoute)));
    }

    // The constructed types belong to the assembly of their generic definition, so only their arguments differ by
    // assembly, which the assembly-qualified names write and the simple names of the assemblies do not.
    [Fact]
    public void GenericTypesOverArgumentsOfTwoAssembliesAreWrittenAsTheirAssemblyQualifiedNames()
    {
        (string firstName, string secondName) = RouteValueTypes.DisplayNames(
            typeof(GenericRoute<Orders.DetailRoute>),
            typeof(GenericRoute<OtherOrders.DetailRoute>));

        // The whole form is written out, except the versions, which the build decides: the definition, its argument
        // with the argument's assembly, then the definition's own assembly.
        const string Definition = @"^AdamE\.AppNav\.Tests\.Routing\.GenericRoute`1";
        const string Argument = @"AdamE\.AppNav\.Tests\.Routing\.Orders\.DetailRoute";
        const string Rest = @", Version=[^\]]+\]\], AdamE\.AppNav\.Tests, Version=[^\]]+$";
        Assert.Matches($@"{Definition}\[\[{Argument}, AdamE\.AppNav\.Tests{Rest}", firstName);
        Assert.Matches($@"{Definition}\[\[{Argument}, AdamE\.AppNav\.Tests\.OtherAssembly{Rest}", secondName);
        Assert.NotEqual(firstName, secondName);
    }

    [Fact]
    public void TypesOfAssembliesOfOneSimpleNameAreWrittenAsTheirAssemblyQualifiedNames()
    {
        Type first = DefineType(new Version(1, 0, 0, 0));
        Type second = DefineType(new Version(2, 0, 0, 0));

        Assert.Equal(
            (
                $"{DynamicType}, {DynamicAssembly}, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                $"{DynamicType}, {DynamicAssembly}, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null"
            ),
            RouteValueTypes.DisplayNames(first, second));
    }

    // Two assemblies of the same name and version, as one loaded twice, leave nothing to tell the types apart by, so
    // the text of the step before is kept.
    [Fact]
    public void TypesOfOneAssemblyQualifiedNameAreWrittenWithTheSimpleNameOfTheirAssembly()
    {
        Type first = DefineType(new Version(1, 0, 0, 0));
        Type second = DefineType(new Version(1, 0, 0, 0));
        Assert.NotEqual(first, second);

        Assert.Equal(
            ($"{DynamicType}, {DynamicAssembly}", $"{DynamicType}, {DynamicAssembly}"),
            RouteValueTypes.DisplayNames(first, second));
    }

    // Each call defines an assembly of its own, so two calls give two different types of the same full name.
    private static Type DefineType(Version version)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(DynamicAssembly) { Version = version },
            AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(DynamicAssembly);
        return module.DefineType(DynamicType, TypeAttributes.Public | TypeAttributes.Sealed).CreateType();
    }
}
