using System;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using Vortice.Direct3D9;

namespace PresentPath;

/// <summary>
/// The conditions are half the output. Two numbers with no adapter, no driver, no render tier and
/// no sample count behind them are a claim rather than a measurement, and PP43 exists precisely
/// because the renderer decision would otherwise be taken on argument.
/// </summary>
internal static class Report
{
    public static void Write(Args args, IPresenter presenter, Stats present, Stats cadence)
    {
        var c = CultureInfo.InvariantCulture;
        string adapter = "unknown", driver = "unknown";
        try
        {
            using var d3d = D3D9.Direct3DCreate9Ex();
            var id = d3d.GetAdapterIdentifier(0);
            adapter = id.Description.Trim();
            driver = $"{id.Driver.Trim()} {id.DriverVersion}";
        }
        catch (Exception ex)
        {
            adapter = $"unavailable: {ex.Message}";
        }

        // Tier >> 16 == 2 means WPF composes on the GPU. A tier of 0 or 1 would make any number
        // from the shared-surface path meaningless, so it is recorded rather than assumed.
        int tier = RenderCapability.Tier >> 16;

        string json = "{"
            + $"\"spike\":\"PP43\""
            + $",\"path\":\"{args.Path}\""
            + $",\"driver\":\"{args.Driver}\""
            + $",\"description\":\"{presenter.Describe()}\""
            + $",\"frame\":{{\"width\":{args.Width},\"height\":{args.Height}}}"
            + $",\"frames_measured\":{present.Count},\"frames_warmup\":{args.Warmup}"
            + $",\"conditions\":{{"
                + $"\"adapter\":\"{Escape(adapter)}\""
                + $",\"driver\":\"{Escape(driver)}\""
                + $",\"wpf_render_tier\":{tier}"
                + $",\"present_interval\":\"immediate\""
                + $",\"dwm_composition\":true"
                + $",\"os\":\"{Escape(Environment.OSVersion.VersionString)}\""
                + $",\"runtime\":\"{Escape(Environment.Version.ToString())}\""
            + "}"
            + $",\"present_us\":{present.ToJson()}"
            + $",\"frame_to_frame_us\":{cadence.ToJson()}"
            + "}";

        File.WriteAllText(args.Out, json + Environment.NewLine);
        Console.WriteLine($"report        : {Path.GetFullPath(args.Out)}");
        Console.WriteLine($"conditions    : {adapter}, driver {driver}, WPF render tier {tier}");
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
