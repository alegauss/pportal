using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>One device as SDL enumerates it, at the index it currently occupies.</summary>
/// <param name="IsGameController">Whether SDL has a mapping for it. Joysticks without one are skipped.</param>
/// <param name="Guid">The device GUID, lower case hex, as SDL_JoystickGetGUIDString writes it.</param>
/// <param name="ButtonCount">How many buttons it reports. Zero is the case the filter exists for.</param>
/// <param name="InstanceId">
/// The id SDL gives this device for its lifetime. NOT the index: the index is a position in a
/// list that shifts when anything is unplugged, and the instance id is what a removal names.
/// </param>
public readonly record struct SdlDevice(bool IsGameController, string Guid, int ButtonCount, int InstanceId);

/// <summary>
/// PP128: which controllers the application believes are attached, and when that changes.
///
/// The hotplug event itself cannot be tested without hardware - PP118 measured SDL rewriting the
/// `which` field on any device event a caller pushes, so a synthesised arrival is not a pad. What
/// CAN be tested is everything the arrival triggers, and that is where the mistakes are.
///
/// Two of them in particular.
///
/// The roster is keyed by INSTANCE ID and enumerated by INDEX. Those are different numbers with
/// different lifetimes: an index is a position in a list that shifts when anything is unplugged,
/// and an instance id belongs to a device until it leaves. controllermanager.cpp walks indices and
/// converts each with SDL_JoystickGetDeviceInstanceID; a port that skipped the conversion would
/// key the roster on positions, and unplugging the first of two pads would silently rename the
/// other one.
///
/// And a device with a motion-controller GUID and NO buttons is skipped. Those are PS Move-style
/// devices that SDL reports as game controllers because it has a mapping for them; leaving them in
/// shows a pad nobody can press anything on, and the console is told a controller is connected.
/// </summary>
public sealed class ControllerRoster
{
    private readonly IReadOnlySet<string> motionGuids;
    private HashSet<int> attached = [];

    /// <param name="motionControllerGuids">
    /// The GUIDs the zero-button filter applies to - <see cref="RosterSource"/> reads them out of
    /// the Qt client, because a list that drifted would show a phantom pad on exactly the devices
    /// nobody testing the port owns.
    /// </param>
    public ControllerRoster(IReadOnlySet<string> motionControllerGuids)
    {
        ArgumentNullException.ThrowIfNull(motionControllerGuids);
        motionGuids = motionControllerGuids;
    }

    /// <summary>The instance ids currently believed attached.</summary>
    public IReadOnlySet<int> Attached => attached;

    /// <summary>
    /// Reconciles against what SDL now enumerates, and reports whether the set CHANGED.
    ///
    /// The return value is the signal: controllermanager.cpp emits only on a difference, so a
    /// roster that reported every reconcile would have every screen bound to it redraw on each
    /// poll - four milliseconds apart, which is PP118's interval.
    /// </summary>
    public bool Reconcile(IEnumerable<SdlDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var current = new HashSet<int>();
        foreach (SdlDevice device in devices)
        {
            if (!device.IsGameController)
                continue;

            // The zero-button filter, and only for the GUIDs it names. A device with no buttons
            // and an ordinary GUID is left alone: this is not "hide empty devices", it is a list
            // of things known to enumerate as controllers they are not.
            if (device.ButtonCount == 0 && motionGuids.Contains(device.Guid))
                continue;

            current.Add(device.InstanceId);
        }

        if (current.SetEquals(attached))
            return false;

        attached = current;
        return true;
    }
}

/// <summary>
/// PP128: the motion-controller GUID list, read out of the Qt client rather than copied.
///
/// Forty-nine of them, and nobody porting this owns most of the devices they name. A list that
/// drifted would be invisible on every machine a developer has and wrong on the ones it is for.
/// </summary>
public static partial class RosterSource
{
    /// <summary>The Qt client's controller code.</summary>
    public const string RelativePath = @"gui\src\controllermanager.cpp";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The GUIDs the Qt client applies the zero-button filter to, deduplicated.
    ///
    /// Deduplicated because the Qt side is a QSet and does the same: the initialiser lists 49
    /// literals and holds 45 distinct values, four of them repeated. That is harmless there and
    /// would be harmless here, and it is recorded rather than tidied because the next person to
    /// count the list will otherwise decide the port lost four.
    /// </summary>
    public static IReadOnlySet<string> MotionControllerGuids(string text)
        => Literals(text).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every GUID literal in the initialiser, repeats included.</summary>
    public static IReadOnlyList<string> Literals(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Match block = ListRegex().Match(text);
        return block.Success
            ? [.. GuidRegex().Matches(block.Groups[1].Value).Select(m => m.Groups[1].Value)]
            : [];
    }

    /// <summary>
    /// Whether the roster is still built from instance ids rather than from indices.
    ///
    /// The conversion is one call and dropping it is the kind of simplification that reads as
    /// tidying - the loop already has `i`, and using it compiles and mostly works.
    /// </summary>
    public static bool RosterIsKeyedByInstanceId(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains(
            "current_controllers.insert(SDL_JoystickGetDeviceInstanceID(i));", StringComparison.Ordinal);
    }

    /// <summary>Whether the update is still emitted only when the set differs.</summary>
    public static bool OnlyEmitsOnChange(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ChangeGuardRegex().IsMatch(text);
    }

    [GeneratedRegex(@"chiaki_motion_controller_guids\(\{(.*?)\}\);", RegexOptions.Singleline)]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"""([0-9a-f]{32})""")]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"if\(current_controllers != available_controllers\)")]
    private static partial Regex ChangeGuardRegex();
}
