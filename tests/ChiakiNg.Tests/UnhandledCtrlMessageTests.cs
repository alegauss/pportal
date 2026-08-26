using System.Globalization;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP331, under PP294: the control message the library will not name, and how it is reported.
///
/// Three things are asserted and they are deliberately different in kind. That a type the port
/// cannot name really does arrive is asserted against PP297's capture rather than against a
/// constant, because a constant would only restate the claim. That the C now reports it is asserted
/// against ctrl.c, and both halves of the branch are read - the sentence and the hexdump under it -
/// because the defect was that the sentence was commented out and the hexdump was left shouting.
/// </summary>
public class UnhandledCtrlMessageTests
{
    private static string? Core() =>
        UnhandledCtrlMessage.Locate() is { } path ? File.ReadAllText(path) : null;

    private static string? Arm()
    {
        string? core = Core();
        return core is null ? null : UnhandledCtrlMessage.DefaultArm(core);
    }

    /// <summary>
    /// THE PREMISE. The one capture this tree has holds a control type the port has no name for,
    /// which is what makes the default branch a path every session takes rather than a corner.
    /// </summary>
    [Fact]
    public void TheCaptureHoldsATypeThePortCannotName()
    {
        string? path = SanitizerSource.LocateRelative(ExchangeCorpusTests.RelativePath);
        if (path is null)
            return;

        ExchangeRecording? recording = ExchangeRecording.Read(File.ReadAllText(path));
        Assert.NotNull(recording);

        var types = new List<ushort>();
        foreach (ExchangeEntry entry in recording.Entries.Where(e => e.Channel == "ctrl"))
        {
            if (ushort.TryParse(
                    entry.Payload.AsSpan(0, 4), NumberStyles.HexNumber, null, out ushort type))
            {
                types.Add(type);
            }
        }

        IReadOnlyList<ushort> unnamed = UnhandledCtrlMessage.UnnamedIn(types);

        Assert.Equal([UnhandledCtrlMessage.Observed], unnamed);
    }

    /// <summary>
    /// And the enum really does not name it, read the other way round - so the test above is not
    /// passing because the parse silently produced nothing.
    /// </summary>
    [Fact]
    public void TheEnumNamesTheHandledTypesAndNotTheObservedOne()
    {
        Assert.False(UnhandledCtrlMessage.IsNamed(UnhandledCtrlMessage.Observed));

        Assert.True(UnhandledCtrlMessage.IsNamed((ushort)CtrlMessage.DisplayB));
        Assert.True(UnhandledCtrlMessage.IsNamed((ushort)CtrlMessage.HeartbeatReq));
        Assert.True(UnhandledCtrlMessage.IsNamed((ushort)CtrlMessage.SessionId));
    }

    /// <summary>The reader finds the branch at all, or the three below assert nothing.</summary>
    [Fact]
    public void TheDispatchStillHasADefaultArm()
    {
        if (Core() is null)
            return;

        Assert.NotNull(Arm());
    }

    /// <summary>THE TASK. The branch says which type arrived.</summary>
    [Fact]
    public void TheUnhandledBranchNamesTheType()
    {
        if (Arm() is not { } arm)
            return;

        Assert.True(
            UnhandledCtrlMessage.ItNamesTheType(arm),
            "the default arm of ctrl.c's dispatch logs nothing carrying msg_type");
    }

    /// <summary>
    /// THE OTHER HALF. It reports at a level meaning unhandled, hexdump included.
    ///
    /// The hexdump is the half worth stating separately: it was the only line that ran, it ran at
    /// WARNING, and a payload nobody has a name for is not a fault.
    /// </summary>
    [Fact]
    public void TheUnhandledBranchDoesNotReportAFault()
    {
        if (Arm() is not { } arm)
            return;

        Assert.True(
            UnhandledCtrlMessage.ItReportsAsUnhandled(arm),
            "the default arm of ctrl.c's dispatch still reports at a warning or error level");
    }

    /// <summary>
    /// And nothing in it is a log somebody disabled instead of deleting, which is the shape this
    /// defect had for as long as the fork has existed.
    /// </summary>
    [Fact]
    public void TheUnhandledBranchCarriesNoCommentedOutLog()
    {
        if (Arm() is not { } arm)
            return;

        Assert.False(
            UnhandledCtrlMessage.ItCarriesACommentedOutLog(arm),
            "the default arm of ctrl.c's dispatch still carries a commented-out log call");
    }

    /// <summary>
    /// The readers answer both ways on text that is not ctrl.c, so a green above is not a reader
    /// that returns the same verdict whatever it is shown.
    /// </summary>
    [Fact]
    public void TheReadersSeeTheDefectTheyWereWrittenFor()
    {
        const string Before = """
            default:
                // CHIAKI_LOGW(ctrl->session->log, "Received Ctrl Message with unknown type %#x", msg_type);
                chiaki_log_hexdump(ctrl->session->log, CHIAKI_LOG_WARNING, payload, payload_size);
                break;
            """;

        Assert.False(UnhandledCtrlMessage.ItNamesTheType(Before));
        Assert.False(UnhandledCtrlMessage.ItReportsAsUnhandled(Before));
        Assert.True(UnhandledCtrlMessage.ItCarriesACommentedOutLog(Before));

        const string After = """
            default:
                CHIAKI_LOGI(ctrl->session->log, "Ctrl received unhandled message of type %#x, size %#llx",
                        (unsigned int)msg_type, (unsigned long long)payload_size);
                if(payload_size > 0)
                    chiaki_log_hexdump(ctrl->session->log, CHIAKI_LOG_INFO, payload, payload_size);
                break;
            """;

        Assert.True(UnhandledCtrlMessage.ItNamesTheType(After));
        Assert.True(UnhandledCtrlMessage.ItReportsAsUnhandled(After));
        Assert.False(UnhandledCtrlMessage.ItCarriesACommentedOutLog(After));
    }
}
