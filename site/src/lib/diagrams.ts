// The illustrative SVGs. These are figures, and two of them are dark mock-ups of an
// application that is itself dark on Windows, so they keep their own fixed palette rather
// than following the page theme; the themed .shot-frame around them is what places them on a
// light or a dark page. Kept as verbatim markup rather than converted to JSX, so the drawing
// stays identical to the hand-written original, and rendered with dangerouslySetInnerHTML
// because it is static, author-controlled content with no interpolation.

const UI = "Segoe UI Variable Text,Segoe UI,Inter,sans-serif";
const MONO = "JetBrains Mono,Consolas,monospace";

/** The video path, from the packet the console sent to the composed window. */
export const compositionDiagram = `
<svg viewBox="0 0 900 300" role="img" aria-label="A packet arrives from the console, is reordered, reassembled and error-corrected, decoded by the GPU, rendered by libplacebo into a shared Direct3D 11 texture, and composed by DirectComposition as a ten-bit video plane with the WPF overlay above it">
  <defs>
    <marker id="pp-arw" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M0 0 L10 5 L0 10 z" fill="#8f9dff"/>
    </marker>
  </defs>
  <rect width="900" height="300" rx="12" fill="#0a1020"/>

  <text x="30" y="34" fill="#8592b5" font-family="${UI}" font-size="12" font-weight="700" letter-spacing="1.4">THE NETWORK</text>
  <line x1="300" y1="16" x2="300" y2="230" stroke="#2a3556" stroke-width="1" stroke-dasharray="5 5"/>
  <text x="330" y="34" fill="#8592b5" font-family="${UI}" font-size="12" font-weight="700" letter-spacing="1.4">THE GPU</text>
  <line x1="614" y1="16" x2="614" y2="230" stroke="#2a3556" stroke-width="1" stroke-dasharray="5 5"/>
  <text x="644" y="34" fill="#8592b5" font-family="${UI}" font-size="12" font-weight="700" letter-spacing="1.4">THE COMPOSITOR</text>

  <rect x="30" y="56" width="240" height="34" rx="8" fill="#141b2e" stroke="#2a3556"/>
  <text x="150" y="78" text-anchor="middle" fill="#e9ecfb" font-family="${MONO}" font-size="13">console → UDP packet</text>

  <rect x="30" y="102" width="240" height="34" rx="8" fill="#141b2e" stroke="#2a3556"/>
  <text x="150" y="124" text-anchor="middle" fill="#e9ecfb" font-family="${MONO}" font-size="13">reorder · reassemble</text>

  <rect x="30" y="148" width="240" height="34" rx="8" fill="#141b2e" stroke="#2a3556"/>
  <text x="150" y="170" text-anchor="middle" fill="#e9ecfb" font-family="${MONO}" font-size="13">error correction</text>

  <path d="M150 90 L150 102" stroke="#8f9dff" stroke-width="1.6" marker-end="url(#pp-arw)"/>
  <path d="M150 136 L150 148" stroke="#8f9dff" stroke-width="1.6" marker-end="url(#pp-arw)"/>
  <path d="M270 165 L344 165" stroke="#8f9dff" stroke-width="1.6" marker-end="url(#pp-arw)"/>

  <rect x="344" y="56" width="240" height="34" rx="8" fill="#141b2e" stroke="#2a3556"/>
  <text x="464" y="78" text-anchor="middle" fill="#e9ecfb" font-family="${MONO}" font-size="13">libplacebo · D3D11</text>

  <rect x="344" y="102" width="240" height="34" rx="8" fill="#141b2e" stroke="#2a3556"/>
  <text x="464" y="124" text-anchor="middle" fill="#e9ecfb" font-family="${MONO}" font-size="13">shared NT texture</text>

  <rect x="344" y="148" width="240" height="34" rx="8" fill="#141b2e" stroke="#2a3556"/>
  <text x="464" y="170" text-anchor="middle" fill="#e9ecfb" font-family="${MONO}" font-size="13">d3d11va · vulkan · cuda</text>

  <path d="M464 148 L464 136" stroke="#8f9dff" stroke-width="1.6" marker-end="url(#pp-arw)"/>
  <path d="M464 102 L464 90" stroke="#8f9dff" stroke-width="1.6" marker-end="url(#pp-arw)"/>
  <path d="M584 90 L658 90" stroke="#8f9dff" stroke-width="1.6" marker-end="url(#pp-arw)"/>

  <rect x="658" y="56" width="212" height="42" rx="8" fill="#1b2440" stroke="#5b6cf9"/>
  <text x="764" y="74" text-anchor="middle" fill="#e9ecfb" font-family="${UI}" font-size="12.5">WPF overlay (HUD, menu)</text>
  <text x="764" y="90" text-anchor="middle" fill="#8f9dff" font-family="${MONO}" font-size="11">visual: above</text>

  <rect x="658" y="108" width="212" height="42" rx="8" fill="#1b2440" stroke="#45d7e3"/>
  <text x="764" y="126" text-anchor="middle" fill="#e9ecfb" font-family="${UI}" font-size="12.5">video plane, 10 bit</text>
  <text x="764" y="142" text-anchor="middle" fill="#45d7e3" font-family="${MONO}" font-size="11">visual: below</text>

  <rect x="658" y="160" width="212" height="34" rx="8" fill="#141b2e" stroke="#2a3556"/>
  <text x="764" y="182" text-anchor="middle" fill="#e9ecfb" font-family="${MONO}" font-size="13">the window on screen</text>

  <path d="M764 98 L764 108" stroke="#8f9dff" stroke-width="1.6"/>
  <path d="M764 150 L764 160" stroke="#8f9dff" stroke-width="1.6" marker-end="url(#pp-arw)"/>

  <line x1="30" y1="230" x2="870" y2="230" stroke="#1c2540"/>
  <text x="30" y="256" fill="#8592b5" font-family="${UI}" font-size="11.5">The frame never passes through WPF's own image path, which refuses anything wider than eight bits per channel. It is composed</text>
  <text x="30" y="274" fill="#8592b5" font-family="${UI}" font-size="11.5">beside the window's content instead, with the overlay above the plane, because that is the one arrangement that keeps both.</text>
</svg>`;

/** The console list: where the application opens. */
export const windowDiagram = `
<svg viewBox="0 0 900 420" role="img" aria-label="The PPortal window: a list of consoles with their name, address, state and what each row can do, mixing discovered, manually added and hidden consoles">
  <rect width="900" height="420" rx="12" fill="#0c1222"/>
  <rect x="0" y="0" width="900" height="42" rx="12" fill="#131b30"/>
  <rect x="0" y="30" width="900" height="12" fill="#131b30"/>
  <text x="24" y="27" fill="#e9ecfb" font-family="${UI}" font-size="13">PPortal</text>
  <text x="820" y="27" fill="#8592b5" font-family="${UI}" font-size="14" letter-spacing="6">─☐✕</text>

  <text x="36" y="78" fill="#e9ecfb" font-family="${UI}" font-size="16">Consoles</text>
  <rect x="742" y="60" width="134" height="30" rx="6" fill="#1b2440" stroke="#5b6cf9"/>
  <text x="809" y="80" text-anchor="middle" fill="#e9ecfb" font-family="${UI}" font-size="12.5">Add by address</text>

  <text x="36" y="120" fill="#a3aecb" font-family="${UI}" font-size="11" font-weight="600" letter-spacing="1">CONSOLE</text>
  <text x="300" y="120" fill="#a3aecb" font-family="${UI}" font-size="11" font-weight="600" letter-spacing="1">ADDRESS</text>
  <text x="500" y="120" fill="#a3aecb" font-family="${UI}" font-size="11" font-weight="600" letter-spacing="1">FOUND BY</text>
  <text x="676" y="120" fill="#a3aecb" font-family="${UI}" font-size="11" font-weight="600" letter-spacing="1">STATE</text>

  <rect x="24" y="134" width="852" height="48" rx="6" fill="#151d33"/>
  <circle cx="42" cy="158" r="6" fill="#2EA043"/>
  <text x="60" y="163" fill="#e9ecfb" font-family="${UI}" font-size="13.5">PS5-385</text>
  <text x="300" y="163" fill="#a3aecb" font-family="${MONO}" font-size="12.5">192.168.0.24</text>
  <text x="500" y="163" fill="#a3aecb" font-family="${UI}" font-size="13">discovery</text>
  <text x="676" y="163" fill="#7ee2a8" font-family="${UI}" font-size="13">ready</text>
  <rect x="770" y="145" width="92" height="26" rx="5" fill="#28336a" stroke="#5b6cf9"/>
  <text x="816" y="163" text-anchor="middle" fill="#e9ecfb" font-family="${UI}" font-size="12.5">Stream</text>

  <circle cx="42" cy="212" r="6" fill="#D29922"/>
  <text x="60" y="217" fill="#e9ecfb" font-family="${UI}" font-size="13.5">PS5 upstairs</text>
  <text x="300" y="217" fill="#a3aecb" font-family="${MONO}" font-size="12.5">192.168.0.31</text>
  <text x="500" y="217" fill="#a3aecb" font-family="${UI}" font-size="13">discovery</text>
  <text x="676" y="217" fill="#f0c36d" font-family="${UI}" font-size="13">standby</text>
  <rect x="770" y="199" width="92" height="26" rx="5" fill="#1b2440" stroke="#39456e"/>
  <text x="816" y="217" text-anchor="middle" fill="#e9ecfb" font-family="${UI}" font-size="12.5">Wake</text>

  <circle cx="42" cy="266" r="6" fill="#8B949E"/>
  <text x="60" y="271" fill="#e9ecfb" font-family="${UI}" font-size="13.5">PS4 (office)</text>
  <text x="300" y="271" fill="#a3aecb" font-family="${MONO}" font-size="12.5">10.0.4.19</text>
  <text x="500" y="271" fill="#a3aecb" font-family="${UI}" font-size="13">added by address</text>
  <text x="676" y="271" fill="#8592b5" font-family="${UI}" font-size="13">not found</text>
  <rect x="770" y="253" width="92" height="26" rx="5" fill="#1b2440" stroke="#39456e"/>
  <text x="816" y="271" text-anchor="middle" fill="#8592b5" font-family="${UI}" font-size="12.5">Retry</text>

  <circle cx="42" cy="320" r="6" fill="#39456e"/>
  <text x="60" y="325" fill="#8592b5" font-family="${UI}" font-size="13.5">PS5 (hidden)</text>
  <text x="300" y="325" fill="#5f6b92" font-family="${MONO}" font-size="12.5">192.168.0.77</text>
  <text x="500" y="325" fill="#5f6b92" font-family="${UI}" font-size="13">hidden by you</text>
  <text x="676" y="325" fill="#5f6b92" font-family="${UI}" font-size="13">muted</text>
  <rect x="770" y="307" width="92" height="26" rx="5" fill="#141b2e" stroke="#2a3556"/>
  <text x="816" y="325" text-anchor="middle" fill="#8592b5" font-family="${UI}" font-size="12.5">Unhide</text>

  <line x1="24" y1="356" x2="876" y2="356" stroke="#1c2540"/>
  <text x="36" y="384" fill="#8592b5" font-family="${UI}" font-size="11.5">Three sources in one list: what discovery found on the network, what you added by address, and what you hid. A hidden console</text>
  <text x="36" y="402" fill="#8592b5" font-family="${UI}" font-size="11.5">is still yours, so it says so rather than disappearing and being registered again a month later.</text>
</svg>`;

/** The controller mapping screen, mid-capture. */
export const mappingDiagram = `
<svg viewBox="0 0 420 300" role="img" aria-label="The controller mapping screen: a grid of buttons with their current bindings, and a capture prompt waiting for a press">
  <rect width="420" height="300" rx="12" fill="#0c1222"/>
  <rect x="0" y="0" width="420" height="36" rx="12" fill="#131b30"/>
  <rect x="0" y="24" width="420" height="12" fill="#131b30"/>
  <text x="18" y="23" fill="#e9ecfb" font-family="${UI}" font-size="12">Controller mapping</text>

  <text x="20" y="62" fill="#a3aecb" font-family="${UI}" font-size="11.5">DualSense Wireless Controller</text>

  <rect x="20" y="76" width="180" height="30" rx="5" fill="#151d33"/>
  <text x="32" y="96" fill="#e9ecfb" font-family="${UI}" font-size="12.5">Cross</text>
  <text x="150" y="96" fill="#8f9dff" font-family="${MONO}" font-size="11.5">a</text>

  <rect x="212" y="76" width="188" height="30" rx="5" fill="#151d33"/>
  <text x="224" y="96" fill="#e9ecfb" font-family="${UI}" font-size="12.5">Circle</text>
  <text x="346" y="96" fill="#8f9dff" font-family="${MONO}" font-size="11.5">b</text>

  <rect x="20" y="112" width="180" height="30" rx="5" fill="#151d33"/>
  <text x="32" y="132" fill="#e9ecfb" font-family="${UI}" font-size="12.5">L2</text>
  <text x="150" y="132" fill="#8f9dff" font-family="${MONO}" font-size="11.5">a2</text>

  <rect x="212" y="112" width="188" height="30" rx="5" fill="#151d33"/>
  <text x="224" y="132" fill="#e9ecfb" font-family="${UI}" font-size="12.5">R2</text>
  <text x="346" y="132" fill="#8f9dff" font-family="${MONO}" font-size="11.5">a5</text>

  <rect x="20" y="158" width="380" height="72" rx="8" fill="#1b2440" stroke="#5b6cf9"/>
  <text x="210" y="186" text-anchor="middle" fill="#e9ecfb" font-family="${UI}" font-size="14">Press the button for Square</text>
  <text x="210" y="208" text-anchor="middle" fill="#a3aecb" font-family="${UI}" font-size="12">Escape cancels. The capture takes the press, not the release.</text>

  <text x="20" y="256" fill="#8592b5" font-family="${UI}" font-size="11">The bindings are the device's own strings, read through SDL,</text>
  <text x="20" y="272" fill="#8592b5" font-family="${UI}" font-size="11">so a pad that already works elsewhere arrives already mapped.</text>
</svg>`;

/** The three console states, as the list draws them. */
export const consoleStateIcons: Record<"ready" | "standby" | "away", string> = {
  ready: `
<svg viewBox="0 0 48 48" role="img" aria-label="Ready: the console is awake and will take a stream">
  <circle cx="24" cy="24" r="20" fill="none" stroke="#2EA043" stroke-width="3"/>
  <path d="M15 24.5 L21.5 31 L33 19" fill="none" stroke="#2EA043" stroke-width="3.4" stroke-linecap="round" stroke-linejoin="round"/>
</svg>`,
  standby: `
<svg viewBox="0 0 48 48" role="img" aria-label="Standby: the console is asleep and can be woken from here">
  <circle cx="24" cy="24" r="20" fill="none" stroke="#D29922" stroke-width="3"/>
  <path d="M24 13 V25" fill="none" stroke="#D29922" stroke-width="3.4" stroke-linecap="round"/>
  <path d="M32 17.5 a11 11 0 1 1 -16 0" fill="none" stroke="#D29922" stroke-width="3.4" stroke-linecap="round"/>
</svg>`,
  away: `
<svg viewBox="0 0 48 48" role="img" aria-label="Not found: the console did not answer, and the row says so instead of disappearing">
  <circle cx="24" cy="24" r="20" fill="none" stroke="#8B949E" stroke-width="3"/>
  <path d="M17 17 L31 31 M31 17 L17 31" fill="none" stroke="#8B949E" stroke-width="3.4" stroke-linecap="round"/>
</svg>`,
};
