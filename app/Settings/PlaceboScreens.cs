using System.Collections.ObjectModel;
using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP17: one row of either placebo screen - a label, a slider and the number beside it.
///
/// Every row on both screens has this shape, which is why the screens are an ItemsControl over
/// these rather than forty blocks of markup: the forty differ in their range, their step, their
/// label and their condition, and in nothing else. Writing them out would be forty chances to
/// mistype a bound that PP168 and PP170 already hold in one place.
/// </summary>
public sealed class PlaceboSliderRow : DialogViewModel
{
    private double value;
    private bool visible = true;

    /// <param name="key">The key in the placebo store.</param>
    /// <param name="minimum">The slider's floor.</param>
    /// <param name="maximum">Its ceiling.</param>
    /// <param name="step">Its step, which also decides how the number beside it is printed.</param>
    /// <param name="initial">The value it opens on, which is the store's default.</param>
    public PlaceboSliderRow(string key, double minimum, double maximum, double step, double initial)
    {
        ArgumentNullException.ThrowIfNull(key);

        Key = key;
        Label = PlaceboLabels.For(key);
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        value = initial;
    }

    protected override string ButtonProperty => nameof(Caption);

    /// <summary>The key, which is what a caller writes back to the store.</summary>
    public string Key { get; }

    /// <summary>The label, transcribed from the QML because nothing else states it.</summary>
    public string Label { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Step { get; }

    /// <summary>The value, which the slider moves.</summary>
    public double Value
    {
        get => value;
        set => Set(ref this.value, value);
    }

    /// <summary>Whether the row is drawn, which its section or its function decides.</summary>
    public bool Visible
    {
        get => visible;
        set => Set(ref visible, value);
    }

    /// <summary>The number beside the slider, at the width its step implies.</summary>
    public string Caption => PlaceboCaption.For(Value, Step);
}

/// <summary>
/// PP17: the colour mapping screen.
///
/// Two function combos decide which of the twenty-two rows are drawn, and the rows themselves are
/// <see cref="PlaceboColorMappingOptions"/>' table rather than markup. Changing a function
/// recomputes every row's visibility in one pass - which is the port's answer to the QML repeating
/// each condition on three elements.
/// </summary>
public sealed class PlaceboColorMappingViewModel : DialogViewModel
{
    private readonly ObservableCollection<PlaceboSliderRow> gamut = [];
    private readonly ObservableCollection<PlaceboSliderRow> tone = [];

    private int gamutFunction = PlaceboColorMapping.GamutMapping.DefaultIndex;
    private int toneFunction = PlaceboColorMapping.ToneMapping.DefaultIndex;
    private int toneMetadata = PlaceboColorMapping.ToneMetadata.DefaultIndex;
    private bool inverseToneMapping;
    private bool lut3dTricubic;
    private bool gamutExpansion;

    public PlaceboColorMappingViewModel()
    {
        foreach (PlaceboSlider option in PlaceboColorMappingOptions.Gamut)
            gamut.Add(Row(option));

        foreach (PlaceboSlider option in PlaceboColorMappingOptions.Tone)
            tone.Add(Row(option));

        Refresh();
    }

    protected override string ButtonProperty => nameof(GamutFunction);

    /// <summary>The gamut half's rows, refilled never and re-shown often.</summary>
    public ObservableCollection<PlaceboSliderRow> Gamut => gamut;

    /// <summary>And the tone half's.</summary>
    public ObservableCollection<PlaceboSliderRow> Tone => tone;

    /// <summary>Which gamut mapping function is chosen, as an index into its combo.</summary>
    public int GamutFunction
    {
        get => gamutFunction;
        set { Set(ref gamutFunction, value); Refresh(); }
    }

    /// <summary>And which tone mapping function.</summary>
    public int ToneFunction
    {
        get => toneFunction;
        set { Set(ref toneFunction, value); Refresh(); }
    }

    /// <summary>Which HDR metadata the tone mapping may use.</summary>
    public int ToneMetadata
    {
        get => toneMetadata;
        set => Set(ref toneMetadata, value);
    }

    /// <summary>The switch that is stored as a word.</summary>
    public bool InverseToneMapping
    {
        get => inverseToneMapping;
        set => Set(ref inverseToneMapping, value);
    }

    public bool Lut3dTricubic
    {
        get => lut3dTricubic;
        set => Set(ref lut3dTricubic, value);
    }

    public bool GamutExpansion
    {
        get => gamutExpansion;
        set => Set(ref gamutExpansion, value);
    }

    /// <summary>What the store would hold for the chosen gamut function.</summary>
    public string GamutStored => PlaceboColorMapping.GamutMapping.StoredFor(GamutFunction);

    /// <summary>And for the tone one.</summary>
    public string ToneStored => PlaceboColorMapping.ToneMapping.StoredFor(ToneFunction);

    private static PlaceboSliderRow Row(PlaceboSlider option)
        => new(option.Key, option.Minimum, option.Maximum, option.Step, option.Default);

    private void Refresh()
    {
        for (int i = 0; i < gamut.Count; i++)
            gamut[i].Visible = PlaceboColorMappingOptions.Gamut[i].VisibleFor(GamutFunction);

        for (int i = 0; i < tone.Count; i++)
            tone[i].Visible = PlaceboColorMappingOptions.Tone[i].VisibleFor(ToneFunction);
    }
}

/// <summary>
/// PP17: the renderer tuning screen.
///
/// Five sections, each with a switch and a preset, and a row that belongs to none of them. Every
/// row's condition is the pair its section carries (PP169), so this recomputes them the same way
/// the screen beside it does - in one pass rather than per element.
/// </summary>
public sealed class PlaceboTuningViewModel : DialogViewModel
{
    private readonly ObservableCollection<PlaceboSliderRow> rows = [];
    private readonly Dictionary<PlaceboSection, bool> enabled = new();
    private readonly Dictionary<PlaceboSection, int> presets = new();

    private int upscaler = PlaceboScalers.Upscaler.DefaultIndex;
    private int planeUpscaler = PlaceboScalers.PlaneUpscaler.DefaultIndex;
    private int downscaler = PlaceboScalers.Downscaler.DefaultIndex;
    private int planeDownscaler = PlaceboScalers.PlaneDownscaler.DefaultIndex;
    private int deinterlaceAlgorithm = PlaceboDeinterlaceChoice.Algorithm.DefaultIndex;

    public PlaceboTuningViewModel()
    {
        foreach (PlaceboSection section in Enum.GetValues<PlaceboSection>())
        {
            // The switches' defaults are the store's, which are not all the same - PP171.
            enabled[section] = section switch
            {
                PlaceboSection.Deband => true,
                PlaceboSection.Sigmoid => true,
                PlaceboSection.PeakDetection => true,
                PlaceboSection.ColorAdjustment => true,
                PlaceboSection.ColorMapping => true,
                _ => true,
            };

            presets[section] = PlaceboSectionPresets.CustomIndex;
        }

        foreach (PlaceboTuningSlider option in PlaceboTuningOptions.All)
            rows.Add(new PlaceboSliderRow(
                option.Key, option.Minimum, option.Maximum, option.Step, option.Default));

        Refresh();
    }

    protected override string ButtonProperty => nameof(Upscaler);

    /// <summary>Every row, in the order the screen draws them.</summary>
    public ObservableCollection<PlaceboSliderRow> Rows => rows;

    public int Upscaler
    {
        get => upscaler;
        set => Set(ref upscaler, value);
    }

    public int PlaneUpscaler
    {
        get => planeUpscaler;
        set => Set(ref planeUpscaler, value);
    }

    public int Downscaler
    {
        get => downscaler;
        set => Set(ref downscaler, value);
    }

    public int PlaneDownscaler
    {
        get => planeDownscaler;
        set => Set(ref planeDownscaler, value);
    }

    public int DeinterlaceAlgorithm
    {
        get => deinterlaceAlgorithm;
        set => Set(ref deinterlaceAlgorithm, value);
    }

    /// <summary>Whether a section's switch is on.</summary>
    public bool EnabledFor(PlaceboSection section) => enabled[section];

    /// <summary>Which preset a section is on.</summary>
    public int PresetFor(PlaceboSection section) => presets[section];

    /// <summary>Turns a section's switch, and re-shows the rows behind it.</summary>
    public void SetEnabled(PlaceboSection section, bool value)
    {
        enabled[section] = value;
        Refresh();
    }

    /// <summary>Chooses a section's preset, which hides its rows unless it is Custom.</summary>
    public void SetPreset(PlaceboSection section, int index)
    {
        presets[section] = index;
        Refresh();
    }

    private void Refresh()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            PlaceboTuningSlider option = PlaceboTuningOptions.All[i];
            rows[i].Visible = option.VisibleFor(enabled[option.Section], presets[option.Section]);
        }
    }
}
