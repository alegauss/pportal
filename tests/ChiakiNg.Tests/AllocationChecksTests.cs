using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP398: every allocation in lib/src is tested before what it produced is used.
///
/// PP345 met this shape once and fixed it one layer up. It is worth a rule rather than a line
/// because nothing about an unchecked allocation looks wrong: the code reads as though it
/// succeeded, which it usually did.
///
/// WHAT THE RULE FOUND was get_websocket_fqdn, whose last statement was an unchecked strdup into an
/// out-parameter. It fell into a cleanup where `err` was already SUCCESS, so a failed allocation
/// returned success with the caller's pointer left NULL - and what consumes that pointer is
/// snprintf into a websocket URL. The session then cannot open a socket to a host built from a null
/// pointer, and the report says the network is broken.
/// </summary>
public class AllocationChecksTests(ITestOutputHelper output)
{
    /// <summary>THE RULE. Nothing allocates without testing what it got.</summary>
    [Fact]
    public void EveryAllocationIsChecked()
    {
        string? dir = AllocationChecks.LocateSources();
        if (dir is null)
            return;

        var unchecked_ = new List<Allocation>();
        var total = 0;

        foreach (string file in Directory.EnumerateFiles(dir, "*.c", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            IReadOnlyList<Allocation> allocations =
                AllocationChecks.AllocationsIn(Path.GetFileName(file), text);

            total += allocations.Count;
            unchecked_.AddRange(AllocationChecks.Unchecked(text, allocations));
        }

        output.WriteLine($"{total} allocations across lib/src");

        // PP271, and PP381's floor: a sweep that quietly stopped matching reads exactly like a
        // clean one. Fifty today.
        Assert.True(total >= 40, $"the sweep found only {total} allocations");

        foreach (Allocation allocation in unchecked_)
            output.WriteLine(allocation.ToString());

        Assert.True(
            unchecked_.Count == 0,
            "these allocate and do not test what they got:\n  " + string.Join("\n  ", unchecked_));
    }

    /// <summary>
    /// BOTH SPELLINGS OF THE CHECK COUNT, which is what the first pass of this sweep got wrong.
    ///
    /// holepunch.c writes <c>if(!(*out))</c> and ctrl.c writes <c>if(!buf)</c>. A reader that knew
    /// only the second called three correct sites defects, and I read the code rather than the
    /// output before believing it.
    /// </summary>
    [Fact]
    public void EitherSpellingOfTheCheckIsACheck()
    {
        const string Dereferenced = """
            	*out = malloc(oauth_header_len);
            	if(!(*out))
            		return CHIAKI_ERR_MEMORY;
            """;

        const string Plain = """
            	uint8_t *buf = malloc(pin_size);
            	if(!buf)
            		return CHIAKI_ERR_MEMORY;
            """;

        foreach (string source in (string[])[Dereferenced, Plain])
        {
            IReadOnlyList<Allocation> allocations = AllocationChecks.AllocationsIn("x.c", source);
            Assert.Single(allocations);
            Assert.Empty(AllocationChecks.Unchecked(source, allocations));
        }
    }

    /// <summary>
    /// And the shape PP398 corrected is still caught, so the green above is not a reader that
    /// forgives everything.
    /// </summary>
    [Fact]
    public void TheUncheckedShapeIsCaught()
    {
        const string AsItWas = """
            	*fqdn = strdup(json_object_get_string(fqdn_json));

            cleanup_json:
            	json_object_put(json);
            	return err;
            """;

        IReadOnlyList<Allocation> allocations = AllocationChecks.AllocationsIn("holepunch.c", AsItWas);

        Allocation found = Assert.Single(allocations);
        Assert.Equal("strdup", found.Call);

        Allocation missing = Assert.Single(AllocationChecks.Unchecked(AsItWas, allocations));
        Assert.Equal("holepunch.c", missing.File);
    }

    /// <summary>
    /// The two excused shapes are excused for a reason about the CONSUMER, not about tolerating a
    /// gap - and both are asserted so the exemption cannot quietly widen.
    /// </summary>
    [Fact]
    public void OnlyTheTwoShapesWithAConsumerThatAcceptsNullAreExcused()
    {
        // A zero-size allocation may conformingly return NULL, and its only consumer here is a
        // realloc that treats NULL as malloc.
        const string ZeroSize = "\t\t.data = malloc(0),";

        Allocation zero = Assert.Single(AllocationChecks.AllocationsIn("holepunch.c", ZeroSize));
        Assert.True(AllocationChecks.IsExcused(zero, [ZeroSize]));

        // An ordinary allocation on the same field in the same file is NOT excused.
        const string Sized = "\t\t.data = malloc(response_size);";

        Allocation sized = Assert.Single(AllocationChecks.AllocationsIn("holepunch.c", Sized));
        Assert.False(AllocationChecks.IsExcused(sized, [Sized]));

        // And the discovery name is excused only in its own file.
        const string Name = "\t\thost_slot->name = strdup(host->name); \\";

        Assert.True(
            AllocationChecks.IsExcused(
                Assert.Single(AllocationChecks.AllocationsIn("discoveryservice.c", Name)), [Name]));

        Assert.False(
            AllocationChecks.IsExcused(
                Assert.Single(AllocationChecks.AllocationsIn("regist.c", Name)), [Name]));
    }

    /// <summary>A commented-out allocation is not one, which is PP374's exclusion.</summary>
    [Fact]
    public void ACommentedOutAllocationIsNotCounted()
    {
        Assert.Empty(AllocationChecks.AllocationsIn("x.c", "\t// char *p = malloc(16);"));
    }

    /// <summary>And the reader reads what it is given (PP272).</summary>
    [Fact]
    public void TheReaderReadsTheFile()
    {
        Assert.Empty(AllocationChecks.AllocationsIn("x.c", ""));
        Assert.Empty(AllocationChecks.Unchecked("", []));
    }
}
