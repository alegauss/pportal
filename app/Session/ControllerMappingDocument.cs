using System.Text;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// One row of the mapping screen: a thing on the PlayStation pad, and what is bound to it.
///
/// The value is the number the Qt client puts in `buttonValue`, and it is a ChiakiControllerButton
/// bit for the sixteen buttons, an analog-button bit for the triggers, and a ControllerButtonExt
/// bit for the four stick axes and the microphone. The number matters because it is what orders
/// the screen - see <see cref="MappingTargets.All"/>.
/// </summary>
public sealed record MappingTarget(int Value, string Name, string Key);

/// <summary>
/// The twenty-three rows the mapping screen shows, in the order it shows them.
///
/// The order is not a layout choice and is not the order the Qt source lists them in either: the
/// backend builds a QMap keyed by the button VALUE, and a QMap iterates sorted by key. So the
/// screen is ordered by the numeric bit, which happens to run Cross, Moon, Box, Pyramid, the d-pad,
/// the shoulders, the sticks-as-buttons, Options, Share, Touchpad, PS, the triggers, then the four
/// stick axes and the microphone last at 1 &lt;&lt; 30.
///
/// A port that listed them in source order would be right for the first eighteen and wrong for the
/// rest, which is the kind of difference nobody notices until a user is looking at both clients.
/// </summary>
public static class MappingTargets
{
    /// <summary>Every row, ascending by value - which is the order the screen draws them in.</summary>
    public static IReadOnlyList<MappingTarget> All { get; } =
    [
        new(1 << 0, "Cross", "a"),
        new(1 << 1, "Moon", "b"),
        new(1 << 2, "Box", "x"),
        new(1 << 3, "Pyramid", "y"),
        new(1 << 4, "D-Pad Left", "dpleft"),
        new(1 << 5, "D-Pad Right", "dpright"),
        new(1 << 6, "D-Pad Up", "dpup"),
        new(1 << 7, "D-Pad Down", "dpdown"),
        new(1 << 8, "L1", "leftshoulder"),
        new(1 << 9, "R1", "rightshoulder"),
        new(1 << 10, "L3", "leftstick"),
        new(1 << 11, "R3", "rightstick"),
        new(1 << 12, "Options", "start"),
        new(1 << 13, "Share", "back"),
        new(1 << 14, "Touchpad", "touchpad"),
        new(1 << 15, "PS", "guide"),
        new(1 << 16, "L2", "lefttrigger"),
        new(1 << 17, "R2", "righttrigger"),

        // ControllerButtonExt, which starts at 1 << 26 so it cannot collide with the above.
        new(1 << 26, "Left Stick X", "leftx"),
        new(1 << 27, "Left Stick Y", "lefty"),
        new(1 << 28, "Right Stick X", "rightx"),
        new(1 << 29, "Right Stick Y", "righty"),
        new(1 << 30, "MIC", "misc1"),
    ];

    /// <summary>The row for a value, or null - the QML only ever passes one of these back.</summary>
    public static MappingTarget? Find(int value) => All.FirstOrDefault(t => t.Value == value);
}

/// <summary>What one row shows: the name, the value bound to it, and the tokens on it.</summary>
public sealed record MappingRow(int Value, string Name, IReadOnlyList<string> Physical);

/// <summary>
/// PP18: the mapping screen's document - the SDL mapping string parsed, edited and written back.
///
/// The screen itself cannot be tested without a pad in the room, which is what its design says and
/// why PP18 waited for the input path. This is the half that can: everything between the token a
/// press produces (PP126 makes those) and the string that goes into settings is arithmetic on a
/// map, and every rule in it is a decision the Qt client already made.
///
/// Four of those rules are worth stating, because each is a place a reasonable port differs:
///
///   a physical control belongs to exactly ONE PlayStation button, and binding it somewhere else
///   moves it rather than copying it. A button it leaves behind with nothing on it is REMOVED from
///   the map, not left as an empty entry - which is what makes the written string shorter than the
///   one it came from;
///
///   a PlayStation button may carry several physical controls, and the index a binding lands at is
///   a request rather than a promise: index 0 prepends, and every other index appends. Assigning to
///   index 2 of a one-entry row puts it at index 1;
///
///   `altered` is a comparison and not a flag. Undo every change by hand and the screen stops
///   offering to save, because the map is equal to the one that was applied;
///
///   and the string is rebuilt in key order, not in the order it was read. SDL does not promise
///   that order, so a round trip of an untouched mapping is not the string it parsed.
///
/// That last one has a consequence the Qt client did not intend - see
/// <see cref="LooksLikeTheOriginal"/>.
/// </summary>
public sealed class ControllerMappingDocument
{
    /// <summary>
    /// The keys in a mapping string that are not controls - metadata SDL carries along. They stay
    /// in the map and are written back out, but nothing may be BOUND to them, so they are kept out
    /// of the physical-control index.
    /// </summary>
    private static readonly string[] NotControls = ["crc", "platform", "type", "hint"];

    private readonly SortedDictionary<string, List<string>> entries =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> physicalToKey = new(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, IReadOnlyList<string>> applied =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    private ControllerMappingDocument(string guid, string controllerType)
    {
        Guid = guid;
        ControllerType = controllerType;
    }

    /// <summary>The controller's GUID - the first field, and the one the rebuilt string drops.</summary>
    public string Guid { get; }

    /// <summary>The controller's name, with `*` resolved to whatever the pad calls itself.</summary>
    public string ControllerType { get; }

    /// <summary>Whether the map differs from the one that was applied.</summary>
    public bool Altered { get; private set; }

    /// <summary>
    /// Parses an SDL mapping string, or returns null for one that is not usable.
    ///
    /// `fallbackType` is what the pad calls itself, used only when the string's own name field is
    /// `*`. An empty one becomes "Unidentified Controller" rather than an empty row heading.
    /// </summary>
    public static ControllerMappingDocument? Parse(string mapping, string fallbackType)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(fallbackType);

        if (mapping.Length == 0)
            return null;

        string[] fields = mapping.Split(',');
        if (fields.Length < 2)
            return null;

        string type = fields[1];
        if (type == "*")
            type = fallbackType.Length == 0 ? "Unidentified Controller" : fallbackType;

        var document = new ControllerMappingDocument(fields[0], type);

        foreach (string field in fields.Skip(2))
        {
            string[] parts = field.Split(':');

            // A field with no colon is skipped, which is how the trailing comma SDL writes -
            // and the empty field it produces - costs nothing.
            if (parts.Length < 2)
                continue;

            string key = parts[0];
            List<string> values = [.. parts.Skip(1)];

            // A repeated key CONCATENATES. SDL emits one for a control with two bindings, and a
            // port that overwrote would silently drop the first of them.
            if (document.entries.TryGetValue(key, out List<string>? existing))
                values = [.. existing, .. values];

            document.entries[key] = values;

            if (IsControl(key))
                foreach (string value in values)
                    document.physicalToKey[value] = key;
        }

        document.applied = document.Snapshot();
        return document;
    }

    /// <summary>The controls bound to a key, in order. Empty for a key nothing is bound to.</summary>
    public IReadOnlyList<string> Physical(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return entries.TryGetValue(key, out List<string>? values) ? values : [];
    }

    /// <summary>
    /// The key a physical control is bound to, or null.
    ///
    /// Empty is a third answer and it is deliberate: a control displaced by another binding is left
    /// in the index pointing at nothing, so that a later binding of it does not try to unbind a key
    /// it no longer sits on.
    /// </summary>
    public string? TargetOf(string physical)
    {
        ArgumentNullException.ThrowIfNull(physical);
        return physicalToKey.TryGetValue(physical, out string? key) ? key : null;
    }

    /// <summary>
    /// Every row the screen shows - all twenty-three of them, whether bound or not, in value order.
    /// </summary>
    public IReadOnlyList<MappingRow> Rows() =>
        [.. MappingTargets.All.Select(t => new MappingRow(t.Value, t.Name, Physical(t.Key)))];

    /// <summary>
    /// Binds a physical control to one of the twenty-three rows, at an index.
    ///
    /// This is the whole edit surface of the screen. Binding a control where it already is does
    /// nothing; binding it elsewhere moves it; and binding onto an occupied index displaces what
    /// was there rather than pushing it along.
    /// </summary>
    public void Assign(int target, string physical, int index)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        MappingTarget row = MappingTargets.Find(target)
            ?? throw new ArgumentOutOfRangeException(nameof(target), target, "not a mapping row");

        string key = row.Key;
        string? previous = TargetOf(physical);

        // Already there. The early return is what stops a re-press of the same control from
        // reordering the row it is already on.
        if (key == previous)
            return;

        if (!string.IsNullOrEmpty(previous))
        {
            List<string> old = entries[previous];
            if (old.Count > 1)
                old.Remove(physical);
            else
                entries.Remove(previous);
        }

        List<string> bound = entries.TryGetValue(key, out List<string>? found) ? found : [];

        // Whatever was at this index is displaced, and is left in the index bound to nothing.
        if (bound.Count > index)
        {
            physicalToKey[bound[index]] = "";
            bound.RemoveAt(index);
        }

        if (index == 0)
            bound.Insert(0, physical);
        else
            bound.Add(physical);

        entries[key] = bound;
        physicalToKey[physical] = key;

        Altered = !SameAsApplied();
    }

    /// <summary>
    /// The string this document writes: the controller name, then every key in key order.
    ///
    /// The GUID is NOT in it. That is the Qt client's shape and not a simplification here, and it
    /// is the reason <see cref="LooksLikeTheOriginal"/> answers the way it does.
    /// </summary>
    public string Serialise()
    {
        var sb = new StringBuilder(ControllerType);
        foreach ((string key, List<string> values) in entries)
            foreach (string value in values)
                sb.Append(',').Append(key).Append(':').Append(value);

        return sb.ToString();
    }

    /// <summary>
    /// The test controllerMappingApply makes to decide whether the user has put the pad back the
    /// way it was - in which case it deletes the stored override instead of writing a new one.
    ///
    /// It is given the ORIGINAL string, which begins with the GUID, and compares it against a
    /// rebuild which begins with the controller name. So it is false for a document that has not
    /// been touched at all, and the branch behind it never runs: a user who undoes every change by
    /// hand gets an override written for a mapping identical to the default, and the pad keeps a
    /// custom mapping until they press reset.
    ///
    /// Reproduced rather than fixed. The port is a port, and this is the behaviour a user's
    /// settings file already has.
    /// </summary>
    public bool LooksLikeTheOriginal(string original)
    {
        ArgumentNullException.ThrowIfNull(original);
        return string.Equals(Serialise(), original, StringComparison.Ordinal);
    }

    private static bool IsControl(string key)
        => !NotControls.Contains(key, StringComparer.Ordinal)
            && !key.StartsWith("sdk", StringComparison.Ordinal);

    private Dictionary<string, IReadOnlyList<string>> Snapshot()
    {
        var copy = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach ((string key, List<string> values) in entries)
            copy[key] = values.ToArray();

        return copy;
    }

    private bool SameAsApplied()
    {
        if (entries.Count != applied.Count)
            return false;

        foreach ((string key, List<string> values) in entries)
        {
            if (!applied.TryGetValue(key, out IReadOnlyList<string>? was))
                return false;
            if (!values.SequenceEqual(was, StringComparer.Ordinal))
                return false;
        }

        return true;
    }
}

/// <summary>
/// PP18: the mapping rules held against qmlbackend.cpp, which is where every one of them is stated.
///
/// The unreachable branch is the reason this exists as more than a formality. A reading of the
/// source is what turned it up, and only a check against the source can say it is still true.
/// </summary>
public static partial class ControllerMappingSource
{
    /// <summary>The Qt client's backend.</summary>
    public const string RelativePath = @"gui\src\qmlbackend.cpp";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether the rebuilt string still starts with the controller name.</summary>
    public static bool RebuildStartsWithTheControllerType(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return RebuildRegex().IsMatch(text);
    }

    /// <summary>Whether the original is still stored whole, GUID and all.</summary>
    public static bool TheOriginalIsStoredWithItsGuid(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return StoredOriginalRegex().IsMatch(text);
    }

    /// <summary>Whether the reset-to-default decision is still those two strings compared.</summary>
    public static bool ResetIsDecidedByComparingThem(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ResetComparisonRegex().IsMatch(text);
    }

    /// <summary>Whether index 0 still prepends where every other index appends.</summary>
    public static bool IndexZeroPrependsAndTheRestAppend(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return PrependRegex().IsMatch(text);
    }

    /// <summary>Whether the metadata keys are still kept out of the physical-control index.</summary>
    public static bool MetadataKeysAreNotControls(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return NotControlsRegex().IsMatch(text);
    }

    /// <summary>Whether a row emptied by a move is still removed rather than left empty.</summary>
    public static bool AnEmptiedRowIsRemoved(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return RemoveEmptiedRegex().IsMatch(text);
    }

    [GeneratedRegex(@"QString new_controller_mapping = controller_mapping_controller_type;")]
    private static partial Regex RebuildRegex();

    [GeneratedRegex(
        @"controller_mapping_original_controller_mappings\.insert\(\s*controller_mapping_controller_guid,\s*original_controller_mapping\s*\)")]
    private static partial Regex StoredOriginalRegex();

    [GeneratedRegex(
        @"new_controller_mapping == controller_mapping_original_controller_mappings\.value\(controller_mapping_controller_guid\)")]
    private static partial Regex ResetComparisonRegex();

    [GeneratedRegex(
        @"if\(new_index == 0\)\s*\r?\n\s*new_mapping_buttons\.prepend\(physical_button\);\s*\r?\n\s*else\s*\r?\n\s*new_mapping_buttons\.append\(physical_button\);")]
    private static partial Regex PrependRegex();

    [GeneratedRegex(
        @"key != ""crc"" && key != ""platform"" && key != ""type"" && key != ""hint"" && !key\.startsWith\(""sdk""\)")]
    private static partial Regex NotControlsRegex();

    [GeneratedRegex(
        @"else\s*\r?\n\s*controller_mapping_controller_mappings\.remove\(old_mapping\);")]
    private static partial Regex RemoveEmptiedRegex();
}
