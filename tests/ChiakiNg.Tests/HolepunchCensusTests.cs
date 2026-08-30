using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP534: what is actually left of holepunch.c, as a list rather than a sentence.
///
/// §PP533 named "the candidate and STUN work, the notification queue and the state machine" as
/// what remains. The count disagrees about the first: the candidate and STUN functions are among
/// those app/ already names. Ten are not, and they are what a reader should be handed.
///
/// The set is asserted EXACTLY, the way UnreferencedExportTests asserts its own. Both directions
/// are news: a name leaving means something started answering it, and a name arriving means either
/// a counterpart went away or holepunch.c grew a function nothing has looked at.
/// </summary>
public class HolepunchCensusTests(ITestOutputHelper output)
{
    /// <summary>One C function the sweep did not find quoted, and what actually answers it.</summary>
    /// <param name="Function">The C name.</param>
    /// <param name="Counterpart">
    /// The managed file that answers it, found by reading - or null where nothing does.
    /// </param>
    public sealed record Unquoted(string Function, string? Counterpart);

    /// <summary>
    /// The ten the sweep does not find quoted, each with what answers it. Kept in the tree rather
    /// than in a commit message, for the reason PP290 gives about its own thirteen: the value of
    /// the list is that the next person sees it without re-deriving it.
    ///
    /// PP536: EIGHT OF THE TEN HAVE A COUNTERPART, which is what PP534 got wrong. It read the
    /// sweep's output as "nothing has looked at these" and shipped that sentence; eight had been
    /// looked at and ported under names that do not quote the C symbol. The counterparts here were
    /// found by reading each one, and the test below checks each file is really there - so the
    /// annotation is a claim this suite keeps rather than a note that rots.
    ///
    /// chiaki_holepunch_session_set_recorded is PP481's own: app/ reaches it through the shim's
    /// wrapper, so the lib name is genuinely unquoted while the behaviour is driven.
    ///
    /// IT LIVES IN tests/ AND MUST. The census reads app/, so a list of these kept there would
    /// quote all ten and report that nothing is left - a check answering its own question. That
    /// this test passes with a non-empty set is the evidence it does not.
    /// </summary>
    public static IReadOnlyList<Unquoted> Unnamed { get; } =
    [
        new("chiaki_holepunch_list_devices", "DeviceList.cs"),
        new("chiaki_holepunch_main_thread_cancel", null),
        new("chiaki_holepunch_session_set_recorded", "NativeHolepunchSession.cs"),
        new("chiaki_holepunch_upnp_discover", "GatewayDiscovery.cs"),
        new("createNq", "NotificationQueue.cs"),
        new("http_create_session", "SessionCalls.cs"),
        new("make_oauth2_header", "PsnEndpoints.cs"),
        new("make_session_id_header", "PsnEndpoints.cs"),
        new("notification_queue_free", "NotificationQueue.cs"),
        new("session_message_get_payload", "SessionMessage.cs"),
    ];

    /// <summary>
    /// PP537: the one function in holepunch.c with no managed counterpart at all, and why.
    ///
    /// chiaki_holepunch_main_thread_cancel takes the stop mutex, sets ws_thread_should_stop, stops
    /// the select pipe, sets main_should_stop and signals the notification condition. Nothing
    /// answers it because everything it stops is what PP533 has to build: there is no managed
    /// websocket thread to tell to stop and no managed main loop to cancel. Two drift checks read
    /// the flag out of the C's source and assert things about it; neither is a port of the cancel.
    ///
    /// Kept as a named constant rather than a null in the list above so that the ONE remaining
    /// name is a thing this suite states, not a gap a reader has to notice.
    /// </summary>
    public const string ArrivesWithTheLoop = "chiaki_holepunch_main_thread_cancel";

    private static (string Source, string Managed)? Checkout()
    {
        string? source = HolepunchCensus.Locate();
        string? managed = HolepunchCensus.LocateManaged();
        return source is null || managed is null ? null : (File.ReadAllText(source), managed);
    }

    /// <summary>
    /// THE CENSUS. Exactly these are unnamed, and everything else holepunch.c defines is named.
    /// </summary>
    [Fact]
    public void ExactlyTheseFunctionsAreNamedNowhereUnderApp()
    {
        if (Checkout() is not { } checkout)
            return;

        (var named, var unnamed) = HolepunchCensus.Split(checkout.Source, checkout.Managed);

        output.WriteLine($"{named.Count} named, {unnamed.Count} not, of {named.Count + unnamed.Count}");
        Assert.Equal([.. Unnamed.Select(u => u.Function)], unnamed);
    }

    /// <summary>
    /// PP536: every counterpart the list claims is a file that is really there.
    ///
    /// Without this the annotation is prose, and prose is what PP534 shipped wrongly in the first
    /// place. A counterpart that gets renamed or deleted has to break something, or the next reader
    /// inherits the same confident sentence about work nobody has done.
    /// </summary>
    [Fact]
    public void EveryCounterpartTheListClaimsIsInTheTree()
    {
        if (HolepunchCensus.LocateManaged() is not { } managed)
            return;

        foreach (Unquoted entry in Unnamed.Where(u => u.Counterpart is not null))
        {
            string[] found = Directory.GetFiles(managed, entry.Counterpart!, SearchOption.AllDirectories);
            Assert.True(found.Length > 0,
                $"{entry.Function} claims {entry.Counterpart} answers it, and no such file is under app/");
        }
    }

    /// <summary>
    /// And most of them DO have one, which is the correction PP536 exists for. Asserted as a floor
    /// so that answering the last one does not fail this, and asserted at all so that quietly
    /// emptying the annotations would.
    /// </summary>
    [Fact]
    public void MostOfTheUnquotedOnesAreAnsweredUnderAnotherName()
    {
        Assert.True(Unnamed.Count(u => u.Counterpart is not null) >= 9,
            "the point of PP536 is that most of these are already answered");
    }

    /// <summary>
    /// PP537: exactly one of the sixty-seven has no counterpart, and it is the cancel.
    ///
    /// This is the sharpest statement of what is left of holepunch.c, and it is worth an assertion
    /// because two rounds of counting got a looser one wrong. If a second name ever joins it, the
    /// port has lost ground somewhere and this is where that shows.
    /// </summary>
    [Fact]
    public void OnlyTheCancelHasNoCounterpartAtAll()
    {
        var without = Unnamed.Where(u => u.Counterpart is null).Select(u => u.Function).ToList();

        Assert.Equal([ArrivesWithTheLoop], without);
    }

    /// <summary>
    /// And most of it IS named, which is the finding §PP533's sentence got wrong. Asserted as a
    /// floor rather than a figure: the number rises as work lands, and pinning it exactly would
    /// make every step of PP533 fail this test on its way past.
    /// </summary>
    [Fact]
    public void MostOfTheFileIsAlreadyNamed()
    {
        if (Checkout() is not { } checkout)
            return;

        (var named, var unnamed) = HolepunchCensus.Split(checkout.Source, checkout.Managed);

        Assert.True(named.Count >= 57, $"only {named.Count} of holepunch.c's functions are named");
        Assert.Equal(Unnamed.Count, unnamed.Count);
    }

    /// <summary>
    /// The candidate and STUN work in particular, because that is the part the sentence called
    /// remaining. If these ever stop being named the sentence was right after all and this test is
    /// the place to find that out.
    /// </summary>
    [Fact]
    public void TheCandidateAndStunWorkIsAmongTheNamed()
    {
        if (Checkout() is not { } checkout)
            return;

        (var named, _) = HolepunchCensus.Split(checkout.Source, checkout.Managed);

        Assert.Contains("check_candidates", named);
        Assert.Contains("candidate_event_cb", named);
    }

    /// <summary>
    /// Definitions, not declarations. holepunch.c declares its statics at the top and defines them
    /// far below, so a sweep that took both would count every static twice - and would count a
    /// name nothing implements as implemented, which is the wrong direction for a census to err in.
    /// </summary>
    [Fact]
    public void ADeclarationIsNotADefinition()
    {
        const string source = """
            static void takes_no_body(ChiakiLog *log, json_object* json);

            static void has_a_body(ChiakiLog *log)
            {
                (void)log;
            }
            """;

        Assert.Equal(["has_a_body"], HolepunchCensus.DefinedFunctions(source));
    }

    /// <summary>
    /// A function nothing mentions comes back unnamed, and one a comment mentions comes back
    /// named. The second is the upper-bound behaviour said out loud: a bare identifier is what
    /// finds a callback, and it also finds prose, and this check would rather over-report coverage
    /// than miss a reference.
    /// </summary>
    [Fact]
    public void AMentionInProseCountsAsNamedAndSaysSo()
    {
        string root = Path.Combine(Path.GetTempPath(), "pp534-" + Guid.NewGuid().ToString("N"));
        string managed = Path.Combine(root, "app");

        try
        {
            Directory.CreateDirectory(managed);
            File.WriteAllText(Path.Combine(managed, "Model.cs"), "// mirrors mentioned_only in C\n");

            const string source = """
                static void mentioned_only(void)
                {
                }

                static void nobody_mentions_this(void)
                {
                }
                """;

            (var named, var unnamed) = HolepunchCensus.Split(source, managed);

            Assert.Equal(["mentioned_only"], named);
            Assert.Equal(["nobody_mentions_this"], unnamed);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
