using ChiakiNg.Native;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP277: the two fields that decide whether this installer touches an installed chiaki-ng.
///
/// The selftest holds both against the real script; these hold the readers against the shapes an
/// Inno Setup file writes them in, which is where they are easy to get subtly wrong - AppId's
/// doubled brace is not part of the identity, and reading it as one would compare a value that
/// matches nothing to a value that matches nothing.
/// </summary>
public class InstallerIdentityTests
{
    /// <summary>The two lines, as the script spells them.</summary>
    private const string Script = """
        #define MyAppName "ChiakiNg"
        #define MyAppExeName "ChiakiNg.exe"

        [Setup]
        AppId={{68DF7098-C6C6-4186-9099-44C66A60793A}
        AppName={#MyAppName}
        DefaultDirName={autopf}\{#MyAppName}
        """;

    /// <summary>
    /// Inno Setup escapes a literal brace by doubling it, so the identity starts one character in.
    /// Read whole, every comparison against it is false and the check that upstream's is gone
    /// passes for the wrong reason.
    /// </summary>
    [Fact]
    public void TheDoubledBraceIsSyntaxAndNotIdentity()
    {
        Assert.Equal("{68DF7098-C6C6-4186-9099-44C66A60793A}", InstallerScript.AppId(Script));
    }

    /// <summary>And upstream's, written the same way, is recognised as the same value.</summary>
    [Fact]
    public void UpstreamsIdentityIsRecognisedInTheFormAScriptCarriesIt()
    {
        Assert.Equal(
            InstallerScript.UpstreamAppId,
            InstallerScript.AppId("AppId={{A329DCDE-074D-4C82-959A-3CFAC9A26B1F}\n"));
    }

    /// <summary>
    /// A script with no AppId at all answers the empty string, which is a value the selftest
    /// refuses - PP272's rule, that a reader which stopped understanding a file says so rather than
    /// finding nothing to disagree with. An AppId Inno Setup cannot find is one it generates from
    /// AppName, so absence here is not the same as absence on the machine.
    /// </summary>
    [Fact]
    public void AnAbsentIdentityIsNotAPassingOne()
    {
        Assert.Empty(InstallerScript.AppId("AppName={#MyAppName}\n"));
        Assert.Empty(InstallerScript.InstalledName("[Setup]\n"));
    }

    /// <summary>
    /// The name is read from the #define and not from AppName, which only refers to it. {app} and
    /// the Start Menu group are built from the same define, so this is the value that decides
    /// whether two installs share a directory.
    /// </summary>
    [Fact]
    public void TheInstalledNameIsTheDefineAndNotTheReferenceToIt()
    {
        Assert.Equal("ChiakiNg", InstallerScript.InstalledName(Script));
    }
}
