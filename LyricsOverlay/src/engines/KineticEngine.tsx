import { hexA } from "../lib/color";
import type { EngineProps } from "./types";

/** Kinetic: one centred line, words filling in as they are sung. */
export function KineticEngine({ frame, colors, font }: EngineProps) {
  return (
    <div
      className="absolute inset-0 flex items-center justify-center overflow-hidden"
      style={{ fontFamily: font }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "baseline",
          gap: "0.28em",
          padding: "0 64px",
          fontSize: 50,
          fontWeight: 700,
          letterSpacing: "-0.035em",
          whiteSpace: "nowrap",
        }}
      >
        {frame.line.words.map((word, i) => {
          const lit = i <= frame.activeWord;
          const active = i === frame.activeWord;
          return (
            <span
              key={`${frame.lineIndex}-${i}`}
              style={{
                display: "inline-block",
                color: lit ? colors.lyricText : hexA(colors.secondaryText, 0.4),
                transform: active ? "scale(1.04)" : "scale(1)",
                transformOrigin: "center bottom",
                textShadow: active ? `0 0 56px ${hexA(colors.highlight, 0.55)}` : "none",
                transition:
                  "color 200ms ease, transform 220ms cubic-bezier(.34,1.56,.64,1), text-shadow 220ms ease",
              }}
            >
              {word.text}
            </span>
          );
        })}
      </div>
    </div>
  );
}
