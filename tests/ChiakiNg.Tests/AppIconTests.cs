using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP330: the icon the executable, its window and the installer all carry.
///
/// The mark is drawn once, in <c>site/public/logo.svg</c>, and Windows cannot read that: an
/// <c>&lt;ApplicationIcon&gt;</c> is an .ico, so <c>site/scripts/app-icon.mjs</c> renders one and the
/// tree holds a second copy of the mark. These are what makes the second copy safe rather than a
/// thing to remember - the stamp beside the .ico says which SVG it was rendered from, and the last
/// case here holds that against the SVG the tree actually has.
///
/// Without them the failure has no symptom at all: the logo is redrawn, the site rebuilds, the host
/// still compiles, and the only place the old mark is left is the one nobody opens.
/// </summary>
public partial class AppIconTests
{
    /// <summary>The rendered icon, and the stamp the renderer writes beside it.</summary>
    private const string IconRelativePath = @"assets\pportal.ico";
    private const string StampRelativePath = @"assets\pportal.ico.source";

    /// <summary>The mark itself, spelled as the stamp spells it - forward slashes and all.</summary>
    private const string LogoRelativePath = "site/public/logo.svg";

    /// <summary>
    /// The two files that name an icon, and neither can read the other: the csproj embeds one in
    /// the executable and the Inno Setup script puts one on the setup window.
    /// </summary>
    private const string ProjectRelativePath = @"app\ChiakiNg.csproj";
    private const string InstallerRelativePath = @"scripts\chiaki-ng.iss";

    /// <summary>
    /// The csproj names an icon that is there. An <c>&lt;ApplicationIcon&gt;</c> pointing at nothing
    /// is not a build error - MSBuild warns and produces an executable with no icon - so a rename
    /// on the assets side reaches a release with only a line in a log to say so.
    /// </summary>
    [Fact]
    public void TheProjectNamesAnIconTheTreeHas()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        string named = DeclaredIn(ApplicationIconRegex(), Path.Combine(root, ProjectRelativePath));
        Assert.Equal(@"..\assets\pportal.ico", named);
        Assert.True(File.Exists(Path.Combine(root, IconRelativePath)), IconRelativePath);
    }

    /// <summary>
    /// The installer wears the icon of the thing it installs. It packages ChiakiNg.exe (PP274), so
    /// the two paths resolve to one file - the csproj's is relative to <c>app\</c> and the script's
    /// to <c>scripts\</c>, which is the whole reason this is asserted and not read off.
    /// </summary>
    [Fact]
    public void TheInstallerWearsTheIconItInstalls()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        string fromProject = DeclaredIn(ApplicationIconRegex(), Path.Combine(root, ProjectRelativePath));
        string fromInstaller = DeclaredIn(SetupIconRegex(), Path.Combine(root, InstallerRelativePath));

        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "app", fromProject)),
            Path.GetFullPath(Path.Combine(root, "scripts", fromInstaller)));
    }

    /// <summary>
    /// The .ico holds every size the stamp says it does, each entry lying inside the file.
    ///
    /// The sizes are read from the stamp rather than written here so that this cannot become the
    /// second list to edit; what it refuses is an .ico assembled wrong, where a directory entry
    /// points past the end and the shell silently falls back to a default icon.
    /// </summary>
    [Fact]
    public void EveryDeclaredSizeIsInTheFileAndInsideIt()
    {
        string? iconPath = SanitizerSource.LocateRelative(IconRelativePath);
        Assert.True(iconPath is not null, "not running out of a checkout");

        byte[] ico = File.ReadAllBytes(iconPath);
        Assert.True(ico.Length >= 6, "an .ico shorter than its own header");
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));

        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        List<int> present = [];
        for (int i = 0; i < count; i++)
        {
            int at = 6 + 16 * i;
            // A dimension byte of 0 is how 256 is spelled: the field is one byte wide.
            int width = ico[at] == 0 ? 256 : ico[at];
            int height = ico[at + 1] == 0 ? 256 : ico[at + 1];
            Assert.Equal(width, height);

            long length = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(at + 8));
            long offset = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(at + 12));
            Assert.True(
                offset >= 6L + 16 * count && offset + length <= ico.Length,
                $"the {width}px entry points outside the file");

            present.Add(width);
        }

        Assert.Equal(StampedSizes(), present);
    }

    /// <summary>
    /// And the mark the .ico was rendered from is the mark the tree draws.
    ///
    /// This is the case the other three exist around. Hashed over LF-normalised bytes, exactly as
    /// app-icon.mjs writes it, so that a checkout with CRLF working files does not read as a
    /// redrawn logo - the thing being compared is the drawing and not the line endings.
    ///
    /// When it fails, the fix is `npm run app-icon` in site\ and the .ico plus its stamp in the
    /// commit that redrew the logo.
    /// </summary>
    [Fact]
    public void TheStampNamesTheMarkTheTreeDraws()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        string stamp = File.ReadAllText(Path.Combine(root, StampRelativePath));
        Assert.Equal(LogoRelativePath, Declared(StampSourceRegex(), stamp));

        string stamped = Declared(StampHashRegex(), stamp);
        Assert.Equal(64, stamped.Length);

        string logo = File.ReadAllText(
            Path.Combine(root, LogoRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        string actual = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(logo.ReplaceLineEndings("\n"))));

        Assert.True(
            stamped == actual,
            $"{IconRelativePath} was rendered from a different {LogoRelativePath} than this tree "
                + "has, so the executable and the site carry different marks: run `npm run "
                + "app-icon` in site\\ and commit the .ico and its stamp");
    }

    /// <summary>The sizes the stamp declares, in the order it declares them.</summary>
    private static List<int> StampedSizes()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        string declared = DeclaredIn(StampSizesRegex(), Path.Combine(root, StampRelativePath));
        Assert.NotEmpty(declared);
        return [.. declared.Split(',').Select(int.Parse)];
    }

    /// <summary>
    /// A field's value, or the empty string - PP272's rule, that a reader which stopped
    /// understanding a file says no rather than finding nothing to disagree with. Every comparison
    /// above is against a value the empty string is not.
    /// </summary>
    private static string Declared(Regex regex, string text)
    {
        Match match = regex.Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    /// <summary>The same, over a file this has not read yet.</summary>
    private static string DeclaredIn(Regex regex, string path)
        => Declared(regex, File.ReadAllText(path));

    [GeneratedRegex(@"<ApplicationIcon>([^<]*)</ApplicationIcon>")]
    private static partial Regex ApplicationIconRegex();

    [GeneratedRegex(@"^SetupIconFile=(\S+)", RegexOptions.Multiline)]
    private static partial Regex SetupIconRegex();

    [GeneratedRegex(@"^source\s+(\S+)", RegexOptions.Multiline)]
    private static partial Regex StampSourceRegex();

    [GeneratedRegex(@"^sha256\s+([0-9a-f]+)", RegexOptions.Multiline)]
    private static partial Regex StampHashRegex();

    [GeneratedRegex(@"^sizes\s+([0-9,]+)", RegexOptions.Multiline)]
    private static partial Regex StampSizesRegex();
}
