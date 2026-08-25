import { picture } from "../../lib/site-content";
import { compositionDiagram } from "../../lib/diagrams";
import { Rich } from "../ui/Rich";
import { RawSvg } from "../ui/RawSvg";

export function Picture() {
  return (
    <section id="picture">
      <div className="wrap">
        <div className="sec-head reveal">
          <div className="eyebrow">{picture.eyebrow}</div>
          <h2>
            <Rich runs={picture.headingRuns} />
          </h2>
          <p>
            <Rich runs={picture.intro} />
          </p>
        </div>
        <RawSvg className="shot-frame reveal" markup={compositionDiagram} />
        <div className="grid" style={{ marginTop: "34px" }}>
          {picture.cards.map((card) => (
            <div className="card reveal" key={card.title}>
              <div className="ico">{card.icon}</div>
              <h3>{card.title}</h3>
              <p>
                <Rich runs={card.body} />
              </p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
