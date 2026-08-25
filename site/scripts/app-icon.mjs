// PP330: the app's icon, rendered from the site's mark.
//
// public/logo.svg is the only drawing of that mark, and the Windows host cannot read it:
// <ApplicationIcon> takes an .ico, which is a container of rasters. This turns the one into
// the other, and lives here rather than in the repository's scripts/ because resvg - the
// renderer og-image.mjs already uses - is a dependency of this package and not of that tree.
//
// It is NOT part of `npm run build`. A site build that writes into the application's tree is
// a surprise, and the stamp below is what catches forgetting to run this instead.
import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { Resvg } from "@resvg/resvg-js";

const here = dirname(fileURLToPath(import.meta.url));
const siteDir = join(here, "..");
const repoDir = join(siteDir, "..");

const logoPath = join(siteDir, "public", "logo.svg");
const icoPath = join(repoDir, "assets", "pportal.ico");
const stampPath = join(repoDir, "assets", "pportal.ico.source");

// 16 is the title bar, 32 the taskbar and Alt-Tab, 256 what a large-icon Explorer view and
// the installer's own window ask for. The rest are what Windows scales between rather than
// resampling one raster it happens to have.
const SIZES = [16, 24, 32, 48, 64, 128, 256];

// The mark is 64 wide and 72 tall; an icon is square. Widening the viewBox pads the canvas
// and leaves the drawing alone, which a width and height would not - and the margin is
// deliberate: the ring reaches y=5..67 of the original, so a bare 72-square would touch two
// edges. Refused rather than guessed when it is not the viewBox this was written against,
// because a redrawn logo on a different canvas would otherwise be silently cropped.
const SOURCE_VIEWBOX = 'viewBox="0 0 64 72"';
const SQUARE_VIEWBOX = 'viewBox="-6 -2 76 76"';

const logo = readFileSync(logoPath, "utf8");
if (!logo.includes(SOURCE_VIEWBOX)) {
  throw new Error(
    `app-icon: public/logo.svg no longer carries ${SOURCE_VIEWBOX}; the square canvas below ` +
      `was chosen for that one, so pick a new one rather than letting this crop the mark`,
  );
}
const square = logo.replace(SOURCE_VIEWBOX, SQUARE_VIEWBOX);

/** One raster of the mark, at `size` square. */
function render(size) {
  // loadSystemFonts off: the mark is paths, and loading the machine's fonts would make this
  // slower and the output a property of the machine.
  const image = new Resvg(square, {
    fitTo: { mode: "width", value: size },
    font: { loadSystemFonts: false },
  }).render();

  if (image.width !== size || image.height !== size) {
    throw new Error(`app-icon: asked for ${size}x${size}, got ${image.width}x${image.height}`);
  }
  return image;
}

/**
 * A raster as an .ico's uncompressed entry: a BITMAPINFOHEADER whose height is doubled
 * because the AND mask counts as the other half, then bottom-up BGRA rows, then the mask.
 *
 * The mask is redundant beside a 32-bit alpha channel and is written anyway: a path that
 * ignores the alpha - and some shell surfaces still do - draws every transparent pixel black
 * without it, which turns the ring into a square tile.
 */
function uncompressed(image) {
  const { width, height } = image;
  const rgba = image.pixels;

  const header = Buffer.alloc(40);
  header.writeUInt32LE(40, 0); // biSize
  header.writeInt32LE(width, 4);
  header.writeInt32LE(height * 2, 8); // biHeight: colour rows plus mask rows
  header.writeUInt16LE(1, 12); // biPlanes
  header.writeUInt16LE(32, 14); // biBitCount
  header.writeUInt32LE(0, 16); // biCompression = BI_RGB

  const colour = Buffer.alloc(width * height * 4);
  const maskStride = Math.ceil(width / 8 / 4) * 4; // 1bpp rows, padded to 4 bytes
  const mask = Buffer.alloc(maskStride * height);

  for (let y = 0; y < height; y++) {
    const source = (height - 1 - y) * width * 4; // bottom-up
    for (let x = 0; x < width; x++) {
      const s = source + x * 4;
      const d = (y * width + x) * 4;
      colour[d] = rgba[s + 2];
      colour[d + 1] = rgba[s + 1];
      colour[d + 2] = rgba[s];
      colour[d + 3] = rgba[s + 3];

      // A set bit means "leave what is behind", so it marks the fully transparent pixels.
      if (rgba[s + 3] === 0) mask[y * maskStride + (x >> 3)] |= 0x80 >> (x & 7);
    }
  }

  return Buffer.concat([header, colour, mask]);
}

const entries = SIZES.map((size) => {
  const image = render(size);
  // 256 as PNG: uncompressed it is 256 kB on its own, and every Windows that reads a 256
  // entry at all reads a PNG one. Below that the uncompressed form is the one nothing argues
  // about, and the bytes are cheap.
  return { size, data: size >= 256 ? image.asPng() : uncompressed(image) };
});

const directory = Buffer.alloc(6 + 16 * entries.length);
directory.writeUInt16LE(0, 0); // reserved
directory.writeUInt16LE(1, 2); // an icon, not a cursor
directory.writeUInt16LE(entries.length, 4);

let offset = directory.length;
entries.forEach((entry, index) => {
  const at = 6 + 16 * index;
  const dimension = entry.size >= 256 ? 0 : entry.size; // 256 is spelled 0 in one byte
  directory.writeUInt8(dimension, at);
  directory.writeUInt8(dimension, at + 1);
  directory.writeUInt8(0, at + 2); // no palette
  directory.writeUInt8(0, at + 3); // reserved
  directory.writeUInt16LE(1, at + 4); // planes
  directory.writeUInt16LE(32, at + 6); // bits per pixel
  directory.writeUInt32LE(entry.data.length, at + 8);
  directory.writeUInt32LE(offset, at + 12);
  offset += entry.data.length;
});

const ico = Buffer.concat([directory, ...entries.map((entry) => entry.data)]);
writeFileSync(icoPath, ico);

// The stamp, which is the whole reason a second copy of the mark is allowed to exist. Hashed
// over LF-normalised bytes so that a checkout's line endings are not mistaken for a redrawn
// logo; AppIconTests normalises the same way before comparing.
const digest = createHash("sha256").update(logo.replace(/\r\n/g, "\n"), "utf8").digest("hex");
writeFileSync(
  stampPath,
  [
    "# PP330: what assets/pportal.ico was rendered from. Written by site/scripts/app-icon.mjs;",
    "# AppIconTests fails when the source below no longer hashes to this.",
    "source site/public/logo.svg",
    `sha256 ${digest}`,
    `sizes ${SIZES.join(",")}`,
    "",
  ].join("\n"),
);

console.log(
  `app-icon: assets/pportal.ico  ${SIZES.join(", ")}  (${(ico.length / 1024).toFixed(0)} kB)`,
);
