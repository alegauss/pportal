// The documentation area, at https://alegauss.github.io/pportal/docs/.
//
// A second build rather than more pages in site/src: that renderer holds its copy as data so a
// claim is an array element a test can reach, which is right for eight curated pages and wrong
// for reference prose. There is no Markdown pipeline there - scripts/markdown.mjs converts the
// other way, HTML to the twin - and no highlighting, sidebar, per-page contents or search.
// Starlight is those four, and it emits static HTML the way the prerender does.
//
// Three things here are joins to the site next door, and each is asserted rather than
// remembered: SiteDocsAreaTests reads this file, and scripts/docs.test.mjs reads what it built.
import { defineConfig } from "astro/config";
import starlight from "@astrojs/starlight";
import react from "@astrojs/react";

// JOIN 1 - the base. GitHub Pages derives "/pportal/" from the repository name, which
// site/vite.config.ts and site/src/routes.tsx both write; this is that prefix plus the one
// segment this area occupies. Astro rewrites the links it generates and NOT the ones written by
// hand, so an absolute href typed into a page drops the prefix and 404s in production alone.
const BASE = "/pportal/docs";

// JOIN 2 - the output. `vite build` empties site/dist, so this build runs after it (site's
// package.json chains build:docs last) or the whole area vanishes from the deploy artefact,
// which is one directory nobody would notice missing.
const OUT_DIR = "../dist/docs";

export default defineConfig({
  site: "https://alegauss.github.io",
  base: BASE,
  outDir: OUT_DIR,

  // JOIN 3 - discovery. The site's robots.txt and sitemap.xml are generated from ROUTE_META,
  // which will never know these pages; Starlight emits its own sitemap here and the prerender
  // names it in robots.txt, so the two halves of one deploy are both crawlable.
  integrations: [
    // Static by default: a React component with no client directive is rendered at build time
    // and ships no JavaScript, which is what lets a page reuse the site's own components.
    react(),
    starlight({
      title: "PPortal internals",
      description:
        "How the port works: the host's interfaces, the session it drives and the C it replaces.",
      // Pagefind, which Starlight indexes at build time - the reason search here needs no
      // Algolia account and no service to be up.
      pagefind: true,
      social: [
        { icon: "github", label: "GitHub", href: "https://github.com/alegauss/pportal" },
      ],
      customCss: ["./src/styles/docs.css"],
      sidebar: [
        {
          label: "The host",
          items: [{ autogenerate: { directory: "host" } }],
        },
      ],
      // No "edit this page": the repository is public and the footer already links it, and a
      // second link per page is a claim about a contribution flow this project has not written.
      editLink: {},
      lastUpdated: true,
    }),
  ],
});
