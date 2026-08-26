using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which of the ctrl channel's two pipes a caller pokes.</summary>
public enum CtrlPipe
{
    /// <summary>The one the loop's select waits on: work has arrived, or the channel is stopping.</summary>
    Notify,

    /// <summary>The one handed to a blocking send, so a send in progress can be cancelled.</summary>
    Stop,
}

/// <summary>
/// PP350, under PP294: the ctrl channel's two pipes, and which caller wakes which.
///
/// The session has one stop pipe; the control channel has two, for two different jobs, and wiring
/// one to both breaks a different half each way.
///
/// THE NOTIFY PIPE IS WHAT THE LOOP WAITS ON. Three callers poke it - a stop, a queued message and
/// a typed PIN - so what woke the thread never says what to do, which is why PP349's loop re-reads
/// all three conditions every time.
///
/// THE STOP PIPE IS FOR A SEND ALREADY IN PROGRESS. It is handed to chiaki_send_fully, so a stop
/// arriving mid-write cancels the write rather than waiting for it. A port that wired the notify
/// pipe here would cancel every send the moment anything else was queued; one that wired the stop
/// pipe to the select would never wake for a queued message at all.
///
/// A STOP POKES BOTH. That is worth stating because it is the one case where the two overlap, and
/// because the first version of this note had it wrong: a stop has to reach a thread waiting in the
/// select AND a thread blocked in a write, so it pokes each. Everything else pokes only the notify
/// pipe.
/// </summary>
public static class CtrlPipes
{
    /// <summary>What a stop pokes: both, because it has to reach either wait.</summary>
    public static IReadOnlyList<CtrlPipe> Stopping { get; } = [CtrlPipe.Stop, CtrlPipe.Notify];

    /// <summary>What queueing a message pokes.</summary>
    public static IReadOnlyList<CtrlPipe> Queueing { get; } = [CtrlPipe.Notify];

    /// <summary>What handing over a PIN pokes.</summary>
    public static IReadOnlyList<CtrlPipe> HandingOverAPin { get; } = [CtrlPipe.Notify];

    /// <summary>The pipe the loop's select waits on.</summary>
    public const CtrlPipe Waited = CtrlPipe.Notify;

    /// <summary>The pipe a blocking send is given so it can be cancelled.</summary>
    public const CtrlPipe HandedToASend = CtrlPipe.Stop;

    /// <summary>
    /// Whether a caller poking only this pipe would wake a thread sitting in the select.
    ///
    /// The answer for the stop pipe is no, and that is the failure a port makes by having one pipe:
    /// a queued message would never be noticed.
    /// </summary>
    public static bool WakesTheSelect(CtrlPipe pipe) => pipe == Waited;

    /// <summary>
    /// Whether poking this pipe would cancel a send already in progress.
    ///
    /// The answer for the notify pipe is no, which is the other half: a stop arriving during a
    /// write has to reach the write, and only this pipe does.
    /// </summary>
    public static bool CancelsASendInProgress(CtrlPipe pipe) => pipe == HandedToASend;
}

/// <summary>
/// PP350: the pipes held against ctrl.c, because which caller pokes which is stated nowhere else.
/// </summary>
public static class CtrlPipesSource
{
    /// <summary>Where they live.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Which pipes a named function pokes, in the order it pokes them.</summary>
    public static IReadOnlyList<CtrlPipe> PipesPokedBy(string filePath, string function)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        string? body = CFunction.BodyIn(filePath, function);
        if (body is null)
            return [];

        var poked = new List<CtrlPipe>();
        for (int at = body.IndexOf("chiaki_stop_pipe_stop(", StringComparison.Ordinal);
             at >= 0;
             at = body.IndexOf("chiaki_stop_pipe_stop(", at + 1, StringComparison.Ordinal))
        {
            int end = body.IndexOf(')', at);
            if (end < 0)
                break;

            string which = body[at..end];
            if (which.Contains("notif_pipe", StringComparison.Ordinal))
                poked.Add(CtrlPipe.Notify);
            else if (which.Contains("stop_pipe", StringComparison.Ordinal))
                poked.Add(CtrlPipe.Stop);
        }

        return poked;
    }

    /// <summary>Whether the loop still waits on the notify pipe rather than the other one.</summary>
    public static bool TheSelectStillWaitsOnTheNotifyPipe(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        return threadBody.Contains("stop_pipe_select_single(&ctrl->notif_pipe", StringComparison.Ordinal)
            || threadBody.Contains("select_single(ctrl->session->rudp, &ctrl->notif_pipe", StringComparison.Ordinal);
    }

    /// <summary>Whether a blocking send is still handed the stop pipe rather than the notify one.</summary>
    public static bool ASendIsStillGivenTheStopPipe(string sendBody)
    {
        ArgumentNullException.ThrowIfNull(sendBody);

        return sendBody.Contains("chiaki_send_fully(&ctrl->stop_pipe", StringComparison.Ordinal)
            && !sendBody.Contains("chiaki_send_fully(&ctrl->notif_pipe", StringComparison.Ordinal);
    }
}
