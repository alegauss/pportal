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
    /// NONE. PP745 took IStreamRunHost off this list, PP747 the event sink, PP748 the message sink,
    /// PP749 the congestion sink, PP750 the feedback sink, PP751 the audio frames and PP773 the last
    /// one - none of them adding a row doing it, which is what PP741 counted the cost of when PP740
    /// closed one seam and opened another in a single commit.
    ///
    /// PP773 IS THE ROW WORTH READING TWICE, because what filled IBangKeying REFUSES. StreamArrivals
    /// hands the bang handler a keying that says no at the derive, so a console's bang now reaches
    /// the handler and fails there rather than reaching nothing at all. That is a real change to the
    /// shape - the interface is consumed by something constructible in app, which is the only
    /// question this sweep asks - and it is NOT the derivation being ported. What reports the
    /// missing derivation is a roadmap line, not this list, and a reader who takes an empty list for
    /// a finished port would be reading it as a census of behaviour rather than of shape.
    ///
    /// The list is kept rather than deleted with its last output row: what would report a
    /// counterpart going back to being a shape is this list, and its absence would report nothing.
    /// </summary>
    public static IReadOnlyList<UnreachedSeam> Expected { get; } = [];

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
