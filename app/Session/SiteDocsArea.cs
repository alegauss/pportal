using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP446: the three joins between the site and the documentation area beside it.
///
/// /docs is a second build with a second toolchain - Astro and Starlight, because site/src holds
/// its copy as data and has no Markdown pipeline, no highlighting, no sidebar and no search, and
/// writing those four is writing a documentation framework. What that buys costs three joins, and
/// every one of them fails in silence rather than loudly:
///
/// THE BUILD ORDER. `vite build` empties site/dist, so the docs build has to run after it. Put
/// `build:docs` anywhere earlier in the chain and the client build deletes the whole area on its
/// way past, leaving a deploy artefact that is correct except for one missing directory.
///
/// THE BASE. GitHub Pages derives "/pportal/" from the repository name, and Astro rewrites only
/// the links it generates itself. A base that disagrees with site/vite.config.ts publishes an area
/// whose every internal link is wrong, and the dev server serves all of them.
///
/// THE OUTPUT. The deploy uploads site/dist. An outDir pointing anywhere else builds the docs
/// perfectly and publishes nothing.
///
/// scripts/docs.test.mjs holds what the build produced; this holds the sources it produced it
/// from, so a reordered script names its own cause rather than being read backwards from a
/// missing folder.
/// </summary>
public static partial class SiteDocsArea
{
    /// <summary>The site's npm project, whose build script chains the docs build last.</summary>
    public const string PackageRelativePath = @"site\package.json";

    /// <summary>Where the site's base prefix is written.</summary>
    public const string ViteConfigRelativePath = @"site\vite.config.ts";

    /// <summary>And the docs area's own configuration.</summary>
    public const string AstroConfigRelativePath = @"site\docs\astro.config.mjs";

    /// <summary>The one segment the documentation area occupies under the site's base.</summary>
    public const string Segment = "docs";

    /// <summary>The three files, or null where the site is not in this checkout.</summary>
    public static (string Package, string Vite, string Astro)? Read()
    {
        string? package = SanitizerSource.LocateRelative(PackageRelativePath);
        string? vite = SanitizerSource.LocateRelative(ViteConfigRelativePath);
        string? astro = SanitizerSource.LocateRelative(AstroConfigRelativePath);

        if (package is null || vite is null || astro is null)
            return null;

        return (File.ReadAllText(package), File.ReadAllText(vite), File.ReadAllText(astro));
    }

    /// <summary>The site's "build" script, or the empty string where the key is gone.</summary>
    public static string BuildScript(string packageJson)
    {
        ArgumentNullException.ThrowIfNull(packageJson);

        return BuildScriptRegex().Match(packageJson) is { Success: true } m
            ? m.Groups["script"].Value
            : "";
    }

    /// <summary>
    /// Whether the build chains the docs build AFTER the prerender.
    ///
    /// The prerender is the last step that writes into dist, so it is the anchor rather than
    /// `vite build`: a fourth step inserted between the two would still be inside the window.
    /// </summary>
    public static bool DocsBuildRunsLast(string packageJson)
    {
        string script = BuildScript(packageJson);
        int prerender = script.IndexOf("prerender.mjs", StringComparison.Ordinal);
        int docs = script.IndexOf("build:docs", StringComparison.Ordinal);

        return prerender >= 0 && docs > prerender;
    }

    /// <summary>The base prefix the site publishes under, from vite.config.ts.</summary>
    public static string? SiteBase(string viteConfig)
    {
        ArgumentNullException.ThrowIfNull(viteConfig);

        return SiteBaseRegex().Match(viteConfig) is { Success: true } m ? m.Groups["base"].Value : null;
    }

    /// <summary>The base the docs area publishes under, from its own configuration.</summary>
    public static string? DocsBase(string astroConfig)
    {
        ArgumentNullException.ThrowIfNull(astroConfig);

        return DocsBaseRegex().Match(astroConfig) is { Success: true } m ? m.Groups["base"].Value : null;
    }

    /// <summary>And where that build writes, relative to site/docs.</summary>
    public static string? DocsOutDir(string astroConfig)
    {
        ArgumentNullException.ThrowIfNull(astroConfig);

        return OutDirRegex().Match(astroConfig) is { Success: true } m ? m.Groups["out"].Value : null;
    }

    /// <summary>
    /// Every join that no longer holds, as sentences a reader can act on.
    ///
    /// Empty is the passing answer, and a file whose declaration has been renamed away reports as
    /// broken rather than as absent: a base this cannot find is one it cannot check.
    /// </summary>
    public static IReadOnlyList<string> Unmet(string packageJson, string viteConfig, string astroConfig)
    {
        ArgumentNullException.ThrowIfNull(packageJson);

        var unmet = new List<string>();

        if (!DocsBuildRunsLast(packageJson))
        {
            unmet.Add(
                "site/package.json does not run build:docs after the prerender, so `vite build` "
                    + "empties dist/ with the whole documentation area in it");
        }

        string? siteBase = SiteBase(viteConfig);
        string? docsBase = DocsBase(astroConfig);

        if (siteBase is null)
            unmet.Add($"{ViteConfigRelativePath} no longer exports a BASE, so nothing states the prefix");
        else if (docsBase is null)
            unmet.Add($"{AstroConfigRelativePath} no longer declares a BASE");
        else if (docsBase != Expected(siteBase))
        {
            unmet.Add(
                $"the docs area publishes under \"{docsBase}\" and the site under \"{siteBase}\", so "
                    + $"its base should be \"{Expected(siteBase)}\"");
        }

        string? outDir = DocsOutDir(astroConfig);
        if (outDir is null)
            unmet.Add($"{AstroConfigRelativePath} no longer declares an OUT_DIR");
        else if (!LandsInTheSiteDist(outDir))
        {
            unmet.Add(
                $"the docs build writes to \"{outDir}\", which is not inside site/dist - the deploy "
                    + "uploads that directory and nothing else");
        }

        return unmet;
    }

    /// <summary>The base the docs area should carry, given the site's: that prefix plus one segment.</summary>
    public static string Expected(string siteBase)
    {
        ArgumentException.ThrowIfNullOrEmpty(siteBase);

        return $"{siteBase.TrimEnd('/')}/{Segment}";
    }

    /// <summary>
    /// Whether an outDir written in site/docs resolves inside site/dist.
    ///
    /// Textual on purpose: this runs against a string in a test as well as against the file, and a
    /// path check that needed the directory to exist would pass on nothing in a fresh checkout.
    /// </summary>
    public static bool LandsInTheSiteDist(string outDir)
    {
        ArgumentNullException.ThrowIfNull(outDir);

        return outDir.Replace('\\', '/').TrimEnd('/') is "../dist" or "../dist/" + Segment;
    }

    // "build": "... && npm run build:docs" - the key exactly, so "build:docs" is not read as it.
    [GeneratedRegex(@"""build""\s*:\s*""(?<script>[^""]*)""")]
    private static partial Regex BuildScriptRegex();

    // export const BASE = "/pportal/";
    [GeneratedRegex(@"export\s+const\s+BASE\s*=\s*""(?<base>[^""]+)""")]
    private static partial Regex SiteBaseRegex();

    // const BASE = "/pportal/docs";
    [GeneratedRegex(@"(?<!export\s)const\s+BASE\s*=\s*""(?<base>[^""]+)""")]
    private static partial Regex DocsBaseRegex();

    // const OUT_DIR = "../dist/docs";
    [GeneratedRegex(@"const\s+OUT_DIR\s*=\s*""(?<out>[^""]+)""")]
    private static partial Regex OutDirRegex();
}
