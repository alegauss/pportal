import { FeatureCards } from "../ui/FeatureCards";

// The landing's own index of the depth pages. The grid is shared with the /features page,
// so the list and the pages cannot disagree about what exists.
export function FeatureIndex() {
  return (
    <section id="features">
      <div className="wrap">
        <div className="sec-head reveal">
          <div className="eyebrow">In depth</div>
          <h2>One page per part of it</h2>
          <p>Each section above has a page to link at, whether from a README, an issue or a search result.</p>
        </div>
        <FeatureCards />
      </div>
    </section>
  );
}
