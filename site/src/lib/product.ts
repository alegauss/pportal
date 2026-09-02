// The accessors the copy uses. The generated module is the data; this is the reading of it.
//
// Two things happen here that a raw import would not do. The flags the page shows are a
// named subset, because the host answers a few that exist for the people who work on it and
// a reader is not one of them. And the subset is resolved by name against the generated
// list, so a flag renamed in the application fails the build here rather than printing a
// line the program no longer answers.

import { HOST_FLAGS, VERSION, FRAMEWORK, type HostFlag } from "./product.generated";

export type { HostFlag };

/** The application version, as the assembly and the installer state it. */
export function version(): string {
  return VERSION;
}

/** The .NET target framework, spelled as the project file spells it. */
export function framework(): string {
  return FRAMEWORK;
}

/**
 * The framework as a reader says it: "net10.0-windows" is the moniker a project file takes,
 * and ".NET 10" is what the release is called. Derived rather than typed, so the day the
 * project moves the page moves with it.
 */
export function dotnet(): string {
  const m = /^net(\d+)(?:\.0)?/.exec(FRAMEWORK);
  if (!m) {
    throw new Error(`product: "${FRAMEWORK}" is not a target framework this can name`);
  }
  return `.NET ${m[1]}`;
}

/**
 * What a person runs, in the order the terminal block prints them.
 *
 * Chosen rather than taken whole: the host also answers flags that report on this
 * repository's own test debt, and those are for whoever is working on it.
 *
 * The first is not a diagnostic and is deliberately first. PP600 gave the console list a
 * connect action, so `--consoles` is the flag that opens the application rather than one
 * that reports on it, and a list headed by a self-test described a tree with no front door.
 */
const SHOWN = [
  "--consoles",
  "--selftest",
  "--controllers",
  "--capture-controller",
  "--analog",
  "--map-controller",
] as const;

export function shownFlags(): HostFlag[] {
  return SHOWN.map((name) => {
    const found = HOST_FLAGS.find((f) => f.name === name);
    if (!found) {
      throw new Error(
        `product: the host no longer answers "${name}", so the page would print a flag that does nothing`,
      );
    }
    return found;
  });
}

/** The flag list as the terminal block draws it: name, argument, then the summary. */
export function flagLines(): string[] {
  const flags = shownFlags();
  const width = Math.max(...flags.map((f) => `${f.name} ${f.argument}`.trimEnd().length));
  return flags.map((f) => {
    const spelled = `${f.name} ${f.argument}`.trimEnd().padEnd(width);
    return `  ${spelled}  ${f.summary}`;
  });
}
