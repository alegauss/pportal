using System.Reflection;

namespace ChiakiNg.Session;

/// <summary>One interface the port declares and consumes, with nothing in app on the other side.</summary>
/// <param name="Interface">The interface's name, as it is declared.</param>
/// <param name="Why">What is missing, or why the shape is the point rather than a gap.</param>
public readonly record struct UnreachedSeam(string Interface, string Why);

/// <summary>
/// PP741: every seam in app that only test doubles fill, asked of the assembly rather than of a list.
///
/// PP734 and PP738 each added the same question to one census: not whether a row has a counterpart,
/// but whether the counterpart is REACHED. Both used the same three lines of reflection, and both
/// asked them of that census's own rows. So the answer was about twenty-five members in one case and
/// eleven in the other, and about nothing else in the assembly.
///
/// WHICH IS HOW PP740 CLOSED ONE AND OPENED ANOTHER IN THE SAME COMMIT. It gave IAudioSink an
/// implementation, the run-host census went to empty, and ManagedAudioReceiver's own output seam -
/// IAudioFrameSink - had nobody on the far side. The census reported success because the new gap was
/// not one of its rows. A sweep of the assembly counts seven in that state; two were ever watched.
///
/// AND SEVEN IS THE REFLECTED ANSWER, not a read one. The list this task was filed with had eight,
/// from a grep for a type declaration naming each interface: it missed IStreamMessageSink and
/// wrongly held IAvArmSink and IVideoReceiverOutbound, both of which a primary-constructor class
/// implements on a line the pattern could not see. Which is the argument for the sweep.
///
/// A LIST WITH REASONS AND NOT A DEMAND FOR ZERO. Some of these are seams on purpose:
/// <see cref="IBangKeying"/> stands in front of OpenSSL because the port keeps that behind a seam,
/// and IStreamRunHost is a shape until something drives a live stream. Asserted in both directions,
/// as PP734's is - a row arriving is a counterpart that stopped being shipping code, and a row
/// leaving is a commit that gave one an implementation.
///
/// PUBLIC INTERFACES ONLY, because a private one is an implementation detail of the file that
/// declares it and its implementor is usually in the same file.
/// </summary>
public static class SeamReach
{
    /// <summary>
    /// The seams nothing in app fills, and what each is waiting for.
    ///
    /// Six: five outputs whose consumer is the run, and the OpenSSL keying which is a seam on
    /// purpose. PP745 took IStreamRunHost off this list by writing the host, and added no row doing
    /// it - its own stage seam ships with the class that fills it, which is what PP741 counted the
    /// cost of when PP740 closed one seam and opened another in the same commit.
    /// </summary>
    public static IReadOnlyList<UnreachedSeam> Expected { get; } =
    [
        new(
            "IAudioFrameSink",
            "PP740's own output: the C's ChiakiAudioSink. Opus and a device are the audio path's work."),
        new(
            "IBangKeying",
            "Deliberate: the keying a bang leads to is OpenSSL's, and the port keeps it behind a seam."),
        new(
            "ICongestionSink",
            "Where a congestion report goes. The takion in the C; whatever the run gives it here."),
        new(
            "IFeedbackSink",
            "PP723's sender output, which the takion answers in the C and nothing answers yet."),
        new(
            "ISessionEventSink",
            "PP719's seam for session->event_cb. Nothing managed listens, which is why Send counts Unheard."),
        new(
            "IStreamMessageSink",
            "Where a built message goes, so a builder needs no socket. The run is what would hold one."),
    ];

    /// <summary>Every public interface the assembly declares.</summary>
    public static IReadOnlyList<string> DeclaredIn(Assembly app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return
        [
            .. app.GetTypes()
                .Where(one => one.IsInterface && one.IsPublic)
                .Select(one => one.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Those with no class in the same assembly on the other side of them.
    ///
    /// A class and not a struct or another interface: what makes a seam reached is something that
    /// can be constructed and handed over, and an interface extending an interface moves the
    /// question rather than answering it.
    /// </summary>
    public static IReadOnlyList<string> UnreachedIn(Assembly app)
    {
        ArgumentNullException.ThrowIfNull(app);

        Type[] classes = [.. app.GetTypes().Where(one => one.IsClass)];

        return
        [
            .. app.GetTypes()
                .Where(one => one.IsInterface && one.IsPublic)
                .Where(one => !Array.Exists(classes, other => one.IsAssignableFrom(other)))
                .Select(one => one.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>The rows whose interface the assembly no longer declares, which is a stale list.</summary>
    public static IReadOnlyList<string> NamedButNotDeclared(Assembly app)
    {
        IReadOnlySet<string> declared = new HashSet<string>(DeclaredIn(app), StringComparer.Ordinal);

        return [.. Expected.Select(one => one.Interface).Where(one => !declared.Contains(one))];
    }
}
