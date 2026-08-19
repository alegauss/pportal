namespace ChiakiNg.Settings;

/// <summary>
/// PP16: a combo whose INDEX is the property and whose STRING is what the store holds.
///
/// The settings screen binds `currentIndex: Chiaki.settings.someProperty` and assigns the index
/// straight back, so the property is an int and the port would reasonably store an int. For several
/// of these the Qt client does not: it writes a string out of a QMap keyed by the C++ enum, and
/// reads it back through `QMap::key(v, default)`.
///
/// A port that stored the index writes 2 where the Qt client writes "ask"; the Qt client then finds
/// no key for "2", falls back to its default, and the user's choice is gone. Nothing throws,
/// nothing logs, and the two clients share one settings file - so the symptom is a preference that
/// resets when the other client is opened, which is not a symptom anybody traces back to a screen.
///
/// One type for every such combo, rather than one per tab. PP93 is the lesson: two files holding
/// the same answer is how a third one gets written.
///
/// The stored strings are NOT the labels, and on the Video tab three of six differ - the combo says
/// "Stream Resolution" where the store holds "Selected Resolution". So the two lists are separate
/// parameters and neither is derived from the other.
///
/// The fallback is copied too. An unknown string is the default, not an error: a settings file
/// written by a newer version, or edited by hand, must leave the screen usable.
/// </summary>
public sealed class StoredChoice
{
    private readonly string[] stored;

    /// <param name="key">The preference the choice is stored under.</param>
    /// <param name="labels">What the combo shows, in the QML's order - the index IS the enum value.</param>
    /// <param name="stored">What the store holds for each of those, in the same order.</param>
    /// <param name="defaultIndex">
    /// The index taken when the store holds nothing or holds something unrecognised. Not always 0:
    /// Action On Disconnect defaults to its LAST choice and Window Type to its fourth.
    /// </param>
    public StoredChoice(string key, IReadOnlyList<string> labels, string[] stored, int defaultIndex)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(stored);

        if (labels.Count != stored.Length)
        {
            throw new ArgumentException(
                $"{key}: {labels.Count} labels against {stored.Length} stored values - the index is "
                + "the enum value, so the two lists are the same length or one of them is wrong.",
                nameof(stored));
        }

        Key = key;
        Labels = labels;
        this.stored = stored;
        DefaultIndex = defaultIndex;
    }

    /// <summary>The preference this choice is stored under.</summary>
    public string Key { get; }

    /// <summary>What the combo shows, in the order the QML declares it.</summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>The index taken when the store holds nothing, or holds something unrecognised.</summary>
    public int DefaultIndex { get; }

    /// <summary>What the store holds for an index. Out-of-range takes the default's string.</summary>
    public string StoredFor(int index)
        => stored[index >= 0 && index < stored.Length ? index : DefaultIndex];

    /// <summary>
    /// The index for a stored string, or the default index where it is not one of them - which is
    /// `QMap::key(v, default)` and includes the case of a key never written.
    /// </summary>
    public int IndexOf(string? storedValue)
    {
        if (storedValue is null)
            return DefaultIndex;

        int found = Array.IndexOf(stored, storedValue);
        return found < 0 ? DefaultIndex : found;
    }

    /// <summary>Whether a string is one this choice recognises. States the fallback, does not gate it.</summary>
    public bool Recognises(string? storedValue)
        => storedValue is not null && Array.IndexOf(stored, storedValue) >= 0;

    /// <summary>
    /// Whether the label and the stored string differ at an index.
    ///
    /// Asserted rather than assumed: where they agree, a port that stored the label works by luck,
    /// and the Video tab is where that luck runs out on three of six rows.
    /// </summary>
    public bool LabelDiffersFromStored(int index)
        => !string.Equals(Labels[index], StoredFor(index), StringComparison.Ordinal);
}
