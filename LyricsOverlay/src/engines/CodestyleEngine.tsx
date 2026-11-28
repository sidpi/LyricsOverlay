import { DEMO_TRACK } from "../lib/lyrics";
import { hexA } from "../lib/color";
import type { EngineProps } from "./types";

const MONO = '"Cascadia Code", "Cascadia Mono", Consolas, ui-monospace, monospace';

/** A shell transcript: the previous line as a comment, the current line after a red prompt. */
export function CodestyleEngine({ frame, colors }: EngineProps) {
  const total = DEMO_TRACK.lines.length;
  const previous = DEMO_TRACK.lines[(frame.lineIndex - 1 + total) % total];
  const text = frame.line.text;
  const typedCount = Math.min(text.length, Math.floor(text.length * frame.lineProgress * 1.12));
  const typed = text.slice(0, typedCount);
  const ghost = text.slice(typedCount);
  const accent = "#FF3B30";
  const upcoming = [1, 2].flatMap((offset) => {
    const line = DEMO_TRACK.lines[frame.lineIndex + offset];
    return line && line.text.trim() ? [line.text] : [];
  });

  return (
    <div
      className="absolute inset-0 flex flex-col items-center justify-center overflow-hidden"
      style={{ fontFamily: MONO }}
    >
      <div style={{ width: "84%", maxWidth: 920 }}>
        <div
          style={{
            display: "flex",
            alignItems: "baseline",
            gap: 12,
            fontSize: 17,
            color: hexA(colors.secondaryText, 0.34),
            filter: "blur(1.2px)",
            marginBottom: 14,
            whiteSpace: "nowrap",
            overflow: "hidden",
          }}
        >
          <span style={{ color: hexA(colors.secondaryText, 0.52) }}>{"//"}</span>
          <span>{previous.text}</span>
        </div>

        <div
          style={{
            display: "flex",
            alignItems: "baseline",
            gap: 14,
            fontSize: 44,
            whiteSpace: "nowrap",
            overflow: "hidden",
          }}
        >
          <span style={{ color: accent, fontSize: 34 }}>&gt;</span>
          <span>
            <span style={{ color: colors.lyricText, textShadow: `0 0 42px ${hexA(accent, 0.28)}` }}>
              {typed}
            </span>
            <span style={{ color: hexA(colors.lyricText, 0.3) }}>{ghost}</span>
          </span>
        </div>

        {upcoming.map((line, offset) => (
          <div
            key={line}
            style={{
              display: "flex",
              alignItems: "baseline",
              gap: 14,
              marginTop: offset === 0 ? 10 : 6,
              fontSize: offset === 0 ? 32 : 26,
              color: hexA(colors.lyricText, offset === 0 ? 0.26 : 0.16),
              whiteSpace: "nowrap",
              overflow: "hidden",
            }}
          >
            <span style={{ color: hexA(colors.lyricText, offset === 0 ? 0.36 : 0.22) }}>&gt;</span>
            <span>{line}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
