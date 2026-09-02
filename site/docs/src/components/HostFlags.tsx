// The host's flags, read out of the application's own source.
//
// The import crosses into the site next door on purpose: scripts/product.mjs regenerates
// product.generated.ts from app/Session/HostCommandLine.cs at the start of every build, and
// this build runs after it. A flag renamed in the application changes this table in the same
// commit, which is the rule the site already holds and the one reason this page is not a list
// somebody typed.
//
// Rendered at build time with no client directive, so the page ships no JavaScript for it.
import { HOST_FLAGS, type HostFlag } from "../../../src/lib/product.generated";

/**
 * The ones the front page prints. A reader running the application is the audience there, and
 * the rest report on this repository's own debt - which is what this area is for, so both
 * halves are shown here and told apart.
 *
 * PP583: the count used to be written out here and had drifted - it said eleven of sixteen. The
 * set below is the claim; how many fall the other side of it is derived, and derived is what
 * this area's own rule asks for.
 *
 * PP600 added the first of them, which is the only one here that is not a diagnostic: it opens
 * the console list, and connecting from that list is the thing the host could not do at all.
 */
const ON_THE_FRONT_PAGE = new Set([
  "--consoles",
  "--selftest",
  "--controllers",
  "--capture-controller",
  "--analog",
  "--map-controller",
]);

function Rows({ flags }: { flags: readonly HostFlag[] }) {
  return (
    <>
      {flags.map((flag) => (
        <tr key={flag.name}>
          <td>
            <code>{flag.name}</code>
          </td>
          <td>{flag.argument ? <code>{flag.argument}</code> : ""}</td>
          <td>{flag.summary}</td>
        </tr>
      ))}
    </>
  );
}

export function HostFlags({ audience }: { audience: "reader" | "maintainer" }) {
  const wanted = audience === "reader";
  const flags = HOST_FLAGS.filter((f) => ON_THE_FRONT_PAGE.has(f.name) === wanted);

  if (flags.length === 0) {
    // Not an empty table: a filter that matches nothing means the names above have drifted
    // from the host, and a page that renders three empty rows says so to nobody.
    throw new Error(
      `HostFlags: no flag is ${wanted ? "on" : "off"} the front page, so the names in ` +
        "ON_THE_FRONT_PAGE no longer match what the host declares",
    );
  }

  return (
    <table className="host-flags">
      <thead>
        <tr>
          <th>Flag</th>
          <th>Takes</th>
          <th>What it does</th>
        </tr>
      </thead>
      <tbody>
        <Rows flags={flags} />
      </tbody>
    </table>
  );
}
