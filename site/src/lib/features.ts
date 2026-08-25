import type { Rich } from "./site-content";
import { dotnet, version } from "./product";

// The five depth pages, one record each. The route, the title and the description are all
// read off the same record (in routes.tsx), so a new page cannot ship half declared or
// untitled: add a record here and its route, its head and its page all appear together, or
// none of them do.

export interface FeatureSection {
  heading: string;
  body?: Rich;
  list?: Rich[];
}

export interface FeatureRecord {
  slug: string;
  title: string;
  description: string;
  ogTitle: string;
  ogDescription: string;
  eyebrow: string;
  heading: string;
  lead: Rich;
  /** a diagram key resolved to markup in the page component */
  figure?: "composition" | "window" | "mapping";
  sections: FeatureSection[];
}

export const features: FeatureRecord[] = [
  {
    slug: "picture",
    title: "The picture: ten bits, composed rather than drawn",
    description:
      "WPF's image path refuses anything wider than eight bits per channel, so the video is composed beside the window instead: libplacebo into a shared Direct3D 11 texture, and DirectComposition with the overlay above the plane.",
    ogTitle: "PPortal: the picture",
    ogDescription:
      "Ten bits through DirectComposition, an overlay that survives them, and pacing taken from the display.",
    eyebrow: "The video path",
    heading: "The picture",
    lead: [
      "The straightforward way to get a decoded frame into a WPF window is the one thing that cannot carry HDR: it accepts eight bits per channel and nothing wider. So the frame does not go through it. libplacebo renders into a shared Direct3D 11 texture, and ",
      { b: "DirectComposition" },
      " puts that texture on screen as a ten-bit plane, with the window's own overlay composed above it.",
    ],
    figure: "composition",
    sections: [
      {
        heading: "Why the order of the two layers matters",
        body: [
          "A composed video plane and a WPF overlay can be arranged three ways, and two of them lose something: put the plane on top and the stream HUD and the in-stream menu are behind it, invisible. The arrangement in use puts the overlay above the plane, which is the only one that keeps the picture at ten bits ",
          { i: "and" },
          " keeps everything drawn over it. All three were built and looked at on a real window before one was chosen.",
        ],
      },
      {
        heading: "Paced by the display",
        list: [
          [
            "The present interval is the refresh rate of the display the window is actually on, read from the system rather than assumed to be 60.",
          ],
          [
            "A display that changes rate during a session changes the pacing with it, so a switch to a different mode does not leave the stream a frame off for the rest of the evening.",
          ],
          [
            "A present that misses its deadline catches up rather than accumulating the delay, because a stream that slips once should not stay slipped.",
          ],
        ],
      },
      {
        heading: "The renderer is yours",
        body: [
          "libplacebo's scalers, debanding, tone curves and colour mapping are exposed on two screens: presets for the settings nobody wants to learn, and every slider underneath for the people who do. The values are stored in the units the renderer takes, so what a slider says and what the renderer receives are the same number.",
        ],
      },
    ],
  },
  {
    slug: "controllers",
    title: "Controllers: the pad works in the menus too",
    description:
      "Directional focus on every screen as an attached property, SDL device and binding strings read on the thread that owns them, and a mapping screen that captures the press you meant on any pad SDL can map, an Xbox controller included.",
    ogTitle: "PPortal: controllers",
    ogDescription:
      "Focus navigation on every screen, SDL bindings read live, a mapping capture that takes the press, and any pad SDL can map.",
    eyebrow: "Input",
    heading: "Controllers",
    lead: [
      "This is used from a sofa, so a screen that needs a mouse is a screen that cannot be reached. Focus moves by the D-pad and the sticks everywhere, not only in the stream, and the button that moves it is the one the mapping screen says it is.",
    ],
    figure: "mapping",
    sections: [
      {
        heading: "Focus is a property, not a widget",
        body: [
          "Directional navigation is attached to ordinary WPF controls rather than built into a private set of six lookalikes, so a screen added later gets it by declaring it. The rule that decides where focus goes is kept apart from the handler that moves it, which is why the same rule holds on a settings row, a console row and a mapping cell.",
        ],
      },
      {
        heading: "The pad in the room, not the pad in a config file",
        list: [
          [
            "The device list, the button events and the axis positions are read on the thread that owns SDL, so a controller plugged in mid-session is a controller that works.",
          ],
          [
            "Bindings come from the device's own strings, so a pad that already works elsewhere on the machine arrives mapped.",
          ],
          [
            "The capture takes a press rather than waiting for movement, and a stick resting off centre reads as resting rather than as input.",
          ],
          [
            "Triggers are captured without turning on the axes, because the sticks flood the log and the trigger is what you were trying to bind.",
          ],
        ],
      },
      {
        heading: "Any pad SDL can map, an Xbox one included",
        body: [
          "SDL is the one native dependency this port did not move. Nothing about the device layer was tied to Qt, so it is called directly rather than through the shim the rest of the C goes through, which means the pads that work here are the pads SDL already knows. An Xbox controller enumerates, arrives carrying its own binding string, and reaches the mapping screen with every row already filled in.",
        ],
        list: [
          [
            "The rows are named for the PlayStation pad: Cross, Moon, Box, Pyramid, the shoulders, the triggers. What sits on each row is whatever your controller calls that control, so rebinding an Xbox pad is the same screen and the same capture as rebinding a DualSense.",
          ],
          [
            "A device SDL has no mapping for is left out of the list rather than shown with empty rows, because a pad with no bindings gives the screen nothing to draw.",
          ],
          [
            "Haptics fold to rumble on a pad with no haptic motors. The console sends a stereo haptic stream a DualSense plays as sound, and a controller that cannot play one gets a single strength scaled by your intensity setting instead, which is what a PS4 pad already feels during a PS5 session.",
          ],
          [
            "What is hardware stays hardware: adaptive triggers and the touchpad are not emulated on a pad that has neither, though the D-pad can be turned into a finger on the touchpad for the ones without.",
          ],
        ],
      },
      {
        heading: "When the machine disagrees with you",
        body: [
          "Running the application with ",
          { code: "--controllers" },
          " prints what SDL sees right now, and ",
          { code: "--capture-controller" },
          " logs presses for twenty seconds so a pad that reports nothing can be told apart from a pad that reports the wrong thing.",
        ],
      },
    ],
  },
  {
    slug: "screens",
    title: "The screens: the console list, the dialogs and nine tabs of settings",
    description:
      "Discovered, manual and hidden consoles in one list; registration dialogs that keep the rules of each console generation apart; and a settings screen that stores what it always stored.",
    ogTitle: "PPortal: the screens",
    ogDescription:
      "One console list from three sources, registration that validates, and settings that did not get redesigned.",
    eyebrow: "The window",
    heading: "The screens",
    lead: [
      "WPF on the built-in Fluent theme with ",
      { code: 'ThemeMode="System"' },
      ", so light and dark follow the OS with no extra package. What the screens do was reproduced rather than reimagined, because a screen that changes shape in the same move that changes framework cannot be judged against the one it replaced.",
    ],
    figure: "window",
    sections: [
      {
        heading: "The console list is the front door",
        list: [
          [
            "Three sources in one view: what discovery found on the network, what you added by address, and what you hid. A hidden console says so rather than vanishing and being registered again a month later.",
          ],
          [
            "A row offers what its state allows. Ready streams, standby wakes, and a console that did not answer offers a retry rather than a dead button.",
          ],
          [
            "Removing a console branches three ways, and one of those ways is to say nothing, which is the branch that is easy to lose in a rewrite.",
          ],
        ],
      },
      {
        heading: "Registration, and the two generations",
        body: [
          "Registration is one dialog on its own path and three more reached from elsewhere, each validating what it takes. The PIN rules for the two console generations are kept as two rules, because they are two rules, and the identifier a PS4 wants is not the one a PS5 wants.",
        ],
      },
      {
        heading: "Settings: nine tabs, stored the way they were stored",
        list: [
          [
            "General, Video, Stream, Audio, Consoles, Keys, Controllers, Remote and Config, committed when a field is finished rather than on every keystroke.",
          ],
          [
            "Where a stored value is not the label shown, the stored value stayed. A setting that reads back differently after a rewrite is a bug found by the person who set it, six months later.",
          ],
          [
            "Audio levels keep their unit conversions, resolution keeps its three representations, and the Consoles tab still deletes by position and unhides by hardware address, because those are what the store holds.",
          ],
        ],
      },
      {
        heading: "Signing in to PSN",
        body: [
          "The login runs in a WebView2 panel on the runtime Windows already carries, so there is no second browser inside the download. What the panel clears when it closes is a decision the screen makes rather than a browser default it inherits.",
        ],
      },
    ],
  },
  {
    slug: "latency",
    title: "Latency: every stage timed, every session written down",
    description:
      "Receive, reorder dwell, reassemble, error correction and decoder send-to-pull are timed per session, alongside a latency floor and the configuration that produced them.",
    ogTitle: "PPortal: latency",
    ogDescription:
      "Per-stage timings, a latency floor, and a comparison tool that refuses to compare two different shapes.",
    eyebrow: "Measured",
    heading: "Latency",
    lead: [
      "Every remote play client claims to be fast. This one leaves evidence: each session appends a line to a record on your own machine, naming the decoder, the renderer, the requested bitrate and both loss settings, with the timing of each stage of the frame path beside them.",
    ],
    sections: [
      {
        heading: "What is timed",
        list: [
          [
            { b: "Receive." },
            " Where the packet lands. The step is budgeted at zero bytes of allocation per packet, because a collection in the middle of a stream is a stutter with no visible cause.",
          ],
          [
            { b: "Reorder dwell." },
            " How long a packet waited for the ones in front of it, which is the difference between a network that is lossy and one that is merely late.",
          ],
          [{ b: "Reassemble." }, " Units back into the frame the encoder produced."],
          [{ b: "Error correction." }, " What it cost to rebuild what the network dropped."],
          [
            { b: "Decoder send-to-pull." },
            " The frame handed to the GPU and taken back, which is the stage a decoder choice actually changes.",
          ],
          [
            { b: "A latency floor." },
            " Input queueing plus the console's own reported round trip, in milliseconds: the part of glass-to-glass the client can see without a camera.",
          ],
        ],
      },
      {
        heading: "Two runs, one table",
        body: [
          { code: "compare-baselines" },
          " reads two records and prints p50, p99 and maximum per stage with the difference between them. It refuses a comparison whose two halves do not have the same field set, because a row that gained a field silently is two questions and one answer.",
        ],
      },
      {
        heading: "It stays on your machine",
        body: [
          "The record is a file in your own application data. Nothing is uploaded, there is no account, and no part of the application asks for one. The reason the numbers exist is so that two builds can be compared here, on the hardware that has to run them.",
        ],
      },
    ],
  },
  {
    slug: "setup",
    title: "Setup: it reads the consoles you already registered",
    description:
      "Registered consoles, manual and hidden hosts, controller mappings and the PSN token are read from the existing store, resolved against the Windows profile you are actually signed into.",
    ogTitle: "PPortal: setup",
    ogDescription:
      "Your consoles, mappings and PSN token come across, and the installer sits beside what you have.",
    eyebrow: "First run",
    heading: "Setup",
    lead: [
      "The first run of a remote play client is usually a setup: find the console, pair it, sign in, map the pad. PPortal skips it where it can, because you have already done all four somewhere else on this machine.",
    ],
    sections: [
      {
        heading: "What comes across",
        list: [
          ["Registered consoles, with the identifiers that make a pairing valid."],
          ["Manual hosts you added by address, and the ones you chose to hide."],
          ["Controller mappings, including both spellings a mapping key has had."],
          ["The PSN token, so signing in again is not the first thing you are asked to do."],
        ],
      },
      {
        heading: "From the profile you are actually using",
        body: [
          "The reader resolves the active profile first and derives every path from it, which is the difference between finding your consoles and finding none: a store read from the default location on a machine with a live profile is an empty list with no error attached.",
        ],
      },
      {
        heading: "The installer",
        body: [
          "Version ",
          { b: version() },
          ", x64, Windows 10 or 11, built against ",
          { b: dotnet() },
          ". It installs under an identifier of its own, so an existing chiaki-ng installation stays where it is instead of being upgraded in place, and the two can be run one after the other on the same machine.",
        ],
      },
    ],
  },
];
