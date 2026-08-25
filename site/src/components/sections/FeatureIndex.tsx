import { features } from "../../lib/features";

// The home for the depth pages: one link per record, so the list and the pages cannot
// disagree about what exists.
export function FeatureIndex() {
  return (
    <section id="features">
      <div className="wrap">
        <div className="sec-head reveal">
          <div className="eyebrow">In depth</div>
          <h2>One page per part of it</h2>
          <p>Each section above has a page to link at, whether from a README, an issue or a search result.</p>
        </div>
        <div className="feature-index reveal">
          {features.map((f) => (
            <a className="feature-card" href={`/pportal/features/${f.slug}/`} key={f.slug}>
              <h3>{f.heading}</h3>
              <p>{f.description}</p>
              <span className="feature-card-go">Read the page →</span>
            </a>
          ))}
        </div>
      </div>
    </section>
  );
}
