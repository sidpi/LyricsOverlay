import { hexA } from "../lib/color";
import type { EngineProps } from "./types";

const W = 1000;
const H = 560;

const seeded = (i: number) => {
  const value = Math.sin(i * 91.733 + 4.1) * 43758.5453;
  return value - Math.floor(value);
};

/** Connected glowing star nodes, each carrying one word of the line. */
export function ConstellationEngine({ frame, colors, font }: EngineProps) {
  const words = frame.line.words;
  const n = Math.max(1, words.length);

  const nodes = words.map((word, i) => {
    const angle = (i / n) * Math.PI * 2 - Math.PI / 2;
    const radiusX = 330 + seeded(i) * 40;
    const radiusY = 150 + seeded(i + 7) * 34;
    return {
      word,
      x: W / 2 + Math.cos(angle) * radiusX,
      y: H / 2 + Math.sin(angle) * radiusY,
      angle,
    };
  });

  return (
    <div className="absolute inset-0 overflow-hidden" style={{ fontFamily: font }}>
      <svg
        viewBox={`0 0 ${W} ${H}`}
        preserveAspectRatio="xMidYMid meet"
        style={{ position: "absolute", inset: 0, width: "100%", height: "100%" }}
      >
        <defs>
          <radialGradient id="star-halo">
            <stop offset="0%" stopColor="#fff" stopOpacity="0.95" />
            <stop offset="35%" stopColor="#fff" stopOpacity="0.35" />
            <stop offset="100%" stopColor="#fff" stopOpacity="0" />
          </radialGradient>
          <filter id="star-glow" x="-80%" y="-80%" width="260%" height="260%">
            <feGaussianBlur stdDeviation="5" result="b" />
            <feMerge>
              <feMergeNode in="b" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
        </defs>

        {nodes.map((node, i) => {
          const next = nodes[(i + 1) % nodes.length];
          const reached = i <= frame.activeWord && (i + 1) <= frame.activeWord + 1;
          return (
            <line
              key={`edge-${i}`}
              x1={node.x}
              y1={node.y}
              x2={next.x}
              y2={next.y}
              stroke={reached ? hexA("#ffffff", 0.55) : hexA(colors.secondaryText, 0.13)}
              strokeWidth={reached ? 1.3 : 0.9}
              style={{ transition: "stroke 300ms ease" }}
            />
          );
        })}
        {nodes.length > 3
          ? nodes.map((node, i) => {
              const across = nodes[(i + Math.floor(nodes.length / 2)) % nodes.length];
              return (
                <line
                  key={`chord-${i}`}
                  x1={node.x}
                  y1={node.y}
                  x2={across.x}
                  y2={across.y}
                  stroke={hexA(colors.secondaryText, 0.06)}
                  strokeWidth={0.7}
                />
              );
            })
          : null}

        {nodes.map((node, i) => {
          const reached = i <= frame.activeWord;
          const active = i === frame.activeWord;
          const dx = Math.cos(node.angle) * 34;
          const dy = Math.sin(node.angle) * 24;
          return (
            <g key={`node-${i}`}>
              {active ? <circle cx={node.x} cy={node.y} r={54} fill="url(#star-halo)" opacity={0.65} /> : null}
              <circle
                cx={node.x}
                cy={node.y}
                r={active ? 10 : reached ? 6.5 : 4}
                fill={reached ? "#fff" : hexA(colors.secondaryText, 0.38)}
                filter={reached ? "url(#star-glow)" : undefined}
              />
              <foreignObject
                x={node.x + dx - 100}
                y={node.y + dy - 20}
                width={200}
                height={56}
                style={{ overflow: "visible" }}
              >
                <div
                  style={{
                    width: "100%",
                    textAlign: "center",
                    fontSize: 24,
                    fontWeight: 600,
                    whiteSpace: "nowrap",
                    color: active ? "#fff" : reached ? hexA(colors.lyricText, 0.78) : hexA(colors.secondaryText, 0.45),
                    textShadow: active ? `0 0 26px ${hexA(colors.highlight, 0.8)}` : "none",
                    transition: "color 300ms ease",
                  }}
                >
                  {node.word.text}
                </div>
              </foreignObject>
            </g>
          );
        })}
      </svg>
    </div>
  );
}
