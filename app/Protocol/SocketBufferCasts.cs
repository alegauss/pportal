using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One send or recv, and the buffer expression it passes.</summary>
/// <param name="File">Repository-relative, with forward slashes as the scan finds them.</param>
/// <param name="Call">"send" or "recv".</param>
/// <param name="Buffer">The second argument, as the C spells it.</param>
public readonly record struct SocketCall(string File, string Call, string Buffer);

/// <summary>
/// PP426: whether every socket call that passes an unsigned buffer casts it.
///
/// CHIAKI_SOCKET_BUF_TYPE is <c>char*</c>, because winsock's send and recv take <c>char*</c> where
/// POSIX takes <c>void*</c>. Nine of the eleven calls in lib/src that hand over a <c>uint8_t*</c>
/// cast it; takion.c did not, at chiaki_takion_send_raw and takion_recv - the two calls the whole
/// stream rides on - and both printed a -Wpointer-sign warning on every build.
///
/// THE RULE ASKS ABOUT THE BUFFER, NOT THE CALL. http.c and regist.c pass a char buffer already, so
/// a cast there would say nothing and demanding one would make this check about spelling. What is
/// refused is an UNSIGNED buffer handed over uncast, which is the shape the compiler objects to.
///
/// AND THE HARM IS THE OUTPUT RATHER THAN THE BYTES. char and uint8_t have the same width and no
/// compiler miscompiles this. What two permanent warnings cost is a build whose warning output a
/// reader learns to skip - which is where a real one will appear. A clean rebuild printed 27, and
/// seventeen of those are the compiler restating what PP357 argued from the source, PP404 censused
/// and PP406 refined, without any of the three citing it.
/// </summary>
public static partial class SocketBufferCasts
{
    /// <summary>The macro that spells what a socket wants.</summary>
    public const string CastMacro = "CHIAKI_SOCKET_BUF_TYPE";

    /// <summary>Where the C lives, relative to the repository root.</summary>
    public const string LibRelativePath = "lib";

    /// <summary>
    /// Every send and recv in the tree, with the buffer each passes.
    ///
    /// <c>sendto</c>, <c>recvfrom</c> and this project's own wrappers are excluded by the word
    /// boundary: what is asked about is the bare socket call.
    /// </summary>
    public static IReadOnlyList<SocketCall> All(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var calls = new List<SocketCall>();

        string lib = Path.Combine(root, LibRelativePath);
        if (!Directory.Exists(lib))
            return calls;

        foreach (string path in Directory.EnumerateFiles(lib, "*.c", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}third-party{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            foreach (Match match in SocketCallRegex().Matches(CCall.Code(File.ReadAllText(path))))
            {
                calls.Add(new SocketCall(
                    relative, match.Groups["call"].Value, match.Groups["buffer"].Value.Trim()));
            }
        }

        return calls;
    }

    /// <summary>
    /// The calls that hand an unsigned buffer to a socket without casting it.
    ///
    /// UNSIGNED IS READ FROM THE DECLARATION, not from the name. The first version of this check kept
    /// a list of buffer names and reported http.c's <c>recv(sock, buf, ..)</c> - whose buf is
    /// <c>char *buf</c> and draws no warning at all. A rule that guesses a type from an identifier is
    /// a rule about naming, and this tree calls both kinds "buf".
    /// </summary>
    public static IReadOnlyList<SocketCall> Uncast(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var uncast = new List<SocketCall>();

        foreach (IGrouping<string, SocketCall> file in All(root).GroupBy(c => c.File))
        {
            string source = Path.Combine(root, file.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
                continue;

            string code = CCall.Code(File.ReadAllText(source));

            uncast.AddRange(
                file.Where(call =>
                    !IsCast(call.Buffer) && IsDeclaredUnsigned(code, BareName(call.Buffer))));
        }

        return uncast;
    }

    /// <summary>Whether a buffer expression carries the cast a socket wants.</summary>
    public static bool IsCast(string buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer.Contains(CastMacro, StringComparison.Ordinal);
    }

    /// <summary>
    /// The identifier a buffer expression leads with, past any cast or address-of.
    ///
    /// <c>(CHIAKI_SOCKET_BUF_TYPE) buf + buf_filled_size</c> is buf, and so is <c>buf</c>.
    /// </summary>
    public static string BareName(string buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        // Past a cast, if there is one.
        int close = buffer.LastIndexOf(')');
        string rest = close >= 0 ? buffer[(close + 1)..] : buffer;

        rest = rest.TrimStart('*', '&', ' ', '\t');

        int end = 0;
        while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] == '_'))
            end++;

        return rest[..end];
    }

    /// <summary>
    /// Whether a file declares this identifier as an unsigned byte buffer.
    ///
    /// Read as a declaration rather than inferred: takion.c has thirty-four
    /// <c>uint8_t *buf</c> and http.c has none, which is exactly the difference between the two
    /// that warn and the one that does not.
    /// </summary>
    public static bool IsDeclaredUnsigned(string code, string name)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
            return false;

        string compact = CCall.Compact(code);

        return compact.Contains($"uint8_t*{name},", StringComparison.Ordinal)
            || compact.Contains($"uint8_t*{name})", StringComparison.Ordinal)
            || compact.Contains($"uint8_t*{name}=", StringComparison.Ordinal)
            || compact.Contains($"uint8_t*{name};", StringComparison.Ordinal)
            || compact.Contains($"uint8_t {name}[", StringComparison.Ordinal);
    }

    /// <summary>
    /// How many uncast unsigned buffers lib/src still hands to a socket.
    ///
    /// Zero. The ratchet rule: it may fall and may not rise, so a socket call added without the cast
    /// turns the suite red in the commit that adds it rather than adding a line to the build output
    /// that nobody reads.
    /// </summary>
    public const int UncastCeiling = 0;

    /// <summary>The repository root, or null outside a checkout.</summary>
    public static string? RepositoryRoot() => SanitizerSource.RepositoryRoot();

    // send(sock, <buffer>, ... - the bare call, so sendto and recvfrom and the project's own
    // wrappers are out by the boundary in front.
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?<call>send|recv)\s*\([^,]+,(?<buffer>[^,]+),")]
    private static partial Regex SocketCallRegex();
}
