import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// GitHub Pages derives this from the repository name, so it is not a preference: the site is
// served at https://alegauss.github.io/pportal/ and every canonical, asset path and sitemap
// entry carries the prefix. Written here and in src/routes.tsx, and nowhere else.
export const BASE = "/pportal/";

export default defineConfig({
  base: BASE,
  plugins: [react(), tailwindcss()],
  build: {
    // docs/ belongs to roadkeep and is never a web root, so the site builds to its own dist/.
    outDir: "dist",
    emptyOutDir: true,
  },
});
