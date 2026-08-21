using System.Text.Json;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: what arrives on the push socket, as the FLAGS the core waits on.
///
/// These are powers of two rather than an ordinary enumeration, and that is not decoration:
/// <c>wait_for_notification</c> takes a <c>uint16_t types</c> and waits for ANY of a set. So the
/// values are a mask a caller builds, and a port that numbered them 0..5 would make every wait
/// match the wrong things - silently, because the numbers are still all distinct.
///
/// <see cref="Unknown"/> is zero for the same reason: it belongs to no mask, so a notification
/// nobody recognises wakes nobody up rather than waking everybody.
/// </summary>
[Flags]
public enum PushNotificationType
{
    /// <summary>Zero, so it is in no mask a caller can build.</summary>
    Unknown = 0,

    SessionCreated = 1 << 0,
    MemberCreated = 1 << 1,
    MemberDeleted = 1 << 2,
    CustomData1Updated = 1 << 3,
    SessionMessageCreated = 1 << 4,
    SessionDeleted = 1 << 5,
}

/// <summary>
/// PP33: the notification envelope, read the way the core reads it.
///
/// One field decides everything - <c>dataType</c> - and three things about that are worth pinning:
///
///   the key is CAMEL CASE. The core's own error message spells it "datatype" and the lookup does
///   not, so the message is the misleading half and the key is the one to copy;
///
///   a dataType that is not a STRING is Unknown rather than an error, and so is a missing one. The
///   two are the same outcome, which is what lets the caller have a single path for "not for me";
///
///   the six strings are namespaced PSN identifiers with no shared shape a port could derive -
///   remotePlaySession for two of them, rps for four, and the two verbs are not in the same place.
/// </summary>
public static class PushNotification
{
    /// <summary>The field the whole envelope turns on, spelled as the LOOKUP spells it.</summary>
    public const string TypeField = "dataType";

    /// <summary>Each type's identifier, exactly as PSN sends it.</summary>
    public static IReadOnlyDictionary<PushNotificationType, string> DataTypes { get; } =
        new Dictionary<PushNotificationType, string>
        {
            [PushNotificationType.SessionCreated] = "psn:sessionManager:sys:remotePlaySession:created",
            [PushNotificationType.MemberCreated] = "psn:sessionManager:sys:rps:members:created",
            [PushNotificationType.MemberDeleted] = "psn:sessionManager:sys:rps:members:deleted",
            [PushNotificationType.CustomData1Updated] = "psn:sessionManager:sys:rps:customData1:updated",
            [PushNotificationType.SessionMessageCreated] = "psn:sessionManager:sys:rps:sessionMessage:created",
            [PushNotificationType.SessionDeleted] = "psn:sessionManager:sys:remotePlaySession:deleted",
        };

    /// <summary>
    /// The type one notification carries, or <see cref="PushNotificationType.Unknown"/>.
    ///
    /// Read through <see cref="JsonC"/> rather than through JsonElement directly, so that a
    /// <c>"dataType":null</c> is the same nothing json-c would see (PP183) instead of a node this
    /// side alone can find.
    /// </summary>
    public static PushNotificationType TypeOf(JsonElement? envelope)
    {
        JsonElement? field = JsonC.Get(envelope, TypeField);

        // Missing and not-a-string are one outcome, which is what lets a caller have a single path
        // for "not for me".
        if (field is not { ValueKind: JsonValueKind.String })
            return PushNotificationType.Unknown;

        string value = field.Value.GetString() ?? "";

        foreach ((PushNotificationType type, string identifier) in DataTypes)
        {
            if (string.Equals(value, identifier, StringComparison.Ordinal))
                return type;
        }

        return PushNotificationType.Unknown;
    }

    /// <summary>
    /// Whether a notification is one a caller waiting on <paramref name="mask"/> should be woken
    /// for. Unknown never is, because zero meets no mask.
    /// </summary>
    public static bool Matches(PushNotificationType type, PushNotificationType mask)
        => type != PushNotificationType.Unknown && (type & mask) != 0;
}

/// <summary>
/// PP33: the notification envelope where the Qt core states it.
/// </summary>
public static class PushNotificationSource
{
    /// <summary>Where the types are declared and parsed.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether every identifier is still the string this port carries.</summary>
    public static bool TheIdentifiersAreStillThese(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach (string identifier in PushNotification.DataTypes.Values)
        {
            if (!core.Contains($"\"{identifier}\"", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>Whether the types are still powers of two, which is what makes them a mask.</summary>
    public static bool TheTypesAreStillFlags(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("NOTIFICATION_TYPE_SESSION_CREATED = 1 << 0", StringComparison.Ordinal)
            && core.Contains("NOTIFICATION_TYPE_SESSION_DELETED = 1 << 5", StringComparison.Ordinal)
            && core.Contains("NOTIFICATION_TYPE_UNKNOWN = 0", StringComparison.Ordinal);
    }

    /// <summary>Whether the lookup still uses the camel-cased key its own message misspells.</summary>
    public static bool TheKeyIsStillCamelCase(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"json_object_object_get_ex(json, \"{PushNotification.TypeField}\"", StringComparison.Ordinal);
    }

    /// <summary>Whether a non-string dataType still reads as unknown rather than as an error.</summary>
    public static bool ANonStringIsStillUnknown(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("!json_object_is_type(datatype, json_type_string)", StringComparison.Ordinal)
            && core.Contains("return NOTIFICATION_TYPE_UNKNOWN;", StringComparison.Ordinal);
    }
}
