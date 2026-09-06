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
    /// PP773 TOOK THE LAST ROW IN TWO STEPS, and the first is why PP776 exists. It filled
    /// IBangKeying with a REFUSAL - StreamArrivals hands the bang handler a keying that says no at
    /// the derive, so a console's bang reached the handler and failed there - and this list went
    /// empty on a stub, because a refusing class and a real one are the same shape. The second step
    /// is <see cref="SessionBangKeying"/>, which derives against the session's own ecdh pair; the
    /// row would be gone either way, which is the finding rather than the fix.
    ///
    /// The list is kept rather than deleted with its last output row: what would report a
    /// counterpart going back to being a shape is this list, and its absence would report nothing.
    ///
    /// PP790 PUT A ROW BACK ON IT, which is the sweep doing its job on the commit that made the
    /// gap rather than three commits later. A run that takes a host interface has a shape until
    /// something implements it, and PP773 is what leaving that unsaid costs.
    /// </summary>
    public static IReadOnlyList<UnreachedSeam> Expected { get; } =
    [
        new(
            "ISenkushaRunHost",
            "PP790's run takes it and PP791 writes what fills it. A run whose host only doubles "
                + "implement is a sequence nothing performs - PP669's rule, and the state PP745 "
                + "took the stream connection's own host out of."),
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

    /// <summary>
    /// PP776: the longest a member's body may be and still be doing nothing.
    ///
    /// Two bytes of IL. <c>=&gt; false</c> is <c>ldc.i4.0; ret</c>, <c>=&gt; null</c> is
    /// <c>ldnull; ret</c>, and an empty body is <c>ret</c> alone. Anything that reads a field,
    /// calls something or branches is longer - a delegating property getter is already seven.
    ///
    /// A THRESHOLD AND NOT A HEURISTIC, which is why it is this tight. A wider bound would start
    /// calling real one-line delegations stand-ins, and the point of this axis is to be believed.
    /// </summary>
    public const int ConstantBodyIlBytes = 2;

    /// <summary>
    /// Whether a class fills an interface with nothing: every member a constant or an empty body.
    ///
    /// ALL of them, because a real implementation is allowed a trivial member. PP684's outbound seam
    /// answers <c>SendIdrRequest</c> with a constant in a test double and with a send in the real
    /// one, and what tells them apart is the other three members.
    /// </summary>
    public static bool IsStandIn(Type candidate, Type seam)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(seam);

        if (!candidate.IsClass || candidate.IsAbstract || !seam.IsAssignableFrom(candidate))
            return false;

        InterfaceMapping map = candidate.GetInterfaceMap(seam);
        if (map.TargetMethods.Length == 0)
            return false;

        return Array.TrueForAll(map.TargetMethods, one => IsConstantBodied(one));
    }

    /// <summary>Whether one method's IL is short enough to be a constant or nothing at all.</summary>
    public static bool IsConstantBodied(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        // An abstract or extern method has no body to read, and neither is a stand-in: what fills a
        // seam with nothing is a body that does nothing, not the absence of one.
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();

        return il is not null && il.Length <= ConstantBodyIlBytes;
    }

    /// <summary>
    /// PP776: the seams whose every implementation in this assembly does nothing.
    ///
    /// <see cref="UnreachedIn"/> asks whether a class exists on the other side and PP773 showed what
    /// that misses: it filled IBangKeying with a refusal - a class whose derive returns false,
    /// because the bang handler needs an instance and the port had no ECDH of its own - and the
    /// unreached list went empty on a stub. A refusing class and a real one are the same SHAPE,
    /// which is exactly why the stub compiles.
    ///
    /// So this asks the other question, and the answer is in the bodies rather than in the types.
    /// A seam every one of whose implementations is constant-bodied is a seam nothing has been
    /// written for yet, whatever the type graph says.
    ///
    /// IT IS NOT A DEMAND FOR ZERO. A stand-in can be the honest answer - audio that goes nowhere,
    /// while the picture has a decoder to reach and the sound has none - so this reports and
    /// <see cref="ExpectedStandIns"/> declares, the same way the list above does.
    /// </summary>
    public static IReadOnlyList<string> FilledOnlyByStandInsIn(Assembly app)
    {
        ArgumentNullException.ThrowIfNull(app);

        Type[] classes = [.. app.GetTypes().Where(one => one.IsClass && !one.IsAbstract)];

        return
        [
            .. app.GetTypes()
                .Where(one => one.IsInterface && one.IsPublic)
                .Where(one =>
                {
                    Type[] filling = [.. classes.Where(other => one.IsAssignableFrom(other))];

                    return filling.Length > 0 && Array.TrueForAll(filling, other => IsStandIn(other, one));
                })
                .Select(one => one.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The seams whose only implementations are stand-ins, and why each is one.
    ///
    /// NONE. PP773 left IBangKeying with both - <see cref="SessionBangKeying"/> derives against the
    /// session's own pair, and StreamArrivals keeps a refusal for a caller that supplies no keying -
    /// so the interface is filled by something that works and this list does not name it.
    ///
    /// Kept empty rather than absent, for the reason <see cref="Expected"/> is: a row arriving is a
    /// seam that went back to being a shape, and nothing would report that but this.
    /// </summary>
    public static IReadOnlyList<UnreachedSeam> ExpectedStandIns { get; } = [];
}
