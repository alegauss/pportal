using ChiakiNg.Native;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP580: what the host imports from the shim, against what the shim exports.
/// </summary>
public class ShimSurfaceTests(ITestOutputHelper output)
{
    private static (string Source, string Managed)? Checkout()
    {
        string? source = ShimSurface.Locate();
        string? managed = ShimSurface.LocateManaged();
        return source is null || managed is null ? null : (File.ReadAllText(source), managed);
    }

    /// <summary>
    /// EVERY IMPORT IS AN EXPORT, which is the half that throws.
    ///
    /// A DllImport resolves on first call, not at build. An export renamed in chiaki_shim.c, or an
    /// EntryPoint typed with one letter wrong, compiles clean and passes the ABI check - a version
    /// is a number, not a symbol table - then throws wherever that call sits. For most of these
    /// that is mid-session, which is the worst place to learn it.
    /// </summary>
    [Fact]
    public void EveryImportIsExportedByTheShim()
    {
        if (Checkout() is not { } checkout)
            return;

        IReadOnlySet<string> exports = ShimSurface.Exports(checkout.Source);
        IReadOnlySet<string> imports = ShimSurface.Imports(checkout.Managed);

        output.WriteLine($"{imports.Count} imported, {exports.Count} exported");

        // PP33: minus the two oracles' wrappers once they have gone. The host still DECLARES them -
        // NativeJson and NativeHolepunchSession are managed code that compiles either way and is
        // never called, because every caller asks a guard first - so an import with no export is
        // expected here and only here. Derived from the same shape questions the guards use, so
        // this reads the build rather than a list somebody kept.
        string[] missing =
        [
            .. imports
                .Where(one => !exports.Contains(one))
                .Where(one => !ChiakiNg.Session.NativeSeam.IsAJsonOracleImport(one))
                .Where(one => !ChiakiNg.Session.NativeSeam.IsAHolepunchOracleImport(one))
                .Order(),
        ];

        Assert.True(missing.Length == 0, $"imported and not exported: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// AND EVERY EXPORT IS IMPORTED, which is the half that rots.
    ///
    /// Shim C nothing reaches is what this port deletes rather than keeps - PP290's argument for
    /// libchiaki's own exports, one seam over. Both directions are news, so the two sets are
    /// asserted equal rather than one being a subset.
    /// </summary>
    [Fact]
    public void EveryExportIsImportedByTheHost()
    {
        if (Checkout() is not { } checkout)
            return;

        IReadOnlySet<string> exports = ShimSurface.Exports(checkout.Source);
        IReadOnlySet<string> imports = ShimSurface.Imports(checkout.Managed);

        string[] unused = [.. exports.Where(one => !imports.Contains(one)).Order()];
        Assert.True(unused.Length == 0, $"exported and not imported: {string.Join(", ", unused)}");
    }

    /// <summary>
    /// Both sets are read, not assumed: a reader that found nothing would pass both tests above.
    /// </summary>
    [Fact]
    public void BothSidesWereActuallyRead()
    {
        if (Checkout() is not { } checkout)
            return;

        Assert.NotEmpty(ShimSurface.Exports(checkout.Source));
        Assert.NotEmpty(ShimSurface.Imports(checkout.Managed));
    }

    /// <summary>
    /// A declaration is not a definition. The header declares every export as well, so a sweep of
    /// declarations would pass on a function declared and never written - the same failure with a
    /// different message.
    /// </summary>
    [Fact]
    public void ADeclarationAloneIsNotAnExport()
    {
        const string declaredOnly = "CHIAKI_SHIM_API int chiaki_shim_never_written(void);";
        const string defined = "CHIAKI_SHIM_API int chiaki_shim_written(void) { return 0; }";

        Assert.Empty(ShimSurface.Exports(declaredOnly));
        Assert.Contains("chiaki_shim_written", ShimSurface.Exports(defined));
    }
}
