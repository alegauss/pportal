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
 * The five the front page prints. A reader running the application is the audience there, and
 * the other eleven report on this repository's own debt - which is what this area is for, so
 * both halves are shown here and told apart.
 */
const ON_THE_FRONT_PAGE = new Set([
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
