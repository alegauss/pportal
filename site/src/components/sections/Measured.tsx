import { measured } from "../../lib/site-content";
import { Rich } from "../ui/Rich";

export function Measured() {
  return (
    <section id="measured">
      <div className="wrap">
        <div className="sec-head reveal">
          <div className="eyebrow">{measured.eyebrow}</div>
          <h2>{measured.heading}</h2>
          <p>
            <Rich runs={measured.intro} />
          </p>
        </div>
        <ul className="feat-list two reveal">
          {/* What the record carries and what compares two of them render as one list and are
              two arrays, so the split stays visible in the source that states it. */}
          {[...measured.rows, ...measured.notes].map(([lead, rest]) => (
            <li key={lead}>
              <span className="chk">✓</span>
              <span>
                <b>{lead}</b>
                {rest}
              </span>
            </li>
          ))}
        </ul>
        <p
          style={{
            textAlign: "center",
            color: "var(--muted-2)",
            fontSize: ".9rem",
            marginTop: "30px",
          }}
        >
          <Rich runs={measured.note} />
        </p>
      </div>
    </section>
  );
}
