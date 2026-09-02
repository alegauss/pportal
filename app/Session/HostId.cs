using System.Globalization;

namespace ChiakiNg.Session;

/// <summary>
/// PP624: the one spelling of a console's identity, because this port had two that never met.
///
/// <see cref="ConsoleList.Build"/> has asked whether a discovered host's
/// <see cref="DiscoveredConsole.Id"/> is in a set of keys since PP13, and every caller of it was a
/// test until PP600 wired the screen - so the sets a test hands it and the ids a console sends had
/// never been compared. They are not the same string:
///
///   the reply's `host-id` is hexadecimal text, as it arrived, in whatever case the console used;
///   this port's <see cref="Settings.RegisteredHost.MacText"/> is six bytes joined with colons,
///   which is the spelling the port shows a person.
///
/// THE QT CLIENT'S ANSWER IS READ AND NOT INVENTED. `DiscoveryHost::GetHostMAC` in
/// gui/src/discoverymanager.cpp parses the host-id from hex and REFUSES it unless it is exactly six
/// bytes; `HostMAC::ToString` in gui/include/host.h is `toHex()` - bare, lower case, no separators.
/// So the key is the six bytes as twelve hexadecimal characters, and a host-id of any other length
/// is not an identity at all.
///
/// The comparison here keeps the exact match as well. A set whose members are already the ids being
/// compared - which is every test written before this - goes on matching, so this widens what is
/// recognised and takes nothing away.
/// </summary>
public static class HostId
{
    /// <summary>How many bytes a console's identity is, which the Qt client refuses anything else for.</summary>
    public const int Bytes = 6;

    /// <summary>Where the client turns a host-id into an identity.</summary>
    public const string DiscoveryManagerRelativePath = @"gui\src\discoverymanager.cpp";

    /// <summary>Where it spells that identity as the key everything is stored under.</summary>
    public const string HostHeaderRelativePath = @"gui\include\host.h";

    /// <summary>
    /// Whether the client still parses the host-id from hex and refuses any other length.
    ///
    /// The half that makes <see cref="Key(string?)"/>'s null an answer rather than a shrug: a
    /// client that started accepting eight bytes would be storing identities this port throws away,
    /// and the symptom would be a Connect button disabled on a console that is paired - which is
    /// exactly the failure PP624 was filed for, arriving from the other side.
    /// </summary>
    public static bool TheClientParsesHexAndRefusesAnyOtherLength(string discoveryManager)
    {
        ArgumentNullException.ThrowIfNull(discoveryManager);

        return discoveryManager.Contains(
                "QByteArray::fromHex(host_id.toUtf8())", StringComparison.Ordinal)
            && discoveryManager.Contains(
                $"data.size() != {Bytes}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the client still writes that identity as bare hexadecimal.
    ///
    /// `HostMAC::ToString` is `toHex()`, which is lower case and carries no separators. This port
    /// shows a person <see cref="Settings.RegisteredHost.MacText"/> - the same bytes with colons -
    /// and the two being different is the whole of what went wrong: one is a key and one is a
    /// caption, and PP13 compared the caption.
    /// </summary>
    public static bool TheClientSpellsItBareHex(string hostHeader)
    {
        ArgumentNullException.ThrowIfNull(hostHeader);

        return hostHeader.Contains(
            "QString ToString() const", StringComparison.Ordinal)
            && hostHeader.Contains(
                "QByteArray((const char *)mac, sizeof(mac)).toHex()", StringComparison.Ordinal);
    }

    /// <summary>
    /// One identity as the key it is stored under: lower case hexadecimal, no separators.
    ///
    /// Answers null rather than a best effort for anything that is not six bytes of hex. A
    /// half-parsed identity would match another half-parsed one, and two consoles agreeing because
    /// neither could be read is worse than neither being recognised.
    /// </summary>
    public static string? Key(string? id)
    {
        if (id is null)
            return null;

        Span<char> kept = stackalloc char[Bytes * 2];
        var length = 0;

        foreach (char c in id)
        {
            if (c is ':' or '-' or ' ')
                continue;

            if (length == kept.Length || !Uri.IsHexDigit(c))
                return null;

            kept[length++] = char.ToLowerInvariant(c);
        }

        return length == kept.Length ? new string(kept) : null;
    }

    /// <summary>
    /// PP626: the bytes behind a key, for the one direction that writes.
    ///
    /// Hiding a console stores its MAC, and what a row carries is the key. Null for anything that is
    /// not an identity, so a store never gains an entry keyed on something unreadable - which would
    /// hide nothing and could not be undone from any screen.
    /// </summary>
    public static byte[]? ToBytes(string? id)
    {
        if (Key(id) is not { } key)
            return null;

        var bytes = new byte[Bytes];
        for (var i = 0; i < Bytes; i++)
            bytes[i] = Convert.ToByte(key.Substring(i * 2, 2), 16);

        return bytes;
    }

    /// <summary>The same key from the bytes the store holds, which is `HostMAC::ToString`.</summary>
    public static string? Key(byte[]? mac)
    {
        if (mac is not { Length: Bytes })
            return null;

        return string.Concat(mac.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Whether a set of keys knows this id, by the key or by the id as it arrived.
    ///
    /// Both, and the exact match is not a courtesy: PP13's own assertions hand Build short strings
    /// as keys - "AA" for a console called Bedroom - and those are about the merge rather than about
    /// identity. A normalisation that refused them would turn a widening into a rewrite.
    /// </summary>
    public static bool Knows(IReadOnlySet<string> keys, string? id)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (id is null)
            return false;

        return keys.Contains(id) || (Key(id) is { } key && keys.Contains(key));
    }
}
