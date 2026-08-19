using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: what the port calls each preference, held against the vocabulary the QML already uses.
///
/// A screen and its settings are the same application, so two spellings for one preference is a
/// support question nobody can answer. These assertions are what stop the port inventing one.
/// </summary>
public class PreferenceNameTests
{
    private static IReadOnlySet<string>? QmlNames()
    {
        IReadOnlyList<string>? files = PreferenceNames.LocateQml();
        return files is null
            ? null
            : PreferenceNames.NamesUsedInQml(files.Select(File.ReadAllText));
    }

    /// <summary>
    /// The acronym rule is not cosmetic: bitrateLocalPs4 and bitrateLocalPS4 are different
    /// property names, and a binding to the wrong one silently reads nothing.
    /// </summary>
    [Theory]
    [InlineData("settings/audio_volume", "audioVolume")]
    [InlineData("settings/bitrate_local_ps4", "bitrateLocalPS4")]
    [InlineData("settings/codec_remote_ps5", "codecRemotePS5")]
    [InlineData("settings/dpad_touch_increment", "dpadTouchIncrement")]
    public void TheConventionIsCamelCaseWithTheConsoleUppercased(string key, string expected)
        => Assert.Equal(expected, PreferenceNames.Transliterate(key));

    /// <summary>
    /// Sixty-five preferences have NO property. Every one in the Placebo scope is read and written
    /// from C++ and never reaches QML, so a port generating one property per row would produce
    /// sixty-five nobody binds to.
    /// </summary>
    [Fact]
    public void ThePlaceboScopeHasNoPropertySurface()
    {
        var placebo = Preferences.All.Values
            .Where(p => p.Scope == QSettingsScope.Placebo)
            .ToList();

        Assert.Equal(65, placebo.Count);
        Assert.All(placebo, p => Assert.Null(PreferenceNames.For(p)));
    }

    /// <summary>And nothing outside that scope is nameless, so the rule is about the scope.</summary>
    [Fact]
    public void EveryOtherPreferenceHasAName()
    {
        Assert.All(
            Preferences.All.Values.Where(p => p.Scope != QSettingsScope.Placebo),
            p => Assert.NotNull(PreferenceNames.For(p)));
    }

    /// <summary>
    /// The three that are renamed rather than transliterated, and the third is the one worth
    /// pausing on: the key says LENGTH and the screen says HEIGHT. A port trusting either alone
    /// writes to a key the Qt client does not read.
    /// </summary>
    [Theory]
    [InlineData("settings/buttons_by_pos", "buttonsByPosition")]
    [InlineData("settings/hw_decoder", "decoder")]
    [InlineData("settings/custom_resolution_length", "customResolutionHeight")]
    public void ThreePreferencesAreRenamedRatherThanTransliterated(string key, string name)
    {
        PreferenceKey preference = Preferences.Find(key)!;

        Assert.Equal(name, PreferenceNames.For(preference));
        Assert.NotEqual(PreferenceNames.Transliterate(key), name);
    }

    /// <summary>
    /// Every renamed name is one the QML actually uses. Without this the exception list is three
    /// strings somebody typed, which is the thing it exists to replace.
    /// </summary>
    [Fact]
    public void EveryRenameIsANameTheQmlUses()
    {
        IReadOnlySet<string>? names = QmlNames();
        if (names is null)
            return;

        Assert.All(PreferenceNames.Renamed.Values, n => Assert.Contains(n, names));
    }

    /// <summary>
    /// And the transliterated names that the QML uses really do come out of the convention -
    /// sixty-three of them, which is what makes the convention worth having rather than a fourth
    /// exception waiting to be found.
    /// </summary>
    [Fact]
    public void TheConventionCoversTheNamesTheQmlUses()
    {
        IReadOnlySet<string>? names = QmlNames();
        if (names is null)
            return;

        int matched = Preferences.All.Values
            .Where(p => p.Scope != QSettingsScope.Placebo)
            .Select(p => PreferenceNames.Transliterate(p.Key))
            .Count(names.Contains);

        Assert.Equal(63, matched);
    }

    /// <summary>
    /// The QML reads more names than there are preferences: 162 against 148, because some are
    /// computed or are actions - availableDecoders, deleteProfile, exportSettings. Recorded so
    /// the difference is a known shape rather than a discrepancy someone re-derives.
    /// </summary>
    [Fact]
    public void TheQmlReadsMoreNamesThanThereArePreferences()
    {
        IReadOnlySet<string>? names = QmlNames();
        if (names is null)
            return;

        Assert.True(names.Count > Preferences.All.Count,
            $"{names.Count} names against {Preferences.All.Count} preferences");

        // Three that are certainly not stored preferences, as evidence of the shape.
        Assert.Contains("availableDecoders", names);
        Assert.Contains("deleteProfile", names);
        Assert.Contains("exportSettings", names);
    }
}
