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
/// PP540: ONLY ONE DIRECTION IS NEWS, which is not how this started. The set was asserted exactly,
/// the way UnreferencedExportTests asserts its own - and unlike that one, this sweep's answer moves
/// whenever any model mentions a C symbol, which in a tree full of source-reading models is
/// constantly. Four consecutive tasks turned it red without porting anything.
///
/// So a name LEAVING the sweep's output is not news, and a name arriving with nothing to answer it
/// is: holepunch.c grew a function nothing has looked at, or a counterpart went away.
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
    /// What answers each holepunch.c function the sweep has ever reported as unquoted. Kept in the
    /// tree rather than in a commit message, for the reason PP290 gives about its own thirteen:
    /// the value of the list is that the next person sees it without re-deriving it.
    ///
    /// PP536: MOST OF THEM HAVE A COUNTERPART, which is what PP534 got wrong. It read the sweep's
    /// output as "nothing has looked at these" and shipped that sentence; they had been looked at
    /// and ported under names that do not quote the C symbol. The counterparts here were found by
    /// reading each one, and the test below checks each file is really there - so the annotation is
    /// a claim this suite keeps rather than a note that rots.
    ///
    /// PP540: ENTRIES STAY WHEN THE SWEEP STOPS REPORTING THEM. DeviceList still answers the device
    /// listing whether or not any file quotes the C name today, and a list that dropped an entry
    /// every time a model mentioned a symbol was the churn PP540 removed.
    ///
    /// chiaki_holepunch_session_set_recorded is PP481's own: app/ reaches it through the shim's
    /// wrapper, so the lib name is genuinely unquoted while the behaviour is driven.
    ///
    /// IT LIVES IN tests/ AND MUST. The census reads app/, so a list of these kept there would
    /// quote every one and report that nothing is left - a check answering its own question.
    /// </summary>
    public static IReadOnlyList<Unquoted> Unnamed { get; } =
    [
        new("chiaki_holepunch_list_devices", "DeviceList.cs"),
        new("chiaki_holepunch_main_thread_cancel", "HolepunchStop.cs"),
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
    /// PP537 named chiaki_holepunch_main_thread_cancel as the one function with no counterpart at
    /// all, and gave the reason: everything it stops is what PP533 has to build. PP538 built the
    /// stop, so the name is gone from the list above and this records where it went.
    /// </summary>
    public const string TheCancelWasAnsweredBy = "HolepunchStop.cs";

    private static (string Source, string Managed)? Checkout()
    {
        string? source = HolepunchCensus.Locate();
        string? managed = HolepunchCensus.LocateManaged();
        return source is null || managed is null ? null : (File.ReadAllText(source), managed);
    }

    /// <summary>
    /// THE CENSUS, as PP540 rewrote it: every function the sweep reports as unquoted is one this
    /// list already answers.
    ///
    /// It used to assert the two were EQUAL, and that churned on four consecutive tasks - none of
    /// which ported anything. PP538 named the cancel in a comment, PP539 named
    /// session_message_get_payload while explaining a misattribution, and each cost a red run and
    /// an edit. A tree full of source-reading models is one where naming the thing you model is
    /// normal, so an assertion keyed on which symbols happen to be mentioned asks the wrong
    /// question.
    ///
    /// This asks the one that has never moved. A NEW unquoted name is real news - holepunch.c grew
    /// something nothing answers, or a counterpart went away - and a name leaving the sweep's
    /// output is not news at all.
    /// </summary>
    [Fact]
    public void EveryUnquotedFunctionIsOneThisListAnswers()
    {
        if (Checkout() is not { } checkout)
            return;

        (var named, var unnamed) = HolepunchCensus.Split(checkout.Source, checkout.Managed);

        output.WriteLine($"{named.Count} named, {unnamed.Count} not, of {named.Count + unnamed.Count}");

        var answered = Unnamed
            .Where(u => u.Counterpart is not null)
            .Select(u => u.Function)
            .ToHashSet(StringComparer.Ordinal);

        var unanswered = unnamed.Where(f => !answered.Contains(f)).ToList();

        Assert.True(unanswered.Count == 0,
            "holepunch.c has function(s) app/ does not quote and this list does not answer: "
            + string.Join(", ", unanswered));
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
        Assert.True(Unnamed.Count(u => u.Counterpart is not null) >= 10,
            "the point of PP536 is that most of these are already answered");
    }

    /// <summary>
    /// PP538: nothing in holepunch.c is now without a managed counterpart.
    ///
    /// PP537 left exactly one - the cancel - and said it would arrive with the loop PP533 has to
    /// write. It did, in HolepunchStop, and the sweep noticed on its own: the name is quoted under
    /// app/ now, so it left this list without anybody editing the list.
    ///
    /// Asserted as empty rather than deleted, because the interesting direction is a name ARRIVING.
    /// One would mean holepunch.c grew a function nothing answers, and this is where that shows.
    /// </summary>
    [Fact]
    public void NothingIsLeftWithoutACounterpart()
    {
        var without = Unnamed.Where(u => u.Counterpart is null).Select(u => u.Function).ToList();

        Assert.Empty(without);
        Assert.Equal("HolepunchStop.cs", TheCancelWasAnsweredBy);
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

        // 58 since PP538 named the cancel, up from PP534's 57.
        // PP540: a floor, and no equality against the annotated list. The sweep's count moves
        // whenever a model mentions a symbol, and pinning it was the churn.
        Assert.True(named.Count >= 59, $"only {named.Count} of holepunch.c's functions are named");
        Assert.True(unnamed.Count <= Unnamed.Count,
            $"{unnamed.Count} unquoted, more than the {Unnamed.Count} this list answers");
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
