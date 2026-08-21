using System.Diagnostics;
using System.Windows.Automation;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP227: the real application, driven through UI Automation.
///
/// INVOKED, NOT CLICKED. A synthesised mouse click lands on whatever is drawn at a point, so it
/// needs the window in the foreground - and Windows refuses the foreground to a process that does
/// not already own it, which means a run started from an editor or an agent drives somebody else's
/// window. InvokePattern asks the control directly: no pointer, no foreground, nothing on top to be
/// confused with. That is what lets these run unattended, which is the whole point of them.
///
/// Not in the gate. `test.cmd` excludes this category and `test.cmd interaction` runs it: these
/// open windows and some need a controller, and a suite that does either fails for reasons about
/// the desk it ran on rather than about the code.
/// </summary>
public sealed class AppUnderTest : IDisposable
{
    private readonly Process process;

    /// <summary>
    /// The Debug build, found by walking UP rather than by counting.
    ///
    /// Written first as five "..", which landed in tests\app\bin\... because the right number is
    /// six - and PP22's own locator already says why not to count: "a count of '..' is the kind of
    /// thing that survives exactly one layout change". Reused rather than repeated.
    /// </summary>
    public static string? Exe { get; } = SanitizerSource.LocateRelative(
        Path.Combine("app", "bin", "Debug", "net10.0-windows", "win-x64", "ChiakiNg.exe"));

    private AppUnderTest(Process process) => this.process = process;

    /// <summary>
    /// Launches it and waits for a window.
    ///
    /// The process is remembered and killed on dispose. That matters more than it sounds: a
    /// leftover ChiakiNg holds its own .exe open, the next build cannot overwrite it, and the run
    /// after that exercises the PREVIOUS binary and reports on code that is not in the tree.
    /// </summary>
    public static AppUnderTest Start(string arguments, TimeSpan timeout)
    {
        Assert.True(Exe is not null, "no ChiakiNg.exe above this assembly - run compile.cmd");

        var process = Process.Start(new ProcessStartInfo(Exe, arguments) { UseShellExecute = false })
            ?? throw new InvalidOperationException("the application did not start");

        var app = new AppUnderTest(process);

        // Every way out of the wait disposes, because the `using` at the call site has not taken
        // ownership yet - and a throw from in here leaves a window on the desk for the rest of the
        // session. Measured: three failing tests left three ChiakiNg windows open, and a leftover
        // one holds its own .exe so the next build cannot overwrite it.
        try
        {
            DateTime until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until)
            {
                // ExitCode THROWS while the process is running, and an interpolated assertion
                // message is built before the assertion decides - so reading it eagerly failed
                // every one of these on their first turn round this loop, with the application
                // perfectly healthy.
                if (process.HasExited)
                    Assert.Fail($"the application exited ({process.ExitCode}) before showing a window");

                if (app.Window is not null)
                    return app;

                Thread.Sleep(200);
            }

            throw new TimeoutException(
                $"no window appeared for `{arguments}` within {timeout.TotalSeconds:0}s");
        }
        catch
        {
            app.Dispose();
            throw;
        }
    }

    /// <summary>Its top-level window, or null while it has none.</summary>
    public AutomationElement? Window =>
        AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));

    public void Dispose()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(3000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone, which is the outcome this wanted.
        }

        process.Dispose();
    }
}

/// <summary>PP227: finding and pressing things, in the vocabulary these tests are written in.</summary>
public static class Ui
{
    /// <summary>By AutomationId, which for a WPF control is its x:Name. Waits for it to appear.</summary>
    public static AutomationElement? ById(AutomationElement root, string id, int timeoutMs = 4000)
    {
        var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, id);

        DateTime until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        do
        {
            AutomationElement? found = root.FindFirst(TreeScope.Descendants, condition);
            if (found is not null)
                return found;

            Thread.Sleep(200);
        }
        while (DateTime.UtcNow < until);

        return null;
    }

    /// <summary>One look and no waiting - for asking whether something is GONE.</summary>
    public static AutomationElement? Now(AutomationElement root, string id)
        => root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id));

    /// <summary>Every descendant of a control type, in tree order.</summary>
    public static IReadOnlyList<AutomationElement> OfType(AutomationElement root, ControlType type)
        => root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, type))
            .Cast<AutomationElement>()
            .ToList();

    /// <summary>
    /// Presses a control without a pointer, and fails the test where it offers no way to be
    /// pressed - which is a real answer about the control rather than a reason to reach for the
    /// mouse and the foreground behind the caller's back.
    /// </summary>
    public static void Invoke(AutomationElement element, string what)
    {
        Assert.True(
            element.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern),
            $"{what} offers no InvokePattern, so it cannot be pressed without the foreground");

        ((InvokePattern)pattern).Invoke();
        Thread.Sleep(400);
    }
}

/// <summary>
/// PP227: a click on a row opens the capture, and closing it puts it away.
///
/// The one thing PP223 left unchecked. Its tests call the plain method underneath the handler and
/// never raise a click, so whether a real press reaches that method at all went unobserved - and a
/// picture cannot see it either, which is the gap between this file and PP226's.
/// </summary>
[Trait("Category", "Interaction")]
public class MappingInteractionTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Why an assertion could not run, in the window's own words.
    ///
    /// xUnit 2 has no way to say "should have been checked, wasn't", and the two readings this has
    /// to keep apart are a screen that is broken and a desk with no controller on it. So it FAILS
    /// and names the precondition: a red run that says "no pad SDL can map" is legible, and a green
    /// one that checked nothing is the defect this whole file exists to stop. These are opt-in
    /// anyway - `test.cmd interaction` is a request to check the thing that needs a pad.
    /// </summary>
    private static string NotChecked(AutomationElement window)
    {
        AutomationElement? why = Ui.Now(window, "Placeholder");

        return "NOT CHECKED, and not a failure of the code: "
            + (why?.Current.Name ?? "the window shows neither a screen nor a reason");
    }

    /// <summary>
    /// The window opens and says which of the two it is: a mapping screen, or the reason there
    /// isn't one. Runs with or without a controller, which is what makes it the first case.
    /// </summary>
    [Fact]
    public void TheWindowSaysWhatItHas()
    {
        using AppUnderTest app = AppUnderTest.Start("--map-controller", Patience);

        AutomationElement window = app.Window!;
        Assert.Equal("chiaki-ng", window.Current.Name);

        AutomationElement? placeholder = Ui.ById(window, "Placeholder", timeoutMs: 6000);
        AutomationElement? title = Ui.Now(window, "Title");

        // Exactly one of the two. A window showing neither is one that opened onto nothing, which
        // is the defect PP224 was filed for.
        Assert.True(
            title is not null || placeholder is not null,
            "the window shows neither a mapping screen nor a reason for having none");

        output.WriteLine(title is not null
            ? $"screen: {title.Current.Name}"
            : $"no screen: {placeholder!.Current.Name}");
    }

    /// <summary>
    /// A row's slot opens the capture, the capture names the pad, and Close puts it away.
    ///
    /// Needs a controller. Without one the window says so and this SKIPS rather than passes: a run
    /// that reports success for a property nobody observed is the defect this whole file exists to
    /// stop, and this port shipped three of that shape in a day.
    /// </summary>
    [Fact]
    public void ARowOpensTheCaptureAndCloseShutsIt()
    {
        using AppUnderTest app = AppUnderTest.Start("--map-controller", Patience);
        AutomationElement window = app.Window!;

        AutomationElement? title = Ui.ById(window, "Title", timeoutMs: 8000);
        if (title is null)
            Assert.Fail(NotChecked(window));

        output.WriteLine($"pad: {title.Current.Name}");

        // A row's slot is a templated Button and carries no AutomationId; the two named buttons do,
        // which is how the rows are told apart from them.
        IReadOnlyList<AutomationElement> buttons = Ui.OfType(window, ControlType.Button);
        List<AutomationElement> rows =
            [.. buttons.Where(b => string.IsNullOrEmpty(b.Current.AutomationId))];

        Assert.NotEmpty(rows);
        output.WriteLine($"{rows.Count} row slot(s)");

        // Not up yet, which is what makes the next assertion mean anything.
        Assert.Null(Ui.Now(window, "CapturePrompt"));

        Ui.Invoke(rows[0], "the first row's slot");

        AutomationElement? prompt = Ui.ById(window, "CapturePrompt");
        Assert.True(prompt is not null, "clicking a row opened no capture - the prompt never appeared");

        // The one string on this screen that changes with the device.
        Assert.Contains(title.Current.Name, prompt.Current.Name, StringComparison.Ordinal);
        output.WriteLine($"prompt: {prompt.Current.Name}");

        AutomationElement? close = Ui.ById(window, "CaptureCloseButton");
        Assert.True(close is not null, "the capture has no Close");

        Ui.Invoke(close, "the capture's Close");

        Assert.Null(Ui.Now(window, "CapturePrompt"));
    }

    /// <summary>
    /// Update follows the alteration and nothing else, asserted on the real control rather than on
    /// the view model that decides it.
    /// </summary>
    [Fact]
    public void UpdateStaysOffUntilSomethingIsBound()
    {
        using AppUnderTest app = AppUnderTest.Start("--map-controller", Patience);
        AutomationElement window = app.Window!;

        AutomationElement? update = Ui.ById(window, "UpdateButton", timeoutMs: 8000);
        if (update is null)
            Assert.Fail(NotChecked(window));

        Assert.False(update.Current.IsEnabled);
    }
}
