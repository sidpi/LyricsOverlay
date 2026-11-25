import { DEMO_TRACK } from "../lib/lyrics";
import { hexA } from "../lib/color";
import type { EngineProps } from "./types";

/** A tight stack bulging through a lens: the centre line is largest and sharpest. */
export function FisheyeEngine({ frame, colors, font }: EngineProps) {
  const total = DEMO_TRACK.lines.length;
  const rows = [-2, -1, 0, 1, 2].map((delta) => ({
    delta,
    line: DEMO_TRACK.lines[(frame.lineIndex + delta + total) % total],
  }));

  return (
    <div
      className="absolute inset-0 flex items-center justify-center overflow-hidden"
      style={{ fontFamily: font, perspective: "900px" }}
    >
      <div className="flex flex-col items-center" style={{ transform: "translateY(2px)" }}>
        {rows.map(({ delta, line }, index) => {
          const distance = Math.abs(delta);
          const active = delta === 0;
          const size = 44 - distance * 9;
          return (
            <div
              key={`${index}-${line.text}`}
              style={{
                fontSize: size,
                fontWeight: 700,
                letterSpacing: "-0.035em",
                lineHeight: 1.2,
                whiteSpace: "nowrap",
                opacity: 1 - distance * 0.24,
                filter: `blur(${distance * 1.8}px)`,
                transform: `rotateX(${delta * 16}deg) scale(${1 - distance * 0.07}, ${1 - distance * 0.16})`,
                transformOrigin: "center",
                textShadow: active ? `0 0 60px ${hexA(colors.highlight, 0.5)}` : "none",
                transition: "font-size 260ms ease, opacity 260ms ease, filter 260ms ease",
              }}
            >
              {line.words.map((word, wi) => {
                const lit = active
                  ? wi <= frame.activeWord
                  : distance === 1 && wi <= frame.activeWord * 0.5;
                return (
                  <span
                    key={`${word.text}-${wi}`}
                    style={{
                      color: active
                        ? lit
                          ? colors.lyricText
                          : hexA(colors.secondaryText, 0.6)
                        : hexA(colors.secondaryText, 0.5),
                      marginRight: "0.26em",
                      transition: "color 200ms ease",
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
