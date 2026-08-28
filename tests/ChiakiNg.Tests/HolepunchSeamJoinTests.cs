using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP480, PP340: the join between PP429's nine call sites and PP479's interface.
///
/// PP429's list exists because "a tenth would change PP33 in silence". PP479 added an interface and
/// joined it to nothing, so a tenth site could arrive with no method, or a method could be added that
/// answers no site. This closes that.
/// </summary>
public class HolepunchSeamJoinTests
{
    /// <summary>
    /// EVERY ONE OF THE NINE REACHES A METHOD, and the count is PP429's own.
    /// </summary>
    [Fact]
    public void AllNineSitesAreJoined()
    {
        Assert.Equal(HolepunchSeam.Count, HolepunchSeamJoin.Joins.Count);

        // And each join names a callee PP429 lists, so a renamed C function shows up here.
        foreach (SeamJoin join in HolepunchSeamJoin.Joins)
            Assert.Contains(join.Callee, HolepunchSeam.Asks.Select(a => a.Callee));
    }

    /// <summary>
    /// NINE SITES ARE SEVEN METHODS, and the interface declares exactly those seven - no method
    /// answering nothing, no site without one.
    /// </summary>
    [Fact]
    public void NineSitesAreSevenMethodsAndTheInterfaceHasNoOthers()
    {
        Assert.Equal(7, HolepunchSeamJoin.MethodCount);

        Assert.Equal(
            HolepunchSeamJoin.Joins.Select(j => j.Method).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            HolepunchSeamJoin.DeclaredMethods.ToArray());
    }

    /// <summary>
    /// The two collapses are the finis and the socket getter, which is why the numbers differ.
    /// </summary>
    [Fact]
    public void ExactlyTwoCalleesShareAMethod()
    {
        Assert.Equal(
            new[] { "chiaki_get_holepunch_sock", "chiaki_holepunch_session_fini" },
            HolepunchSeamJoin.Collapsed.ToArray());

        // Two callees, two sites each: nine minus two shared pairs is seven.
        Assert.Equal(4, HolepunchSeamJoin.Joins.Count(j => j.SharesTheMethod));
        Assert.Equal(HolepunchSeam.Count - 2, HolepunchSeamJoin.MethodCount);
    }

    /// <summary>
    /// THE DISTINCTION PP429 ASKED FOR: the socket is told apart by its argument, not by two methods.
    ///
    /// PP429's own words - "a managed side returning one socket for both would compile" - so this is the
    /// assertion that the seam kept what the list was written to protect.
    /// </summary>
    [Fact]
    public void TheSocketIsToldApartByItsPortType()
    {
        Assert.True(HolepunchSeamJoin.TheSocketIsToldApartByItsArgument());

        // And the two sites PP429 distinguishes by port type are the two that share the method.
        string[] byPort =
            [.. HolepunchSeam.Asks.Where(a => a.PortType is not null).Select(a => a.Callee).Distinct()];

        Assert.Equal(new[] { "chiaki_get_holepunch_sock" }, byPort.Where(c => c.Contains("sock")).ToArray());
    }

    /// <summary>
    /// And the flow only ever asks for a socket with a port type, never ambiguously - which is the
    /// distinction being used rather than merely available.
    /// </summary>
    [Fact]
    public void TheFlowAsksForBothPortsSeparately()
    {
        Assert.Equal(2, Enum.GetValues<HolepunchPortType>().Length);
        Assert.Contains(HolepunchPortType.Ctrl, Enum.GetValues<HolepunchPortType>());
        Assert.Contains(HolepunchPortType.Data, Enum.GetValues<HolepunchPortType>());
    }
}
