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
    /// THE SECOND CRITERION: what keeps gf-complete linked, swept rather than counted.
    ///
    /// One call site in the whole tree, and it is not in fec.c. Both halves matter - the count
    /// says nothing else pulls the library in, and the location says the module PP30 deletes is
    /// not what is holding it.
    /// </summary>
    [Fact]
    public void TheFieldInitHasOneCallSiteAndItIsNotInTheFecModule()
    {
        IReadOnlyList<string> callers = FecConsumers.Sweep(FecConsumers.InitialisesTheField);
        if (callers.Count == 0)
            return;

        output.WriteLine("field init callers: " + string.Join(", ", callers));

        string only = Assert.Single(callers);
        Assert.Equal(FecConsumers.FieldInitRelativePath, only, StringComparer.OrdinalIgnoreCase);

        // Said the other way round, because the point is which module does NOT hold the library.
        Assert.DoesNotContain(@"fec.c", only, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And the call is inside chiaki_lib_init, which is what makes it every session's.
    ///
    /// A gf-complete call somewhere in common.c would be a smaller fact. Being in library init
    /// means the field is installed before a session does anything, so no deletion of the frame
    /// path can reach it.
    /// </summary>
    [Fact]
    public void TheCallIsInsideLibraryInit()
    {
        if (Read(FecConsumers.FieldInitRelativePath) is not { } source)
            return;

        string? body = CFunction.Body(source, FecConsumers.FieldInitFunction);

        Assert.NotNull(body);
        Assert.Contains(FecConsumers.FieldInit + "(", body, StringComparison.Ordinal);

        // And it is sized by the header's constant, which is why common.c includes fec.h at all.
        Assert.Contains(FecConsumers.WordSizeMacro, body, StringComparison.Ordinal);
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
    /// The three-way split is really three ways: each bucket has a member.
    ///
    /// PP271's rule. A classifier that answered Decode for everything would satisfy the placement
    /// check on a list that was all Decode, and the whole finding is that one file is not.
    /// </summary>
    [Fact]
    public void EachOfTheThreeUsesHasAnIncluder()
    {
        Assert.Contains(FecConsumers.Includers, one => one.Uses == FecHeaderUse.Decode);

        FecIncluder constant = Assert.Single(
            FecConsumers.Includers, one => one.Uses == FecHeaderUse.WordSize);
        Assert.Equal(FecConsumers.FieldInitRelativePath, constant.Path, StringComparer.OrdinalIgnoreCase);

        Assert.Contains(FecConsumers.Includers, one => one.Uses == FecHeaderUse.Neither);
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
