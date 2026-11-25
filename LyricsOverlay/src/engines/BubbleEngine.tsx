import { hexA } from "../lib/color";
import { DEMO_TRACK } from "../lib/lyrics";
import type { EngineProps } from "./types";

/** Bubble: recent lines as chat bubbles, the newest revealing word by word. */
export function BubbleEngine({ frame, colors, font }: EngineProps) {
  const total = DEMO_TRACK.lines.length;
  const older = [
    DEMO_TRACK.lines[(frame.lineIndex - 2 + total) % total],
    DEMO_TRACK.lines[(frame.lineIndex - 1 + total) % total],
  ];

  return (
    <div
      className="absolute inset-0 flex flex-col items-center justify-center"
      style={{ fontFamily: font, gap: 12 }}
    >
      {older.map((line, index) => (
        <div
          key={`${index}-${line.text}`}
          style={{
            maxWidth: "74%",
            padding: "12px 22px",
            borderRadius: 22,
            background: "rgba(255,255,255,0.06)",
            border: "1px solid rgba(255,255,255,0.07)",
            color: hexA(colors.secondaryText, index === 0 ? 0.22 : 0.4),
            fontSize: 28,
            letterSpacing: "-0.01em",
            textAlign: "center",
            transform: `scale(${index === 0 ? 0.94 : 0.97})`,
          }}
        >
          {line.text}
        </div>
      ))}

      <div
        style={{
          maxWidth: "78%",
          padding: "15px 26px",
          borderRadius: 26,
          background: hexA(colors.highlight, 0.2),
          border: `1px solid ${hexA(colors.highlight, 0.45)}`,
          display: "flex",
          flexWrap: "wrap",
          justifyContent: "center",
          gap: "0.28em",
          fontSize: 34,
          fontWeight: 600,
          letterSpacing: "-0.02em",
          boxShadow: `0 20px 60px -24px ${hexA(colors.highlight, 0.65)}`,
        }}
      >
        {frame.line.words.map((word, i) => (
          <span
            key={`${word.text}-${i}`}
            style={{
              color: i <= frame.activeWord ? "#fff" : "rgba(255,255,255,0.34)",
              transition: "color 180ms ease",
            }}
          >
            {word.text}
          </span>
        ))}
      </div>
    </div>
  );
}
