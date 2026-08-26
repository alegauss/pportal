import { Fragment } from "react";
import { Nav } from "../components/Nav";
import { Footer } from "../components/Footer";
import { Ad } from "../components/ui/Ad";
import { Rich } from "../components/ui/Rich";
import { RawSvg } from "../components/ui/RawSvg";
import { features, type FeatureRecord } from "../lib/features";
import { compositionDiagram, mappingDiagram, windowDiagram } from "../lib/diagrams";

function Figure({ kind }: { kind: FeatureRecord["figure"] }) {
  if (kind === "composition") {
    return <RawSvg className="shot-frame reveal" markup={compositionDiagram} />;
  }
  if (kind === "window") return <RawSvg className="shot-frame reveal" markup={windowDiagram} />;
  if (kind === "mapping") return <RawSvg className="shot-frame reveal" markup={mappingDiagram} />;
  return null;
}

// The second level, and the reason there is no dropdown in the nav: the sibling pages are
// listed once you are on one of them, where they are the choice actually in front of you.
// A <nav> rather than a <div>, which is what keeps it out of the Markdown twins - it is
// chrome, and the twins carry content.
function Siblings({ slug }: { slug: string }) {
  return (
    <nav className="feature-siblings" aria-label="The other pages">
      {features.map((f) => (
        <a
          className={f.slug === slug ? "current" : undefined}
          aria-current={f.slug === slug ? "page" : undefined}
          href={`/pportal/features/${f.slug}/`}
          key={f.slug}
        >
          {f.heading}
        </a>
      ))}
    </nav>
  );
}

export function FeaturePage({ record }: { readonly record: FeatureRecord }) {
  // The seam the ad takes: before the section that starts the back half. A page here runs
  // three or four sections, so this is the boundary a reader crosses about midway, and it is
  // always a boundary BETWEEN sections rather than a gap inside one.
  const adBefore = Math.ceil(record.sections.length / 2);

  return (
    <>
      <Nav />
      <header className="hero page-hero" id="top">
        <div className="wrap">
          <a className="feature-back" href="/pportal/features/">
            ← All pages
          </a>
          <div className="eyebrow">{record.eyebrow}</div>
          <h1>{record.heading}</h1>
          <p className="sub">
            <Rich runs={record.lead} />
          </p>
        </div>
      </header>

      <section>
        <div className="wrap">
          <Siblings slug={record.slug} />
          {record.figure && <Figure kind={record.figure} />}
          <div className="feature-body">
            {record.sections.map((s, at) => (
              <Fragment key={s.heading}>
                {at === adBefore && <Ad slot={`feature-${record.slug}`} band={false} />}
                <div className="feature-section reveal">
                  <h2>{s.heading}</h2>
                {s.body && (
                  <p>
                    <Rich runs={s.body} />
                  </p>
                )}
                {s.list && (
                  <ul className="feat-list">
                    {s.list.map((item, i) => (
                      <li key={i}>
                        <span className="chk">✓</span>
                        <span>
                          <Rich runs={item} />
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
                  {record.slug === "picture" && s.heading === "Paced by the display" && (
                    <p>
                      <a className="feature-link" href="/pportal/hardware/">
                        What this asks of your GPU →
                      </a>
                    </p>
                  )}
                </div>
              </Fragment>
            ))}
          </div>
        </div>
      </section>

      <Footer />
    </>
  );
}
