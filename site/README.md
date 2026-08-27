# The PPortal site

The public site, at <https://alegauss.github.io/pportal/>. It is a separate npm project from
the .NET solution and it builds to its own `dist/`, because `docs/` belongs to roadkeep and
is never a web root.

```
npm install
npm --prefix docs install     # the documentation area, a second npm project
npm run dev                   # the site at http://localhost:5173/pportal/
npm run dev:docs              # the docs at http://localhost:4321/pportal/docs/
npm run build                 # generate, typecheck, build, social card, SSR build, prerender, docs
npm test                      # the site's own claims, against what the build produced
```

## How it is put together

- **The copy lives in `src/lib/site-content.ts` and `src/lib/features.ts`.** Sections render
  it and never contain it, so a claim is an array element a reviewer can check against the
  product rather than a string welded into the markup that displays it.
- **No figure is typed.** `scripts/product.mjs` reads the version and the target framework
  out of `app/ChiakiNg.csproj` and the flag list out of `app/Session/HostCommandLine.cs` on
  every build, and the copy reaches them through `src/lib/product.ts`. A version typed into a
  sentence is true the day it is typed and wrong in silence afterwards.
- **The routes are a pair.** `src/routes.tsx` holds one metadata row and one component row
  per route, and an assertion at import time refuses either one without the other, in both
  directions, so a page cannot ship under another page's title.
- **Every page is prerendered**, with its `<head>` patched by replace-or-throw: a drifted
  template fails the build rather than publishing a page with the wrong canonical.
- **Every page has a Markdown twin** at the same address with `index.md`, converted from the
  same render as the HTML, so it cannot drift from the page. `manifest.json` lists the routes,
  the twins and their sizes; `robots.txt` and `sitemap.xml` are generated from the same route
  table.
- **The theme follows the OS**, and a stored choice overrides it. The pre-paint script in
  `index.html` applies the stored choice before first paint, because the token the body
  background reads is keyed off `data-theme`.

## The documentation area (`docs/`)

`/pportal/docs` is a **second npm project** with its own toolchain — Astro and Starlight —
building into this one's `dist/docs`. It exists because the renderer above holds its copy as
data and has no Markdown pipeline, no highlighting, no sidebar and no search, and writing
those four is writing a documentation framework. Starlight is those four, plus a Pagefind index
built from the pages, which is why search here needs no service.

Three lines join the two builds, and each of the three fails in silence:

- **`build:docs` runs last.** `vite build` empties `dist/`, so a docs build placed anywhere
  earlier is deleted by the step after it.
- **The base is the site's plus one segment.** Astro rewrites the links it generates and not
  the ones written by hand, so an absolute href typed into a page 404s in production alone.
- **`outDir` is `../dist/docs`.** The deploy uploads `dist/` and nothing else.

None of them is left as a comment. `SiteDocsAreaTests` reads the three source files and
`scripts/docs.test.mjs` reads what they built, so a reordered script names its own cause rather
than being read backwards from a folder that went missing.

The pages state no figure of their own either: `HostFlags.tsx` imports `product.generated.ts`
from the site next door, which `scripts/product.mjs` writes out of `app/Session/HostCommandLine.cs`
at the start of every build.

## Publishing

`.github/workflows/site.yml` builds and tests on every push and pull request. The deploy to
GitHub Pages runs on `workflow_dispatch` only, because the site is the one artefact where a
defect is public immediately.

One repository setting has to be made once, or the deploy is inert: **Settings → Pages →
Build and deployment → Source: "GitHub Actions"**.
