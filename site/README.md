# The PPortal site

The public site, at <https://alegauss.github.io/pportal/>. It is a separate npm project from
the .NET solution and it builds to its own `dist/`, because `docs/` belongs to roadkeep and
is never a web root.

```
npm install
npm run dev      # the site at http://localhost:5173/pportal/
npm run build    # generate, typecheck, build, social card, SSR build, prerender
npm test         # the site's own claims, against what the build produced
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

## Publishing

`.github/workflows/site.yml` builds and tests on every push and pull request. The deploy to
GitHub Pages runs on `workflow_dispatch` only, because the site is the one artefact where a
defect is public immediately.

One repository setting has to be made once, or the deploy is inert: **Settings → Pages →
Build and deployment → Source: "GitHub Actions"**.
