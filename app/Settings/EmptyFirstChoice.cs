namespace ChiakiNg.Settings;

/// <summary>
/// PP16: a runtime-built list whose FIRST entry stores the empty string.
///
/// The settings screen has three of these - the hardware decoder on the Video tab, and the audio
/// output and input devices on the Audio tab - and they share a shape that is wrong in the same way
/// twice if written twice:
///
///   the list is `[something]` concatenated with whatever the machine reports, so its contents are
///   not knowable ahead of time;
///
///   `onActivated: x = index ? model[index] : ""` - picking the first entry stores the EMPTY STRING
///   and not the word the list shows. The empty string is what "let the system choose" is spelled as
///   downstream, so storing the label instead hands a device name nothing will match;
///
///   and `currentIndex: Math.max(0, model.indexOf(x))` - anything the list does not hold shows as
///   the first entry. Which is lenient in the opposite direction: a device that was present last run
///   and is not now reads as "auto", and since the first entry stores the empty string, opening the
///   tab and touching nothing else can rewrite the setting.
///
/// One type, because PP93 is what two copies of one answer turns into.
/// </summary>
public static class EmptyFirstChoice
{
    /// <summary>What selecting the first entry stores, whatever that entry is called.</summary>
    public const string Stored = "";

    /// <summary>The list as the QML builds it: a leading label, then what the machine reported.</summary>
    public static IReadOnlyList<string> Build(string firstLabel, IEnumerable<string> available)
    {
        ArgumentNullException.ThrowIfNull(firstLabel);
        ArgumentNullException.ThrowIfNull(available);

        var list = new List<string> { firstLabel };
        list.AddRange(available);
        return list;
    }

    /// <summary>What the store receives for a chosen index. Index 0, and anything out of range, is empty.</summary>
    public static string StoredFor(IReadOnlyList<string> list, int index)
    {
        ArgumentNullException.ThrowIfNull(list);
        return index <= 0 || index >= list.Count ? Stored : list[index];
    }

    /// <summary>
    /// The index a stored value shows at: its position, or ZERO for anything the list does not hold.
    /// Zero rather than a default index, which is what makes an absent device read as the first
    /// entry.
    /// </summary>
    public static int IndexOf(IReadOnlyList<string> list, string? stored)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (string.IsNullOrEmpty(stored))
            return 0;

        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], stored, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }

    /// <summary>Whether a stored value means "let the system choose", which is emptiness and nothing else.</summary>
    public static bool MeansAutomatic(string? stored) => string.IsNullOrEmpty(stored);
}
