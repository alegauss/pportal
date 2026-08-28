using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP477, PP27: the socket - the last of the four things PP27's own sentence names.
///
/// PP449 did the thread's timer, PP450 the handshake, PP473 the postpone, PP475 the resend loop. The
/// socket is the smallest and the most repetitive: the configuring is written twice, which is why its
/// option names were wrong in four places rather than two.
/// </summary>
public class TakionSocketTests
{
    private static string? Connect()
    {
        if (TakionSocket.Locate() is not { } path)
            return null;

        return TakionSocket.ConnectBody(File.ReadAllText(path));
    }

    /// <summary>
    /// THE RECEIVE BUFFER IS THE ADVERTISED WINDOW, so the socket's buffer and the protocol's promise
    /// are one number.
    ///
    /// A port choosing a buffer size of its own would advertise one thing to the console and hold
    /// another.
    /// </summary>
    [Fact]
    public void TheReceiveBufferIsTheWindowTheInitAdvertises()
    {
        Assert.Equal(TakionHandshake.ARwnd, TakionSocket.ReceiveBufferIs);

        if (Connect() is not { } body)
            return;

        Assert.True(
            TakionSocket.TheReceiveBufferIsStillTheWindow(body),
            "SO_RCVBUF is no longer set from a_rwnd in both branches");
    }

    /// <summary>Only one of the two branches makes a socket; both configure one.</summary>
    [Fact]
    public void OnlyOneBranchMakesTheSocket()
    {
        Assert.True(TakionSocket.Creates(SocketOrigin.MadeHere));
        Assert.False(TakionSocket.Creates(SocketOrigin.HandedIn));

        if (Connect() is not { } body)
            return;

        Assert.True(TakionSocket.OnlyOneBranchMakesTheSocket(body));
    }

    /// <summary>
    /// PP477: every fragment-bit log names IP_DONTFRAGMENT, which is what the call sets.
    ///
    /// All four said IP_MTU_DISCOVER - a different option, controlling path MTU discovery rather than
    /// the fragment bit. Reachable whenever the option fails, which is exactly when somebody reads the
    /// log to find out why: the same shape as PP463's bind ladder naming the port it was about to try.
    ///
    /// Both halves are asserted, because counting only the right name would pass with two of four
    /// corrected.
    /// </summary>
    [Fact]
    public void EveryFragmentLogNamesTheOptionTheCallSets()
    {
        Assert.Equal("IP_DONTFRAGMENT", TakionSocket.FragmentOption);
        Assert.NotEqual(TakionSocket.FragmentOption, TakionSocket.TheOptionTheLogsUsedToName);

        if (Connect() is not { } body)
            return;

        Assert.True(
            TakionSocket.EveryFragmentLogNamesTheRightOption(body),
            "a fragment-bit log names IP_MTU_DISCOVER again, or one of the four stopped naming the "
                + "option at all");

        // And the four logs match four calls, so neither count drifted on its own.
        Assert.True(TakionSocket.TheCallsStillSetTheFragmentOption(body));
    }

    /// <summary>
    /// The guard on those four is a constant, and that is not a defect - it is what no guard would do.
    ///
    /// `mac_dontfrag` is named for the macOS build this tree's non-goals delete. Stated so nobody reads
    /// it as a platform switch that stopped switching, and so a real assignment to it shows up here.
    /// </summary>
    [Fact]
    public void TheGuardIsAConstantAndNotASwitch()
    {
        Assert.True(TakionSocket.AFragmentFailureIsFatal(macDontfrag: true));
        Assert.False(TakionSocket.AFragmentFailureIsFatal(macDontfrag: false));

        if (Connect() is not { } body)
            return;

        Assert.True(
            TakionSocket.TheGuardIsStillAConstant(body),
            "mac_dontfrag is assigned more than once now, so it has become a switch and the four "
                + "guards are no longer the same test");
    }

    /// <summary>Four log lines: two per branch, two branches.</summary>
    [Fact]
    public void ThereAreFourOfEachBecauseTheBranchIsWrittenTwice()
    {
        Assert.Equal(4, TakionSocket.FragmentLogLines);
        Assert.Equal(2 * 2, TakionSocket.FragmentLogLines);
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(TakionSocket.ConnectBody(""));
        Assert.False(TakionSocket.TheReceiveBufferIsStillTheWindow(""));
        Assert.False(TakionSocket.TheCallsStillSetTheFragmentOption(""));
        Assert.False(TakionSocket.TheGuardIsStillAConstant(""));
        Assert.False(TakionSocket.OnlyOneBranchMakesTheSocket(""));

        // This one is true of nothing, because it asserts an absence - so it is checked against a body
        // that has the wrong name in it instead.
        Assert.False(
            TakionSocket.EveryFragmentLogNamesTheRightOption("setsockopt IP_MTU_DISCOVER: failed"));
    }
}
