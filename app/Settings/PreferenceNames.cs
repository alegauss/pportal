using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP16: what the port calls each preference, which PP2 deferred on purpose.
///
/// The table in <see cref="Preferences"/> holds keys, kinds, scopes and defaults, and deliberately
/// no names - "inventing 148 property names here would take that decision by accident and make
/// PP16 a rename". This is the decision, and it is taken by reading the vocabulary the QML already
/// uses rather than by choosing one: a screen and its settings are the same application, and two
/// spellings for one preference is a support question nobody can answer.
///
/// Reading it turned up three things a mechanical derivation would have got wrong.
///
/// The convention is camelCase of the key's tail, EXCEPT that ps4 and ps5 are uppercased -
/// settings/bitrate_local_ps4 is bitrateLocalPS4, not bitrateLocalPs4. Sixty-three of the 148
/// follow it exactly.
///
/// Sixty-five have NO property at all: every preference in the Placebo scope is read and written
/// from C++ and never reaches QML. So the settings screen is not a view onto 148 properties, and a
/// port that generated one property per row would produce sixty-five nobody binds to.
///
/// And three are renamed rather than transliterated, which is the half that cannot be derived:
/// buttons_by_pos is buttonsByPosition, hw_decoder is decoder, and custom_resolution_LENGTH is
/// customResolutionHEIGHT - the stored key and the screen disagree about what the dimension is
/// called, and only one of those names is in the file a user's settings live in.
/// </summary>
public static partial class PreferenceNames
{
    /// <summary>
    /// The names the QML uses that are not a transliteration of their key.
    ///
    /// Explicit because they cannot be derived. Each is a decision someone made in the Qt client,
    /// and the port inherits it rather than re-deriving a name that would be reasonable and wrong.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Renamed { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["settings/buttons_by_pos"] = "buttonsByPosition",
            ["settings/hw_decoder"] = "decoder",

            // The one worth pausing on. The key says length and the screen says height, so a port
            // that trusted either alone writes to a key the Qt client does not read.
            ["settings/custom_resolution_length"] = "customResolutionHeight",
        };

    /// <summary>
    /// The property name for a key, or null where the preference has none.
    ///
    /// Null is a real answer here and not a failure: the Placebo scope has no property surface,
    /// and a caller that treated null as "derive one" would invent sixty-five names.
    /// </summary>
    public static string? For(PreferenceKey preference)
    {
        ArgumentNullException.ThrowIfNull(preference);

        if (Renamed.TryGetValue(preference.Key, out string? renamed))
            return renamed;

        return preference.Scope == QSettingsScope.Placebo ? null : Transliterate(preference.Key);
    }

    /// <summary>
    /// The mechanical half: camelCase of the key's tail, with ps4 and ps5 uppercased.
    ///
    /// The acronym rule is not cosmetic - bitrateLocalPs4 and bitrateLocalPS4 are different
    /// property names, and a binding to the wrong one silently reads nothing.
    /// </summary>
    public static string Transliterate(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        string tail = key.Contains('/') ? key[(key.IndexOf('/') + 1)..] : key;
        string[] parts = tail.Split('_');

        string name = parts[0] + string.Concat(parts.Skip(1).Select(Capitalise));
        return name.Replace("Ps4", "PS4", StringComparison.Ordinal)
            .Replace("Ps5", "PS5", StringComparison.Ordinal);
    }

    private static string Capitalise(string part)
        => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..];

    /// <summary>Every name the QML reads off Chiaki.settings, across all its files.</summary>
    public static IReadOnlySet<string> NamesUsedInQml(IEnumerable<string> qmlTexts)
    {
        ArgumentNullException.ThrowIfNull(qmlTexts);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string text in qmlTexts)
            foreach (Match m in SettingsRegex().Matches(text))
                names.Add(m.Groups[1].Value);

        return names;
    }

    /// <summary>Every QML file the screens live in, or null outside a checkout.</summary>
    public static IReadOnlyList<string>? LocateQml()
    {
        string? one = SanitizerSource.LocateRelative(Path.Combine("gui", "src", "qml", "SettingsDialog.qml"));
        return one is null ? null : Directory.GetFiles(Path.GetDirectoryName(one)!, "*.qml");
    }

    [GeneratedRegex(@"Chiaki\.settings\.([a-zA-Z0-9_]+)")]
    private static partial Regex SettingsRegex();
}
