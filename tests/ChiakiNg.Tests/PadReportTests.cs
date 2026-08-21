using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP218: what `--controllers` says, and the ownership rule behind the two calls that feed it.
///
/// The enumeration itself needs SDL up on its own thread and a pad plugged in, which is the half
/// PP18 says cannot be stood in for. What is here is the half that can: the report's shape, and
/// the header contract that decides whether one of those two calls leaks.
/// </summary>
public class PadReportTests
{
    private static readonly Version Sdl = new(2, 30, 9);

    private const string DualSense =
        "030000004c050000e60c000000006800,PS5 Controller,a:b1,b:b2,x:b0,y:b3,";

    /// <summary>A machine with nothing plugged in says so, rather than printing an empty list.</summary>
    [Fact]
    public void NoDevicesIsAnOrdinaryAnswer()
    {
        string report = PadReport.Format(0, [], Sdl);

        Assert.Contains("0 device(s), 0 mappable", report, StringComparison.Ordinal);
        Assert.Contains(PadReport.NoDevices, report, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a device SDL sees but cannot map is a THIRD answer, distinguishable from both - which is
    /// why the joystick count is reported beside the mappable count rather than derived from it.
    /// </summary>
    [Fact]
    public void SeenButUnmappableIsItsOwnAnswer()
    {
        string report = PadReport.Format(2, [], Sdl);

        Assert.Contains("2 device(s), 0 mappable", report, StringComparison.Ordinal);
        Assert.Contains(PadReport.NoneMappable, report, StringComparison.Ordinal);
        Assert.DoesNotContain(PadReport.NoDevices, report, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mapping string is printed whole. It is the input to the document parser, so a report
    /// that shortened it would be one nobody could act on.
    /// </summary>
    [Fact]
    public void TheMappingStringIsPrintedWhole()
    {
        string report = PadReport.Format(1, [new SdlPad(0, "PS5 Controller", DualSense)], Sdl);

        Assert.Contains(DualSense, report, StringComparison.Ordinal);
        Assert.Contains("[0] PS5 Controller", report, StringComparison.Ordinal);
        Assert.Contains("SDL 2.30.9", report, StringComparison.Ordinal);
    }

    /// <summary>And what it prints round-trips into the document the screen edits.</summary>
    [Fact]
    public void WhatIsPrintedIsWhatTheScreenParses()
    {
        var pad = new SdlPad(0, "PS5 Controller", DualSense);

        ControllerMappingDocument? document =
            ControllerMappingDocument.Parse(pad.Mapping, pad.Name);

        Assert.NotNull(document);
        Assert.Equal("PS5 Controller", document.ControllerType);
        Assert.Equal("030000004c050000e60c000000006800", document.Guid);
    }

    /// <summary>Every pad is listed, in the order SDL indexed them.</summary>
    [Fact]
    public void EveryPadIsListed()
    {
        string report = PadReport.Format(
            2,
            [new SdlPad(0, "First", DualSense), new SdlPad(1, "Second", DualSense)],
            Sdl);

        Assert.Contains("2 device(s), 2 mappable", report, StringComparison.Ordinal);
        Assert.True(
            report.IndexOf("[0] First", StringComparison.Ordinal)
                < report.IndexOf("[1] Second", StringComparison.Ordinal));
    }

    /// <summary>
    /// The rule the port would otherwise have to remember: the name is SDL's and the mapping string
    /// is the caller's, from two calls that are indistinguishable once they reach managed code.
    /// </summary>
    [Fact]
    public void TheTwoCallsStillOwnTheirMemoryOppositely()
    {
        string? header = SdlPadSource.LocateHeader();
        if (header is null)
            return;

        string text = File.ReadAllText(header);

        Assert.True(SdlPadSource.TheyAreStillNeighbours(text), "both still declared");
        Assert.True(SdlPadSource.TheNameIsStillOwnedBySdl(text), "the name is const char*");
        Assert.True(
            SdlPadSource.TheMappingStringIsStillTheCallersToFree(text),
            "and the mapping string says it must be freed");
    }
}
