import { download, frameTrip, hero, repoUrl } from "../../lib/site-content";
import { Rich } from "../ui/Rich";
import { FrameTrip } from "../FrameTrip";
import { Signal } from "../ui/Signal";

export function Hero() {
  return (
    <header className="hero" id="top">
      <div className="wrap">
        <img className="hero-icon" src="/pportal/logo.svg" alt="PPortal logo" />
        <div className="badge">
          <span className="dot" /> {hero.badge}
        </div>
        <h1>
          {hero.titleLead}
          <br />
          <span className="grad">{hero.titleAccent}</span>
        </h1>
        <p className="sub">
          <Rich runs={hero.sub} />
        </p>
        {/* The call to action is dropped from the Markdown twin by this attribute: it
            converts a reader and costs an agent the same forty words on every page. The
            button scrolls to the download section rather than leaving for GitHub, because
            the question between a reader and an install is what it touches on their machine,
            and that section answers it. */}
        <div className="hero-cta" data-twin="omit">
          <a className="btn btn-primary" href="#download">
            {download.cta}
          </a>
          <a className="btn btn-ghost" href={repoUrl}>
            ★ View on GitHub
          </a>
        </div>

        <div className="session-eyebrow">{frameTrip.eyebrow}</div>
        <FrameTrip />
        <div className="hero-meta">
          {hero.meta.map((item) => (
            <span key={item}>{item}</span>
          ))}
        </div>
        <div className="pills">
          {hero.pills.map((runs, i) => (
            <span className="pill" key={i}>
              <Rich runs={runs} />
            </span>
          ))}
        </div>
      </div>
      <Signal />
    </header>
  );
}
