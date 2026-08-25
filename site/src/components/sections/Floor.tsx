import { floor } from "../../lib/site-content";
import { Rich } from "../ui/Rich";

export function Floor() {
  return (
    <section style={{ paddingTop: "20px" }}>
      <div className="wrap reveal">
        <div className="banner">
          <div className="lock">{floor.icon}</div>
          <h2>{floor.heading}</h2>
          {floor.body.map((runs, i) => (
            <p key={i}>
              <Rich runs={runs} />
            </p>
          ))}
          <p>
            <a className="feature-link" href={floor.linkHref}>
              {floor.linkLabel}
            </a>
          </p>
        </div>
      </div>
    </section>
  );
}
