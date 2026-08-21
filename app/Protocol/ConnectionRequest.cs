using System.Globalization;
using System.Text.Json;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One side's offer: who it is, how its NAT behaves, and where it might be reached.</summary>
/// <param name="Sid">This side's session id.</param>
/// <param name="PeerSid">The other side's.</param>
/// <param name="Skey">Sixteen bytes, base64 on the wire.</param>
/// <param name="NatType">A number, and only ever a number - see <see cref="ConnectionRequestReader"/>.</param>
/// <param name="DefaultRouteMac">Six bytes, or all zeros when the field was not the right length.</param>
/// <param name="LocalHashedId">Twenty bytes, base64 on the wire.</param>
/// <param name="Candidates">Where this side thinks it can be reached.</param>
public readonly record struct ConnectionRequest(
    uint Sid,
    uint PeerSid,
    byte[] Skey,
    byte NatType,
    byte[] DefaultRouteMac,
    byte[] LocalHashedId,
    IReadOnlyList<Candidate> Candidates);

/// <summary>
/// PP33: reading a connection request, and the two fields whose handling is not what it looks like.
///
/// 1. THE MAC ADDRESS IS PARSED ONLY AT EXACTLY SEVENTEEN CHARACTERS, and a field of any other
///    length is left as SIX ZEROS with no error and no log. So a malformed address does not fail
///    the request - it becomes 00:00:00:00:00:00 and the session continues against a route nobody
///    has. A port that validated it would refuse offers the Qt client accepts; a port that only
///    read the first six octets it found would invent one.
///
/// 2. THE TWO BASE64 FIELDS ARE DECODED WITH DIFFERENT IDEAS OF THE BUFFER. localHashedId passes
///    sizeof(the destination) as the capacity, which is right. skey passes strlen(the INPUT) -
///    twenty-four for a sixteen-byte key - into a sixteen-byte array, so the decoder is told it
///    has eight bytes it does not. It is safe only because the key is always sixteen bytes.
///
///    That one is NOT reproduced. This reads by the destination's size and refuses anything
///    longer, which is what the other call already does and what the day skey grows would need.
///    Ported behaviour stops at behaviour; a latent overflow is not behaviour, and copying it
///    across would carry a defect into a language that was going to catch it anyway.
/// </summary>
public static class ConnectionRequestReader
{
    /// <summary>The session key's length.</summary>
    public const int SkeyLength = 16;

    /// <summary>The hashed id's.</summary>
    public const int LocalHashedIdLength = 20;

    /// <summary>A MAC address, in bytes.</summary>
    public const int MacLength = 6;

    /// <summary>The one length at which the MAC field is parsed at all: XX:XX:XX:XX:XX:XX.</summary>
    public const int MacTextLength = 17;

    /// <summary>
    /// The MAC bytes, or six zeros - which is what the core leaves when the field is not exactly
    /// seventeen characters, and it leaves them without saying so.
    /// </summary>
    public static byte[] ReadMac(string? text)
    {
        var mac = new byte[MacLength];

        if (text is null || text.Length != MacTextLength)
            return mac;

        string[] octets = text.Split(':');
        if (octets.Length != MacLength)
            return mac;

        for (int i = 0; i < MacLength; i++)
        {
            if (!byte.TryParse(octets[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                return new byte[MacLength];

            mac[i] = value;
        }

        return mac;
    }

    /// <summary>
    /// A base64 field of an exact length, or null.
    ///
    /// Sized by the DESTINATION and not by the input, which is the half the core gets right in one
    /// of its two calls - see the class note.
    /// </summary>
    public static byte[]? ReadFixedBase64(string? text, int length)
    {
        if (text is null)
            return null;

        try
        {
            byte[] decoded = Convert.FromBase64String(text);
            return decoded.Length == length ? decoded : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// One connection request, or null when a required field is missing or the wrong type.
    ///
    /// natType must be a NUMBER. The core tests json_type_int specifically, so a natType sent as
    /// "2" invalidates the request rather than being coerced - which is the opposite of what
    /// json-c's accessors would do if it asked one of them (PP183).
    /// </summary>
    public static ConnectionRequest? Read(JsonElement? json)
    {
        JsonElement? sid = JsonC.Get(json, "sid");
        JsonElement? peerSid = JsonC.Get(json, "peerSid");
        if (sid is not { ValueKind: JsonValueKind.Number } || peerSid is not { ValueKind: JsonValueKind.Number })
            return null;

        JsonElement? nat = JsonC.Get(json, "natType");
        if (JsonC.TypeOf(nat) != JsonCType.Int)
            return null;

        byte[]? skey = ReadFixedBase64(JsonC.String(JsonC.Get(json, "skey")), SkeyLength);
        if (skey is null)
            return null;

        byte[]? hashedId = ReadFixedBase64(
            JsonC.String(JsonC.Get(json, "localHashedId")), LocalHashedIdLength);
        if (hashedId is null)
            return null;

        JsonElement? mac = JsonC.Get(json, "defaultRouteMacAddr");
        if (mac is not { ValueKind: JsonValueKind.String })
            return null;

        // ONE BAD CANDIDATE FAILS THE WHOLE REQUEST. The core's per-field guards jump to
        // invalid_schema, which is the exit for the entire message and not for the candidate being
        // read - so there is no salvaging the good ones, and PP195 stopped this reader from
        // pretending otherwise.
        var candidates = new List<Candidate>();
        JsonElement? list = JsonC.Get(json, "candidate");
        for (int i = 0; i < JsonC.ArrayLength(list); i++)
        {
            Candidate? candidate = CandidateReader.Read(JsonC.ArrayAt(list, i));
            if (candidate is null)
                return null;

            candidates.Add(candidate.Value);
        }

        return new ConnectionRequest(
            (uint)JsonC.Int64(sid),
            (uint)JsonC.Int64(peerSid),
            skey,
            (byte)JsonC.Int(nat),
            ReadMac(mac.Value.GetString()),
            hashedId,
            candidates);
    }
}

/// <summary>
/// PP33: the connection request's rules where the Qt core states them.
/// </summary>
public static class ConnectionRequestSource
{
    /// <summary>Where the request is parsed.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether the MAC is still parsed only at that one length.</summary>
    public static bool TheMacIsStillLengthGated(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"if (strlen(mac_str) == {ConnectionRequestReader.MacTextLength})", StringComparison.Ordinal);
    }

    /// <summary>Whether natType must still be an int rather than anything coercible.</summary>
    public static bool NatTypeMustStillBeAnInt(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("!json_object_is_type(obj, json_type_int)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the two base64 decodes still disagree about the buffer. Asserted as STILL TRUE
    /// rather than fixed: the port diverges here on purpose, and a divergence nobody re-reads is
    /// indistinguishable from a mistake.
    /// </summary>
    public static bool TheTwoDecodesStillDisagree(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // skey: the INPUT's length as the capacity.
        bool skeyByInput = core.Contains(
            "err = chiaki_base64_decode(skey_str, strlen(skey_str), msg->conn_request->skey, &skey_len);",
            StringComparison.Ordinal);

        // localHashedId: the DESTINATION's, which is the right one.
        bool idByDestination = core.Contains(
            "size_t local_hashed_id_len = sizeof(msg->conn_request->local_hashed_id);",
            StringComparison.Ordinal);

        return skeyByInput && idByDestination;
    }
}
