using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP437: every entry point the host imports is one the shim declares.
///
/// An EntryPoint is a string the compiler has no opinion about, so the failure it prevents is an
/// EntryPointNotFoundException the first time a particular call is reached - inside a live session,
/// for most of this seam, and not at startup.
/// </summary>
public class NativeSeamTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE RULE, and the direction that crashes.
    /// </summary>
    [Fact]
    public void EveryImportedEntryPointIsDeclaredByTheShim()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;
        if (NativeSeam.ReadHeaders() is not { } headers)
            return;

        IReadOnlyList<string> sources = NativeSeam.ManagedSources(root);
        IReadOnlySet<string> imported = NativeSeam.Imported(sources);
        IReadOnlySet<string> exported = NativeSeam.Exported(headers);

        output.WriteLine($"{sources.Count} managed files, {imported.Count} imported, {exported.Count} declared");

        // PP271: a reader that found no imports would satisfy the claim below by finding nothing.
        Assert.True(imported.Count >= 200, $"only {imported.Count} entry points read - the reader is not working");
        Assert.True(exported.Count >= 200, $"only {exported.Count} declarations read from the headers");

        IReadOnlyList<string> undefined = NativeSeam.Undefined(imported, exported);

        Assert.True(
            undefined.Count == 0,
            "the host imports entry points no shim header declares, so each is an "
                + "EntryPointNotFoundException at its first call:\n  " + string.Join("\n  ", undefined));
    }

    /// <summary>
    /// And the other way: an export nothing imports is either named with a reason or reported.
    /// </summary>
    [Fact]
    public void EveryUnimportedExportCarriesItsReason()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;
        if (NativeSeam.ReadHeaders() is not { } headers)
            return;

        IReadOnlySet<string> imported = NativeSeam.Imported(NativeSeam.ManagedSources(root));
        IReadOnlySet<string> exported = NativeSeam.Exported(headers);

        IReadOnlyList<string> unexplained = NativeSeam.UnimportedWithoutReason(imported, exported);

        Assert.True(
            unexplained.Count == 0,
            "the shim exports surface nothing imports and nothing explains:\n  "
                + string.Join("\n  ", unexplained));
    }

    /// <summary>
    /// PP131's wrapper is the one known member, and it is still exported and still unimported - so
    /// the allowlist describes the tree rather than outliving it.
    /// </summary>
    [Fact]
    public void ThePP131WrapperIsStillTheOnlyOne()
    {
        const string Wrapper = "chiaki_render_share_to_d3d9";

        string because = Assert.Contains(Wrapper, NativeSeam.ExportsNothingImports);
        Assert.True(because.Length > 60, "the exemption carries no reason a reader could act on");

        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;
        if (NativeSeam.ReadHeaders() is not { } headers)
            return;

        IReadOnlySet<string> imported = NativeSeam.Imported(NativeSeam.ManagedSources(root));
        IReadOnlySet<string> exported = NativeSeam.Exported(headers);

        // Both halves: still declared, and still not imported. Either changing is a stale allowlist.
        Assert.Contains(Wrapper, exported);
        Assert.DoesNotContain(Wrapper, imported);

        // The wider one it was superseded by IS imported, which is why the exemption is a decision.
        Assert.Contains("chiaki_render_share_to_d3d9_format", imported);
    }

    /// <summary>
    /// PP400: a name that only a comment mentions is not a declaration.
    ///
    /// chiaki_render.h documents PP131's experiment at length and names the _format function inside
    /// that prose, so a reader of raw header text would find declarations that are sentences.
    /// </summary>
    [Fact]
    public void ANameInACommentIsNotADeclaration()
    {
        const string Header = """
            /* PP131 picked D3DImage, so chiaki_render_share_to_d3d9_format(surface) is what HDR
             * would need. */
            // chiaki_shim_removed_last_year(void);
            CHIAKI_RENDER_API void *chiaki_render_share_to_d3d9(void *texture);
            """;

        IReadOnlySet<string> exported = NativeSeam.Exported(CCall.Code(Header));

        Assert.Contains("chiaki_render_share_to_d3d9", exported);
        Assert.DoesNotContain("chiaki_render_share_to_d3d9_format", exported);
        Assert.DoesNotContain("chiaki_shim_removed_last_year", exported);
    }

    /// <summary>An import naming a function no header declares is reported.</summary>
    [Fact]
    public void AnImportWithNoDeclarationIsReported()
    {
        IReadOnlySet<string> imported = NativeSeam.ImportedFrom(
            """[DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_never_existed")]""");

        Assert.Equal(["chiaki_shim_never_existed"], imported);

        string undefined = Assert.Single(NativeSeam.Undefined(
            imported, NativeSeam.Exported("void chiaki_shim_something_else(void);")));

        Assert.Equal("chiaki_shim_never_existed", undefined);
    }

    /// <summary>
    /// An import into another library is not this seam's business. There are three kernel32 ones and
    /// a shell32, and counting those would put names in the set that no shim header could declare.
    /// </summary>
    [Fact]
    public void AnImportFromAnotherLibraryIsIgnored()
    {
        Assert.Empty(NativeSeam.ImportedFrom(
            """[DllImport("kernel32", EntryPoint = "GetModuleHandleW", SetLastError = true)]"""));

        Assert.Empty(NativeSeam.ImportedFrom(
            """[DllImport("shell32.dll", EntryPoint = "SHGetKnownFolderPath")]"""));
    }

    /// <summary>Two overloads sharing one entry point are one name, not two.</summary>
    [Fact]
    public void TwoOverloadsOnOneEntryPointAreOneName()
    {
        IReadOnlySet<string> imported = NativeSeam.ImportedFrom("""
            [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_v9_av_packet_parse")]
            [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_takion_v9_av_packet_parse")]
            """);

        Assert.Single(imported);
    }

    /// <summary>PP272: and empty text imports and declares nothing.</summary>
    [Fact]
    public void EmptyTextNamesNothing()
    {
        Assert.Empty(NativeSeam.ImportedFrom(""));
        Assert.Empty(NativeSeam.Exported(""));
        Assert.Empty(NativeSeam.Undefined(NativeSeam.ImportedFrom(""), NativeSeam.Exported("")));
        Assert.Empty(NativeSeam.UnimportedWithoutReason(
            NativeSeam.ImportedFrom(""), NativeSeam.Exported("")));
    }

    /// <summary>The build output is not read, so a stale generated copy cannot answer.</summary>
    [Fact]
    public void TheBuildOutputIsNotRead()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        foreach (string path in NativeSeam.ManagedSources(root))
        {
            string[] parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
            Assert.DoesNotContain(parts, part => ForeignBinaries.SkippedDirectoryNames.Contains(part));
        }
    }
}
