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
    /// <summary>
    /// The ten, as of PP534. Kept in the tree rather than in a commit message, for the reason
    /// PP290 gives about its own thirteen: the value of the list is that the next person sees it
    /// without re-deriving it.
    ///
    /// chiaki_holepunch_session_set_recorded is PP481's own, and is honestly here: app/ reaches it
    /// through the shim's wrapper, so nothing managed names the lib function.
    ///
    /// IT LIVES IN tests/ AND MUST. The census reads app/, so a list of unnamed functions kept
    /// there would name all ten and report that nothing is left - a check answering its own
    /// question. That this test passes with a non-empty set is the evidence it does not.
    /// </summary>
    public static IReadOnlyList<string> Unnamed { get; } =
    [
        "chiaki_holepunch_list_devices",
        "chiaki_holepunch_main_thread_cancel",
        "chiaki_holepunch_session_set_recorded",
        "chiaki_holepunch_upnp_discover",
        "createNq",
        "http_create_session",
        "make_oauth2_header",
        "make_session_id_header",
        "notification_queue_free",
        "session_message_get_payload",
    ];

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
        Assert.Equal(Unnamed, unnamed);
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
