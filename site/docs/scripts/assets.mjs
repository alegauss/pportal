// The favicon, copied from the site's own rather than drawn again.
//
// Astro serves a favicon out of public/, and this area has no artwork of its own: the mark is
// site/public/logo.svg, which app-icon.mjs also rasterises into the executable's .ico. Copying
// it at build time rather than committing a second SVG is what keeps the two from drifting -
// a redrawn logo reaches the docs in the commit that redraws it, and nothing has to remember.
import { copyFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const source = join(here, "..", "..", "public", "logo.svg");
const target = join(here, "..", "public", "favicon.svg");

mkdirSync(dirname(target), { recursive: true });
copyFileSync(source, target);

console.log("docs: favicon.svg copied from site/public/logo.svg");
