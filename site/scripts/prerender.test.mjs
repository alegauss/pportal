// The route pair, the twin per route, the sitemap and the social card, asserted against the
// built output. These read dist/, so they run after `npm run build`, which is what CI does.
// A claim that has gone false (a route with no file, a duplicate title, a twin that leaked
// the nav or the call to action, a card that is not 1200x630) fails here rather than staying
// invisible until somebody reads the page against the product.
import { test, before } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const siteDir = join(dirname(fileURLToPath(import.meta.url)), "..");
const distDir = join(siteDir, "dist");

let manifest;
before(() => {
  const mf = join(distDir, "manifest.json");
  assert.ok(existsSync(mf), "dist/manifest.json is missing, so run `npm run build` first");
  manifest = JSON.parse(readFileSync(mf, "utf8"));
});

const EXPECTED = [
  "/",
  "/hardware",
  "/features",
  "/features/picture",
  "/features/controllers",
  "/features/screens",
  "/features/latency",
  "/features/setup",
];

test("every expected route is in the manifest", () => {
  const paths = manifest.routes.map((r) => r.path);
  for (const p of EXPECTED) assert.ok(paths.includes(p), `route ${p} missing from manifest`);
});

test("each route has its HTML and Markdown file at the stated size", () => {
  for (const r of manifest.routes) {
    const html = join(distDir, r.html);
    const md = join(distDir, r.markdown);
    assert.ok(existsSync(html), `${r.html} missing`);
    assert.ok(existsSync(md), `${r.markdown} missing`);
    assert.equal(statSync(html).size, r.htmlBytes, `${r.html} size drifted from manifest`);
    assert.equal(statSync(md).size, r.markdownBytes, `${r.markdown} size drifted from manifest`);
  }
});

test("each page has a unique title, its canonical, and an og:image", () => {
  const titles = new Set();
  for (const r of manifest.routes) {
    const html = readFileSync(join(distDir, r.html), "utf8");
    const title = html.match(/<title>([\s\S]*?)<\/title>/)?.[1];
    assert.ok(title, `${r.html} has no <title>`);
    assert.ok(!titles.has(title), `duplicate <title>: ${title}`);
    titles.add(title);
    assert.ok(html.includes(`<link rel="canonical" href="${r.url}"`), `${r.html} canonical wrong`);
    assert.ok(html.includes('property="og:image"'), `${r.html} has no og:image`);
  }
});

// The nav is rendered into every route, so a link written as a bare "#section" is a link that
// works on the landing page and silently does nothing on the other six: the browser sets the
// hash, finds no element of that id, and stays where it is. There is no router to rescue it.
// This is the assertion that catches it, and it reads every anchor rather than the nav's,
// because the same mistake anywhere on a page has the same silence.
test("every in-page anchor has the element it names, on the page that carries it", () => {
  for (const r of manifest.routes) {
    const html = readFileSync(join(distDir, r.html), "utf8");
    const ids = new Set([...html.matchAll(/\sid="([^"]+)"/g)].map((m) => m[1]));
    const targets = [...html.matchAll(/href="#([^"]*)"/g)].map((m) => m[1]);
    const dangling = targets.filter((t) => !ids.has(t));
    assert.deepEqual(
      dangling,
      [],
      `${r.html} links to #${dangling.join(", #")}, which is not on that page: ` +
        "a cross-page section link carries the base and the path in front of its anchor",
    );
  }
});

test("no twin leaks the nav, the footer or the call to action", () => {
  // Strings that exist only in the nav, the theme control and the footer: if one
  // reaches a twin, a whole subtree the converter is meant to drop has leaked into it.
  const banned = ["★ View on GitHub", "Switch to dark theme", "Not affiliated with"];
  for (const r of manifest.routes) {
    const md = readFileSync(join(distDir, r.markdown), "utf8");
    assert.ok(md.trim().length > 0, `${r.markdown} is empty`);
    for (const b of banned) {
      assert.ok(!md.includes(b), `${r.markdown} leaked "${b}"`);
    }
  }
});

test("the landing twin carries the frame path, which is the argument the page makes", () => {
  const md = readFileSync(join(distDir, "index.md"), "utf8");
  for (const stage of ["receive", "reorder", "reassemble", "correct", "decode", "present"]) {
    assert.ok(md.includes(stage), `landing twin is missing the ${stage} stage`);
  }
});

test("the terminal block prints the flags the application answers", () => {
  // The list is generated from app/Session/HostCommandLine.cs, so this asserts that the
  // generation reached the built page rather than that somebody typed the flags correctly.
  const md = readFileSync(join(distDir, "index.md"), "utf8");
  for (const flag of ["--selftest", "--controllers", "--capture-controller", "--map-controller"]) {
    assert.ok(md.includes(flag), `the landing twin is missing ${flag}`);
  }
});

test("the sitemap lists every route exactly once, and nothing else", () => {
  const xml = readFileSync(join(distDir, "sitemap.xml"), "utf8");
  const locs = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1]);

  // Exactly once and in both directions: a route missing from the sitemap is one a crawler
  // finds only if something links inward, and a URL with no route is an address that 404s.
  assert.equal(locs.length, manifest.routes.length, "sitemap URL count differs from the routes");
  assert.equal(new Set(locs).size, locs.length, "the sitemap lists a URL twice");
  for (const r of manifest.routes) {
    assert.ok(locs.includes(r.url), `sitemap missing ${r.url}`);
  }
});

test("every sitemap URL carries the base prefix", () => {
  // The prefix GitHub Pages derives from the repository name. A sitemap that lost it would
  // publish addresses nothing serves.
  const xml = readFileSync(join(distDir, "sitemap.xml"), "utf8");
  for (const [, loc] of xml.matchAll(/<loc>([^<]+)<\/loc>/g)) {
    assert.ok(
      loc.startsWith(`https://alegauss.github.io${manifest.base}`),
      `${loc} does not carry ${manifest.base}`,
    );
  }
});

test("the sitemap states no lastmod it cannot derive, and never the build clock", () => {
  const xml = readFileSync(join(distDir, "sitemap.xml"), "utf8");
  const stamps = [...xml.matchAll(/<lastmod>([^<]+)<\/lastmod>/g)].map((m) => m[1]);
  for (const s of stamps) {
    assert.match(s, /^\d{4}-\d{2}-\d{2}$/, `lastmod ${s} is not a plain date`);
  }

  // Either every URL carries one or none does: a sitemap where some routes look fresher for
  // want of a source, rather than for having changed, is the misleading half.
  assert.ok(
    stamps.length === 0 || stamps.length === manifest.routes.length,
    "lastmod is on some routes and not others",
  );
});

test("robots allows everything and names a sitemap that was written", () => {
  const robots = readFileSync(join(distDir, "robots.txt"), "utf8");
  assert.match(robots, /^User-agent: \*$/m);
  assert.match(robots, /^Allow: \/$/m);

  const named = robots.match(/^Sitemap: (\S+)$/m);
  assert.ok(named, "robots.txt names no sitemap");

  // Absolute, and the file it names is the one beside it: a Sitemap: line pointing at
  // nothing is worse than no line, because it is a claim a crawler acts on.
  const url = named[1];
  assert.ok(url.startsWith("https://"), "the Sitemap: line is not absolute");
  assert.equal(url, `https://alegauss.github.io${manifest.base}sitemap.xml`);
  assert.ok(existsSync(join(distDir, "sitemap.xml")), "robots names a sitemap that is not there");
});

test("the social card is a 1200x630 PNG", () => {
  const png = join(distDir, "og.png");
  assert.ok(existsSync(png), "dist/og.png missing");
  const buf = readFileSync(png);
  assert.equal(buf.toString("ascii", 1, 4), "PNG", "og.png is not a PNG");
  assert.equal(buf.readUInt32BE(16), 1200, "og.png width");
  assert.equal(buf.readUInt32BE(20), 630, "og.png height");
});
