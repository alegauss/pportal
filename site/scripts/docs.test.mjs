// PP446: the documentation area, asserted against what the build produced.
//
// It is a second build with its own toolchain, joined to this one in three places, and each
// join fails silently rather than loudly:
//
//   the build order  `vite build` empties dist/, so the docs build runs after it or the whole
//                    area is missing from the deploy artefact - one directory nobody notices.
//   the base prefix  Astro rewrites the links it generates and not the ones written by hand,
//                    so an absolute href typed into a page 404s in production alone.
//   discovery        robots.txt and sitemap.xml come from ROUTE_META, which does not know
//                    these pages.
//
// These read dist/docs, so they run after `npm run build`, which is what CI does.
import { test, before } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const siteDir = join(dirname(fileURLToPath(import.meta.url)), "..");
const distDir = join(siteDir, "dist");
const docsDir = join(distDir, "docs");
const BASE = "/pportal/";

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) walk(full, out);
    else out.push(full);
  }
  return out;
}

let pages;
before(() => {
  assert.ok(
    existsSync(docsDir),
    "dist/docs is missing, so run `npm run build` - which builds the docs area last, after "
      + "`vite build` has emptied dist/",
  );
  pages = walk(docsDir).filter((f) => f.endsWith(".html"));
});

const relative = (f) => f.replace(distDir, "").replace(/^[\\/]/, "").replace(/\\/g, "/");

test("the docs build reached the tree the deploy uploads", () => {
  // Not the directory's existence: the pages. An outDir pointed somewhere else leaves the
  // folder behind with the search index in it and no HTML.
  assert.ok(pages.length >= 2, `dist/docs holds ${pages.length} HTML file(s)`);
  assert.ok(existsSync(join(docsDir, "index.html")), "dist/docs/index.html missing");
});

test("robots names the docs sitemap, and the file it names was built", () => {
  // The prerender writes this line before the docs build has run, so it is a claim about a
  // file this side of the build has never seen. This is where it is held.
  const robots = readFileSync(join(distDir, "robots.txt"), "utf8");
  const named = [...robots.matchAll(/^Sitemap: (\S+)$/gm)].map((m) => m[1]);

  const docsSitemap = `https://alegauss.github.io${BASE}docs/sitemap-index.xml`;
  assert.ok(
    named.includes(docsSitemap),
    `robots.txt names ${named.join(", ")} and not the docs sitemap`,
  );
  assert.ok(
    existsSync(join(docsDir, "sitemap-index.xml")),
    "robots names a docs sitemap that is not in dist: the docs build did not run, or a later "
      + "`vite build` emptied dist/ after it did",
  );
});

test("every sitemap URL the docs area emits carries the base", () => {
  const index = readFileSync(join(docsDir, "sitemap-index.xml"), "utf8");
  const parts = [...index.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1]);
  assert.ok(parts.length > 0, "the docs sitemap index lists no sitemap");

  for (const part of parts) {
    const file = part.replace(`https://alegauss.github.io${BASE}docs/`, "");
    const xml = readFileSync(join(docsDir, file), "utf8");
    const locs = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1]);
    assert.ok(locs.length > 0, `${file} lists no URL`);
    for (const loc of locs) {
      assert.ok(
        loc.startsWith(`https://alegauss.github.io${BASE}docs/`),
        `${loc} does not carry ${BASE}docs/`,
      );
    }
  }
});

// The one that catches a hand-written link. Astro prefixes what it generates; a href or src
// typed into MDX is emitted verbatim, and locally the dev server serves it, so the first
// reader to find it is on the published site.
test("no absolute href or src in the docs escapes the base", () => {
  const escaped = [];
  for (const page of pages) {
    const html = readFileSync(page, "utf8");
    for (const [, attr, url] of html.matchAll(/\s(href|src)="(\/[^/"][^"]*)"/g)) {
      if (!url.startsWith(BASE)) escaped.push(`${relative(page)}: ${attr}="${url}"`);
    }
  }
  assert.deepEqual(
    escaped,
    [],
    "a root-absolute link that does not carry /pportal/ is served locally and 404s in "
      + "production, because GitHub Pages puts every path under the repository name",
  );
});

test("the search index was built and holds the pages", () => {
  // Pagefind is why this area needs no Algolia account: the index is a build artefact. An
  // entry file with no fragments is an index of nothing, which searches as an empty site.
  const entry = join(docsDir, "pagefind", "pagefind-entry.json");
  assert.ok(existsSync(entry), "dist/docs/pagefind/pagefind-entry.json missing");

  const fragments = existsSync(join(docsDir, "pagefind", "fragment"))
    ? readdirSync(join(docsDir, "pagefind", "fragment"))
    : [];
  assert.ok(fragments.length > 0, "the search index holds no page fragment");
});

// The rule the site already holds, extended to this area: no figure is typed. The flag table
// is rendered from product.generated.ts, which scripts/product.mjs writes out of
// app/Session/HostCommandLine.cs at the start of every build.
test("the command-line page prints every flag the host declares", () => {
  const page = join(docsDir, "host", "command-line", "index.html");
  assert.ok(existsSync(page), "the command-line page is missing from dist/docs");
  const html = readFileSync(page, "utf8");

  const generated = readFileSync(join(siteDir, "src", "lib", "product.generated.ts"), "utf8");
  const flags = [...generated.matchAll(/"name":\s*"(--[a-z-]+)"/g)].map((m) => m[1]);
  assert.ok(flags.length >= 10, `only ${flags.length} flags read out of product.generated.ts`);

  const missing = flags.filter((f) => !html.includes(`<code>${f}</code>`));
  assert.deepEqual(
    missing,
    [],
    "the docs page states fewer flags than the host declares, so the two tables no longer "
      + "partition the list and a flag is documented nowhere",
  );
});

test("the docs fetch no third-party font at page load", () => {
  // Stated as a non-goal for the site and true of this area for the same reason: a page that
  // waits on fonts.googleapis.com is one that renders late for a reader whose network does
  // not reach it. Starlight's default stack is the system's.
  const offenders = pages.filter((p) => readFileSync(p, "utf8").includes("fonts.googleapis.com"));
  assert.deepEqual(offenders.map(relative), []);
});

test("every docs page has a unique title and a canonical carrying the base", () => {
  const titles = new Set();
  for (const page of pages) {
    const html = readFileSync(page, "utf8");
    const title = html.match(/<title>([\s\S]*?)<\/title>/)?.[1];
    assert.ok(title, `${relative(page)} has no <title>`);
    assert.ok(!titles.has(title), `duplicate <title> in the docs: ${title}`);
    titles.add(title);

    const canonical = html.match(/<link rel="canonical" href="([^"]+)"/)?.[1];
    assert.ok(canonical, `${relative(page)} has no canonical`);
    assert.ok(
      canonical.startsWith(`https://alegauss.github.io${BASE}docs/`),
      `${relative(page)} is canonical at ${canonical}`,
    );
  }
});
