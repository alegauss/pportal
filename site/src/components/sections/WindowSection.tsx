import { windowSection } from "../../lib/site-content";
import { consoleStateIcons, windowDiagram } from "../../lib/diagrams";
import { Rich } from "../ui/Rich";
import { RawSvg } from "../ui/RawSvg";

export function WindowSection() {
  return (
    <section id="window">
      <div className="wrap">
        <div className="sec-head reveal">
          <div className="eyebrow">{windowSection.eyebrow}</div>
          <h2>{windowSection.heading}</h2>
          <p>
            <Rich runs={windowSection.intro} />
          </p>
        </div>
        <figure className="shot-frame reveal" style={{ margin: 0 }}>
          <RawSvg markup={windowDiagram} />
          <figcaption>
            <Rich runs={windowSection.caption} />
          </figcaption>
        </figure>

        <div className="sec-head reveal" style={{ marginTop: "62px" }}>
          <div className="eyebrow">{windowSection.statesEyebrow}</div>
          <h2>{windowSection.statesHeading}</h2>
        </div>
        <div className="states reveal">
          {windowSection.states.map((state) => (
            <div className={`state ${state.kind}`} key={state.kind}>
              <RawSvg markup={consoleStateIcons[state.kind]} />
              <h3>{state.title}</h3>
              <p>
                <Rich runs={state.body} />
              </p>
            </div>
          ))}
        </div>

        <div className="sec-head reveal" style={{ marginTop: "62px" }}>
          <div className="eyebrow">{windowSection.detailsEyebrow}</div>
          <h2>{windowSection.detailsHeading}</h2>
        </div>
        <ul className="feat-list two reveal">
          {windowSection.detailsList.map((runs, i) => (
            <li key={i}>
              <span className="chk">✓</span>
              <span>
                <Rich runs={runs} />
              </span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}
