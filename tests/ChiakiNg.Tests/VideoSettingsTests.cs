using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Video tab, and the two rows where a port that guessed would be wrong.
/// </summary>
public class VideoSettingsTests
{
    /// <summary>
    /// Half the window types store a word the combo does not show. This is the assertion the whole
    /// tab is worth taking for: a port deriving the stored value from the label gets three of six.
    /// </summary>
    [Theory]
    [InlineData(0, "Stream Resolution", "Selected Resolution")]
    [InlineData(1, "Custom Resolution", "Custom Resolution")]
    [InlineData(2, "Adjust Resolution Manually", "Adjust Manually")]
    [InlineData(3, "Fullscreen", "Fullscreen")]
    [InlineData(4, "Zoom [adjust zoom using slider in stream menu]", "Zoom")]
    [InlineData(5, "Stretch", "Stretch")]
    public void TheWindowTypeStoresWordsThatAreNotItsLabels(int index, string label, string stored)
    {
        Assert.Equal(label, WindowTypeChoice.Window.Labels[index]);
        Assert.Equal(stored, WindowTypeChoice.Window.StoredFor(index));
        Assert.Equal(index, WindowTypeChoice.Window.IndexOf(stored));
    }

    /// <summary>Exactly three of the six differ, which is the count that makes it a finding.</summary>
    [Fact]
    public void ThreeOfTheSixDiffer()
    {
        int differing = Enumerable.Range(0, WindowTypeChoice.Window.Labels.Count)
            .Count(WindowTypeChoice.Window.LabelDiffersFromStored);

        Assert.Equal(3, differing);
    }

    /// <summary>
    /// And the default is the FOURTH choice, so the three rows a label-deriving port gets wrong
    /// fall back to exactly the value a fresh install has - which is why it looks like the setting
    /// not sticking rather than like a bug.
    /// </summary>
    [Fact]
    public void TheDefaultIsFullscreenAndIsWhereAWrongValueLands()
    {
        Assert.Equal(3, WindowTypeChoice.Window.DefaultIndex);
        Assert.Equal("Fullscreen", WindowTypeChoice.Window.StoredFor(WindowTypeChoice.Window.DefaultIndex));
        Assert.Equal("Fullscreen", Preferences.Find(WindowTypeChoice.Window.Key)!.Default);

        // A label stored instead of the value lands on the default, silently.
        Assert.Equal(
            WindowTypeChoice.Window.DefaultIndex,
            WindowTypeChoice.Window.IndexOf("Stream Resolution"));
        Assert.False(WindowTypeChoice.Window.Recognises("Adjust Resolution Manually"));
    }

    /// <summary>The list is "none", the available device types, then "auto" - in that order.</summary>
    [Fact]
    public void TheDecoderListIsNoneThenWhatIsAvailableThenAuto()
    {
        IReadOnlyList<string> list = DecoderChoice.Available(
            ["cuda", "vaapi", "d3d11va", "videotoolbox", "vulkan"]);

        // vaapi and videotoolbox are not on the allow-list, so they do not appear even though
        // ffmpeg reported them.
        Assert.Equal(new[] { "none", "cuda", "d3d11va", "vulkan", "auto" }, list);
        Assert.Equal(new[] { "none", "auto" }, DecoderChoice.Available([]));
    }

    /// <summary>
    /// The finding: selecting the first entry stores the EMPTY STRING, not the word "none" the
    /// list shows. And empty is not a placeholder - it is precisely how "no hardware decoder"
    /// reaches ffmpeg, because streamsession.cpp tests isEmpty() and nothing else.
    /// </summary>
    [Fact]
    public void TheFirstDecoderStoresTheEmptyStringAndNotItsOwnName()
    {
        IReadOnlyList<string> list = DecoderChoice.Available(["vulkan"]);

        Assert.Equal("none", list[0]);
        Assert.Equal("", DecoderChoice.StoredFor(list, 0));
        Assert.NotEqual(list[0], DecoderChoice.StoredFor(list, 0));

        Assert.True(DecoderChoice.MeansNoHardwareDecoder(""));
        Assert.True(DecoderChoice.MeansNoHardwareDecoder(null));

        // The trap in full: the word the list shows does NOT mean no decoder downstream.
        Assert.False(DecoderChoice.MeansNoHardwareDecoder("none"));
    }

    /// <summary>The other entries store themselves, which is what makes the first one easy to miss.</summary>
    [Theory]
    [InlineData(1, "vulkan")]
    [InlineData(2, "d3d11va")]
    [InlineData(3, "auto")]
    public void EveryOtherDecoderStoresItsOwnName(int index, string stored)
    {
        IReadOnlyList<string> list = DecoderChoice.Available(["vulkan", "d3d11va"]);
        Assert.Equal(stored, DecoderChoice.StoredFor(list, index));
    }

    /// <summary>
    /// Reading is lenient the other way: anything the list does not hold shows as index 0. So a
    /// decoder that was available last run and is not now silently reads as "none" - and index 0
    /// stores the empty string, so opening the tab and touching nothing else can turn "auto" into
    /// no hardware decoder at all.
    /// </summary>
    [Fact]
    public void AnUnavailableDecoderReadsAsTheFirstEntry()
    {
        IReadOnlyList<string> withCuda = DecoderChoice.Available(["cuda"]);
        IReadOnlyList<string> without = DecoderChoice.Available([]);

        Assert.Equal(1, DecoderChoice.IndexOf(withCuda, "cuda"));
        Assert.Equal(0, DecoderChoice.IndexOf(without, "cuda"));
        Assert.Equal(0, DecoderChoice.IndexOf(without, ""));

        // Which is the whole round trip that loses the setting.
        Assert.Equal("", DecoderChoice.StoredFor(without, DecoderChoice.IndexOf(without, "cuda")));

        // "auto" survives, because it is always in the list.
        Assert.Equal("auto", DecoderChoice.StoredFor(without, DecoderChoice.IndexOf(without, "auto")));
    }

    /// <summary>A tab with no store shows the Qt defaults.</summary>
    [Fact]
    public void AnEmptyStoreGivesTheQtDefaults()
    {
        var model = new VideoSettingsViewModel(new FakePreferences(), ["vulkan"]);

        Assert.Equal(3, model.WindowIndex);
        Assert.Equal("Fullscreen", model.WindowStored);
        Assert.False(model.CustomResolutionVisible);
        Assert.Equal("auto", model.DecoderStored);
        Assert.True(model.ZeroCopy);
        Assert.False(model.FullscreenDoubleClick);
        Assert.True(model.HideCursor);
        Assert.False(model.VSync);
        Assert.Equal(1920, model.Width.Value);
        Assert.Equal(1080, model.Height.Value);
    }

    /// <summary>The resolution fields appear for one window type and no other.</summary>
    [Fact]
    public void TheResolutionFieldsBelongToOneWindowType()
    {
        var model = new VideoSettingsViewModel();

        for (int i = 0; i < WindowTypeChoice.Window.Labels.Count; i++)
        {
            model.WindowIndex = i;
            Assert.Equal(i == WindowTypeChoice.CustomResolution, model.CustomResolutionVisible);
        }
    }

    /// <summary>
    /// The height is stored under custom_resolution_LENGTH - PP142's third rename, and the only
    /// place the store and the screen disagree about what the dimension is called.
    /// </summary>
    [Fact]
    public void TheHeightIsStoredUnderLength()
    {
        Assert.Equal("settings/custom_resolution_length", VideoSettingsViewModel.HeightKey);
        Assert.Equal("customResolutionHeight",
            PreferenceNames.For(Preferences.Find(VideoSettingsViewModel.HeightKey)!));
    }

    /// <summary>
    /// Vertical Sync is the only control on the screen that can end the process, and only on one
    /// renderer. Asserted through the runtime backend rather than the stored preference, because
    /// that is what the QML compares.
    /// </summary>
    [Theory]
    [InlineData("vulkan", false)]
    [InlineData("opengl", true)]
    [InlineData("", false)]
    [InlineData("d3d11", false)]
    public void VSyncNeedsARestartOnOneBackendOnly(string backend, bool restart)
    {
        var model = new VideoSettingsViewModel { RenderBackend = backend };
        Assert.Equal(restart, model.VSyncNeedsRestart);
        Assert.Equal(VideoSettingsViewModel.OpenGlBackend == 1, true);
    }

    /// <summary>Every rule above is still the Qt client's.</summary>
    [Fact]
    public void TheRulesAreStillTheQtClients()
    {
        string? qml = GeneralSettingsSource.LocateQml();
        string? qmlSettings = VideoSettingsSource.Locate(VideoSettingsSource.QmlSettingsCpp);
        string? streamSession = VideoSettingsSource.Locate(VideoSettingsSource.StreamSessionCpp);
        if (qml is null || qmlSettings is null || streamSession is null)
            return;

        string qmlText = File.ReadAllText(qml);
        string settingsText = File.ReadAllText(qmlSettings);
        string sessionText = File.ReadAllText(streamSession);

        Assert.True(GeneralSettingsSource.ComboOffers(qmlText, WindowTypeChoice.Window.Labels),
            "window type labels");

        Assert.True(VideoSettingsSource.FirstDecoderStoresEmpty(qmlText), "first decoder stores empty");
        Assert.True(VideoSettingsSource.UnknownDecoderShowsAsFirst(qmlText), "unknown shows as first");
        Assert.True(VideoSettingsSource.EmptyDecoderMeansNull(sessionText), "empty means NULL");
        Assert.True(VideoSettingsSource.DecoderAllowListIs(settingsText, DecoderChoice.Allowed),
            "decoder allow-list");

        // Four labels and two fields, all gated on the same window type.
        Assert.Equal(6, VideoSettingsSource.CustomResolutionVisibilityCount(qmlText));

        Assert.True(VideoSettingsSource.VSyncStillRestartsTheApplication(qmlText), "vsync restarts");
        Assert.True(VideoSettingsSource.RestartLaunchesADetachedCopy(settingsText),
            "restartApplication launches a copy");
    }

    /// <summary>
    /// And the window type's stored strings are still settings.cpp's, entry by entry - the half
    /// that lives in C++ rather than in the markup.
    /// </summary>
    [Fact]
    public void TheStoredWordsAreStillSettingsCpps()
    {
        string? cpp = GeneralSettingsSource.Locate(GeneralSettingsSource.SettingsCpp);
        string? header = GeneralSettingsSource.Locate(GeneralSettingsSource.SettingsHeader);
        if (cpp is null || header is null)
            return;

        string cppText = File.ReadAllText(cpp);
        Assert.True(GeneralSettingsSource.StoredAsStrings(
            cppText, WindowTypeChoice.Window, "window_type_values"), "window_type stored as strings");

        for (int i = 0; i < WindowTypeChoice.Window.Labels.Count; i++)
        {
            Assert.Contains(
                $"\"{WindowTypeChoice.Window.StoredFor(i)}\"",
                cppText);
        }

        Assert.True(GeneralSettingsSource.EnumOrderIs(
            File.ReadAllText(header), "WindowType",
            "SelectedResolution", "CustomResolution", "AdjustableResolution",
            "Fullscreen", "Zoom", "Stretch"), "WindowType order");
    }
}
