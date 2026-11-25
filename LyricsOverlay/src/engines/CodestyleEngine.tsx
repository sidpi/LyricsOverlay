import { DEMO_TRACK, type LyricLine } from "../lib/lyrics";
import { hexA } from "../lib/color";
import type { EngineProps } from "./types";

const MONO = '"Cascadia Code", "JetBrains Mono", "Consolas", ui-monospace, monospace';

const clean = (text: string) => text.replace(/[^a-zA-Z0-9']/g, "");

/** Turn a lyric line into something that reads like a code expression. */
const codeFor = (line: LyricLine): string => {
  const words = line.words.map((w) => clean(w.text)).filter(Boolean);
  if (words.length === 0) return "";
  if (words.length === 1) return words[0];
  if (words.length === 2) return `${words[0]}.${words[1]}`;
  return `${words[0]}.${words[1]}(${words.slice(2).join(", ")})`;
};

/** Three lines of source; the newest lyric types out under a red cursor. */
export function CodestyleEngine({ frame, colors, font }: EngineProps) {
  const total = DEMO_TRACK.lines.length;
  const previous = DEMO_TRACK.lines[(frame.lineIndex - 1 + total) % total];
  const code = codeFor(frame.line);
  const typedCount = Math.min(code.length, Math.floor(code.length * frame.lineProgress * 1.12));
  const typed = code.slice(0, typedCount);
  const ghost = code.slice(typedCount);
  const typingDone = typedCount >= code.length;
  const accent = "#FF3B30";

  return (
    <div
      className="absolute inset-0 flex flex-col items-center justify-center overflow-hidden"
      style={{ fontFamily: MONO, color: colors.lyricText }}
    >
      <div style={{ width: "82%", maxWidth: 880 }}>
        <div
          style={{
            display: "flex",
            alignItems: "baseline",
            gap: 14,
            fontSize: 22,
            color: hexA(colors.secondaryText, 0.28),
            filter: "blur(1.5px)",
            marginBottom: 18,
            whiteSpace: "nowrap",
            overflow: "hidden",
          }}
        >
          <span style={{ color: hexA(colors.secondaryText, 0.3) }}>{"//"}</span>
          <span>{codeFor(previous)}</span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: 16, fontSize: 46 }}>
          <span style={{ color: hexA(accent, 0.55), fontSize: 30 }}>&gt;</span>
          <span style={{ whiteSpace: "nowrap", overflow: "hidden" }}>
            <span
              style={{
                color: colors.lyricText,
                textShadow: `0 0 42px ${hexA(accent, 0.3)}`,
              }}
            >
              {typed}
            </span>
            <span style={{ color: hexA(colors.secondaryText, 0.16) }}>{ghost}</span>
            <span
              className={typingDone ? "blink" : undefined}
              style={{
                display: "inline-block",
                width: "0.52em",
                height: "1.02em",
                marginLeft: 4,
                transform: "translateY(0.12em)",
                background: accent,
                boxShadow: `0 0 26px ${hexA(accent, 0.9)}`,
                opacity: typingDone ? undefined : 0.92,
              }}
            />
          </span>
        </div>

        <div
          style={{
            marginTop: 22,
            fontSize: 14,
            letterSpacing: "0.02em",
            color: hexA(colors.secondaryText, 0.3),
            fontFamily: font,
          }}
        >
          {frame.line.text}
        </div>
      </div>
    </div>
  );
}
