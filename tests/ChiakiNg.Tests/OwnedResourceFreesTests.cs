using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP368: nothing that owns a thread is released with a bare free, and nothing is copied into an
/// allocation nobody checked.
/// </summary>
public class OwnedResourceFreesTests
{
    /// <summary>
    /// THE CHECK: no bare free of something that owns more than memory.
    ///
    /// A gkcrypt owns a key-buffer thread, and chiaki_gkcrypt_fini stops and JOINS it before
    /// releasing anything. A bare free left that thread running with its struct already freed.
    /// </summary>
    [Theory]
    [InlineData(@"lib\src\streamconnection.c")]
    [InlineData(@"lib\src\ctrl.c")]
    [InlineData(@"lib\src\session.c")]
    public void NothingOwningAThreadIsFreedBare(string relative)
    {
        string? path = OwnedResourceFrees.Locate(relative);
        if (path is null)
            return;

        IReadOnlyList<string> bare =
            OwnedResourceFrees.BareFreesOfOwnedResources(File.ReadAllText(path));

        Assert.True(
            bare.Count == 0,
            $"{relative} frees something that owns a thread without stopping it:\n  "
                + string.Join("\n  ", bare));
    }

    /// <summary>And the reader finds the bare free, so the check means something.</summary>
    [Fact]
    public void TheReaderFindsABareFree()
    {
        const string asItWas = """
            		CHIAKI_LOGE(log, "failed to initialize remote GKCrypt");
            		free(stream_connection->gkcrypt_local);
            		stream_connection->gkcrypt_local = NULL;
            """;

        string found = Assert.Single(OwnedResourceFrees.BareFreesOfOwnedResources(asItWas));

        Assert.Contains("gkcrypt_local", found, StringComparison.Ordinal);
    }

    /// <summary>And ignores the wrapper, which is the fix.</summary>
    [Fact]
    public void TheReaderIgnoresTheWrapper()
    {
        const string fixedUp = """
            		chiaki_gkcrypt_free(stream_connection->gkcrypt_local);
            		stream_connection->gkcrypt_local = NULL;
            """;

        Assert.Empty(OwnedResourceFrees.BareFreesOfOwnedResources(fixedUp));
    }

    /// <summary>
    /// THE SECOND HALF: nothing is copied into an allocation nobody tested.
    ///
    /// The two lines look like one operation, which is why a reader skips the missing test.
    /// </summary>
    [Theory]
    [InlineData(@"lib\src\streamconnection.c")]
    [InlineData(@"lib\src\ctrl.c")]
    [InlineData(@"lib\src\session.c")]
    public void NothingIsCopiedIntoAnUncheckedAllocation(string relative)
    {
        string? path = OwnedResourceFrees.Locate(relative);
        if (path is null)
            return;

        IReadOnlyList<string> unchecked_ =
            OwnedResourceFrees.UncheckedAllocationsCopiedInto(File.ReadAllText(path));

        Assert.True(
            unchecked_.Count == 0,
            $"{relative} copies into an allocation it never tested:\n  "
                + string.Join("\n  ", unchecked_));
    }

    /// <summary>And the reader finds that one too.</summary>
    [Fact]
    public void TheReaderFindsAnUncheckedAllocation()
    {
        const string asItWas = """
            				stream_connection->streaminfo_early_buf = malloc(buf_size);
            				memcpy(stream_connection->streaminfo_early_buf, buf, buf_size);
            """;

        Assert.Single(OwnedResourceFrees.UncheckedAllocationsCopiedInto(asItWas));
    }

    /// <summary>And ignores one that is tested first.</summary>
    [Fact]
    public void TheReaderIgnoresACheckedAllocation()
    {
        const string fixedUp = """
            				stream_connection->streaminfo_early_buf = malloc(buf_size);
            				if(!stream_connection->streaminfo_early_buf)
            					return;
            				memcpy(stream_connection->streaminfo_early_buf, buf, buf_size);
            """;

        Assert.Empty(OwnedResourceFrees.UncheckedAllocationsCopiedInto(fixedUp));
    }
}
