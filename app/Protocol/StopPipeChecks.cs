using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP407: the one primitive the library checks everywhere, and the two places it did not.
///
/// <c>chiaki_stop_pipe_init</c> is WSACreateEvent, and it answers CHIAKI_ERR_UNKNOWN when that
/// returns WSA_INVALID_EVENT. Ten call sites in lib/src: eight of them - two in ctrl.c, two in
/// discovery.c, one each in regist.c, session.c, takion.c and rudp.c - test the result and take a
/// failure path. The two in holepunch.c asserted it, and Release defines NDEBUG.
///
/// WHAT THE SESSION KEPT was an invalid event handle in notif_pipe or select_pipe. Nothing later
/// re-creates it, so every wait built on that pipe fails for the life of the session, and it
/// surfaces far from here as a notification that never arrives.
///
/// THE FIVE PRIMITIVES AROUND THEM ARE STILL ASSERTED, and that is the point rather than an
/// oversight. A mutex or cond init on Windows is a single return of the success constant, so a
/// check on one adds a branch nothing can take - see <see cref="ThreadPrimitives"/>. The library's
/// own convention is the argument here: it disagrees with itself in exactly two places, and this is
/// the shape of the disagreement rather than a count of what might fail.
/// </summary>
public static class StopPipeChecks
{
    /// <summary>The tree this reads.</summary>
    public const string RelativePath = @"lib\src";

    /// <summary>That directory, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateDirectory(RelativePath);

    /// <summary>The call whose result every site has to look at.</summary>
    public const string Call = "chiaki_stop_pipe_init(";

    /// <summary>
    /// The fewest call sites the tree may hold, so a reader that stopped matching says so.
    ///
    /// PP271: ten today. A floor and not the count, because a site can go with the code around it
    /// and a number edited on every deletion gets edited without being read.
    /// </summary>
    public const int Floor = 8;

    /// <summary>Every place the tree creates a stop pipe, with the text that follows it.</summary>
    /// <returns>File name to the statement after each call.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Sites(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var found = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(directory, "*.c", SearchOption.AllDirectories))
        {
            IReadOnlyList<string> after = InFile(File.ReadAllText(path));
            if (after.Count > 0)
                found[Path.GetFileName(path)] = after;
        }

        return found;
    }

    /// <summary>
    /// What follows each call in one file's text - the statement that does or does not check it.
    ///
    /// Through <see cref="CCall.Code"/>, because the note left above the corrected pair names the
    /// call: PP399, PP400 and PP401 each read a comment as the thing it described, once apiece.
    /// </summary>
    public static IReadOnlyList<string> InFile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);
        var found = new List<string>();

        for (int at = code.IndexOf(Call, StringComparison.Ordinal); at >= 0;
             at = code.IndexOf(Call, at + 1, StringComparison.Ordinal))
        {
            int semicolon = code.IndexOf(';', at);
            if (semicolon < 0)
                break;

            int next = code.IndexOf(';', semicolon + 1);
            found.Add(next < 0
                ? code[(semicolon + 1)..].Trim()
                : code[(semicolon + 1)..next].Trim());
        }

        return found;
    }

    /// <summary>
    /// Whether every site in a file looks at what it got back rather than asserting it.
    ///
    /// PP272: a file that creates no stop pipe answers NO. Read the other way this is the absence
    /// of an assert, and an empty string has that.
    /// </summary>
    public static bool EverySiteIsChecked(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        IReadOnlyList<string> after = InFile(source);

        return after.Count > 0
            && after.All(statement => !statement.StartsWith("assert", StringComparison.Ordinal));
    }
}
