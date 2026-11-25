import { hexA } from "../lib/color";
import type { EngineProps } from "./types";

const W = 1000;
const H = 560;

export function BranchesEngine({ frame, colors, font }: EngineProps) {
  const words = frame.line.words;
  const n = Math.max(words.length, 1);
  const nodes = words.map((word, i) => ({
    word,
    x: 130 + (i * (W - 260)) / Math.max(1, n - 1),
    y: H / 2 + Math.sin(i * 1.15 - 0.4) * 58,
    above: i % 2 === 0,
  }));

  let d = nodes.length ? `M ${nodes[0].x} ${nodes[0].y}` : "";
  for (let i = 1; i < nodes.length; i += 1) {
    const prev = nodes[i - 1];
    const cur = nodes[i];
    const mx = (prev.x + cur.x) / 2;
    const my = (prev.y + cur.y) / 2;
    d += ` Q ${prev.x} ${prev.y} ${mx} ${my}`;
  }
  if (nodes.length > 1) {
    const last = nodes[nodes.length - 1];
    d += ` L ${last.x} ${last.y}`;
  }

  const progress = Math.min(1, (frame.activeWord + 1) / n);

  return (
    <div className="absolute inset-0 overflow-hidden" style={{ fontFamily: font }}>
      <svg
        viewBox={`0 0 ${W} ${H}`}
        preserveAspectRatio="xMidYMid meet"
        style={{ position: "absolute", inset: 0, width: "100%", height: "100%" }}
      >
        <defs>
          <radialGradient id="branch-tip">
            <stop offset="0%" stopColor="#fff" stopOpacity="1" />
            <stop offset="45%" stopColor="#fff" stopOpacity="0.5" />
            <stop offset="100%" stopColor="#fff" stopOpacity="0" />
          </radialGradient>
          <filter id="branch-glow" x="-60%" y="-60%" width="220%" height="220%">
            <feGaussianBlur stdDeviation="9" result="b" />
            <feMerge>
              <feMergeNode in="b" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
        </defs>

        {d ? (
          <>
            <path d={d} fill="none" stroke={hexA(colors.secondaryText, 0.12)} strokeWidth={1.6} />
            <path
              d={d}
              fill="none"
              stroke="#fff"
              strokeWidth={2.1}
              strokeLinecap="round"
              pathLength={1}
              strokeDasharray="1"
              strokeDashoffset={1 - progress}
              style={{ filter: "drop-shadow(0 0 10px rgba(255,255,255,0.8))", transition: "stroke-dashoffset 220ms linear" }}
            />
          </>
        ) : null}

        {nodes.map(({ word, x, y, above }, i) => {
          const reached = i <= frame.activeWord;
          const active = i === frame.activeWord;
          const labelY = y + (above ? -86 : 86);
          return (
            <g key={`${word.text}-${i}`} style={{ opacity: reached ? 1 : 0.42, transition: "opacity 300ms ease" }}>
              <line
                x1={x}
                y1={y}
                x2={x + (above ? -14 : 14)}
                y2={labelY + (above ? 32 : -32)}
                stroke={reached ? hexA("#ffffff", 0.72) : hexA(colors.secondaryText, 0.18)}
                strokeWidth={1.3}
              />
              {active ? <circle cx={x} cy={y} r={34} fill="url(#branch-tip)" opacity={0.55} /> : null}
              <circle
                cx={x}
                cy={y}
                r={active ? 7.5 : 4.2}
                fill={reached ? "#fff" : hexA(colors.secondaryText, 0.4)}
                filter={reached ? "url(#branch-glow)" : undefined}
                style={{ transition: "r 220ms ease" }}
              />
              <foreignObject x={x - 110} y={above ? labelY - 26 : labelY - 16} width={220} height={64}>
                <div
                  style={{
                    width: "100%",
                    textAlign: "center",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    gap: 8,
                    fontSize: 26,
                    fontWeight: 600,
                    letterSpacing: "-0.02em",
                    color: active ? "#fff" : reached ? hexA(colors.lyricText, 0.82) : hexA(colors.secondaryText, 0.55),
                    textShadow: active ? "0 0 30px rgba(255,255,255,0.45)" : "none",
                    whiteSpace: "nowrap",
                  }}
                >
                  {word.text}
                </div>
              </foreignObject>
            </g>
          );
        })}
      </svg>
    </div>
  );
}
