using System.Runtime.InteropServices;

namespace ChiakiNg.Settings;

/// <summary>
/// PP3: where the Qt client puts a file, stated once so the .NET host puts it in the same place.
///
/// sessionlog.cpp writes a log per session and qmlmainwindow.cpp writes two render-parameter
/// files, all through QStandardPaths::writableLocation. Environment.SpecialFolder does not
/// produce the same answers, so a host that reaches for the obvious one starts a second tree
/// beside the first - and the cost is small and permanent: a support request quotes a log the
/// other build cannot find.
///
/// These are the Qt paths and not better ones. Relocating the data is a separate decision, and
/// taking it during a port means never knowing which of the two changes broke a file that went
/// missing.
///
/// Every value below was measured, not inferred: a Qt 6.11 program with this application's own
/// organisation and application names printed writableLocation for each of them on Windows.
///
/// The two traps
/// -------------
/// 1. AppDataLocation is ROAMING and ConfigLocation is LOCAL. On Windows those are different
///    roots - AppData\Roaming\Chiaki\Chiaki against AppData\Local\Chiaki\Chiaki - and .NET spells
///    them ApplicationData and LocalApplicationData, which read as near-synonyms and are not. A
///    host that uses one for both puts the session logs where nothing looks for them.
///
/// 2. There is no SpecialFolder for Downloads. It is a known folder with an id and no enum
///    member, so it takes a P/Invoke; falling back to a guess under the profile directory is
///    wrong on any machine where the user moved it, which is common.
///
/// Qt renders these with forward slashes and Windows renders them with backslashes. They name
/// the same directory, and the separator is normalised here to the platform's rather than to
/// Qt's, because these strings are handed to .NET file APIs and printed to Windows users.
/// </summary>
public static class QtPaths
{
    /// <summary>QGuiApplication::setOrganizationName in gui/src/main.cpp.</summary>
    public const string Organization = "Chiaki";

    /// <summary>QGuiApplication::setApplicationName. The same word, and they are two segments.</summary>
    public const string Application = "Chiaki";

    /// <summary>
    /// How QStandardPaths builds an application location out of a root: the organisation, then
    /// the application, each its own directory. Pure, and separate from the environment for that
    /// reason - it is what a selftest can assert on any machine.
    /// </summary>
    public static string Compose(string root, string organization, string application)
        => Path.Combine(root, organization, application);

    /// <summary>
    /// QStandardPaths::AppDataLocation - AppData\Roaming\Chiaki\Chiaki. The session logs and the
    /// shader cache live under here. ROAMING, not Local; see trap 1.
    /// </summary>
    public static string AppDataLocation => Compose(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Organization, Application);

    /// <summary>QStandardPaths::AppLocalDataLocation - AppData\Local\Chiaki\Chiaki.</summary>
    public static string AppLocalDataLocation => Compose(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Organization, Application);

    /// <summary>
    /// QStandardPaths::ConfigLocation. On Windows this is the LOCAL application data directory
    /// and not the roaming one, which is the whole of trap 1: it is the same string as
    /// AppLocalDataLocation and a different root from AppDataLocation.
    /// </summary>
    public static string ConfigLocation => AppLocalDataLocation;

    /// <summary>
    /// Where sessionlog.cpp keeps the per-session logs: the "log" directory it mkpaths under
    /// AppDataLocation. The directory the user is asked for when a stream misbehaves.
    /// </summary>
    public static string LogDirectory => Path.Combine(AppDataLocation, "log");

    /// <summary>
    /// The session baseline ledger, which qmlbackend.cpp appends one line of JSON to per session
    /// and puts in the log directory rather than beside it.
    ///
    /// It is here because it is the file the port is measured with: two builds are compared by
    /// holding their rows against each other, and a host that appended to a second file would
    /// make the comparison silently meaningless rather than obviously broken.
    /// </summary>
    public static string SessionBaselineFile => Path.Combine(LogDirectory, "chiaki_baseline.jsonl");

    /// <summary>qmlmainwindow.cpp: AppDataLocation + "/pl_shader.cache".</summary>
    public static string ShaderCacheFile => Path.Combine(AppDataLocation, "pl_shader.cache");

    /// <summary>
    /// qmlmainwindow.cpp: ConfigLocation + "/Chiaki/pl_render_params.conf".
    ///
    /// That leaves three "Chiaki" segments in a row, because ConfigLocation already ends in the
    /// organisation and the application and the literal adds a third. It is reproduced and not
    /// tidied: the file the Qt build writes is the file this host has to find, and the tidying
    /// is a relocation, which this line explicitly is not.
    /// </summary>
    public static string PlaceboConfigFile
        => Path.Combine(ConfigLocation, "Chiaki", "pl_render_params.conf");

    /// <summary>
    /// QStandardPaths::DesktopLocation. The shell answers this, so a Desktop redirected into
    /// OneDrive comes back redirected here too - which is what Qt gets and therefore right.
    /// </summary>
    public static string DesktopDirectory
        => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>
    /// QStandardPaths::DownloadLocation, where the settings screen offers to put an export.
    ///
    /// Trap 2: there is no SpecialFolder for it. This asks the shell for the known folder by id,
    /// which is what Qt does, so a user who moved their Downloads gets the folder they moved it
    /// to. The fallback under the profile directory is for the case where the shell refuses -
    /// wrong for a moved folder, and better than an empty path.
    /// </summary>
    public static string DownloadsDirectory => KnownFolder(FolderIdDownloads)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>FOLDERID_Downloads, from KnownFolders.h.</summary>
    private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        in Guid folderId, uint flags, IntPtr token, out IntPtr path);

    private static string? KnownFolder(Guid id)
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (SHGetKnownFolderPath(in id, 0, IntPtr.Zero, out buffer) != 0)
                return null;
            return Marshal.PtrToStringUni(buffer);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(buffer);
        }
    }
}
