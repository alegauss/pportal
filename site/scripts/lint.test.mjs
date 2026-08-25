// Only the reader moves the window. The rule: a panel that keeps its own content in view
// scrolls its own element, never scrollIntoView, which scrolls every scrollable ancestor
// including the document. This is the source lint that stops the autoplaying frame path from
// dragging a reader back to the hero once a second.
//
// Also here: the site fetches no third-party font at page load, which is a stated non-goal,
// so no source file may link fonts.googleapis.com.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, extname } from "node:path";
import { fileURLToPath } from "node:url";

const siteDir = join(dirname(fileURLToPath(import.meta.url)), "..");

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    if (name === "node_modules" || name === "dist" || name === "dist-server") continue;
    const full = join(dir, name);
    if (statSync(full).isDirectory()) walk(full, out);
    else out.push(full);
  }
  return out;
}

const sourceFiles = walk(join(siteDir, "src")).filter((f) =>
  [".ts", ".tsx", ".js", ".jsx"].includes(extname(f)),
);

const relative = (f) => f.replace(siteDir, "").replace(/^[\\/]/, "").replace(/\\/g, "/");

test("no source calls scrollIntoView", () => {
  // the call, not the word: a comment explaining why we avoid it is fine
  const offenders = sourceFiles.filter((f) => readFileSync(f, "utf8").includes("scrollIntoView("));
  assert.deepEqual(
    offenders.map(relative),
    [],
    "a panel must scroll its own element (scrollTop), never scrollIntoView",
  );
});

test("no source fetches a third-party font at page load", () => {
  const all = [...sourceFiles, join(siteDir, "index.html")];
  const offenders = all.filter((f) => readFileSync(f, "utf8").includes("fonts.googleapis.com"));
  assert.deepEqual(offenders.map(relative), []);
});

// The band loops by translating one whole repeat of its traces, so a period that does not
// divide that repeat puts a visible seam through the signal once per cycle, which is every
// few seconds, forever. The arithmetic is the whole of the illusion, so it is asserted rather
// than left to whoever next retunes the speeds.
test("every signal period closes on the repeat the drift translates by", () => {
  const src = readFileSync(join(siteDir, "src", "components", "ui", "Signal.tsx"), "utf8");
  const span = Number(/const SPAN = (\d+)/.exec(src)?.[1]);
  assert.ok(span > 0, "Signal.tsx no longer declares SPAN");
  // the drift animation moves by 50% of a band drawn at 200%, so one repeat is half the span
  const repeat = span / 2;
  const periods = [...src.matchAll(/period: (\d+)/g)].map((m) => Number(m[1]));
  assert.ok(periods.length >= 3, "expected a period per layer");
  assert.deepEqual(
    periods.filter((p) => repeat % p !== 0),
    [],
    `every period must divide ${repeat}`,
  );
});

// The copy states no count and no version of its own: both come off the application's source
// through src/lib/product.ts. A number typed into a sentence is true the day it is typed.
test("the copy does not hard-code the application version", () => {
  const content = readFileSync(join(siteDir, "src", "lib", "site-content.ts"), "utf8");
  const features = readFileSync(join(siteDir, "src", "lib", "features.ts"), "utf8");
  for (const [name, src] of [
    ["site-content.ts", content],
    ["features.ts", features],
  ]) {
    const literals = [...src.matchAll(/"\d+\.\d+\.\d+"/g)].map((m) => m[0]);
    assert.deepEqual(literals, [], `${name} states a version literal instead of calling version()`);
  }
});
