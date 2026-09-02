using System.Text;
using Microsoft.Win32;

namespace ChiakiNg.Settings;

/// <summary>
/// PP626: how a value goes back into the store <see cref="QSettingsValue"/> reads.
///
/// This port has read the Qt client's settings since PP2 and has never written one. The two
/// removals `ConsoleActions` models - Delete a manual console, Hide a discovered one - are writes,
/// and there was nowhere to make them.
///
/// EVERY RULE HERE IS THE INVERSE OF A RULE ALREADY WRITTEN DOWN, and none is invented. It matters
/// that they are exact: this is the store the Qt client reads, holding somebody's real
/// registrations, and a value written in a spelling Qt does not recognise is a console list that
/// comes back wrong on their next launch rather than an error anybody sees.
///
///   a QByteArray is `@ByteArray(` + one Latin-1 char per byte + `)` (rule 1), and a payload
///   containing a NUL cannot be a REG_SZ - so it is written as REG_BINARY holding the UTF-16LE
///   bytes of that same text (rule 3);
///
///   a plain string beginning with `@` is escaped by doubling it, because `@` is the escape for
///   the whole scheme (rule 4);
///
///   an int is a REG_DWORD (rule 7).
///
/// The round trip is the assertion rather than the encoding: what this writes,
/// <see cref="QSettingsValue"/> reads back as what was handed in.
/// </summary>
public static class QSettingsWriter
{
    private const string ByteArrayPrefix = "@ByteArray(";

    /// <summary>The text a byte array becomes - rules 1 and 2.</summary>
    public static string ByteArrayText(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return ByteArrayPrefix + Encoding.Latin1.GetString(value) + ")";
    }

    /// <summary>
    /// The text a plain string becomes - rule 4.
    ///
    /// Only a leading `@` is doubled, because only the first character starts the escape. A
    /// nickname with an `@` in the middle is written as it was typed, which is what Qt does.
    /// </summary>
    public static string StringText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.StartsWith('@') ? "@" + value : value;
    }

    /// <summary>
    /// Writes a byte array, choosing the type by whether the payload can survive a REG_SZ.
    ///
    /// A NUL in the middle of a REG_SZ ends it, so a key that contains one - a registration key is
    /// eight characters and eight NULs - has to go through the binary form. The choice is made from
    /// the payload rather than from the caller, because a caller that guessed wrong would write a
    /// value that reads back short and says nothing.
    /// </summary>
    public static void WriteByteArray(RegistryKey key, string name, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        string text = ByteArrayText(value);

        if (Array.IndexOf(value, (byte)0) < 0)
        {
            key.SetValue(name, text, RegistryValueKind.String);
            return;
        }

        // Rule 3. Trailing NULs are QSettings' own padding on the way out; what is written is the
        // text itself, UTF-16LE, with nothing added.
        key.SetValue(name, Encoding.Unicode.GetBytes(text), RegistryValueKind.Binary);
    }

    /// <summary>Writes a plain string, escaped.</summary>
    public static void WriteString(RegistryKey key, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(key);

        key.SetValue(name, StringText(value), RegistryValueKind.String);
    }

    /// <summary>Writes an int - rule 7's type, from the side that has the whole 32 bits.</summary>
    public static void WriteInt(RegistryKey key, string name, int value)
    {
        ArgumentNullException.ThrowIfNull(key);

        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Replaces a QSettings array whole: `size` beside numbered subkeys from 1.
    ///
    /// REPLACED AND NOT EDITED, and that is the part that has to be right. QSettings reads entries
    /// 1..size, so removing the middle one of three by deleting its subkey leaves a size of three
    /// and a hole - and the reader here trusts size only as far as a subkey exists, while Qt's own
    /// does not. Every remaining entry is renumbered from 1 and the leftovers are deleted, which is
    /// what Qt's own rewrite of an array does.
    ///
    /// An array that ends up empty keeps its key with a size of 0 rather than being deleted: the
    /// difference between "no consoles are hidden" and "this store has never hidden one" is not a
    /// distinction any reader here makes, and leaving the key is the smaller change to somebody
    /// else's store.
    /// </summary>
    public static void ReplaceArray(
        RegistryKey parent, string arrayName, IReadOnlyList<Action<RegistryKey>> entries)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(entries);

        using RegistryKey array = parent.CreateSubKey(arrayName, writable: true);

        int before = QSettingsValue.AsInt(array.GetValue("size")) ?? 0;

        for (var i = 0; i < entries.Count; i++)
        {
            using RegistryKey entry = array.CreateSubKey(
                (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), writable: true);

            // Cleared before it is written: an entry that used to carry a field the new one does
            // not would otherwise keep the old value, and a half-updated console is worse than a
            // missing one.
            foreach (string stale in entry.GetValueNames())
                entry.DeleteValue(stale, throwOnMissingValue: false);

            entries[i](entry);
        }

        for (int i = entries.Count + 1; i <= before; i++)
        {
            array.DeleteSubKeyTree(
                i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                throwOnMissingSubKey: false);
        }

        WriteInt(array, "size", entries.Count);
    }
}
