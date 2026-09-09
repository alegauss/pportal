using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP692: fec.h's whole surface, and the one thing that keeps gf-complete linked.
///
/// PP574 built <see cref="FecConsumers"/> because PP30's line said "one caller" when there were
/// three, and the one always missed was this port's own shim. On the question it asks - who calls
/// chiaki_fec_decode - it is right, and these tests do not disturb it.
///
/// PP30 ASKS A DIFFERENT QUESTION. It deletes gf-complete and jerasure, not the decode, and the
/// answer to that one is not in the frame path at all: galois_init_default_field has exactly one
/// call site in the tree, at lib/src/common.c inside chiaki_lib_init, beside the random seed and
/// WSAStartup. fec.c never calls it. So the day fec.c and the shim's two create_matrix wrappers
/// leave, gf-complete is still linked by a function every session calls first.
///
/// PP697: THAT DAY WAS PP696, and the sentence above held. fec.c is out of the build, the two
/// wrappers are behind an #ifdef that is off, and gf-complete is linked exactly as before - which
/// is what leaves PP30 something to delete rather than a job already done by somebody else.
///
/// Which makes this the same lesson one question over: a census is short unless it counted the
/// consumer that is not in the module. Both criteria below therefore SWEEP rather than trust the
/// list - a seventh includer or a second field-init caller fails here, and so does the day
/// common.c stops being the one.
///
/// PP30: THAT DAY IS THIS ONE, AND THE ASSERTIONS INVERTED. The call site is gone, so the criterion
/// that found one now holds that there are none; the include went with it, so the word-size bucket
/// is empty and the split is two ways. Written as inversions rather than deleted, because what a
/// deletion has to keep provable is that nothing came back - and both still sweep, so a file that
/// starts calling the field init again fails here rather than linking a library the build no longer
/// names.
/// </summary>
public class FecHeaderSurfaceTests(ITestOutputHelper output)
{
    private static string? Read(string relative)
        => FecConsumers.Locate(relative) is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// The header carries exactly two names, which is what makes "the whole surface" a small claim.
    ///
    /// If a third arrived, every placement below would be reading a header that had grown a use
    /// nothing classifies - and the three-way split would silently become a four-way one.
    /// </summary>
    [Fact]
    public void TheHeaderHoldsTheConstantAndTheExportAndNothingElse()
    {
        if (Read(FecConsumers.HeaderRelativePath) is not { } header)
            return;

        Assert.Contains(FecConsumers.WordSizeMacro, header, StringComparison.Ordinal);
        Assert.Contains(FecConsumers.Export, header, StringComparison.Ordinal);

        // Every exported declaration in it, which should be the decode alone.
        string[] exports = [.. CCall.Code(header)
            .Split('\n')
            .Where(line => line.Contains("CHIAKI_EXPORT", StringComparison.Ordinal))];

        string only = Assert.Single(exports);
        Assert.Contains(FecConsumers.Export, only, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE FIRST CRITERION: every includer is placed, and the placement is what its text says.
    ///
    /// Read rather than asserted from the list: the modelled use is compared against what the file
    /// actually takes, so a file that started calling the decode or stopped using the word size
    /// fails by name rather than drifting.
    /// </summary>
    [Fact]
    public void EveryIncluderIsPlacedAsItsOwnTextPlacesIt()
    {
        var wrong = new List<string>();

        foreach (FecIncluder includer in FecConsumers.Includers)
        {
            if (Read(includer.Path) is not { } source)
                return;

            if (!FecConsumers.IncludesTheHeader(source))
            {
                wrong.Add($"{includer.Path}: modelled as an includer and does not include fec.h");
                continue;
            }

            FecHeaderUse actual = FecConsumers.UseIn(source);
            output.WriteLine($"{includer.Path}: modelled {includer.Uses}, reads {actual}");

            if (actual != includer.Uses)
                wrong.Add($"{includer.Path}: modelled as {includer.Uses} and its text says {actual}");
        }

        Assert.True(
            wrong.Count == 0,
            "these includers are not what the model says:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// And the list is the WHOLE list, swept from the tree.
    ///
    /// This is the half a named list cannot have. A seventh file including fec.h is exactly the
    /// miss PP574 was written about, and the only way to see it is to look.
    /// </summary>
    [Fact]
    public void NoFileOutsideTheListIncludesTheHeader()
    {
        IReadOnlyList<string> swept = FecConsumers.Sweep(FecConsumers.IncludesTheHeader);
        if (swept.Count == 0)
            return;

        // fec.h includes nothing of itself; the sweep reads the header directory too, so its own
        // include guard is not a hit and the header is not expected in either list.
        output.WriteLine("swept: " + string.Join(", ", swept));

        Assert.Equal(
            [.. FecConsumers.Includers.Select(one => one.Path).OrderBy(one => one, StringComparer.OrdinalIgnoreCase)],
            [.. swept.OrderBy(one => one, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// THE SECOND CRITERION, INVERTED BY PP30: nothing in the tree calls the field init.
    ///
    /// It read "one call site, and it is not in fec.c" - both halves mattering, because the count
    /// said nothing else pulled gf-complete in and the location said the module everybody would
    /// look in was not what held it. The deletion is that call, so the criterion is now that the
    /// sweep comes back empty.
    ///
    /// AN EMPTY SWEEP IS ALSO WHAT A BROKEN SWEEP RETURNS, which is the one hazard in inverting a
    /// census. So the includer sweep is run beside it as a control: the same directories, the same
    /// reader shape, and a non-empty answer. Without that this assertion would be green on a tree
    /// with no files in it at all.
    /// </summary>
    [Fact]
    public void TheFieldInitHasNoCallSiteLeft()
    {
        if (SanitizerSource.RepositoryRoot() is null)
            return;

        IReadOnlyList<string> reading = FecConsumers.Sweep(FecConsumers.IncludesTheHeader);
        Assert.NotEmpty(reading);

        IReadOnlyList<string> callers = FecConsumers.Sweep(FecConsumers.InitialisesTheField);
        output.WriteLine("field init callers: " + (callers.Count == 0 ? "(none)" : string.Join(", ", callers)));

        Assert.True(
            callers.Count == 0,
            $"{FecConsumers.FieldInit} is called again, so gf-complete has a consumer the build does "
                + $"not link: {string.Join(", ", callers)}");
    }

    /// <summary>
    /// And chiaki_lib_init is still there, no longer installing a field.
    ///
    /// Both halves. The function survives PP30 - WSAStartup is what a managed caller cannot do for
    /// itself, and every socket libchiaki opens depends on it - so a green here that came from the
    /// function having been deleted would be answering a different question.
    ///
    /// The word size goes with the call, because sizing the field was the only thing common.c took
    /// from fec.h and is why it included the header at all.
    ///
    /// READ OFF THE COMMENT-STRIPPED SOURCE, and the first version of this was not: the note PP30
    /// left where the deleted call had been names the function it deleted, inside the very body this
    /// reads, so the assertion found its own explanation and went red. That is the whole of what
    /// <see cref="CCall.Code"/> is for - a mention is not a use, and a check that cannot tell them
    /// apart turns every account of a deletion into evidence the deletion did not happen.
    /// </summary>
    [Fact]
    public void LibraryInitNoLongerInstallsTheField()
    {
        if (Read(FecConsumers.FieldInitRelativePath) is not { } source)
            return;

        string code = CCall.Code(source);
        string? body = CFunction.Body(code, FecConsumers.FieldInitFunction);

        Assert.NotNull(body);
        Assert.Contains("WSAStartup(", body, StringComparison.Ordinal);

        Assert.DoesNotContain(FecConsumers.FieldInit + "(", body, StringComparison.Ordinal);
        Assert.DoesNotContain(FecConsumers.WordSizeMacro, code, StringComparison.Ordinal);
        Assert.False(FecConsumers.IncludesTheHeader(source));
    }

    /// <summary>
    /// PP30's END STATE: no target in the build graph names jerasure or gf-complete.
    ///
    /// The deliverable itself, and the one thing none of the readers above can see - they read C,
    /// and what this task removed last was a link line. Three files carried it: the root's option
    /// and probe, third-party's two static libraries, and lib's <c>target_link_libraries</c>. A
    /// grep over all three is the whole assertion, and it is what would fail if a later merge
    /// brought any of the four back.
    ///
    /// The submodules are deliberately not asserted absent: they stay in the checkout as unbuilt
    /// vendored source, the way holepunch.c and gui/ did.
    /// </summary>
    [Fact]
    public void NothingInTheBuildGraphNamesTheVendoredLibraries()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        string[] buildFiles =
        [
            "CMakeLists.txt",
            Path.Combine("third-party", "CMakeLists.txt"),
            Path.Combine("lib", "CMakeLists.txt"),
            Path.Combine("test", "CMakeLists.txt"),
        ];

        var linked = new List<string>();

        foreach (string relative in buildFiles)
        {
            string full = Path.Combine(root, relative);
            if (!File.Exists(full))
                continue;

            foreach (string line in File.ReadAllLines(full))
            {
                // Comments are where this deletion is explained, so a note naming the library is
                // not a build step - which is the same distinction UseIn draws for C.
                string code = line.TrimStart();
                if (code.StartsWith('#') || code.Length == 0)
                    continue;

                if (code.Contains("jerasure", StringComparison.OrdinalIgnoreCase)
                    || code.Contains("gf_complete", StringComparison.OrdinalIgnoreCase)
                    || code.Contains("gf-complete", StringComparison.OrdinalIgnoreCase))
                {
                    linked.Add($"{relative}: {code}");
                }
            }
        }

        Assert.True(
            linked.Count == 0,
            "the build graph names the libraries PP30 removed:\n  " + string.Join("\n  ", linked));

        // And the find module they were the only consumer of is gone with them.
        Assert.False(File.Exists(Path.Combine(root, "cmake", "FindJerasure.cmake")));
    }

    /// <summary>
    /// fec.c calls jerasure and never gf-complete's init, which is the asymmetry PP692 is about.
    ///
    /// Stated as an assertion rather than left to the sweep above, because the sweep proves only
    /// that common.c is the one caller. This proves the module everybody would look in is not.
    /// </summary>
    [Fact]
    public void TheFecModuleUsesJerasureAndNeverInitialisesTheField()
    {
        if (Read(@"lib\src\fec.c") is not { } source)
            return;

        Assert.False(FecConsumers.InitialisesTheField(source));
        Assert.True(FecConsumers.Calls(source) || source.Contains("jerasure", StringComparison.Ordinal));
    }

    /// <summary>
    /// The split is two ways now, and PP30 is which way went.
    ///
    /// PP271's rule was that each bucket has a member, because a classifier answering Decode for
    /// everything would satisfy the placement check on a list that was all Decode. Two of the three
    /// still have one, and the WordSize bucket is empty because its only member was common.c - the
    /// file whose include this task deleted.
    ///
    /// Asserted as empty rather than dropped from the check: an includer arriving that takes the
    /// word size alone would mean something is sizing a field again, which is the shape of the
    /// consumer PP692 was written to find.
    /// </summary>
    [Fact]
    public void TheDecodeAndNeitherBucketsHaveMembersAndWordSizeDoesNot()
    {
        Assert.Contains(FecConsumers.Includers, one => one.Uses == FecHeaderUse.Decode);
        Assert.Contains(FecConsumers.Includers, one => one.Uses == FecHeaderUse.Neither);

        Assert.DoesNotContain(FecConsumers.Includers, one => one.Uses == FecHeaderUse.WordSize);

        // The classifier still answers all three, which is the coverage the empty bucket costs
        // nothing of - the theory below reaches WordSize on text.
        Assert.Equal(
            FecHeaderUse.WordSize,
            FecConsumers.UseIn($"int w = {FecConsumers.WordSizeMacro};"));
    }

    /// <summary>
    /// PP574's own count is untouched, which is the claim this line does not make.
    ///
    /// The three decode callers are still three - fec.c defines the export, so the includers
    /// marked Decode are those three plus the definition.
    /// </summary>
    [Fact]
    public void TheDecodeCountIsUnchanged()
    {
        Assert.Equal(3, FecConsumers.All.Count);

        Assert.Equal(
            [.. FecConsumers.All.OrderBy(one => one, StringComparer.OrdinalIgnoreCase)],
            [.. FecConsumers.Includers
                .Where(one => one.Uses == FecHeaderUse.Decode && one.Path != @"lib\src\fec.c")
                .Select(one => one.Path)
                .OrderBy(one => one, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>The classifier, on text rather than on the tree, in all three directions.</summary>
    [Theory]
    [InlineData("void f(void) { chiaki_fec_decode(a, b); }", FecHeaderUse.Decode)]
    [InlineData("int r = galois_init_default_field(CHIAKI_FEC_WORDSIZE);", FecHeaderUse.WordSize)]
    [InlineData("uint32_t units_in_frame_fec_raw = 10273;", FecHeaderUse.Neither)]
    // The decode wins where both appear: a file calling it is FecConsumers' subject regardless.
    [InlineData("chiaki_fec_decode(x); size_t w = CHIAKI_FEC_WORDSIZE;", FecHeaderUse.Decode)]
    // And a comment is not a use, which is what keeps the header itself out of the buckets.
    [InlineData("/* CHIAKI_FEC_WORDSIZE is the field width */ int x;", FecHeaderUse.Neither)]
    public void TheClassifierReadsUsesAndNotMentions(string source, FecHeaderUse expected)
        => Assert.Equal(expected, FecConsumers.UseIn(source));

    /// <summary>PP272: the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.False(FecConsumers.IncludesTheHeader(""));
        Assert.False(FecConsumers.InitialisesTheField(""));
        Assert.Equal(FecHeaderUse.Neither, FecConsumers.UseIn(""));
        Assert.Empty(FecConsumers.Sweep(_ => false));
    }
}
