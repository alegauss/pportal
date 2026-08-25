// The copy lives here and nowhere else. Every section component imports a value from this
// module and only renders it, so a claim is an array element a reviewer can check against
// the product rather than a string welded into the markup that displays it. The composition
// (which section, in which order, and the illustrative SVGs) lives in the JSX; this file is
// the words.
//
// Fragments carrying inline code or emphasis are modelled as a small tagged run list
// (`Rich`) rather than raw HTML, so a section renders them without dangerouslySetInnerHTML
// and the twin generator has a structure to convert rather than markup to parse.
//
// No figure below is typed. The version and the flag list come off the application's own
// source through ./product, because a number in a sentence is true the day it is written and
// wrong in silence afterwards.

import { dotnet, flagLines, version } from "./product";

export type Run =
  | string
  | { code: string }
  | { b: string }
  | { i: string };

export type Rich = Run[];

/* ------------------------------------------------------------------ meta + chrome */

export const meta = {
  title: "PPortal: PlayStation Remote Play, rebuilt for Windows",
  description:
    "A free PlayStation Remote Play client for Windows, built on .NET 10 and WPF. Ten-bit video through DirectComposition, hardware decode on whatever card you own, your consoles and pairings carried over, and no bundled browser.",
  og: {
    title: "PPortal",
    description:
      "PlayStation Remote Play as a Windows application: WPF on the system Fluent theme, ten-bit video through DirectComposition, and the decode path your machine actually has.",
    url: "https://alegauss.github.io/pportal/",
  },
} as const;

export const repoUrl = "https://github.com/alegauss/pportal";
export const parentUrl = "https://alegauss.github.io/";

// The release page rather than a file: the installer carries its version in its name, so
// there is no version-independent URL for the asset itself, and a hard-coded filename would
// return a 404 on the day the next version ships. `releases/latest` is the one link that
// cannot go stale, and it is also where the checksums live.
export const releasesUrl = `${repoUrl}/releases/latest`;

// The first three are sections of the landing page, and the nav is on every route, so each
// one carries the landing page's own URL in front of its anchor. A bare "#picture" is not a
// link from anywhere else: the browser sets the hash, finds no element of that id on a
// feature page, and does nothing at all. There is no router here, so this is an ordinary
// full load that lands on the section. The brand and the footer link home the same way.
export const navLinks = [
  { href: "/pportal/#picture", label: "The picture" },
  { href: "/pportal/#input", label: "Controllers" },
  { href: "/pportal/#window", label: "Screens" },
  { href: "/pportal/hardware/", label: "Hardware" },
] as const;

export const footer = {
  links: [
    { href: "/pportal/hardware/", label: "Hardware" },
    { href: repoUrl, label: "GitHub" },
    { href: releasesUrl, label: "Releases" },
    { href: `${repoUrl}/blob/main/CONTRIBUTOR_GUIDE.md`, label: "Contributing" },
    { href: `${repoUrl}/blob/main/COPYING`, label: "Licence" },
  ],
  disclaimer:
    "Unofficial community project. Not affiliated with, endorsed by, or sponsored by Sony Interactive Entertainment. “PlayStation”, “PS4” and “PS5” are trademarks of Sony Interactive Entertainment Inc. PPortal is free software under the AGPL-3.0, and it stands on the Chiaki and chiaki-ng projects, whose protocol work made it possible. © 2026 Alexandre Oliveira.",
} as const;

/* --------------------------------------------------------------- sponsor */

// Mirrors alegauss.github.io/sponsor.json, the canonical sponsor declaration for these
// projects. Transcribed here rather than fetched at runtime: this site is prerendered, and
// the whole point of naming a sponsor is that crawlers and readers meet it in the served
// HTML.
export const sponsor = {
  label: "Sponsored by",
  name: "Viglet",
  url: "https://www.viglet.org",
  siteLabel: "viglet.org",
  logo: "/pportal/viglet/viglet-logo.png",
  summary:
    "Open source search and content tools for organisations with a lot to publish. Run on your own servers, with no per-user licence.",
  products: [
    {
      name: "Viglet Turing ES",
      url: "https://turing.viglet.org",
      logo: "/pportal/viglet/turing-logo.png",
      inline:
        "so visitors find what they came for, with AI answers drawn only from your own content",
    },
    {
      name: "Viglet Shio CMS",
      url: "https://shio.viglet.org",
      logo: "/pportal/viglet/shio-logo.png",
      inline:
        "so a new page goes live the same day, reviewed and approved by your own team",
    },
  ],
} as const;

/* ------------------------------------------------------------------ hero */

export const hero = {
  badge: "Windows 10 / 11 · x64 · AGPL-3.0",
  titleLead: "PlayStation Remote Play,",
  titleAccent: "rebuilt as a Windows app.",
  sub: [
    "PPortal streams your PS5 or PS4 to a window that behaves like the rest of Windows: ",
    { b: "WPF on the system Fluent theme" },
    ", ten bits of colour composed by DirectComposition, and hardware decode on the card the machine already has. It reads the consoles and the pairings you have already set up, so the first run is not a setup.",
  ] as Rich,
  // No emoji on these three, and that is a writing rule rather than a taste: an emoji glued
  // to the front of a feature line is the most recognisable mannerism of a generated landing
  // page, and these strings are also bullets in the Markdown twin an agent reads.
  meta: [
    "Free software, no account",
    "Nothing is sent anywhere",
    "Your consoles come across",
  ],
  pills: [
    [{ b: dotnet() }, " · WPF, Fluent, follows the OS theme"] as Rich,
    ["Ten-bit video through ", { b: "DirectComposition" }] as Rich,
    [{ b: "d3d11va" }, ", Vulkan and CUDA decode"] as Rich,
    ["One self-contained ", { b: "executable" }] as Rich,
  ],
};

/* ------------------------------------------------------- hero: the frame path */

// The hero is a frame's journey rather than a feature list, because latency is what this
// application is judged on and the path is where latency is spent. Rendered as an
// autoplaying transcript that scrolls its own panel.
//
// The one measurement on it is attributed to the machine it was taken on. A number without
// the card beside it is folklore, and this project's own hardware note says so.

export const frameTrip = {
  eyebrow: "What a frame goes through",
  question: "from the packet the console sent to the light your display makes",
  steps: [
    {
      stage: "receive",
      tag: "0 bytes",
      note: "The packet arrives on the transport, and the receive step spends no allocation on it, because a garbage collection in the middle of a stream is a stutter with no cause on screen.",
    },
    {
      stage: "reorder",
      tag: "timed",
      note: "UDP arrives out of order, so packets wait in a reorder queue. How long each one waits is measured, since a late frame and a lost frame look identical from the sofa.",
    },
    {
      stage: "reassemble",
      tag: "timed",
      note: "The units of one frame are joined back into the frame the encoder produced.",
    },
    {
      stage: "correct",
      tag: "timed",
      note: "Erasure coding rebuilds what the network dropped, so a missing packet costs arithmetic instead of a whole frame.",
    },
    {
      stage: "decode",
      tag: "your GPU",
      note: "H.264 or HEVC through d3d11va, Vulkan or CUDA, chosen from what the machine reports rather than from what the vendor is.",
    },
    {
      stage: "present",
      tag: "47 µs",
      note: "libplacebo renders into a shared texture and DirectComposition puts it on screen, paced by the display's own refresh rate.",
    },
  ],
  measured: "47 µs median present, 60 fps held",
  where: "measured on an RTX 4060",
  note: [
    "Every stage above is timed on every session and written to a file on your machine, so a build that got slower can be compared against the one before it rather than argued about. ",
    { code: "compare-baselines" },
    " prints the p50, p99 and maximum of each stage, and the difference between two runs.",
  ] as Rich,
};

/* ------------------------------------------------------------------ why */

export const why = {
  eyebrow: "Why it was rebuilt",
  heading: "A remote play client that is a Windows program",
  intro: [
    "The stream is the hard part and it was already solved by the Chiaki and chiaki-ng projects. What was left was everything around it: a window that follows the OS theme, a decode path chosen on the machine's own answer, and an installer that does not carry a second browser. PPortal is that half, rewritten on ",
    { b: dotnet() },
    ".",
  ] as Rich,
  cards: [
    {
      icon: "🪟",
      title: "It looks like Windows because it is Windows",
      body: [
        "WPF on the built-in Fluent theme, with ",
        { code: 'ThemeMode="System"' },
        ", so light and dark follow the OS with no extra package and no theme of its own to keep current.",
      ] as Rich,
    },
    {
      icon: "🧩",
      title: "Nothing bundled that Windows already ships",
      body: [
        "Signing in to PSN uses the ",
        { b: "WebView2" },
        " runtime that is already on the machine, instead of carrying a copy of Chromium inside the download for one login screen.",
      ] as Rich,
    },
    {
      icon: "📥",
      title: "Your setup is already there",
      body: [
        "Registered consoles, manual and hidden hosts, controller mappings and the PSN token are read from the store you already have, resolved against the profile you are actually signed into.",
      ] as Rich,
    },
    {
      icon: "🎯",
      title: "It runs on the card you own",
      body: [
        "The tuning goes to NVIDIA first, and first is not only: what must keep working on a machine with no NVIDIA card is written down as a contract rather than left as an intention.",
      ] as Rich,
    },
  ],
};

/* ------------------------------------------------------------------ the picture */

export const picture = {
  eyebrow: "The picture",
  headingRuns: ["Ten bits, and an overlay that survives them"] as Rich,
  intro: [
    "The obvious way to get a decoded frame into a WPF window refuses anything wider than eight bits per channel, which rules out HDR before the stream starts. So the video does not go through WPF at all: libplacebo renders it into a shared Direct3D 11 texture, and ",
    { b: "DirectComposition" },
    " puts that texture on screen as a ten-bit plane with the window's own overlay composed above it.",
  ] as Rich,
  cards: [
    {
      icon: "🎞",
      title: "The overlay is above the video, not behind it",
      body: [
        "Of the three arrangements that compose, one keeps both the ten-bit plane and the stream HUD visible. That is the one it uses, and the other two were built and looked at before it was chosen.",
      ] as Rich,
    },
    {
      icon: "⏱",
      title: "Paced by the display, not by a guess",
      body: [
        "The present interval comes from the refresh rate of the display the window is on, and a display that changes rate mid-stream changes the pacing with it rather than drifting a frame at a time.",
      ] as Rich,
    },
    {
      icon: "🎛",
      title: "The renderer is yours to tune",
      body: [
        "libplacebo's scalers, debanding, colour mapping and tone curves are exposed on two screens, with presets for the settings nobody wants to learn and every slider underneath for the people who do.",
      ] as Rich,
    },
  ],
};

/* ------------------------------------------------------------------ input */

export const input = {
  eyebrow: "Controllers",
  heading: "The pad is the primary input, including in the menus",
  intro: [
    "A remote play client is used from a sofa, so a screen that can only be reached with a mouse is a screen that cannot be reached. Focus moves by the D-pad and the sticks on every screen, and the button that moves it is the one the mapping screen says it is.",
  ] as Rich,
  list: [
    [{ b: "Focus navigation everywhere." }, " Directional focus is an attached property rather than a control of its own, so it applies to the ordinary WPF controls the screens are built from instead of a private widget vocabulary."] as Rich,
    [{ b: "Your own bindings." }, " The mapping screen reads the device's real binding strings through SDL, captures a press against the slot you picked, and writes the mapping back."] as Rich,
    [{ b: "A pad that is plugged in now." }, " The device list, the button events and the axis positions are read on the thread that owns SDL, so a controller connected mid-session is a controller that works."] as Rich,
    [{ b: "Not only a PlayStation pad." }, " SDL is called directly rather than rewritten, so anything it can map arrives mapped, an Xbox controller included, and the haptics the console sends fold to plain rumble on a pad with no haptic motors."] as Rich,
    [{ b: "Keyboard too." }, " Every key binding is a row in the settings, for the machine where the pad is in another room."] as Rich,
  ],
  terminalTitle: "pportal.exe --help",
  helpLead: "PPortal opens the application when you run it with nothing. The rest are the diagnostics for when a pad, a driver or a machine disagrees with you:",
  helpNote: "An unrecognised flag is refused with this list and a non-zero exit, rather than silently opening the window.",
};

/* ------------------------------------------------------------------ the window */

export const windowSection = {
  eyebrow: "The screens",
  heading: "The front door, the dialogs, and every setting",
  intro: [
    "The console list is where the application opens: the consoles discovery found, the ones you added by address, and the ones you hid, merged into one list that says which is which. Everything else is reached from it.",
  ] as Rich,
  caption: [
    "The console list. Discovery, manual hosts and hidden consoles in one view, each row saying what it can do rather than what it is.",
  ] as Rich,
  statesEyebrow: "Console states",
  statesHeading: "A row offers what its state allows",
  states: [
    {
      kind: "ready" as const,
      title: "Ready",
      body: [
        "The console answered and is awake, so the row streams. This is the only state where that button does anything, and it is the only state where it is offered.",
      ] as Rich,
    },
    {
      kind: "standby" as const,
      title: "Standby",
      body: [
        "The console answered and is asleep. The row wakes it, and the wake and the connection are two steps rather than one that silently retries.",
      ] as Rich,
    },
    {
      kind: "away" as const,
      title: "Not found",
      body: [
        "Nothing answered at that address. The console you registered stays in the list saying so, because a console that disappears when the network hiccups is one you register again.",
      ] as Rich,
    },
  ],
  detailsEyebrow: "Settings",
  detailsHeading: "Nine tabs, and none of them invented",
  detailsList: [
    [{ b: "General, Video, Stream, Audio, Consoles, Keys, Controllers, Remote and Config." }, " Each tab keeps the rules its own store spells, including the ones that are inconsistent, because a setting that reads back differently after the rewrite is a bug the user finds and not a tidy-up."] as Rich,
    [{ b: "Written when you finish." }, " A setting is committed when the field is done rather than on every keystroke, which is what the screen it replaces did."] as Rich,
    [{ b: "Registration is a dialog with rules." }, " Manual host, console PIN and profile each validate what they take, and the PIN rules for the two console generations stay apart because they are not the same rule."] as Rich,
    [{ b: "Signing in to PSN." }, " The login runs in a WebView2 panel, and what it clears when it closes is a decision the screen makes explicitly rather than a browser default."] as Rich,
  ],
};

/* ------------------------------------------------------------------ measured */

export const measured = {
  eyebrow: "Measured, not argued",
  heading: "Every session writes down what it did",
  intro: [
    "Latency claims are cheap. This one leaves evidence: each session appends one line to a record on your machine naming the decoder, the renderer, the requested bitrate and both loss settings, with the timing of every stage of the frame path beside them.",
  ] as Rich,
  rows: [
    ["The stages.", " Receive, reorder dwell, reassemble, error correction and the decoder's send-to-pull are timed separately, so a slower build says which stage and not just which build."],
    ["A latency floor.", " Input queueing plus the console's own reported round trip, in milliseconds, which is the part of glass-to-glass the client can actually see."],
    ["The configuration.", " A number without the settings that produced it compares with nothing, so the row carries them and the card the run was taken on."],
  ] as [string, string][],
  notes: [
    ["Two runs, one table.", " compare-baselines reads two records and prints p50, p99 and maximum per stage with the difference, and refuses a comparison whose two halves are not the same shape."],
  ] as [string, string][],
  note: [
    "The record is a file in your own application data. Nothing is uploaded, and there is no account to sign in to for any of it.",
  ] as Rich,
};

/* ------------------------------------------------------------------ the floor */

export const floor = {
  icon: "🧊",
  heading: "First is not only",
  body: [
    [
      "NVIDIA is where the tuning goes, so it is where the measurements and the vendor features land. That is a focus and not a requirement: a Windows machine with Intel graphics is an ordinary laptop, and an application that fails on one has not shipped, it has narrowed.",
    ] as Rich,
    [
      "So three things hold on a machine with no NVIDIA card at all: hardware decode through ",
      { code: "d3d11va" },
      ", a renderer that is Vulkan with an OpenGL fallback, and an SDR present that loads nothing from NVIDIA. A feature that is not available is simply not in the menu, because a dialog explaining that another card would be better is a requirement with a friendly voice.",
    ] as Rich,
  ],
  linkLabel: "The hardware page →",
  linkHref: "/pportal/hardware/",
};

/* ------------------------------------------------------------------ non-goals */

export const nonGoals = {
  eyebrow: "What it is not",
  heading: "Five things this deliberately does not do",
  intro: [
    "A list of what a program refuses is more useful than another list of what it does, because it is the half you cannot find out by reading the features.",
  ] as Rich,
  items: [
    {
      title: "No Linux, macOS, Android or Switch build",
      body: "The target is Windows by construction, not by default. Keeping a second platform alive would mean a second application with a shared name.",
    },
    {
      title: "No cross-platform UI toolkit",
      body: "A portable toolkit would give back none of the Win32, DXGI and WebView2 access the screens and the video path depend on, which is the whole reason WPF was chosen.",
    },
    {
      title: "No vendor path whose absence you can see",
      body: "A machine with no NVIDIA card keeps hardware decode, a neutral renderer and an SDR present. What is missing is missing from the menu, not explained in a warning.",
    },
    {
      title: "No redesign of what already worked",
      body: "Screens were reproduced rather than reimagined, down to the settings whose stored values do not match their labels, because a screen that changes shape cannot be judged against the one it replaced.",
    },
    {
      title: "No telemetry",
      body: "The session record exists so that two builds can be compared on your machine. It is a file on your disk, it is not uploaded, and there is no account anywhere in the application.",
    },
  ],
};

/* ------------------------------------------------------------------ download */

export const download = {
  eyebrow: "Get it",
  ctaShort: "Download",
  cta: "⬇ Download for Windows",
  secondary: "Checksums and release notes",
  heading: "One installer, and it sits beside what you already have",
  intro: [
    "Version ",
    { b: version() },
    ", x64, Windows 10 or 11. It installs under an identifier of its own, so an existing chiaki-ng installation is left where it is rather than upgraded in place, and the two can be run one after the other on the same machine.",
  ] as Rich,
  facts: [`Version ${version()}`, "Windows 10 / 11, x64", "AGPL-3.0"],
  note: [
    "The application is a single self-contained executable with the native libraries it loads placed beside it. Signing in to PSN uses the WebView2 runtime Windows already carries, so there is no browser inside the download.",
  ] as Rich,
};

/* ------------------------------------------------------------------ hardware */

// The page behind the banner. It is the product's own hardware note, written for a reader
// rather than for a contributor, and it keeps the note's least comfortable sentence: the
// measurements were all taken on one card.
export const hardware = {
  meta: {
    title: "Hardware: what PPortal asks of your GPU",
    description:
      "NVIDIA is where the tuning goes, and first is not only: hardware decode, a vendor-neutral renderer and an SDR present hold on a machine with no NVIDIA card, and a missing feature is missing from the menu.",
    ogTitle: "PPortal: hardware",
    ogDescription:
      "The floor a machine with no NVIDIA card keeps, and what absence is allowed to look like.",
  },
  eyebrow: "The contract",
  heading: "What this asks of your GPU",
  lead: [
    "The tuning goes to NVIDIA first: it is the decoder that gets measured, the upscaler that gets integrated, the path that gets a number. This page is the other half of that sentence, written down so it is a commitment rather than an intention. ",
    { b: "First is not only." },
    " A Windows machine with Intel graphics is an ordinary laptop, and an application that fails on one has not shipped, it has narrowed.",
  ] as Rich,
  floorHeading: "The floor",
  floorIntro: [
    "Three things hold on a machine with no NVIDIA card at all. They are not aspirations: a change that breaks one of them is a regression whether or not anything else improved.",
  ] as Rich,
  floor: [
    {
      what: "Hardware decode",
      how: [
        "d3d11va, chosen from what the runtime can actually open rather than from what the vendor is. CUDA is preferred only when the window reports an NVIDIA card ",
        { i: "and" },
        " the decoder is available; otherwise the choice falls to Vulkan and then to d3d11va.",
      ] as Rich,
    },
    {
      what: "A vendor-neutral renderer",
      how: [
        "Vulkan is the default and OpenGL is the fallback when Vulkan does not initialise. Neither is an NVIDIA path, and the fallback is taken on the driver's answer rather than on the name of the card.",
      ] as Rich,
    },
    {
      what: "An SDR present that loads nothing",
      how: [
        "Nothing on the present path asks for an NVIDIA library. The vendor features that would are asked for only where they are available, and never on the path a machine without them takes.",
      ] as Rich,
    },
  ],
  absenceHeading: "What absence looks like",
  absence: [
    [
      "Nothing, except that the option is not offered. A machine without CUDA does not see CUDA in the decoder list, because the list is built from what the runtime reports and what it can open. There is no error, no warning and no degraded mode to explain.",
    ] as Rich,
    [
      "That rule binds every vendor feature: the absence path is the quiet one. A dialog saying that an NVIDIA card would be better is a requirement with a friendly voice.",
    ] as Rich,
  ],
  evidenceHeading: "Where the numbers came from",
  evidence: [
    "Worth saying plainly, because a table always reads stronger than it is. All three hardware decode paths were measured rather than only the NVIDIA one, and paced at 60fps the CUDA path came ",
    { i: "last" },
    " of the three. Every one of those runs was taken on the same machine, an RTX 4060, which is named in each recorded result. So the floor above is held by design and by review, and the numbers beside it describe one card.",
  ] as Rich,
  backLabel: "← Back to the landing page",
};

/* ------------------------------------------------------------------ generated */

/** The flag list the terminal block prints, read off the application's own source. */
export const helpLines = flagLines;
