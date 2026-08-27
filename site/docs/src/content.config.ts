// The docs collection, as Starlight's loader defines it. Pages live in src/content/docs and
// their frontmatter is validated against docsSchema at build time, so a page with no title
// fails the build rather than publishing a heading-less entry into the sidebar.
import { defineCollection } from "astro:content";
import { docsLoader } from "@astrojs/starlight/loaders";
import { docsSchema } from "@astrojs/starlight/schema";

export const collections = {
  docs: defineCollection({ loader: docsLoader(), schema: docsSchema() }),
};
