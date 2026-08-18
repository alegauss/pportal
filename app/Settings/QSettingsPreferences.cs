using Microsoft.Win32;

namespace ChiakiNg.Settings;

/// <summary>How a preference is spelled in the store. Maps 1:1 onto QVariant's to* conversions.</summary>
public enum QSettingsKind { String, Bool, Int, UInt, Double, Rect, ByteArray }

/// <summary>
/// Which of the three stores a preference lives in. Not a detail: the same key name means a
/// different value in two of them, and PP79 is what reading the wrong one costs.
/// </summary>
public enum QSettingsScope { Default, Profile, Placebo }

/// <summary>One preference as the Qt client declares it, key and kind and the default it reads.</summary>
public sealed record PreferenceKey(string Key, QSettingsKind Kind, QSettingsScope Scope, object? Default);

/// <summary>
/// PP2's remaining half: every preference the Qt client reads, with the store it lives in, the
/// conversion it takes and the default it falls back to.
///
/// A table and not 148 properties, deliberately. What the .NET side calls each of these is PP16's
/// decision and it has not been taken; inventing 148 property names here would take it by
/// accident and make PP16 a rename. What cannot wait is the part that is silently wrong when it
/// is wrong - the default a key falls back to, and which of the three stores it comes out of -
/// so that is what is written down, once, in the order Settings declares it.
///
/// Every row was extracted from gui/src/settings.cpp and gui/include/settings.h rather than
/// typed: the key, the .to*() that follows it, and the default expression, with the enum tables
/// (codecs, resolutions, the placebo presets) resolved to the literal they hold. A default that
/// disagrees with Qt's is a preference that reads differently in the two clients on a store where
/// the user never touched it, which is the failure this file exists to prevent and the one no
/// screen would report.
///
/// Two rows carry a default the call site does not: settings/packet_loss_max and
/// settings/packet_loss_reported_max are read with no default at all, because
/// GetPacketLossReportedMax falls through one to the other and ends at 0.05f. That chain is the
/// caller's, and 0.05 is what it lands on.
/// </summary>
public static class Preferences
{
    private static PreferenceKey Declare(string key, QSettingsKind kind, QSettingsScope scope, object? dflt)
        => new(key, kind, scope, dflt);

    private static readonly PreferenceKey[] declared =
    {
        // --- Default scope, 2 keys ---
        Declare("settings/current_profile", QSettingsKind.String, QSettingsScope.Default, null),
        Declare("settings/profile_name", QSettingsKind.String, QSettingsScope.Default, null),

        // --- Profile scope, 81 keys ---
        Declare("settings/add_steam_shortcut_ask", QSettingsKind.Bool, QSettingsScope.Profile, true),
        Declare("settings/allow_joystick_background_events", QSettingsKind.Bool, QSettingsScope.Profile, true),
        Declare("settings/audio_buffer_size", QSettingsKind.UInt, QSettingsScope.Profile, 0u),
        Declare("settings/audio_in_device", QSettingsKind.String, QSettingsScope.Profile, null),
        Declare("settings/audio_out_device", QSettingsKind.String, QSettingsScope.Profile, null),
        Declare("settings/audio_video_disabled", QSettingsKind.Int, QSettingsScope.Profile, 0),
        Declare("settings/audio_volume", QSettingsKind.Int, QSettingsScope.Profile, 128),
        Declare("settings/auto_connect_mac", QSettingsKind.ByteArray, QSettingsScope.Profile, null),
        Declare("settings/auto_discovery", QSettingsKind.Bool, QSettingsScope.Profile, true),
        Declare("settings/automatic_connect", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/bitrate_local_ps4", QSettingsKind.UInt, QSettingsScope.Profile, 0u),
        Declare("settings/bitrate_local_ps5", QSettingsKind.UInt, QSettingsScope.Profile, 0u),
        Declare("settings/bitrate_remote_ps4", QSettingsKind.UInt, QSettingsScope.Profile, 0u),
        Declare("settings/bitrate_remote_ps5", QSettingsKind.UInt, QSettingsScope.Profile, 0u),
        Declare("settings/buttons_by_pos", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/codec_local_ps5", QSettingsKind.String, QSettingsScope.Profile, "h265"),
        Declare("settings/codec_ps4", QSettingsKind.String, QSettingsScope.Profile, "h264"),
        Declare("settings/codec_remote_ps5", QSettingsKind.String, QSettingsScope.Profile, "h265"),
        Declare("settings/custom_resolution_length", QSettingsKind.UInt, QSettingsScope.Profile, 1080u),
        Declare("settings/custom_resolution_width", QSettingsKind.UInt, QSettingsScope.Profile, 1920u),
        Declare("settings/disconnect_action", QSettingsKind.String, QSettingsScope.Profile, "ask"),
        Declare("settings/display_target_contrast", QSettingsKind.Int, QSettingsScope.Profile, 0),
        Declare("settings/display_target_peak", QSettingsKind.Int, QSettingsScope.Profile, 0),
        Declare("settings/display_target_prim", QSettingsKind.Int, QSettingsScope.Profile, 0),
        Declare("settings/display_target_trc", QSettingsKind.Int, QSettingsScope.Profile, 0),
        Declare("settings/dpad_touch_enabled", QSettingsKind.Bool, QSettingsScope.Profile, true),
        Declare("settings/dpad_touch_increment", QSettingsKind.UInt, QSettingsScope.Profile, 30u),
        Declare("settings/dpad_touch_shortcut1", QSettingsKind.UInt, QSettingsScope.Profile, 9u),
        Declare("settings/dpad_touch_shortcut2", QSettingsKind.UInt, QSettingsScope.Profile, 10u),
        Declare("settings/dpad_touch_shortcut3", QSettingsKind.UInt, QSettingsScope.Profile, 7u),
        Declare("settings/dpad_touch_shortcut4", QSettingsKind.UInt, QSettingsScope.Profile, 0u),
        Declare("settings/echo_suppress_level", QSettingsKind.Int, QSettingsScope.Profile, 30),
        Declare("settings/enable_speech_processing", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/fps_local_ps4", QSettingsKind.Int, QSettingsScope.Profile, 60),
        Declare("settings/fps_local_ps5", QSettingsKind.Int, QSettingsScope.Profile, 60),
        Declare("settings/fps_remote_ps4", QSettingsKind.Int, QSettingsScope.Profile, 60),
        Declare("settings/fps_remote_ps5", QSettingsKind.Int, QSettingsScope.Profile, 60),
        Declare("settings/fullscreen_doubleclick", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/geometry", QSettingsKind.Rect, QSettingsScope.Profile, null),
        Declare("settings/haptic_override", QSettingsKind.Double, QSettingsScope.Profile, 1.0),
        Declare("settings/hide_cursor", QSettingsKind.Bool, QSettingsScope.Profile, true),
        Declare("settings/hw_decoder", QSettingsKind.String, QSettingsScope.Profile, "auto"),
        Declare("settings/idr_on_fec_failure", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/keyboard_enabled", QSettingsKind.Bool, QSettingsScope.Profile, true),
        Declare("settings/log_sanitize", QSettingsKind.Bool, QSettingsScope.Profile, true),
        Declare("settings/log_verbose", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/mouse_touch_enabled", QSettingsKind.Bool, QSettingsScope.Profile, true),
        Declare("settings/noise_suppress_level", QSettingsKind.Int, QSettingsScope.Profile, 6),
        Declare("settings/packet_loss_max", QSettingsKind.Double, QSettingsScope.Profile, 0.05),
        Declare("settings/packet_loss_reported_max", QSettingsKind.Double, QSettingsScope.Profile, 0.05),
        Declare("settings/placebo_preset", QSettingsKind.String, QSettingsScope.Profile, "high_quality"),
        Declare("settings/port_guessing_count", QSettingsKind.Int, QSettingsScope.Profile, 75),
        Declare("settings/port_guessing_enabled", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/port_guessing_socket_count", QSettingsKind.Int, QSettingsScope.Profile, 250),
        Declare("settings/psn_account_id", QSettingsKind.String, QSettingsScope.Profile, null),
        Declare("settings/psn_auth_token", QSettingsKind.String, QSettingsScope.Profile, null),
        Declare("settings/psn_auth_token_expiry", QSettingsKind.String, QSettingsScope.Profile, null),
        Declare("settings/psn_refresh_token", QSettingsKind.String, QSettingsScope.Profile, null),
        Declare("settings/remote_play_ask", QSettingsKind.Bool, QSettingsScope.Profile, true),
        Declare("settings/render_backend", QSettingsKind.String, QSettingsScope.Profile, "vulkan"),
        Declare("settings/resolution_local_ps4", QSettingsKind.String, QSettingsScope.Profile, "720p"),
        Declare("settings/resolution_local_ps5", QSettingsKind.String, QSettingsScope.Profile, "1080p"),
        Declare("settings/resolution_remote_ps4", QSettingsKind.String, QSettingsScope.Profile, "720p"),
        Declare("settings/resolution_remote_ps5", QSettingsKind.String, QSettingsScope.Profile, "720p"),
        Declare("settings/rumble_haptics_intensity", QSettingsKind.String, QSettingsScope.Profile, "Normal"),
        Declare("settings/show_stream_stats", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/start_mic_unmuted", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/stream_geometry", QSettingsKind.Rect, QSettingsScope.Profile, null),
        Declare("settings/stream_menu_enabled", QSettingsKind.Bool, QSettingsScope.Profile, true),
        Declare("settings/stream_menu_shortcut1", QSettingsKind.UInt, QSettingsScope.Profile, 9u),
        Declare("settings/stream_menu_shortcut2", QSettingsKind.UInt, QSettingsScope.Profile, 10u),
        Declare("settings/stream_menu_shortcut3", QSettingsKind.UInt, QSettingsScope.Profile, 11u),
        Declare("settings/stream_menu_shortcut4", QSettingsKind.UInt, QSettingsScope.Profile, 12u),
        Declare("settings/streamer_mode", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/suspend_action", QSettingsKind.String, QSettingsScope.Profile, "nothing"),
        Declare("settings/use_zero_copy", QSettingsKind.Bool, QSettingsScope.Profile, true),
        Declare("settings/vsync", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/vulkan_deferred_swap", QSettingsKind.Bool, QSettingsScope.Profile, false),
        Declare("settings/wifi_dropped_notif_percent", QSettingsKind.UInt, QSettingsScope.Profile, 3u),
        Declare("settings/window_type", QSettingsKind.String, QSettingsScope.Profile, "Fullscreen"),
        Declare("settings/zoom_factor", QSettingsKind.Double, QSettingsScope.Profile, -1.0),

        // --- Placebo scope, 65 keys ---
        Declare("placebo_settings/allow_delayed_peak", QSettingsKind.String, QSettingsScope.Placebo, "no"),
        Declare("placebo_settings/antiringing_strength", QSettingsKind.Double, QSettingsScope.Placebo, 0.0),
        Declare("placebo_settings/black_cutoff", QSettingsKind.Double, QSettingsScope.Placebo, 1.0),
        Declare("placebo_settings/brightness", QSettingsKind.Double, QSettingsScope.Placebo, 0.0),
        Declare("placebo_settings/color_adjustment", QSettingsKind.String, QSettingsScope.Placebo, "yes"),
        Declare("placebo_settings/color_adjustment_preset", QSettingsKind.String, QSettingsScope.Placebo, ""),
        Declare("placebo_settings/color_map", QSettingsKind.String, QSettingsScope.Placebo, "yes"),
        Declare("placebo_settings/color_map_preset", QSettingsKind.String, QSettingsScope.Placebo, ""),
        Declare("placebo_settings/colorimetric_gamma", QSettingsKind.Double, QSettingsScope.Placebo, 1.80),
        Declare("placebo_settings/contrast", QSettingsKind.Double, QSettingsScope.Placebo, 1.0),
        Declare("placebo_settings/contrast_recovery", QSettingsKind.Double, QSettingsScope.Placebo, 0.0),
        Declare("placebo_settings/contrast_smoothness", QSettingsKind.Double, QSettingsScope.Placebo, 3.5),
        Declare("placebo_settings/deband", QSettingsKind.String, QSettingsScope.Placebo, "yes"),
        Declare("placebo_settings/deband_grain", QSettingsKind.Double, QSettingsScope.Placebo, 4.0),
        Declare("placebo_settings/deband_iterations", QSettingsKind.Int, QSettingsScope.Placebo, 1),
        Declare("placebo_settings/deband_preset", QSettingsKind.String, QSettingsScope.Placebo, ""),
        Declare("placebo_settings/deband_radius", QSettingsKind.Double, QSettingsScope.Placebo, 16.0),
        Declare("placebo_settings/deband_threshold", QSettingsKind.Double, QSettingsScope.Placebo, 3.0),
        Declare("placebo_settings/deinterlace", QSettingsKind.String, QSettingsScope.Placebo, "no"),
        Declare("placebo_settings/deinterlace_algo", QSettingsKind.String, QSettingsScope.Placebo, "yadif"),
        Declare("placebo_settings/deinterlace_preset", QSettingsKind.String, QSettingsScope.Placebo, "default"),
        Declare("placebo_settings/deinterlace_skip_spatial", QSettingsKind.String, QSettingsScope.Placebo, "no"),
        Declare("placebo_settings/downscaler", QSettingsKind.String, QSettingsScope.Placebo, "hermite"),
        Declare("placebo_settings/exposure", QSettingsKind.Double, QSettingsScope.Placebo, 1.0),
        Declare("placebo_settings/gamma", QSettingsKind.Double, QSettingsScope.Placebo, 1.0),
        Declare("placebo_settings/gamut_expansion", QSettingsKind.String, QSettingsScope.Placebo, "no"),
        Declare("placebo_settings/gamut_mapping", QSettingsKind.String, QSettingsScope.Placebo, "perceptual"),
        Declare("placebo_settings/hue", QSettingsKind.Double, QSettingsScope.Placebo, 0.0),
        Declare("placebo_settings/inverse_tone_mapping", QSettingsKind.String, QSettingsScope.Placebo, "no"),
        Declare("placebo_settings/knee_adaptation", QSettingsKind.Double, QSettingsScope.Placebo, 0.4),
        Declare("placebo_settings/knee_default", QSettingsKind.Double, QSettingsScope.Placebo, 0.4),
        Declare("placebo_settings/knee_maximum", QSettingsKind.Double, QSettingsScope.Placebo, 0.8),
        Declare("placebo_settings/knee_minimum", QSettingsKind.Double, QSettingsScope.Placebo, 0.1),
        Declare("placebo_settings/knee_offset", QSettingsKind.Double, QSettingsScope.Placebo, 1.0),
        Declare("placebo_settings/linear_knee", QSettingsKind.Double, QSettingsScope.Placebo, 0.3),
        Declare("placebo_settings/lut3d_size_C", QSettingsKind.Int, QSettingsScope.Placebo, 32),
        Declare("placebo_settings/lut3d_size_I", QSettingsKind.Int, QSettingsScope.Placebo, 48),
        Declare("placebo_settings/lut3d_size_h", QSettingsKind.Int, QSettingsScope.Placebo, 256),
        Declare("placebo_settings/lut3d_tricubic", QSettingsKind.String, QSettingsScope.Placebo, "no"),
        Declare("placebo_settings/peak_detect", QSettingsKind.String, QSettingsScope.Placebo, "yes"),
        Declare("placebo_settings/peak_detect_preset", QSettingsKind.String, QSettingsScope.Placebo, ""),
        Declare("placebo_settings/peak_percentile", QSettingsKind.Double, QSettingsScope.Placebo, 100.0),
        Declare("placebo_settings/peak_smoothing_period", QSettingsKind.Double, QSettingsScope.Placebo, 20.0),
        Declare("placebo_settings/perceptual_deadzone", QSettingsKind.Double, QSettingsScope.Placebo, 0.30),
        Declare("placebo_settings/perceptual_strength", QSettingsKind.Double, QSettingsScope.Placebo, 0.80),
        Declare("placebo_settings/plane_downscaler", QSettingsKind.String, QSettingsScope.Placebo, "none"),
        Declare("placebo_settings/plane_upscaler", QSettingsKind.String, QSettingsScope.Placebo, "none"),
        Declare("placebo_settings/reinhard_contrast", QSettingsKind.Double, QSettingsScope.Placebo, 0.5),
        Declare("placebo_settings/saturation", QSettingsKind.Double, QSettingsScope.Placebo, 1.0),
        Declare("placebo_settings/scene_threshold_high", QSettingsKind.Double, QSettingsScope.Placebo, 3.0),
        Declare("placebo_settings/scene_threshold_low", QSettingsKind.Double, QSettingsScope.Placebo, 1.0),
        Declare("placebo_settings/sigmoid", QSettingsKind.String, QSettingsScope.Placebo, "yes"),
        Declare("placebo_settings/sigmoid_center", QSettingsKind.Double, QSettingsScope.Placebo, 0.75),
        Declare("placebo_settings/sigmoid_preset", QSettingsKind.String, QSettingsScope.Placebo, ""),
        Declare("placebo_settings/sigmoid_slope", QSettingsKind.Double, QSettingsScope.Placebo, 6.5),
        Declare("placebo_settings/slope_offset", QSettingsKind.Double, QSettingsScope.Placebo, 0.2),
        Declare("placebo_settings/slope_tuning", QSettingsKind.Double, QSettingsScope.Placebo, 1.5),
        Declare("placebo_settings/softclip_desat", QSettingsKind.Double, QSettingsScope.Placebo, 0.35),
        Declare("placebo_settings/softclip_knee", QSettingsKind.Double, QSettingsScope.Placebo, 0.70),
        Declare("placebo_settings/spline_contrast", QSettingsKind.Double, QSettingsScope.Placebo, 0.5),
        Declare("placebo_settings/temperature", QSettingsKind.Double, QSettingsScope.Placebo, 0.0),
        Declare("placebo_settings/tone_lut_size", QSettingsKind.Int, QSettingsScope.Placebo, 256),
        Declare("placebo_settings/tone_map_metadata", QSettingsKind.String, QSettingsScope.Placebo, "any"),
        Declare("placebo_settings/tone_mapping", QSettingsKind.String, QSettingsScope.Placebo, "spline"),
        Declare("placebo_settings/upscaler", QSettingsKind.String, QSettingsScope.Placebo, "ewa_lanczossharp"),
    };

    /// <summary>Every declared preference, by its Qt key.</summary>
    public static readonly IReadOnlyDictionary<string, PreferenceKey> All =
        declared.ToDictionary(p => p.Key, StringComparer.Ordinal);

    /// <summary>The declaration for a key, or null where the key is not one this port knows.</summary>
    public static PreferenceKey? Find(string key) => All.TryGetValue(key, out PreferenceKey? p) ? p : null;
}

/// <summary>
/// Reads a declared preference out of whichever store declares it, falling back to the Qt
/// default when the user has never set it.
///
/// Undeclared keys throw rather than returning a default. A key this port does not know is a typo
/// or a preference nobody transcribed, and both are bugs in this tree; answering them with a zero
/// would hide the second one for as long as the port lasts.
/// </summary>
public sealed class QSettingsPreferences
{
    private readonly QSettingsStore store;

    public QSettingsPreferences(QSettingsStore store) => this.store = store;

    /// <summary>
    /// The raw registry value, or null where the key has never been written.
    ///
    /// QSettings turns every `/` in a key into a subkey, so settings/hw_decoder is the value
    /// hw_decoder under the subkey settings - not a value named "settings/hw_decoder".
    /// </summary>
    public object? Raw(string key)
    {
        PreferenceKey declaration = Declaration(key);
        string root = declaration.Scope switch
        {
            QSettingsScope.Default => store.DefaultPath,
            QSettingsScope.Profile => store.KeyPath,
            QSettingsScope.Placebo => QSettingsStore.PlaceboKeyPath,
            _ => store.KeyPath,
        };

        int cut = key.LastIndexOf('/');
        string path = cut < 0 ? root : $@"{root}\{key[..cut].Replace('/', '\\')}";
        string name = cut < 0 ? key : key[(cut + 1)..];

        using RegistryKey? subkey = Registry.CurrentUser.OpenSubKey(path);
        return subkey?.GetValue(name);
    }

    public string? GetString(string key)
        => QSettingsValue.AsString(Raw(key)) ?? (string?)Declaration(key, QSettingsKind.String).Default;

    public bool GetBool(string key)
        => QSettingsValue.AsBool(Raw(key)) ?? (bool)(Declaration(key, QSettingsKind.Bool).Default ?? false);

    public int GetInt(string key)
        => QSettingsValue.AsInt(Raw(key)) ?? (int)(Declaration(key, QSettingsKind.Int).Default ?? 0);

    public uint GetUInt(string key)
        => QSettingsValue.AsUInt(Raw(key)) ?? (uint)(Declaration(key, QSettingsKind.UInt).Default ?? 0u);

    public double GetDouble(string key)
        => QSettingsValue.AsDouble(Raw(key)) ?? (double)(Declaration(key, QSettingsKind.Double).Default ?? 0.0);

    /// <summary>Null where the store holds no geometry, which is Qt's own default of QRect().</summary>
    public QRectValue? GetRect(string key)
    {
        Declaration(key, QSettingsKind.Rect);
        return QSettingsValue.AsRect(Raw(key));
    }

    public byte[]? GetBytes(string key)
    {
        Declaration(key, QSettingsKind.ByteArray);
        return QSettingsValue.AsByteArray(Raw(key));
    }

    private static PreferenceKey Declaration(string key)
        => Preferences.Find(key)
           ?? throw new KeyNotFoundException(
               $"'{key}' is not a preference this port declares. Add it to Preferences with the "
               + "kind, scope and default the Qt client reads it with.");

    /// <summary>
    /// The declaration, refusing a read at the wrong width. Asking a bool for its int is not a
    /// conversion this can do quietly: Qt writes a bool as the text "true", and reading it as an
    /// int gives 0 for both of its values.
    /// </summary>
    private static PreferenceKey Declaration(string key, QSettingsKind expected)
    {
        PreferenceKey declaration = Declaration(key);
        if (declaration.Kind != expected)
            throw new InvalidOperationException(
                $"'{key}' is declared {declaration.Kind} and was read as {expected}.");
        return declaration;
    }
}
