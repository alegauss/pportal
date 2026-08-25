import { useEffect, useRef } from "react";
import { frameTrip } from "../lib/site-content";
import { Rich } from "./ui/Rich";

// The hero is a frame's journey rather than a feature list, because latency is what this
// application is judged on and the path is where latency is spent. All steps render on the
// server and with no JavaScript, so the twin and a crawler read the whole thing; the
// autoplay only reveals them one at a time after mount, which keeps the server render and
// the first client render identical and never trips hydration.
//
// Only the reader moves the window. As each step lands the panel scrolls its own element
// with scrollTop, never scrollIntoView, which would scroll every scrollable ancestor and
// drag a reader who has scrolled past the hero back to it once a second.
export function FrameTrip() {
  const panelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const panel = panelRef.current;
    if (!panel) return;
    if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;

    const steps = Array.from(panel.querySelectorAll<HTMLElement>(".session-step"));
    if (steps.length === 0) return;

    panel.classList.add("session--playing"); // CSS hides the steps until each gets .in
    let i = 0;
    let timer = 0;
    const tick = () => {
      if (i >= steps.length) return;
      steps[i].classList.add("in");
      panel.scrollTop = panel.scrollHeight; // own element only
      i += 1;
      timer = window.setTimeout(tick, 1150);
    };
    timer = window.setTimeout(tick, 450);
    return () => window.clearTimeout(timer);
  }, []);

  return (
    <div className="session reveal">
      <div className="session-ask">
        <span className="session-ask-tag">One frame</span>
        <span className="session-ask-text">{frameTrip.question}</span>
      </div>
      <div className="session-scroll" ref={panelRef}>
        {frameTrip.steps.map((step) => (
          <div className="session-step" key={step.stage}>
            <div className="session-cmd">
              <span className="session-prompt">›</span>
              <code>{step.stage}</code>
              <span className="session-cost">{step.tag}</span>
            </div>
            <div className="session-out">{step.note}</div>
          </div>
        ))}
      </div>
      <div className="session-foot">
        <span className="session-cmp target">{frameTrip.measured}</span>
        <span className="session-cmp today">{frameTrip.where}</span>
      </div>
      <p className="session-note">
        <Rich runs={frameTrip.note} />
      </p>
    </div>
  );
}
