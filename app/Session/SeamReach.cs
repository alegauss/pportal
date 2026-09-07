using System.Reflection;
using System.Text.RegularExpressions;

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
    ///
    /// PP791 TOOK IT OFF AGAIN. <see cref="ChiakiNg.Protocol.ManagedSenkushaRunHost"/> implements
    /// ISenkushaRunHost against the port's own takion, so the row lasted the two commits between the
    /// run and the thing that performs it - which is what a row on this list is for, and shorter
    /// than the three blocks PP741 measured.
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

    /// <summary>The directory whose code decides whether an implementation is ever built.</summary>
    public const string AppRelativeDirectory = "app";

    /// <summary>
    /// PP800: the seams whose every implementation is a type nothing in app ever constructs.
    ///
    /// <see cref="UnreachedIn"/> asks whether a class exists and <see cref="FilledOnlyByStandInsIn"/>
    /// asks whether it does anything. Neither asks whether it is ever BUILT, and PP799 is what that
    /// cost: PP747 wrote the event router, this census called the seam reached, and nothing in app
    /// had ever constructed one - so a streaming session counted its CONNECTED event Unheard, eight
    /// tasks later, on a tree where every check was green.
    ///
    /// PP767 MET THIS ONE LEVEL UP AND ANSWERED IT THE SAME WAY. Its first driver check asked
    /// whether any file installs a run, and PP762 satisfied it from inside the class that would do
    /// the driving, which nothing constructed. The repair was a narrower question, and this is that
    /// question for the seven seams: not "is there an implementation" but "is one made".
    ///
    /// A CONSTRUCTION SITE IS A `new` IN APP'S OWN CODE, read as code and never as text - PP735's
    /// trap, and this file is full of type names in prose. Two spellings count, because C# has two:
    /// the explicit <c>new Foo(</c> and the target-typed <c>Foo bar = new(</c> that names the type
    /// on the left. A census that knew only the first would report half this tree's fields as
    /// unbuilt.
    /// </summary>
    public static IReadOnlyList<string> UnconstructedIn(Assembly app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (AppCode() is not { Length: > 0 } code)
            return [];

        Type[] classes = [.. app.GetTypes().Where(one => one.IsClass && !one.IsAbstract)];

        return
        [
            .. app.GetTypes()
                .Where(one => one.IsInterface && one.IsPublic)
                .Where(one =>
                {
                    Type[] filling = [.. classes.Where(other => one.IsAssignableFrom(other))];

                    return filling.Length > 0
                        && !Array.Exists(filling, other => IsConstructed(code, other.Name));
                })
                .Select(one => one.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Whether app's own code builds one of these, in either of C#'s two spellings.
    /// </summary>
    /// <param name="code">app's source with comments and string contents removed.</param>
    /// <param name="name">The type's own name, without its namespace.</param>
    public static bool IsConstructed(string code, string name)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentException.ThrowIfNullOrEmpty(name);

        // The generic arity marker, which reflection spells and source does not.
        string bare = name.Split('`')[0];

        if (code.Contains($"new {bare}(", StringComparison.Ordinal))
            return true;

        // Target-typed, where the type is named on the LEFT: `private readonly Foo bar = new();`
        // and `public Foo Bar { get; } = new();`. Bounded to one line, so a declaration and an
        // unrelated construction further down cannot be read as one site - and the accessor list
        // is stepped over as a unit, because the semicolon inside `{ get; }` is not the statement's.
        return Regex.IsMatch(
            code,
            $@"\b{Regex.Escape(bare)}\b[^;{{\r\n]*(?:\{{[^}}\r\n]*\}})?[^;\r\n]*=\s*new\s*\(",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// app's own source, comments and string contents removed, or empty outside a checkout.
    ///
    /// The obj and bin trees are skipped for the reason every sweep here skips them: a generated
    /// copy of this assembly's source would answer about a build rather than about the tree.
    /// </summary>
    public static string AppCode()
    {
        if (SanitizerSource.LocateDirectory(AppRelativeDirectory) is not { } root)
            return string.Empty;

        var built = new System.Text.StringBuilder();

        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains(@"\obj\", StringComparison.Ordinal)
                || path.Contains(@"\bin\", StringComparison.Ordinal))
            {
                continue;
            }

            built.Append(DeadAssertions.CodeOnly(File.ReadAllText(path))).Append('\n');
        }

        return built.ToString();
    }

    /// <summary>
    /// The seams whose implementations exist and are never built, and what each one is.
    ///
    /// THREE, AND THE THIRD IS WHY THIS AXIS EXISTS. The first two are harnesses and say so; the
    /// last is a ported module with no caller, found by this sweep on the commit that wrote it.
    /// PP799 was the same shape found eight tasks late, on a tree where every other check was
    /// green - which is what a row here is worth.
    ///
    /// A ROW IS NOT A PERMISSION. <see cref="Expected"/>'s rows say what a seam is waiting for and
    /// so do these: two are waiting for nothing and one is waiting for PP811.
    /// </summary>
    public static IReadOnlyList<UnreachedSeam> ExpectedUnconstructed { get; } =
    [
        // The replay harness. ExchangeReplay drives a recorded conversation against a participant
        // and the three participants are the port's side of it - a corpus replay is a test by
        // construction, so the test project building them is where they belong.
        new(
            "IExchangeParticipant",
            "the exchange replay is a test harness: its participants answer a recording, and app "
                + "has no path that replays one"),

        // PP632 took holepunch off every path the host runs, so the live steps have no caller here
        // by design rather than by omission - PP481's tests are what reach them, and those need a
        // punched hole to a real machine as well.
        new(
            "IHolepunchPunchSteps",
            "PP632 left holepunch with no caller in app, and PP481's live tests are what build the "
                + "steps - which need a console as well"),

        // And the finding. TakionSendPath is PP675's port of chiaki_takion_send and nothing sends
        // through it; ManagedTakion's four sends stamp and send inline, so the lock the C holds
        // across both is not held. PP811 is the reading that says whether that is reachable.
        new(
            "ITakionWire",
            "PP811: TakionSendPath has no caller, so the only implementation in app is the "
                + "recording one and a live takion sends round the ported path"),
    ];
}
