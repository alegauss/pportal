using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP544: the second consumer of libchiaki's holepunch, which PP33 recorded as not existing.
///
/// The interesting failure here is a PASS that should not be: if holepunch-test.c is deleted or
/// stops linking the library, PP544's question is settled by removal, and these say so rather than
/// quietly going green on a tree where the file is gone again.
///
/// PP591 IS THAT REMOVAL, MADE DELIBERATELY. So the harness assertions here have turned over: they
/// held that the file was in the tree and built, and they now hold that it is gone and that nothing
/// declares its target. Turned over rather than deleted, because a harness arriving back - as a
/// port, or as a merge from upstream - is a consumer of holepunch.c again, and PP544 exists because
/// this one was in the tree while the record said it was not.
/// </summary>
public class HolepunchConsumersTests
{
    /// <summary>
    /// PP591: the harness that was deleted, spelled here and not in the app assembly.
    ///
    /// PP278's corpus sweeps that assembly's string constants and asserts every repository path
    /// among them is on disk, so a constant naming a deliberately deleted file reports a correct
    /// tree as broken. PP435 put the two binaries it removed in its own test for the same reason:
    /// a path that must NOT resolve belongs beside the assertion that says so.
    /// </summary>
    private const string DeletedHarness = @"lib\src\remote\holepunch-test.c";

    /// <summary>
    /// PP563: THE THIRD CONSUMER, which this port wrote itself.
    ///
    /// PP481 wrapped nine holepunch exports in the shim so the managed side could drive the C. That
    /// was deliberate and is not reopened here. What was not written down is the consequence: the
    /// port's own seam is a caller of the file PP33 exists to delete, and it arrived from a task the
    /// roadmap lists among PP33's satisfied deps.
    /// </summary>
    [Fact]
    public void TheShimIsNoLongerAConsumer()
    {
        if (HolepunchConsumers.LocateShim() is not { } path)
            return;

        // PP33: the nine are ALL missing now, which is the flip. The list is kept and so is the
        // reader - one of the nine reappearing is what this exists to catch, and the shim is the
        // last consumer holepunch.c had.
        Assert.Equal(
            HolepunchConsumers.ShimCalls.Order(StringComparer.Ordinal),
            HolepunchConsumers.MissingFromShim(File.ReadAllText(path)).Order(StringComparer.Ordinal));

        Assert.Equal(9, HolepunchConsumers.ShimCalls.Count);
    }

    /// <summary>
    /// Both are named rather than counted - a deletion needs which, not how many.
    ///
    /// PP564: it was three until a linker was asked. Building chiaki-lib without holepunch.c named
    /// ctrl.c in thirty seconds, after PP563 had read the tree and concluded three.
    ///
    /// PP590 took ctrl.c off it and PP591 the harness. PP632 took session.c, which is the one that
    /// was actually the work: its nine went with the `holepunch_session` field, and the Qt client's
    /// build - the only thing that ever set that field - retired in the same commit.
    ///
    /// ONE IS LEFT and it is this port's own seam. It does not leave by being read again either: the
    /// shim wraps nine exports as the oracle PP481 put there, so removing it is a decision about
    /// what holds the managed holepunch flow honest, not a deletion.
    /// </summary>
    [Fact]
    public void TheDeletionHasOneNamedConsumer()
    {
        // PP632: session.c left with its nine, and the Qt client's build - the only thing that ever
        // put a holepunch handle into a ChiakiSession - retired in the same commit. What is left is
        // the one this port wrote itself.
        Assert.Equal([@"shim\chiaki_shim.c"], HolepunchConsumers.All);

        Assert.DoesNotContain(@"lib\src\session.c", HolepunchConsumers.All);
        Assert.DoesNotContain(HolepunchConsumers.CtrlRelativePath, HolepunchConsumers.All);
        Assert.DoesNotContain(DeletedHarness, HolepunchConsumers.All);
    }

    /// <summary>
    /// PP590: ctrl.c reads the port session.c recorded, and asks nobody. PP33 is what it is for.
    ///
    /// PP564 found it here by asking a linker, and its one ask was for the control port - a value
    /// session.c reads out of the same handle a few hundred lines earlier, when it builds its own
    /// request. So the ask was a second reading of something already known, and the fallback that
    /// made it easy to miss is what made it cheap to remove: the file already had an answer for not
    /// having a holepunch session, and zero is now that same answer.
    /// </summary>
    [Fact]
    public void CtrlTakesTheRecordedPortAndAsksNobody()
    {
        if (HolepunchConsumers.LocateCtrl() is not { } path)
            return;

        Assert.True(
            HolepunchConsumers.CtrlReadsTheRecordedPort(File.ReadAllText(path)),
            "ctrl.c is a consumer of holepunch.c again, or lost the fallback that covers the LAN path");
    }

    /// <summary>
    /// PP590: and session.c records it, which is the half that keeps the PSN path working.
    ///
    /// Asserted separately because the two files fail in opposite directions. A ctrl.c that kept the
    /// call is a consumer the deletion still has to answer for and the suite says so loudly; a
    /// session.c that stopped recording is silent - ctrl.c falls back to 9295 and remote play over
    /// PSN breaks on hardware nothing in this tree can reach.
    /// </summary>
    [Fact]
    public void NothingRecordsThePortAndCtrlFallsBack()
    {
        if (SessionHolepunchShape.Locate() is not { } path)
            return;
        if (HolepunchConsumers.LocateCtrl() is not { } ctrl)
            return;

        // PP632: the ask session.c recorded FROM was one of the nine, so this turned over. PP590's
        // arrangement has one half left and it is the half that matters: ctrl.c reads the field,
        // finds zero, and uses SESSION_CTRL_PORT - which is what its fallback was written for.
        Assert.True(
            HolepunchConsumers.NothingRecordsTheCtrlPortAndCtrlStillFallsBack(
                File.ReadAllText(path), File.ReadAllText(ctrl)),
            "session.c asks for the ctrl port again, or ctrl.c lost the fallback that covers the LAN path");
    }

    /// <summary>
    /// PP590: and the reader can see a ctrl.c that reads the field and kept the call, which is the
    /// shape a half-finished removal leaves and the one that would otherwise pass.
    /// </summary>
    [Fact]
    public void KeepingTheCallBesideTheFieldIsNotEnough()
    {
        Assert.False(HolepunchConsumers.CtrlReadsTheRecordedPort(
            "int port = session->ctrl_port ? chiaki_get_ps_ctrl_port(h) : SESSION_CTRL_PORT;"));

        Assert.False(HolepunchConsumers.CtrlReadsTheRecordedPort(
            "int port = session->ctrl_port;"));

        Assert.True(HolepunchConsumers.CtrlReadsTheRecordedPort(
            "int port = session->ctrl_port ? session->ctrl_port : SESSION_CTRL_PORT;"));
    }

    /// <summary>
    /// PP564: and one export carries no chiaki_ prefix, so a prefix sweep misses it.
    ///
    /// session.c calls holepunch_session_create_offer, which is CHIAKI_EXPORT all the same. Every
    /// reader that finds these by their prefix - which is how they are found - walks past it.
    /// </summary>
    [Fact]
    public void OneExportCarriesNoPrefix()
    {
        Assert.DoesNotContain("chiaki_", HolepunchConsumers.UnprefixedExport, StringComparison.Ordinal);

        // PP591: found from the repository root, not from the harness. This used to walk up from
        // holepunch-test.c, so deleting that file would have turned the header check off rather
        // than red - a check that stops asking is the failure PP56 and PP226 were both filed for.
        if (SanitizerSource.LocateRelative(@"lib\include\chiaki\remote\holepunch.h") is not { } header)
            return;

        Assert.Contains(
            "CHIAKI_EXPORT ChiakiErrorCode " + HolepunchConsumers.UnprefixedExport,
            File.ReadAllText(header), StringComparison.Ordinal);
    }

    /// <summary>A shim that stopped wrapping one is caught, which is how the list stays true.</summary>
    [Fact]
    public void AShimMissingAWrapperIsCaught()
    {
        IReadOnlyList<string> missing = HolepunchConsumers.MissingFromShim("int nothing(void) { return 0; }");

        Assert.Equal(HolepunchConsumers.ShimCalls.Count, missing.Count);
    }

    /// <summary>
    /// PP565: the tree the curl-and-json-c measurement was taken on is the tree in front of us.
    ///
    /// The measurement itself needs a build with three lines commented out, so it cannot live in a
    /// test. What can is its precondition: holepunch.c still in the library's sources, and both
    /// libraries still linked. Change any of the three and the recorded result - that libchiaki.a
    /// builds with neither, once that one file is gone - is about a different tree.
    ///
    /// PP566: AND PP33'S BUILD CRITERION NOW CITES IT. That criterion said the build "configures
    /// and links without either" as one step; it is two, and the compile half is already true. Its
    /// reason names this measurement, so this assertion is what keeps the reason honest - a tree
    /// where these three lines moved is one the criterion is describing wrongly.
    /// </summary>
    [Fact]
    public void TheThreeLinesTheMeasurementNeededAreGone()
    {
        if (HolepunchConsumers.LocateLibCMake() is not { } path)
            return;

        Assert.True(
            HolepunchConsumers.TheThreeAreGoneFromTheBuild(File.ReadAllText(path)),
            "lib/CMakeLists.txt names holepunch.c, curl or json-c again, so PP33's flip has been "
                + "undone and the build links what the criterion says it does not");

        Assert.Equal(@"lib\src\remote\holepunch.c", HolepunchConsumers.OnlyFileNeedingCurlAndJsonC);
    }

    /// <summary>
    /// And the file the two libraries are for is the file the deletion is about - one claim, not
    /// two, which is what makes PP33's DoD line reachable by removing a single source.
    /// </summary>
    [Fact]
    public void TheFileTheyAreForIsTheFileBeingDeleted()
        => Assert.Contains(
            HolepunchConsumers.OnlyFileNeedingCurlAndJsonC,
            DeletedHarness.Replace(
                "holepunch-test.c", "holepunch.c", StringComparison.Ordinal));

    /// <summary>
    /// PP573: PP33'S OWN LINE AGREES ON THE COUNT, and for a long time it did not.
    ///
    /// Its reason read "session.c is its only caller" while PP544 found the harness, PP563 the shim
    /// and PP564 - by asking a linker - ctrl.c. Three shipped tasks each falsified it and none
    /// changed it, because a task's models are where a finding lands and its LINE is somewhere else.
    ///
    /// It is the first sentence a session picking the ready line reads, so the deletion's scope was
    /// wrong exactly where somebody decides what it costs. PP501 fixed the same shape on PP27's line.
    /// </summary>
    [Fact]
    public void ThePP33LineHasLeftTheRoadmapWithItsCountStillRight()
    {
        // PP33 shipped, so the line a session picking work would read is gone from the roadmap.
        // The claim it carried can no longer be wrong where it mattered - which is the outcome
        // this check wanted, not a reason to stop looking.
        if (SanitizerSource.LocateRelative(@"docs\ROADMAP.md") is { } roadmap)
        {
            Assert.DoesNotContain(
                File.ReadLines(roadmap),
                one => one.Contains("**PP33**", StringComparison.Ordinal));
        }

        // The count itself is still asserted, against the ledger's copy. PP622 is why it reads at
        // all: the clause used to be a count word and a fixed plural, so the demand at one would
        // have been "one files call it" - the very count PP632 arrived at.
        if (SanitizerSource.LocateRelative(@"docs\CHANGELOG.md") is not { } changelog)
            return;

        string? line = File.ReadLines(changelog)
            .FirstOrDefault(one => one.Contains("**PP33**", StringComparison.Ordinal));

        Assert.NotNull(line);
        Assert.Single(HolepunchConsumers.All);
    }

    /// <summary>And the old claim is what the check refuses, not merely an absent phrase.</summary>
    [Fact]
    public void TheOldOnlyCallerClaimIsRefused()
    {
        Assert.False(HolepunchConsumers.TheRoadmapLineAgreesOnTheCount(
            "the deletion: session.c is its only caller."));

        Assert.False(HolepunchConsumers.TheRoadmapLineAgreesOnTheCount("says nothing about callers"));

        // PP591: and every count the list has moved past is refused, so the line cannot keep an
        // older number. Both of the two it held before are here, because the line was wrong at four
        // and would be wrong at three the same way.
        Assert.False(HolepunchConsumers.TheRoadmapLineAgreesOnTheCount(
            "and four files call it - session.c, ctrl.c, the harness, the shim."));

        Assert.False(HolepunchConsumers.TheRoadmapLineAgreesOnTheCount(
            "and three files call it - session.c, the harness, the shim."));

        // PP632: and the one it held until this commit.
        Assert.False(HolepunchConsumers.TheRoadmapLineAgreesOnTheCount(
            "and two files call it - session.c, the shim."));

        Assert.True(HolepunchConsumers.TheRoadmapLineAgreesOnTheCount(
            "and one file calls it - the shim, which wraps nine of its exports."));
    }

    /// <summary>
    /// PP622: the counts this line is HEADING for have sentences, and they are sentences.
    ///
    /// The check built the clause as a count word and a fixed plural, so the demand at one was "one
    /// files call it" - a sentence no writer would write, for the very count PP33 arrives at when
    /// session.c stops asking and the shim is what is left. The gate would have turned from holding
    /// the line honest to refusing every correct spelling of it, in the commit that made it true.
    ///
    /// Zero is here too, and it is not the plural with a different word in front. A line whose
    /// consumers are all gone is not claiming anything about how many files call the C.
    /// </summary>
    [Fact]
    public void EveryCountTheLineCanReachHasASentence()
    {
        Assert.Equal("no file calls it", HolepunchConsumers.SentenceFor(0));
        Assert.Equal("one file calls it", HolepunchConsumers.SentenceFor(1));
        Assert.Equal("two files call it", HolepunchConsumers.SentenceFor(2));
        Assert.Equal("four files call it", HolepunchConsumers.SentenceFor(4));

        // The shape that was demanded, refused as a sentence rather than merely unproduced.
        Assert.DoesNotContain("one files", HolepunchConsumers.SentenceFor(1), StringComparison.Ordinal);
    }

    /// <summary>
    /// PP622: and the check reads whichever of them the list currently makes true.
    ///
    /// Written through <see cref="HolepunchConsumers.SentenceFor"/> rather than as a literal, for
    /// PP573's reason one level down: a hand-typed clause is what let the line say "only caller"
    /// through three findings, and a hand-typed clause HERE would be the same defect in the check.
    /// </summary>
    [Fact]
    public void TheCheckAsksForTheSentenceTheCountActuallyHas()
    {
        string current = HolepunchConsumers.SentenceFor(HolepunchConsumers.All.Count);

        Assert.True(HolepunchConsumers.TheRoadmapLineAgreesOnTheCount($"the deletion: {current}."));

        // And every other count is still refused, so the clause is a claim and not a formality.
        foreach (int other in (int[])[0, 1, 3, 4])
        {
            if (other == HolepunchConsumers.All.Count)
                continue;

            Assert.False(
                HolepunchConsumers.TheRoadmapLineAgreesOnTheCount(
                    $"the deletion: {HolepunchConsumers.SentenceFor(other)}."),
                $"a line claiming {other} passed while the list holds {HolepunchConsumers.All.Count}");
        }
    }

    /// <summary>
    /// PP591: the harness is NOT in the tree, which is the decision PP544 held open.
    ///
    /// Its own comment named the three outcomes - ported, deleted with the C, or kept as the
    /// hardware probe - and said the point of recording the file was that the decision could not be
    /// made while the backlog called it absent. It was deleted: it read an oauth token from
    /// /tmp/token.txt on a port whose first non-goal is Windows-only, no ctest case ran it, and the
    /// probe this port keeps is the managed one PP479 drives and PP508 measured.
    ///
    /// Held as its absence rather than dropped, because a file arriving back - ported, or carried in
    /// by a merge from upstream - is a consumer of holepunch.c again, and silently.
    /// </summary>
    [Fact]
    public void TheHarnessIsNotInTheTree()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        string path = Path.Combine(root, DeletedHarness);

        Assert.False(
            File.Exists(path),
            $"{DeletedHarness} is back, so PP33's deletion has a "
                + "consumer again - port it, or delete it the way PP591 did");
    }

    /// <summary>
    /// And the eight exports it called are still written down, which is the size of what left.
    ///
    /// The list outlives the file on purpose. It is what a returning harness would be measured
    /// against, and it is what the ledger's sentence about PP544 means by "eight".
    /// </summary>
    [Fact]
    public void TheEightItCalledAreStillNamed()
    {
        Assert.Equal(8, HolepunchConsumers.HarnessCalls.Count);
        Assert.Equal(8, HolepunchConsumers.MissingFromHarness("int main(void) { return 0; }").Count);
    }

    /// <summary>
    /// PP591: and nothing declares its target any more - the other half of being gone.
    ///
    /// Both halves, because they fail apart. A source file with no target is dead weight the build
    /// walks past; a target with no source is a configure error. What PP33 needed removed is the
    /// pair, and lib/CMakeLists.txt is where the pair was.
    /// </summary>
    [Fact]
    public void NothingDeclaresTheTargetAnyMore()
    {
        if (HolepunchConsumers.LocateLibCMake() is not { } path)
            return;

        Assert.False(HolepunchConsumers.TargetStillLinksTheLibrary(File.ReadAllText(path)),
            "holepunch-test is declared again, and it is a consumer of the file PP33 deletes");
    }

    /// <summary>
    /// And the reader can see a target that stopped linking. Written out because the check above
    /// is two conditions and a rule that only tested the first would call a dangling declaration a
    /// consumer.
    /// </summary>
    [Fact]
    public void ADeclarationWithoutTheLinkIsNotAConsumer()
    {
        const string declaredOnly = """
            if(CHIAKI_ENABLE_TESTS)
                add_executable(holepunch-test include/chiaki/remote/holepunch.h src/remote/holepunch-test.c)
            endif()
            """;

        Assert.False(HolepunchConsumers.TargetStillLinksTheLibrary(declaredOnly));
    }
}
