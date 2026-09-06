using System.Reflection;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP765, under PP762: the eleven parts a live run host needs, held against the constructor.
///
/// PP762 owes a composition root and nobody knew what one cost. Every test builds this host from
/// doubles, so the question a real session asks of each part - where does this come from - had never
/// been asked. PP696 shipped without asking it.
///
/// THE JOIN IS THE SIGNATURE. A census of constructor parameters that drifts from the constructor
/// is a list describing a host that no longer exists, and the failure would be silent in exactly the
/// way this task exists to stop.
/// </summary>
public class LiveHostPartsTests(ITestOutputHelper output)
{
    private static ConstructorInfo Constructor =>
        typeof(ManagedStreamRunHost).GetConstructors().Single();

    /// <summary>
    /// EVERY PARAMETER HAS A ROW, IN ORDER, AND NO ROW NAMES A PARAMETER THAT IS GONE.
    ///
    /// Both directions and as a sequence: a parameter added is a question somebody has to answer, a
    /// row left behind is a claim about a host that changed, and a reordering is visible because the
    /// list reads as the signature rather than as a set.
    /// </summary>
    [Fact]
    public void TheCensusIsTheConstructorsOwnParameters()
    {
        string[] parameters = [.. Constructor.GetParameters().Select(one => one.Name!)];
        string[] rows = [.. LiveHostParts.All.Select(one => one.Parameter)];

        output.WriteLine($"{parameters.Length} parameter(s): {string.Join(", ", parameters)}");

        Assert.Equal(parameters, rows);
    }

    /// <summary>
    /// Every row says what supplies it and why, because a row with neither is a table.
    /// </summary>
    [Fact]
    public void EveryPartNamesASupplierAndAReason()
        => Assert.All(
            LiveHostParts.All,
            one =>
            {
                Assert.False(string.IsNullOrWhiteSpace(one.Supplier));
                Assert.False(string.IsNullOrWhiteSpace(one.Why));
            });

    /// <summary>
    /// NOTHING IS MISSING, WHICH IS THE ANSWER PP762 WAS WAITING FOR.
    ///
    /// The composition root is wiring rather than new subsystems: every part composes from something
    /// that shipped, and the two that reach into the C reach for things the session already holds.
    ///
    /// Asserted in both directions. A row arriving in Missing is a part that stopped being
    /// buildable, which is a finding; and the list going empty by everything leaving the census
    /// would be the other way to make this pass, which the check above rules out.
    /// </summary>
    [Fact]
    public void NoPartIsMissingAndTwoComeFromTheSession()
    {
        output.WriteLine(string.Join(
            "\n", LiveHostParts.All.Select(one => $"  {one.Parameter,-11} {one.Supply}")));

        Assert.Empty(LiveHostParts.Missing);

        Assert.Equal(
            ["peer", "big"],
            LiveHostParts.FromTheSession.Select(one => one.Parameter));
    }

    /// <summary>
    /// THE BIG IS THE ONE THAT IS NOT WHAT IT LOOKS LIKE, and this is what says so.
    ///
    /// The host takes it as a factory and every test hands it a heartbeat, so the call that STARTS a
    /// stream has never been built by anything this port runs. BigMessage.Encode can build one -
    /// asserted here, because "it can be built" is the whole reason this part is FromTheSession
    /// rather than Missing, and the day that method goes the row is wrong.
    /// </summary>
    [Fact]
    public void TheBigCanBeBuiltEvenThoughNothingBuildsOne()
    {
        LiveHostPart big = Assert.Single(LiveHostParts.All, one => one.Parameter == "big");

        Assert.Equal(PartSupply.FromTheSession, big.Supply);
        Assert.Contains("BigMessage.Encode", big.Supplier, StringComparison.Ordinal);

        // The builder the row names, which is what keeps it out of Missing.
        Assert.NotNull(typeof(BigMessage).GetMethod(nameof(BigMessage.Encode)));

        // And the host really does take it as a factory rather than as a message, which is why a
        // heartbeat fits where a BIG belongs and nothing complained.
        ParameterInfo parameter = Constructor.GetParameters().Single(one => one.Name == "big");
        Assert.Equal(typeof(Func<StreamMessage>), parameter.ParameterType);
    }
}
