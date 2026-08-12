namespace MeasureStartup;

/// <summary>What a build weighs, and whether the thing PP46 is about is in it.</summary>
internal readonly record struct TreeSize(int Files, long Bytes, bool WebEnginePresent, long WebEngineBytes)
{
    public double Megabytes => Bytes / 1024.0 / 1024.0;
    public double WebEngineMegabytes => WebEngineBytes / 1024.0 / 1024.0;
}

internal static class Tree
{
    /// <summary>
    /// Files whose presence means Chromium is in this tree. Matched on the name because the layout
    /// differs between a deploy tree and an installed one, and the resource pak is what actually
    /// carries the weight - QtWebEngineCore.dll alone would undercount by an order of magnitude.
    /// </summary>
    private static readonly string[] WebEngineMarkers =
    [
        "QtWebEngineCore", "Qt6WebEngineCore", "QtWebEngineProcess",
        "icudtl.dat", "qtwebengine_resources",
    ];

    public static TreeSize Measure(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        int files = 0;
        long bytes = 0, webBytes = 0;
        bool web = false;

        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var fi = new FileInfo(path);
            files++;
            bytes += fi.Length;
            if (IsWebEngine(fi.Name))
            {
                web = true;
                webBytes += fi.Length;
            }
        }

        return new TreeSize(files, bytes, web, webBytes);
    }

    public static bool IsWebEngine(string fileName)
    {
        foreach (string marker in WebEngineMarkers)
        {
            if (fileName.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
