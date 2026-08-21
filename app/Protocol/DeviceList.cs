using System.Text.Json;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Why a device list could not be read.</summary>
public enum DeviceListResult
{
    /// <summary>It was read.</summary>
    Ok,

    /// <summary>The body is not JSON.</summary>
    NotJson,

    /// <summary>There is no clients array.</summary>
    NoClients,

    /// <summary>One of the clients is missing a field, or has one of the wrong type.</summary>
    BadClient,
}

/// <summary>One console PSN says this account has.</summary>
/// <param name="DeviceUid">Its thirty-two byte identifier.</param>
/// <param name="Name">Its name, as far as it fits.</param>
/// <param name="RemotePlayEnabled">Whether remote play is among its enabled features.</param>
/// <param name="Type">What the CALLER asked for - never something the JSON said.</param>
public readonly record struct HolepunchDevice(
    byte[] DeviceUid, string Name, bool RemotePlayEnabled, string Type);

/// <summary>
/// PP33: reading the list of consoles, where the failures are more interesting than the fields.
///
/// AN EMPTY LIST LOOKS LIKE RUNNING OUT OF MEMORY. The array is allocated with
/// <c>malloc(sizeof(…) * num_clients)</c> and the result is tested for NULL - so an account with no
/// consoles asks for zero bytes, and on any allocator that answers NULL to that, the caller is told
/// CHIAKI_ERR_MEMORY. "You have no consoles" and "this machine is out of memory" arrive as the same
/// error, and the first one is the common case for a new account.
///
/// AND THAT ONE FAILURE PATH RETURNS WITHOUT CLEANING UP. Every other error in the function jumps
/// to a label; this one returns straight out, leaking the tokener, the parsed document, the curl
/// handle, the OAuth header and the response body. It is the only <c>return</c> in a function built
/// entirely out of gotos, which is how it got missed.
///
/// THE COUNT IS PUBLISHED BEFORE THE DEVICES ARE FILLED IN. <c>*device_count</c> is set from the
/// array length before the loop runs, so a client that fails to parse halfway through leaves the
/// caller holding a count for devices that were never written - which matters because the failure
/// path frees the array and the count is not reset.
///
/// ONE BAD DEVICE LOSES THE WHOLE LIST. Every missing field and every wrong type jumps out of the
/// function, so a single console PSN describes oddly costs the account every other console it has.
/// The port reproduces that: refusing everything is what the Qt client does, and a port that
/// skipped the bad one would show a console list the Qt client never shows.
///
/// EXCEPT INSIDE enabledFeatures, WHERE NOTHING IS STRICT AT ALL. That array is scanned for the
/// literal "remotePlay" and any entry that is not a string is simply passed over - the one place in
/// the function where an unexpected type is not fatal.
///
/// AND THE CONSOLE TYPE IS NOT READ. It is stamped onto every device from the ARGUMENT the caller
/// passed, so the list says what was asked for rather than what came back.
///
/// The name is copied with strncpy into <c>char[32]</c>, which does not terminate when the source
/// fills it - so a thirty-two character console name leaves the buffer running into whatever
/// follows. This port keeps the truncation, which is behaviour, and drops the missing terminator,
/// which is not.
/// </summary>
public static class DeviceListReader
{
    /// <summary>How long a device identifier is, in bytes.</summary>
    public const int DeviceUidLength = HolepunchIdentifiers.DeviceUidLength;

    /// <summary>How much room a device name is given, terminator included in the core's buffer.</summary>
    public const int DeviceNameBuffer = 32;

    /// <summary>The feature that has to be present for remote play to be offered.</summary>
    public const string RemotePlayFeature = "remotePlay";

    /// <summary>
    /// Whether the core would report a memory failure for a list of this length - which it does for
    /// an empty one, on any allocator that answers NULL to a zero-byte request.
    /// </summary>
    public static bool WouldLookLikeAMemoryFailure(int clients) => clients == 0;

    /// <summary>
    /// The devices in a response body, or null with a reason.
    ///
    /// <paramref name="consoleType"/> is stamped onto every device without being checked against
    /// anything in the JSON - see the class note.
    /// </summary>
    public static IReadOnlyList<HolepunchDevice>? Read(
        string body, string consoleType, out DeviceListResult result)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(consoleType);

        using JsonDocument? document = JsonC.Parse(body);
        if (document is null)
        {
            result = DeviceListResult.NotJson;
            return null;
        }

        JsonElement? clients = JsonC.Get(document.RootElement, "clients");
        if (clients is not { ValueKind: JsonValueKind.Array })
        {
            result = DeviceListResult.NoClients;
            return null;
        }

        var devices = new List<HolepunchDevice>();
        for (int i = 0; i < JsonC.ArrayLength(clients); i++)
        {
            HolepunchDevice? device = ReadOne(JsonC.ArrayAt(clients, i), consoleType);
            if (device is null)
            {
                // One bad device loses the whole list, which is what the Qt client does.
                result = DeviceListResult.BadClient;
                return null;
            }

            devices.Add(device.Value);
        }

        result = DeviceListResult.Ok;
        return devices;
    }

    private static HolepunchDevice? ReadOne(JsonElement? client, string consoleType)
    {
        JsonElement? duid = JsonC.Get(client, "duid");
        if (duid is not { ValueKind: JsonValueKind.String })
            return null;

        byte[]? uid = HolepunchIdentifiers.HexToBytes(duid.Value.GetString() ?? "", DeviceUidLength);
        if (uid is null)
            return null;

        JsonElement? device = JsonC.Get(client, "device");
        if (device is not { ValueKind: JsonValueKind.Object })
            return null;

        JsonElement? features = JsonC.Get(device, "enabledFeatures");
        if (features is not { ValueKind: JsonValueKind.Array })
            return null;

        // The one place an unexpected type is not fatal: anything that is not the string we are
        // looking for is simply passed over.
        bool remotePlay = false;
        for (int i = 0; i < JsonC.ArrayLength(features); i++)
        {
            JsonElement? feature = JsonC.ArrayAt(features, i);
            if (feature is { ValueKind: JsonValueKind.String }
                && string.Equals(feature.Value.GetString(), RemotePlayFeature, StringComparison.Ordinal))
            {
                remotePlay = true;
                break;
            }
        }

        JsonElement? name = JsonC.Get(device, "name");
        if (name is not { ValueKind: JsonValueKind.String })
            return null;

        return new HolepunchDevice(uid, Truncate(name.Value.GetString() ?? ""), remotePlay, consoleType);
    }

    /// <summary>
    /// The name as it fits, which is the truncation strncpy performs - without the missing
    /// terminator, which is not behaviour.
    /// </summary>
    public static string Truncate(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length <= DeviceNameBuffer ? name : name[..DeviceNameBuffer];
    }
}

/// <summary>
/// PP33: the device list's rules where the Qt core states them.
/// </summary>
public static class DeviceListSource
{
    /// <summary>Where the list is read.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether an empty list still asks for zero bytes and tests the answer.</summary>
    public static bool AnEmptyListStillAsksForNothing(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("*devices = malloc(sizeof(ChiakiHolepunchDeviceInfo) * num_clients);", StringComparison.Ordinal)
            && core.Contains("if(!(*devices))", StringComparison.Ordinal)
            && core.Contains("return CHIAKI_ERR_MEMORY;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether that path is still the only one that returns rather than jumping to a label - which
    /// is what makes it the one that leaks.
    /// </summary>
    public static bool TheMemoryPathStillReturnsWithoutCleanup(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int at = core.IndexOf("if(!(*devices))", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = core.IndexOf("*device_count = num_clients;", at, StringComparison.Ordinal);
        if (end < at)
            return false;

        string block = core[at..end];
        return block.Contains("return CHIAKI_ERR_MEMORY;", StringComparison.Ordinal)
            && !block.Contains("goto ", StringComparison.Ordinal);
    }

    /// <summary>Whether the count is still published before the devices are filled in.</summary>
    public static bool TheCountIsStillSetFirst(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int count = core.IndexOf("*device_count = num_clients;", StringComparison.Ordinal);
        int loop = core.IndexOf("for (size_t i = 0; i < num_clients; i++)", StringComparison.Ordinal);

        return count > 0 && loop > count;
    }

    /// <summary>Whether one bad device still loses the whole list.</summary>
    public static bool OneBadDeviceStillLosesTheList(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int at = core.IndexOf("for (size_t i = 0; i < num_clients; i++)", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = core.IndexOf("cleanup_devices:", at, StringComparison.Ordinal);
        if (end < at)
            return false;

        // Six jumps out of the loop body, and not one recovery.
        return CountIn(core[at..end], "goto cleanup_devices;") >= 5
            && !core[at..end].Contains("continue;", StringComparison.Ordinal);
    }

    /// <summary>Whether the feature scan is still the one lenient place in the function.</summary>
    public static bool TheFeatureScanIsStillLenient(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            $"if (json_object_is_type(feature, json_type_string) && strcmp(json_object_get_string(feature), \"{DeviceListReader.RemotePlayFeature}\") == 0)",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the console type is still stamped from the argument rather than read.</summary>
    public static bool TheTypeIsStillStampedFromTheArgument(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("device.type = console_type;", StringComparison.Ordinal);
    }

    /// <summary>Where the device struct is declared.</summary>
    public const string HeaderPath = @"lib\include\chiaki\remote\holepunch.h";

    /// <summary>The header, or null outside a checkout.</summary>
    public static string? LocateHeader() => SanitizerSource.LocateRelative(HeaderPath);

    /// <summary>Whether the name is still copied with the call that may not terminate.</summary>
    public static bool TheNameIsStillCopiedWithoutATerminator(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "strncpy(device.device_name, json_object_get_string(device_name), sizeof(device.device_name));",
            StringComparison.Ordinal);
    }

    /// <summary>And whether the buffer it copies into is still that size.</summary>
    public static bool TheNameBufferIsStillThatSize(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.Contains($"char device_name[{DeviceListReader.DeviceNameBuffer}];", StringComparison.Ordinal);
    }

    private static int CountIn(string text, string needle)
    {
        int count = 0;
        int at = 0;

        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at++;
        }

        return count;
    }
}
