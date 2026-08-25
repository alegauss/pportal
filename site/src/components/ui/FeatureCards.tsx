import { features } from "../../lib/features";

// The card grid, in one place because two routes draw it: the landing's index section and
// the /features page the nav points at. Written once so the two cannot come to disagree
// about what exists, which is the same reason both read the feature records rather than a
// list of their own.
export function FeatureCards() {
  return (
    <div className="feature-index reveal">
      {features.map((f) => (
        <a className="feature-card" href={`/pportal/features/${f.slug}/`} key={f.slug}>
          <h3>{f.heading}</h3>
          <p>{f.description}</p>
          <span className="feature-card-go">Read the page →</span>
        </a>
      ))}
    </div>
  );
}
