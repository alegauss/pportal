using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ChiakiNg.Session;

namespace ChiakiNg;

/// <summary>
/// PP1: the application object. PP2 added the one thing it does before drawing anything.
///
/// This host exists so that every screen in Block D is filed against something that already
/// builds. The alternative - a first screen that carries the project, the manifest and the
/// packaging on its back - cannot be reviewed as a screen at all, because a reviewer cannot tell
/// which half is wrong.
///
/// The Qt client stays until Block D empties. Two executables in one tree is the ordinary shape
/// of a port, and the one that is not shipped yet is the one being written.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// A WinExe is built with no console, so writing to stdout from one goes nowhere by default.
    /// Attaching to the parent's console is what makes `ChiakiNg.exe --selftest` print into the
    /// shell that launched it; a run with no parent console (double-clicked) simply fails this
    /// call and the selftest still sets the exit code.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    private const int AttachParentProcess = -1;

    /// <summary>
    /// Make Console.Write actually reach somewhere. AttachConsole alone is not enough and that
    /// is measured, not assumed: with only the attach, `ChiakiNg.exe --selftest` exited 0 and
    /// printed nothing at all. The process was started with no console, so the CLR had already
    /// bound Console.Out to a null writer, and attaching one afterwards does not rebind it.
    ///
    /// So the standard handle is reopened by hand. A run with no parent console - double-clicked,
    /// or launched detached - fails the attach, gets an invalid handle, and falls through to
    /// leaving Console.Out as it was: the selftest still runs and still sets the exit code, which
    /// is the half a caller can read without a console anyway.
    /// </summary>
    private static void ReopenStdOut()
    {
        // The inherited handle first, and AttachConsole only if there is none.
        //
        // The order is the whole of it, and it cost two wrong versions. AttachConsole does not
        // add a console beside the standard handles - it REPLACES them with the console's. So a
        // run whose stdout was already a file or a pipe loses that redirect the moment it is
        // called, and `ChiakiNg.exe --selftest > out.txt` writes a zero-byte file and exits 0.
        // A redirected caller already has somewhere to write; only a bare double-click or a
        // prompt with no redirect needs a console attached at all.
        if (!Bind())
        {
            AttachConsole(AttachParentProcess);
            Bind();
        }

        static bool Bind()
        {
            try
            {
                Stream stdout = Console.OpenStandardOutput();
                // Stream.Null is what a process with no usable standard handle gets back.
                // Writing to it is legal and silent, so it is checked rather than caught.
                if (stdout == Stream.Null)
                    return false;
                var writer = new StreamWriter(stdout) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
                return true;
            }
            catch (IOException)
            {
                // No usable handle. The exit code is still the half a caller can read.
                return false;
            }
        }
    }

    /// <summary>
    /// PP218: `--controllers`, which prints what SDL sees and exits.
    ///
    /// Its own flag rather than a corner of the selftest, because the selftest is a gate that must
    /// pass on a build machine with no pad, and this is the opposite - a thing you run BECAUSE a
    /// pad is plugged in, whose whole output is the device it found.
    ///
    /// The enumeration goes through the thread rather than beside it: SDL's device tables belong to
    /// whichever thread called SDL_Init, which is what <see cref="SdlThread"/> exists to own.
    /// </summary>
    /// <summary>
    /// PP304: `--recount`, which prints every size the backlog states that the tree disagrees with,
    /// and the roadkeep call that corrects each.
    ///
    /// It does not write. The three governed files are roadkeep's, a hook denies a hand edit to
    /// them, and going around that would solve the transcription by removing the gate. What it
    /// removes instead is the part a person does badly: knowing that a claim about session.c on
    /// IMPROVEMENTS.md:216 belongs to §PP28 and not to §PP293, and that a claim in a roadmap line's
    /// symptom takes `restate` while one in its why takes `amend`.
    ///
    /// Exits 1 where anything is stale, so it answers the same question the test does and can be
    /// run before the work rather than after it.
    /// </summary>
    private static int Recount()
    {
        string? root = SanitizerSource.RepositoryRoot();
        if (root is null)
        {
            Console.WriteLine("[recount] not running out of a checkout - there is no backlog to read.");
            return 2;
        }

        IReadOnlyList<CountedClaim> claims = CountedClaims.All(root);
        Console.WriteLine($"[recount] {claims.Count} counted claim(s) in the backlog");

        var stale = 0;
        var unresolved = 0;

        foreach (CountedClaim claim in claims)
        {
            int actual = CountedClaims.Actual(root, claim);

            if (actual < 0)
            {
                unresolved++;
                Console.WriteLine();
                Console.WriteLine($"  {claim.Document}:{claim.Line}  {claim.Subject} does not resolve to one thing, so the claim cannot be checked");
                continue;
            }

            if (actual == claim.Stated)
                continue;

            stale++;
            Console.WriteLine();
            Console.WriteLine($"  {claim.Document}:{claim.Line}  {claim.Subject} says {claim.Stated} and is {actual}");

            string? remedy = CountedClaims.Remedy(root, claim, actual);
            Console.WriteLine(remedy is null
                ? "    (no remedy: this line is not a shape --recount knows how to address)"
                : "    " + remedy);
        }

        Console.WriteLine();
        if (stale == 0 && unresolved == 0)
        {
            Console.WriteLine("[recount] every claim holds.");
            return 0;
        }

        Console.WriteLine($"[recount] {stale} stale, {unresolved} unresolvable.");
        return 1;
    }

    /// <summary>
    /// PP305: `--ratchet`, which prints the debt PP38 counts in the form it can be paid in.
    ///
    /// The count alone is a gate and not a task list. Ninety-seven bare ids say nothing about where
    /// an assertion for one would go, or whether one already exists under a neighbouring id - and
    /// that second case is common enough to be the rule: PP300 finished a port whose assertions
    /// were all written under PP29, so it reads as untested while being among the better-checked
    /// things in the tree. Each id is printed with the ledger's own sentence for it, which is what
    /// makes that visible without opening the ledger.
    ///
    /// Exits 0 whatever it finds. It is a list, not the gate - the gate is
    /// <c>AssertionRatchetTests</c>, and having two things fail for one reason only ever teaches
    /// people to read one of them.
    /// </summary>
    private static int Ratchet()
    {
        string? root = SanitizerSource.RepositoryRoot();
        if (root is null)
        {
            Console.WriteLine("[ratchet] not running out of a checkout - there is no ledger to read.");
            return 2;
        }

        string? ledgerPath = AssertionRatchet.LocateLedger();
        if (ledgerPath is null)
        {
            Console.WriteLine($"[ratchet] no {AssertionRatchet.LedgerRelativePath} here.");
            return 2;
        }

        IReadOnlyDictionary<string, string> symptoms =
            AssertionRatchet.ShippedWithSymptom(File.ReadAllText(ledgerPath));
        IReadOnlyList<string> uncovered = AssertionRatchet.Uncovered(root);
        int ceiling = AssertionRatchet.Ceiling(root);

        Console.WriteLine($"[ratchet] {uncovered.Count} shipped task(s) named by no assertion, ceiling {ceiling}");
        Console.WriteLine("[ratchet] newest first - an assertion that already holds one of these only needs to say so");
        Console.WriteLine();

        foreach (string id in uncovered)
        {
            string symptom = symptoms.TryGetValue(id, out string? text) ? text : "(no sentence in the ledger)";
            Console.WriteLine($"  {id,-7} {symptom}");
        }

        return 0;
    }

    /// <summary>
    /// PP311: where one id is named, which is how a payment on the ratchet is audited.
    ///
    /// The join cannot tell a claim from data by syntax - SuiteCoverageTests writes its coverage
    /// claims as a table of strings, deliberately, so an id in a literal there IS a claim while the
    /// same shape in a fixture next door is not. Three times in one commit an id written as test
    /// data paid a real task's debt, and every time the only symptom was the count falling by one
    /// more than expected.
    ///
    /// This is the question that was asked by grep each of those times. A line of output that reads
    /// like a fixture is a payment nobody made.
    /// </summary>
    private static int RatchetFor(string id)
    {
        string? root = SanitizerSource.RepositoryRoot();
        if (root is null)
        {
            Console.WriteLine("[ratchet] not running out of a checkout.");
            return 2;
        }

        IReadOnlyList<string> where = AssertionRatchet.WhereNamed(root, id);

        if (where.Count == 0)
        {
            Console.WriteLine($"[ratchet] {id} is named by no assertion.");

            IReadOnlyDictionary<string, string> exempt = AssertionRatchet.Exemptions(root);
            if (exempt.TryGetValue(id, out string? reason))
                Console.WriteLine($"[ratchet] and is exempt: {reason}");
            else
                ShippedBesideATest(root, id);

            return 0;
        }

        Console.WriteLine($"[ratchet] {id} is named in {where.Count} place(s):");
        Console.WriteLine();
        foreach (string line in where)
            Console.WriteLine("  " + line);

        return 0;
    }

    /// <summary>
    /// PP308: whether the commit that shipped this task also carried a test, asked of git.
    ///
    /// A DIAGNOSTIC and not the gate - see <see cref="AssertionRatchet.AssertionFilesIn"/> for why
    /// that decision went the way it did. It runs where somebody is paying the debt down, on a
    /// machine that has the history, and answers the question that makes paying cheap: seventy-five
    /// of the ninety-six already have their assertions written, under the id the work continued.
    ///
    /// Every failure here is silent by design. No git, no history, a shallow clone, a commit
    /// message that names the task somewhere other than its scope - all of them mean "cannot say",
    /// and a diagnostic that shouted about its own absence would be worse than one that stops.
    /// </summary>
    private static void ShippedBesideATest(string root, string id)
    {
        string? sha = Git(root, $"log --format=%H -E --grep \\({id}\\)")?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (sha is null)
            return;

        string? changed = Git(root, $"show --name-only --format= {sha.Trim()}");
        if (changed is null)
            return;

        IReadOnlyList<string> tests =
            AssertionRatchet.AssertionFilesIn(changed.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        if (tests.Count == 0)
            return;

        Console.WriteLine($"[ratchet] but the commit that shipped it touched {tests.Count} assertion file(s):");
        foreach (string file in tests)
            Console.WriteLine("    " + file);

        Console.WriteLine($"[ratchet] so the assertions likely exist - naming {id} in one of those pays it.");
    }

    /// <summary>One git command, or null where git cannot answer for any reason at all.</summary>
    private static string? Git(string root, string arguments)
    {
        try
        {
            using var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (git is null)
                return null;

            string output = git.StandardOutput.ReadToEnd();
            return git.WaitForExit(10_000) && git.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No git on PATH. The count does not need one; only this line did.
            return null;
        }
    }

    private static int Controllers()
    {
        using var sdl = new SdlThread();

        SdlStart start = sdl.Start(TimeSpan.FromSeconds(10));
        if (start != SdlStart.Started)
        {
            Console.Error.WriteLine($"SDL did not start ({start}): {sdl.Error}");
            return 1;
        }

        string report = "";
        if (!sdl.Invoke(
                () => report = PadReport.Format(
                    Gamepads.NumJoysticks(), Gamepads.Pads(), Gamepads.LinkedVersion()),
                TimeSpan.FromSeconds(10)))
        {
            Console.Error.WriteLine("the SDL thread did not answer");
            return 1;
        }

        Console.Write(report);
        sdl.Stop(TimeSpan.FromSeconds(10));
        return 0;
    }

    /// <summary>
    /// PP219: `--capture-controller`, which opens the first mappable pad and prints what pressing
    /// it produces.
    ///
    /// The tool that found the defect PP219 files. Opening and closing both happen on the SDL
    /// thread, because the handle belongs to whichever thread called SDL_Init - the same rule as
    /// everything else in <see cref="Gamepads"/>.
    /// </summary>
    private static int CaptureController(TimeSpan window, bool analog)
    {
        // OFF unless asked for, which is the opposite of what a diagnostic usually does. Measured
        // on a DualSense: twenty seconds with it on produced 1684 tokens, one unbroken run of 798
        // being the left stick's Y axis alone, and the eight deliberate presses could not be found
        // by eye. Showing everything here shows nothing.
        var arm = new MappingCapture { AllowAnalogStick = analog };
        var taken = new List<string>();
        var ranges = new AxisRanges();

        // PP222. The triggers are axes, so with the opt-in off the capture never names one - and a
        // person pressing R2 gets no sign it was seen. They are shown separately because they can
        // be: a trigger at rest emits nothing, so it is the sticks alone that flood.
        var pulls = analog ? null : new TriggerPulls();
        object gate = new();

        // The SDL thread ENQUEUES and the main thread prints. SdlThread's own note says the
        // callback runs between two polls and must not block, and writing to a console is I/O -
        // so the token crosses a queue rather than reaching stdout on the thread that owns SDL.
        var pending = new System.Collections.Concurrent.ConcurrentQueue<string>();

        using var sdl = new SdlThread(ev =>
        {
            string? token;
            lock (gate)
            {
                token = arm.Offer(ev);

                // Re-armed, so one run records a sequence. The screen does not do this.
                if (token is not null)
                    arm.Arm();

                // A trigger the capture was not allowed to name still gets one line per PULL.
                token ??= pulls?.Pull(ev);

                // The RANGE is kept whatever the capture did with it, because the question it
                // answers - is this stick resting off centre, or merely not still - is about the
                // values and not about the bindings.
                ranges.Observe(ev);
            }

            if (token is not null)
                pending.Enqueue(token);
        });

        SdlStart start = sdl.Start(TimeSpan.FromSeconds(10));
        if (start != SdlStart.Started)
        {
            Console.Error.WriteLine($"SDL did not start ({start}): {sdl.Error}");
            return 1;
        }

        SdlPad? pad = null;
        IntPtr handle = IntPtr.Zero;

        sdl.Invoke(
            () =>
            {
                // PP228: the count answers absence; FirstOrDefault over a struct answers with a
                // zero value that is not null, and this report would then name a pad with no name.
                IReadOnlyList<SdlPad> pads = Gamepads.Pads();
                if (pads.Count == 0)
                    return;

                pad = pads[0];
                handle = Gamepads.OpenController(pads[0].Index);
            },
            TimeSpan.FromSeconds(10));

        lock (gate)
            arm.Arm();

        // Before the listening, not after it: a window whose start nobody can see is one where a
        // silent result cannot be told from a mistimed press.
        Console.Write(CaptureReport.Opening(pad, handle != IntPtr.Zero));
        Console.WriteLine($"listening for {window.TotalSeconds:0}s - press the pad");
        Console.Out.Flush();

        DateTime until = DateTime.UtcNow + window;
        while (DateTime.UtcNow < until)
        {
            while (pending.TryDequeue(out string? token))
            {
                taken.Add(token);
                Console.WriteLine(CaptureReport.Live(token));
                Console.Out.Flush();
            }

            Thread.Sleep(20);
        }

        while (pending.TryDequeue(out string? last))
            taken.Add(last);

        Console.Write(CaptureReport.Summary(taken, window));

        // The axis ranges last, and whether or not the capture was allowed to bind one: a stick
        // that never became a token still says here where it was resting.
        IReadOnlyList<(string Axis, short Low, short High, int Samples)> seen;
        lock (gate)
            seen = ranges.Seen();

        if (seen.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("axes that moved:");
            foreach ((string axis, short low, short high, int samples) in seen)
                Console.WriteLine(CaptureReport.AxisRange(axis, low, high, samples));
        }

        // And where they are NOW, which no event can say: SDL raises one when the value CHANGES,
        // so a stick resting off centre and still is invisible to everything above this line.
        if (handle != IntPtr.Zero)
        {
            var resting = new List<string>();
            sdl.Invoke(
                () =>
                {
                    foreach ((int axis, string name) in Gamepads.ControllerAxis.All)
                    {
                        resting.Add(RestingAxes.Line(
                            name,
                            Gamepads.AxisNow(handle, axis),
                            Gamepads.ControllerAxis.IsTrigger(axis)));
                    }
                },
                TimeSpan.FromSeconds(10));

            Console.WriteLine();
            Console.WriteLine("resting now:");
            foreach (string line in resting)
                Console.WriteLine(line);
        }

        sdl.Invoke(() => Gamepads.CloseController(handle), TimeSpan.FromSeconds(10));
        sdl.Stop(TimeSpan.FromSeconds(10));
        return 0;
    }

    /// <summary>The SDL thread the mapping screen runs on, held for the application's lifetime.</summary>
    private SdlThread? mappingSdl;

    /// <summary>The open pad, closed on the way out.</summary>
    private IntPtr mappingPad;

    /// <summary>
    /// PP223: `--map-controller`, the mapping screen against a real pad.
    ///
    /// The first screen this host has ever shown, and it is behind a flag rather than in the
    /// window's ordinary path: PP13's console list is what MainWindow is filed to open with, and
    /// putting this there instead would be a navigation decision taken by a diagnostic.
    ///
    /// The marshal is the dispatcher, which is the whole reason PP217 made it a parameter: there
    /// it is a test's inline call, here it is the thread the bindings live on, and the session does
    /// not know the difference.
    /// </summary>
    /// <summary>
    /// PP226: `--capture-mapping [path]`, the mapping screen as a PNG with no window shown.
    ///
    /// Rendered from a FIXTURE rather than from whatever is plugged in, so the picture is the same
    /// on every machine and needs no pad - the fixture being a real DualSense's mapping string,
    /// captured through <see cref="Gamepads.Pads"/> rather than invented.
    /// </summary>
    private static int CaptureMapping(string path)
    {
        ControllerMappingDocument? document =
            ControllerMappingDocument.Parse(ScreenCapture.SampleDualSense, ScreenCapture.SampleName);

        if (document is null)
        {
            Console.Error.WriteLine("the sample mapping would not parse");
            return 1;
        }

        var screen = new ControllerMappingViewModel();
        screen.Show(document);

        var view = new Views.ControllerMappingView { DataContext = screen };

        // 23 rows at 52 pixels plus the chrome: tall enough that the picture is the whole screen
        // rather than the top of it, which is what a window would have scrolled.
        byte[] png = ScreenCapture.Png(view, 900, 1500);
        File.WriteAllBytes(path, png);

        Console.WriteLine($"{png.Length} bytes -> {path}");
        Console.WriteLine($"  background from {ScreenCapture.BackgroundSource}");
        return 0;
    }

    /// <summary>
    /// Which composition demo `--dcomp-demo` opens, which is two different questions and one flag.
    ///
    /// PP163: --topmost is the CONTROL on the one-layer window, not a mode. That window's own
    /// arrangement is false - the visual behind the window's content - and the reading taken on
    /// 2026-08-23 was solid red over the whole client area, which is neither of the outcomes
    /// DcompDemo predicted for it. Running the same tree with true was what told "the flag is
    /// ignored on this HWND" apart from "WPF drew nothing"; PP319 then found it is neither, because
    /// that flag orders the tree against CHILD WINDOWS and a redirection bitmap is not one.
    ///
    /// PP322: --layers is the reading PP319's choice still owes. The one-layer window has been read
    /// and its answer is recorded; this one puts the SECOND visual over the plane, which is the
    /// arrangement the choice actually names and which nothing has looked at. --topmost does not
    /// reach it - after PP319 it is a control for nothing.
    /// </summary>
    private static int DcompDemo(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--layers", StringComparison.OrdinalIgnoreCase)))
            return Views.DcompDemo.RunLayers();

        bool topmost = args.Any(a => string.Equals(a, "--topmost", StringComparison.OrdinalIgnoreCase));
        return Views.DcompDemo.Run(topmost);
    }

    /// <summary>The fallback window, for the case where there is somehow no window to fill.</summary>
    private MainWindow OpenedByHand()
    {
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        return window;
    }

    private void StartMappingScreen()
    {
        // PP224: the window StartupUri opened, which by now exists - this runs queued behind it.
        // If it somehow does not, one is opened rather than leaving without a word, which is the
        // failure this whole task is about.
        MainWindow window = this.MainWindow as MainWindow ?? OpenedByHand();

        // The session does not exist yet and the thread needs its callback now, so the callback
        // reads a variable rather than closing over a value. Nothing can arrive through it before
        // the pad is opened, which is the last thing this method does.
        ControllerMappingSession? session = null;

        mappingSdl = new SdlThread(ev => session?.OnSdlEvent(ev));
        if (mappingSdl.Start(TimeSpan.FromSeconds(10)) != SdlStart.Started)
        {
            window.ShowMessage($"SDL did not start: {mappingSdl.Error}");
            return;
        }

        // PP228: the COUNT, because FirstOrDefault over a struct answers with its zero value and
        // that value is not null - so an `is not SdlPad` beneath it can only pass, and what
        // reaches the parser is a default whose Mapping is null.
        IReadOnlyList<SdlPad> pads = [];
        mappingSdl.Invoke(() => pads = Gamepads.Pads(), TimeSpan.FromSeconds(10));

        if (pads.Count == 0)
        {
            window.ShowMessage("no pad SDL can map - plug one in and start again");
            return;
        }

        SdlPad pad = pads[0];

        // The pad's own mapping string IS the document, and its name is the fallback for the `*`
        // a DualSense actually sends where a name would be.
        ControllerMappingDocument? document = ControllerMappingDocument.Parse(pad.Mapping, pad.Name);
        if (document is null)
        {
            window.ShowMessage($"could not parse the mapping: {pad.Mapping}");
            return;
        }

        session = new ControllerMappingSession(document, work => Dispatcher.BeginInvoke(work));

        var view = new Views.ControllerMappingView { DataContext = session.Screen };
        view.CaptureRequested += session.OpenCapture;
        view.CloseCaptureRequested += session.CloseCapture;
        view.ApplyRequested += session.Apply;

        window.ShowScreen(view);

        // Opened LAST, which is PP219's rule used as a switch: until this call the pad delivers
        // nothing, so the screen is up and wired before the first event can reach it.
        mappingSdl.Invoke(
            () => mappingPad = Gamepads.OpenController(pad.Index), TimeSpan.FromSeconds(10));

        if (mappingPad == IntPtr.Zero)
            window.ShowMessage($"{pad.Name} enumerated and would not open");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (mappingSdl is not null)
        {
            mappingSdl.Invoke(() => Gamepads.CloseController(mappingPad), TimeSpan.FromSeconds(5));
            mappingSdl.Stop(TimeSpan.FromSeconds(5));
            mappingSdl.Dispose();
        }

        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // PP306: the list, and the refusal, before anything dispatches.
        //
        // Both first, and the refusal is the half that matters. Every match below is an `Any` that
        // falls through when it misses, so --self-test used to reach StartupUri and open the
        // application - on a machine with no console, that was the entire report. An argument
        // spelled like a flag and answered by nothing is now an exit code and the list.
        if (HostCommandLine.IsHelp(e.Args))
        {
            ReopenStdOut();
            Console.Write(HostCommandLine.Usage());
            Environment.Exit(0);
        }

        IReadOnlyList<string> unknown = HostCommandLine.Unrecognised(e.Args);
        if (unknown.Count > 0)
        {
            ReopenStdOut();
            Console.Error.WriteLine($"ChiakiNg: no such flag: {string.Join(", ", unknown)}");
            Console.Error.WriteLine();
            Console.Error.Write(HostCommandLine.Usage());
            Environment.Exit(2);
        }

        if (e.Args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            ReopenStdOut();
            // Environment.Exit and not Shutdown(): base.OnStartup has not run, so StartupUri has
            // not opened MainWindow, and there is no message loop to unwind. Returning instead
            // would open the window behind the test output.
            Environment.Exit(SelfTest.Run());
        }

        if (e.Args.Any(a => string.Equals(a, "--controllers", StringComparison.OrdinalIgnoreCase)))
        {
            ReopenStdOut();
            Environment.Exit(Controllers());
        }

        // PP304: the sizes the backlog states, and the command that corrects each stale one.
        if (e.Args.Any(a => string.Equals(a, "--recount", StringComparison.OrdinalIgnoreCase)))
        {
            ReopenStdOut();
            Environment.Exit(Recount());
        }

        // PP305: the debt PP38 counts, in the form it can be paid in. PP311: with an id after it,
        // where that id is named instead - which is how a payment is audited.
        int ratchet = Array.FindIndex(
            e.Args, a => string.Equals(a, "--ratchet", StringComparison.OrdinalIgnoreCase));

        if (ratchet >= 0)
        {
            ReopenStdOut();

            string? id = ratchet + 1 < e.Args.Length ? e.Args[ratchet + 1] : null;
            Environment.Exit(id is null ? Ratchet() : RatchetFor(id));
        }

        // PP284: the window PP163's last question is answered by looking at. Here rather than after
        // base.OnStartup for the same reason --selftest is: StartupUri would open MainWindow behind
        // it, and this demo is about what one window composes.
        if (e.Args.Any(a => string.Equals(a, "--dcomp-demo", StringComparison.OrdinalIgnoreCase)))
        {
            ReopenStdOut();
            Environment.Exit(DcompDemo(e.Args));
        }

        if (e.Args.Any(a => string.Equals(a, "--capture-controller", StringComparison.OrdinalIgnoreCase)))
        {
            ReopenStdOut();

            // --analog is opt-in: see the note on CaptureController for the measurement that made
            // it so. The axis RANGES are printed either way, because a stick that never became a
            // token still says where it was resting.
            bool analog = e.Args.Any(a => string.Equals(a, "--analog", StringComparison.OrdinalIgnoreCase));
            Environment.Exit(CaptureController(TimeSpan.FromSeconds(20), analog));
        }

        int capture = Array.FindIndex(
            e.Args, a => string.Equals(a, "--capture-mapping", StringComparison.OrdinalIgnoreCase));

        if (capture >= 0)
        {
            ReopenStdOut();

            string path = capture + 1 < e.Args.Length ? e.Args[capture + 1] : "mapping.png";
            Environment.Exit(CaptureMapping(path));
        }

        base.OnStartup(e);

        if (e.Args.Any(a => string.Equals(a, "--map-controller", StringComparison.OrdinalIgnoreCase)))
        {
            // QUEUED rather than called. StartupUri creates MainWindow after this method returns,
            // so running here would find no window - which is exactly what the first version did,
            // silently. Clearing StartupUri instead is not an option: the property refuses null
            // and the process dies before anything is drawn at all.
            Dispatcher.BeginInvoke(StartMappingScreen, DispatcherPriority.ApplicationIdle);
        }
    }
}
