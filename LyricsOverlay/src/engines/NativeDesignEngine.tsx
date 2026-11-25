import { DEMO_TRACK } from "../lib/lyrics";
import { hexA } from "../lib/color";
import type { EngineProps } from "./types";

/** Clean vertical stack: the active line is sharp, its neighbours blur away. */
export function NativeDesignEngine({ frame, colors, font }: EngineProps) {
  const total = DEMO_TRACK.lines.length;
  const rows = [-1, 0, 1].map((delta) => ({
    delta,
    line: DEMO_TRACK.lines[(frame.lineIndex + delta + total) % total],
  }));

  return (
    <div
      className="absolute inset-0 flex items-center justify-center overflow-hidden"
      style={{ fontFamily: font }}
    >
      <div className="flex flex-col items-center" style={{ gap: 34 }}>
        {rows.map(({ delta, line }, index) => {
          const active = delta === 0;
          return (
            <div
              key={`${index}-${line.text}`}
              style={{
                fontSize: active ? 46 : 32,
                fontWeight: active ? 600 : 500,
                letterSpacing: "-0.03em",
                lineHeight: 1.24,
                whiteSpace: "nowrap",
                opacity: active ? 1 : 0.24,
                filter: active ? "none" : "blur(7px)",
                transform: `scale(${active ? 1 : 0.94})`,
                transition: "all 320ms ease",
                textShadow: active ? `0 0 80px ${hexA(colors.highlight, 0.35)}` : "none",
              }}
            >
              {line.words.map((word, wi) => {
                const lit = active && wi <= frame.activeWord;
                return (
                  <span
                    key={`${word.text}-${wi}`}
                    style={{
                      color: active
                        ? lit
                          ? colors.lyricText
                          : hexA(colors.secondaryText, 0.55)
                        : hexA(colors.secondaryText, 0.5),
                      marginRight: "0.24em",
                      transition: "color 260ms ease",
                    }}
                  >
                    {word.text}
                  </span>
                );
              })}
            </div>
          );
        })}
      </div>
    </div>
  );
}
