import { Nav } from "../components/Nav";
import { Footer } from "../components/Footer";
import { Rich } from "../components/ui/Rich";
import { FeatureCards } from "../components/ui/FeatureCards";
import { featuresIndex } from "../lib/site-content";

// The index the nav's "Features" points at. It exists as a route rather than as the landing's
// #features anchor because the nav is on every page: an anchor is a link that works from the
// landing and does nothing from anywhere else, and a section of one page is not a thing the
// other six can address.
export function Features() {
  return (
    <>
      <Nav />
      <header className="hero page-hero" id="top">
        <div className="wrap">
          <a className="feature-back" href="/pportal/">
            {featuresIndex.backLabel}
          </a>
          <div className="eyebrow">{featuresIndex.eyebrow}</div>
          <h1>{featuresIndex.heading}</h1>
          <p className="sub">
            <Rich runs={featuresIndex.lead} />
          </p>
        </div>
      </header>

      <section style={{ paddingTop: "28px" }}>
        <div className="wrap">
          <FeatureCards />
        </div>
      </section>

      <Footer />
    </>
  );
}
