// The band that closes the hero and opens the footer. The subject is a stream, so the page
// is bounded by one: three traces drifting sideways at different speeds, the nearest one
// filled, like a signal on an instrument rather than an ornament.
//
// Seamlessness here is arithmetic, not luck. Every trace is drawn twice across a 2880-unit
// viewBox laid out at 200% of the band, so 1440 units is exactly one band width; the drift
// translates by 50%, which is one whole repeat, so the frame after the last is the first.
// Each period below divides 1440 for the same reason: a trace that does not close on 1440
// shows a seam once per cycle, and once per cycle is every few seconds.
//
// The drift and the rise sit on two elements on purpose. Both are transforms, and two
// animations on one element are one property overwriting the other, so the outer div moves
// vertically and the inner svg travels sideways.
//
// Decorative only: it carries no copy, so it is hidden from the accessibility tree and
// dropped from the Markdown twin, and it stops moving under prefers-reduced-motion.

const SPAN = 2880; // two identical repeats of 1440
const FLOOR = 200; // the viewBox floor the fill reaches down to

// One trace as an open line: half a period up, half a period down, repeated across the span.
// The control points sit at a quarter and three quarters of each half, which makes the
// outgoing tangent of every segment equal the incoming tangent of the next, so the joins and
// the wrap at 1440 are smooth rather than kinked.
function trace(baseline: number, amplitude: number, period: number): string {
  const half = period / 2;
  const c1 = half * 0.25;
  const c2 = half * 0.75;
  let d = `M0 ${baseline}`;
  for (let x = 0; x < SPAN; x += period) {
    d += ` c${c1} ${-amplitude} ${c2} ${-amplitude} ${half} 0`;
    d += ` c${c1} ${amplitude} ${c2} ${amplitude} ${half} 0`;
  }
  return d;
}

// The same trace, closed down to the floor: the area under the line.
const area = (baseline: number, amplitude: number, period: number): string =>
  `${trace(baseline, amplitude, period)} V${FLOOR} H0 Z`;

// Back to front. The far trace is taller, slower and paler, which is what reads as depth;
// the speeds that go with it are in the stylesheet, next to the colours.
const LAYERS = [
  { key: "back", baseline: 74, amplitude: 26, period: 720 },
  { key: "mid", baseline: 102, amplitude: 16, period: 480 },
  { key: "front", baseline: 124, amplitude: 10, period: 360 },
] as const;

export function Signal({ className }: { className?: string }) {
  return (
    <div
      className={className ? `signal ${className}` : "signal"}
      aria-hidden="true"
      data-twin="omit"
    >
      {LAYERS.map((layer) => (
        <div className={`sig-rise sig-${layer.key}`} key={layer.key}>
          <svg
            className="sig-drift"
            viewBox={`0 0 ${SPAN} ${FLOOR}`}
            preserveAspectRatio="none"
            focusable="false"
          >
            <path
              className="sig-area"
              d={area(layer.baseline, layer.amplitude, layer.period)}
            />
            {/* The lit line rides its own layer's trace, drawn in the same svg so it cannot
                drift out of step with the area beneath it. */}
            <path
              className="sig-line"
              d={trace(layer.baseline, layer.amplitude, layer.period)}
            />
          </svg>
        </div>
      ))}
    </div>
  );
}
