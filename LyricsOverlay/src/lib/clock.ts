import { useEffect, useState } from "react";
import { DEMO_TRACK, TRACK_LOOP, type LyricLine } from "./lyrics";

export interface LyricFrame {
  time: number;
  lineIndex: number;
  line: LyricLine;
  /** index of the word currently being sung */
  activeWord: number;
  /** 0..1 progress across the whole line */
  lineProgress: number;
  /** 0..1 progress inside the active word */
  wordProgress: number;
}

let baseSeconds = 0;
let baseStamp = typeof performance !== "undefined" ? performance.now() : 0;

export const currentTime = () => {
  const now = typeof performance !== "undefined" ? performance.now() : baseStamp;
  const t = baseSeconds + (now - baseStamp) / 1000;
  return t % TRACK_LOOP;
};

export const frameAt = (time: number): LyricFrame => {
  const lines = DEMO_TRACK.lines;
  // The line is current from its own start stamp, so progress can run start -> next start.
  let index = 0;
  for (let i = 0; i < lines.length; i += 1) {
    if (lines[i].start <= time) index = i;
  }
  const line = lines[index];
  const nextStart = lines[index + 1]?.start ?? line.end;
  const lineProgress = Math.min(1, Math.max(0, (time - line.start) / Math.max(0.001, nextStart - line.start)));

  let activeWord = 0;
  for (let i = 0; i < line.words.length; i += 1) {
    if (time >= line.words[i].start) activeWord = i;
  }
  const word = line.words[activeWord];
  const wordProgress = word
    ? Math.min(1, Math.max(0, (time - word.start) / Math.max(0.18, word.end - word.start)))
    : 0;

  return { time, lineIndex: index, line, activeWord, lineProgress, wordProgress };
};

/** A shared clock drives the hero and every gallery thumbnail. */
export const useLyricFrame = (fps = 60, enabled = true): LyricFrame => {
  const [frame, setFrame] = useState<LyricFrame>(() => frameAt(currentTime()));

  useEffect(() => {
    if (!enabled) return;
    let timer = 0;
    const step = () => {
      setFrame(frameAt(currentTime()));
      timer = window.setTimeout(step, Math.max(16, 1000 / fps));
    };
    timer = window.setTimeout(step, Math.max(16, 1000 / fps));
    return () => window.clearTimeout(timer);
  }, [fps, enabled]);

  return frame;
};
