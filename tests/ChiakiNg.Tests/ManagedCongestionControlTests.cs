using System.Collections.Concurrent;
using System.Diagnostics;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP714: congestioncontrol.c - the clamp, the thread, and the two members it answers for.
///
/// Two of the five PP712's census still owes are this thread's start and its stop, so these hold
/// PP707's second criterion as well: shipping a subsystem is supposed to shorten that list, and
/// StreamRunHostConsumersTests asserts that it did.
/// </summary>
public class ManagedCongestionControlTests(ITestOutputHelper output)
{
    /// <summary>Every report, in order, safe to read while the thread is still sending.</summary>
    private sealed class Recorder : ICongestionSink
    {
        private readonly ConcurrentQueue<CongestionReport> sent = new();

        public IReadOnlyList<CongestionReport> Sent => [.. sent];

        public void Send(CongestionReport report) => sent.Enqueue(report);
    }

    /// <summary>The interval the C names, so a port that picked a round 250 is caught.</summary>
    [Fact]
    public void TheIntervalIsTwoHundredMilliseconds()
        => Assert.Equal(200, ManagedCongestionControl.IntervalMs);

    /// <summary>Under the ceiling, both numbers go out exactly as measured.</summary>
    [Fact]
    public void LossUnderTheCeilingIsReportedAsMeasured()
    {
        CongestionReport report = ManagedCongestionControl.Clamp(97, 3, 0.05, out double measured);

        output.WriteLine($"measured {measured:P2} -> {report.Received}/{report.Lost}");

        Assert.Equal(new CongestionReport(97, 3), report);
        Assert.Equal(0.03, measured, 6);
    }

    /// <summary>
    /// Over the ceiling BOTH numbers are rewritten, against an unchanged total.
    ///
    /// This is the assertion that fails for a port that clamps the ratio and derives one number.
    /// 800 lost of 1000 is 80%; clamped to 5% the console is told 50 lost and 950 received - the
    /// same thousand packets accounted for, with the loss among them reduced.
    /// </summary>
    [Fact]
    public void LossOverTheCeilingRewritesBothNumbersAgainstTheSameTotal()
    {
        CongestionReport report = ManagedCongestionControl.Clamp(200, 800, 0.05, out double measured);

        output.WriteLine($"measured {measured:P2} -> {report.Received}/{report.Lost}");

        Assert.Equal(0.8, measured, 6);
        Assert.Equal(new CongestionReport(950, 50), report);
        Assert.Equal(1000, report.Received + report.Lost);
    }

    /// <summary>The ratio reported is the one measured, not the clamped one. The C keeps it too.</summary>
    [Fact]
    public void TheMeasuredRatioSurvivesTheClamp()
    {
        ManagedCongestionControl.Clamp(0, 10, 0.05, out double measured);
        Assert.Equal(1.0, measured, 6);
    }

    /// <summary>
    /// A window with nothing in it divides by zero in the obvious way and still sends.
    ///
    /// The C guards the division and sends a 0/0 packet regardless, which is what an idle 200ms
    /// looks like on the wire. A port that skipped the send would go quiet under a stall - the one
    /// moment the console most needs to hear from it.
    /// </summary>
    [Fact]
    public void AnEmptyWindowIsZeroLossAndIsStillSent()
    {
        CongestionReport report = ManagedCongestionControl.Clamp(0, 0, 0.05, out double measured);

        Assert.Equal(new CongestionReport(0, 0), report);
        Assert.Equal(0.0, measured);

        var sink = new Recorder();
        using var control = new ManagedCongestionControl(new ManagedPacketStats(), sink, 0.05);

        control.Tick();

        Assert.Equal([new CongestionReport(0, 0)], sink.Sent);
    }

    /// <summary>
    /// The truncation rounds the reported loss DOWN, which is the C's cast and not a rounding.
    ///
    /// 3 * 0.05 is 0.15 and the console is told nothing was lost, over a total of three. Asserted
    /// because a port using a rounding conversion would send 1 here and disagree with the client on
    /// every small window.
    /// </summary>
    [Fact]
    public void TheClampTruncatesRatherThanRounds()
    {
        CongestionReport report = ManagedCongestionControl.Clamp(0, 3, 0.05, out _);

        Assert.Equal(new CongestionReport(3, 0), report);
    }

    /// <summary>
    /// Above 65535 the numbers narrow, which is what the console is told rather than a guard.
    ///
    /// A window of 70000 received cannot be said in sixteen bits, and the C says the low half of it
    /// without complaint. Held here so the port's silence on it is deliberate.
    /// </summary>
    [Fact]
    public void AWindowWiderThanSixteenBitsNarrows()
    {
        CongestionReport report = ManagedCongestionControl.Clamp(70000, 0, 0.05, out _);

        output.WriteLine($"70000 received reported as {report.Received}");

        Assert.Equal(70000 - 65536, report.Received);
    }

    /// <summary>One tick reads the stats WITH reset, so a second tick reports the next window.</summary>
    [Fact]
    public void ATickClosesTheWindowItReported()
    {
        var stats = new ManagedPacketStats();
        var sink = new Recorder();
        using var control = new ManagedCongestionControl(stats, sink, 0.05);

        // Under the ceiling, so what the window held is what goes out unaltered.
        stats.PushGeneration(97, 3);
        Assert.Equal(new CongestionReport(97, 3), control.Tick());

        // Nothing pushed since, so the next window is empty rather than the same one again.
        Assert.Equal(new CongestionReport(0, 0), control.Tick());

        stats.PushGeneration(7, 0);
        Assert.Equal(new CongestionReport(7, 0), control.Tick());

        Assert.Equal(3, control.Reports);
    }

    /// <summary>
    /// The thread runs, reports more than once, and stops when told - StartCongestionControl and
    /// StopCongestionControl, the two members this task answers for.
    ///
    /// Timed rather than counted exactly: 200ms is a real interval and a loaded machine can miss
    /// one. What is asserted is that reports arrive at all, that they keep arriving, and that Stop
    /// joins - a thread still sending after a join is the failure worth catching here.
    /// </summary>
    [Fact]
    public void TheThreadReportsUntilItIsStopped()
    {
        var stats = new ManagedPacketStats();
        var sink = new Recorder();
        using var control = new ManagedCongestionControl(stats, sink, 0.05);

        Assert.False(control.Running);
        control.Start();
        Assert.True(control.Running);

        var clock = Stopwatch.StartNew();
        while (control.Reports < 2 && clock.ElapsedMilliseconds < 5000)
            Thread.Sleep(20);

        output.WriteLine($"{control.Reports} report(s) in {clock.ElapsedMilliseconds}ms");
        Assert.True(control.Reports >= 2, $"only {control.Reports} report(s) in {clock.ElapsedMilliseconds}ms");

        control.Stop();
        Assert.False(control.Running);

        int settled = sink.Sent.Count;
        Thread.Sleep(ManagedCongestionControl.IntervalMs * 2);

        Assert.Equal(settled, sink.Sent.Count);
    }

    /// <summary>Starting twice is a mistake rather than a second thread, and Stop is idempotent.</summary>
    [Fact]
    public void StartingTwiceIsRefusedAndStoppingTwiceIsNot()
    {
        using var control = new ManagedCongestionControl(new ManagedPacketStats(), new Recorder(), 0.05);

        control.Start();
        Assert.Throws<InvalidOperationException>(control.Start);

        control.Stop();
        control.Stop();

        Assert.False(control.Running);
    }

    /// <summary>What the thread sends is what the stats held, end to end through the clamp.</summary>
    [Fact]
    public void WhatTheReceiversPushedIsWhatGoesOut()
    {
        var stats = new ManagedPacketStats();
        var sink = new Recorder();
        using var control = new ManagedCongestionControl(stats, sink, 0.05);

        // A frame that lost four of two hundred, and ten audio packets over a span of eleven. Kept
        // under the ceiling deliberately: this is the path where nothing is rewritten, so a clamp
        // that fired on every window would show up here rather than hiding behind its own test.
        stats.PushGeneration(196, 4);
        foreach (ushort seq in (ushort[])[1, 2, 3, 4, 5, 6, 7, 8, 9, 11])
            stats.PushSeq(seq);

        CongestionReport report = control.Tick();
        output.WriteLine($"{report.Received}/{report.Lost} at {control.PacketLoss:P2}");

        // 196 + 10 received; 4 lost from the frame plus the one the span cannot account for.
        Assert.Equal(new CongestionReport(206, 5), report);
        Assert.Equal(5.0 / 211.0, control.PacketLoss, 6);
        Assert.True(control.PacketLoss < 0.05, "the case is supposed to sit under the ceiling");
        Assert.Equal([report], sink.Sent);
    }

    /// <summary>The report is fifteen bytes on the wire, which the shim is asked for rather than told.</summary>
    [Fact]
    public void TheReportFitsThePacketTheShimDescribes()
    {
        CongestionReport report = ManagedCongestionControl.Clamp(97, 3, 0.05, out _);
        byte[] packet = Takion.FormatCongestion(0, report.Received, report.Lost, 0);

        Assert.Equal(Takion.CongestionPacketSize, packet.Length);
    }
}
