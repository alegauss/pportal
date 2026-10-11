using System.Globalization;
using System.Runtime.InteropServices;
using ChiakiNg.Session;

namespace ChiakiNg.Native;

/// <summary>PP851: a render endpoint that is a DualSense, as the engine needs it named.</summary>
/// <param name="Id">IMMDevice::GetId, which is what XAudio2 masters on.</param>
/// <param name="Name">The friendly name, for a log line and nothing else.</param>
public readonly record struct PadEndpoint(string Id, string Name);

/// <summary>
/// PP851: which audio endpoint is the pad, chosen by the hardware and not by its label.
///
/// The Qt client picks the first output whose name contains "Wireless Controller". That name is
/// localised - this machine reads <c>Alto-falantes</c> for it - and the words are not the pad's
/// alone: an Xbox pad carries two of them. docs/HARDWARE-CONTRACT.md records both.
///
/// WHAT IS NOT LOCALISED IS THE PARENT DEVICE. Every endpoint's property store carries the instance
/// path of the hardware behind it, and a DualSense's begins <c>USB\VID_054C&amp;PID_0CE6</c> - the same
/// pair PP846 routes the effects report by. So the choice is made on the numbers
/// <see cref="PadIdentity"/> already holds, and the name is kept for the log.
///
/// ABSENCE IS QUIET. Over Bluetooth the pad has no endpoint at all, so null here is the ordinary
/// answer for a wireless pad and the caller keeps PP846's fold.
/// </summary>
public static class HapticsEndpoint
{
    /// <summary>
    /// Whether an instance path names a USB DualSense or DualSense Edge.
    /// </summary>
    /// <param name="parent">
    /// The path as the store holds it, with or without the "{1}." the store prefixes.
    /// </param>
    public static bool IsPad(string? parent)
    {
        if (string.IsNullOrEmpty(parent))
            return false;

        int vid = parent.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        int pid = parent.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);

        if (vid < 0 || pid < 0 || !parent.Contains("USB\\", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryHex(parent, vid + 4, out ushort vendor) || !TryHex(parent, pid + 4, out ushort product))
            return false;

        return new PadIdentity(vendor, product, 0).TakesEffectsReport;
    }

    /// <summary>
    /// The first active render endpoint that is a DualSense, or null where none is.
    /// </summary>
    /// <remarks>
    /// The first and not a choice among several, which is the Qt client's own rule: it opens one
    /// haptics device and ignores the rest.
    /// </remarks>
    public static PadEndpoint? Find()
    {
        Wasapi.IMMDeviceEnumerator? enumerator = Wasapi.Enumerator(out _);
        if (enumerator is null)
            return null;

        try
        {
            if (enumerator.EnumAudioEndpoints(Wasapi.EDataFlow.Render, Wasapi.DeviceStateActive,
                    out Wasapi.IMMDeviceCollection? devices) != 0 || devices is null)
                return null;

            try
            {
                if (devices.GetCount(out int count) != 0)
                    return null;

                for (var i = 0; i < count; i++)
                {
                    if (devices.Item(i, out Wasapi.IMMDevice? device) != 0 || device is null)
                        continue;

                    try
                    {
                        if (IsPad(Wasapi.ParentOf(device)) && device.GetId(out string id) == 0)
                            return new PadEndpoint(id, Wasapi.NameOf(device));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }

                return null;
            }
            finally
            {
                Marshal.ReleaseComObject(devices);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static bool TryHex(string text, int at, out ushort value)
    {
        value = 0;

        return at + 4 <= text.Length
            && ushort.TryParse(text.AsSpan(at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }
}
