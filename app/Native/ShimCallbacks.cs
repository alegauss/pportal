using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>One C callback typedef and the file whose thunk is passed for it.</summary>
/// <param name="Typedef">The typedef's name in chiaki_shim.h.</param>
/// <param name="ManagedRelativePath">The file declaring the matching function pointer.</param>
public readonly record struct ShimCallback(string Typedef, string ManagedRelativePath);

/// <summary>
/// PP581: the six function pointers that cross to the shim, against the typedefs they answer.
///
/// These are the sharpest hand-written promises in the port. A missing export throws
/// (PP580); a shifted enum mislabels a value (PP577); a wrong function-pointer signature corrupts
/// the stack, because the C pushes what its typedef says and the thunk reads what its generic
/// argument list says. Nothing throws and nothing is wrong until it is very wrong.
///
/// FIVE RETURN void AND ONE RETURNS bool, which is why a sweep keyed on `typedef void (*` finds
/// five - as the first pass at this did. ChiakiShimVideoSampleCb is the sixth.
///
/// bool IS byte ON PURPOSE. C's bool is one byte and .NET marshals bool as the four-byte Windows
/// BOOL by default, so the thunks take byte and the map below says so. That is the one row where
/// the obvious mapping is the wrong one.
///
/// THE PAIRING IS A LIST AND THE SIGNATURES ARE READ. What is written down here is only which
/// typedef belongs to which file; both signatures are then read from the tree, so this cannot
/// agree with itself.
/// </summary>
public static partial class ShimCallbacks
{
    /// <summary>The shim's header, where the typedefs are.</summary>
    public const string HeaderRelativePath = @"shim\chiaki_shim.h";

    /// <summary>The header, or null outside a checkout.</summary>
    public static string? LocateHeader() => SanitizerSource.LocateRelative(HeaderRelativePath);

    /// <summary>The six, and which file answers each.</summary>
    public static IReadOnlyList<ShimCallback> All { get; } =
    [
        new("ChiakiShimLogCb", @"app\Native\ChiakiLog.cs"),
        new("ChiakiShimTapCb", @"app\Native\ChiakiMessageTap.cs"),
        new("ChiakiShimEventCb", @"app\Native\ChiakiSession.cs"),
        new("ChiakiShimDiscoveryServiceCb", @"app\Session\DiscoveryService.cs"),
        new("ChiakiShimReorderDropCb", @"app\Protocol\NativeReorderQueue.cs"),
        new("ChiakiShimVideoSampleCb", @"app\Protocol\VideoReceiver.cs"),
    ];

    /// <summary>
    /// What each C spelling is on the managed side.
    ///
    /// Every pointer is IntPtr, whatever it points at: the thunks take addresses and hand them on.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Widths { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["void"] = "void",
            ["bool"] = "byte",
            ["int32_t"] = "int",
            ["uint16_t"] = "ushort",
            ["uint64_t"] = "ulong",
            ["uint8_t"] = "byte",
        };

    /// <summary>
    /// The typedef's signature as managed spellings: its parameters, then its return.
    ///
    /// Which is the order a function pointer's generic argument list is written in, so the two can
    /// be compared as they stand.
    /// </summary>
    public static IReadOnlyList<string>? SignatureOf(string header, string typedef)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(typedef);

        Match? found = Typedef().Matches(header)
            .FirstOrDefault(one => one.Groups["name"].Value == typedef);

        if (found is null)
            return null;

        var said = new List<string>();

        foreach (string raw in found.Groups["args"].Value.Split(','))
        {
            string arg = raw.Trim();
            if (arg.Length == 0)
                continue;

            // A pointer is an address whatever its target, and const is not a width.
            if (arg.Contains('*', StringComparison.Ordinal))
            {
                said.Add("IntPtr");
                continue;
            }

            string type = arg.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
            said.Add(Widths.TryGetValue(type, out string? managed) ? managed : type);
        }

        string ret = found.Groups["ret"].Value.Trim();
        said.Add(Widths.TryGetValue(ret, out string? mapped) ? mapped : ret);

        return said;
    }

    /// <summary>
    /// The generic argument list of the function pointer a managed file declares.
    ///
    /// PP674: ONE SIGNATURE, NOT ONE OCCURRENCE. This required exactly one match, and a file taking
    /// the same callback into two entry points then had none - which is what happened the day the
    /// reorder queue gained its thirty-two-bit create beside the sixteen-bit one, both handing the
    /// shim the same drop thunk.
    ///
    /// Repeats of the SAME signature are one signature, and refusing them was refusing a fact the
    /// check does not care about. Two DIFFERENT ones are still refused: the comparison is against a
    /// single typedef, so a file with two shapes has no unambiguous answer to give and saying so is
    /// the whole reason this returns null rather than picking.
    /// </summary>
    public static IReadOnlyList<string>? ManagedSignatureIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string[] signatures =
        [
            .. FunctionPointer().Matches(source)
                .Select(one => one.Groups["args"].Value.Trim())
                .Distinct(StringComparer.Ordinal)
        ];

        if (signatures.Length != 1)
            return null;

        return [.. signatures[0].Split(',').Select(one => one.Trim())];
    }

    /// <summary>A callback typedef, with its return type and its parameter list.</summary>
    [GeneratedRegex(@"typedef\s+(?<ret>\w+)\s*\(\*(?<name>\w+)\)\s*\((?<args>[^)]*)\)\s*;")]
    private static partial Regex Typedef();

    /// <summary>A managed function pointer's generic argument list.</summary>
    [GeneratedRegex(@"delegate\*\s*unmanaged\[Cdecl\]<(?<args>[^>]+)>")]
    private static partial Regex FunctionPointer();
}
