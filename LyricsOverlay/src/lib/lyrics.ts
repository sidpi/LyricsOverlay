export interface TimedWord {
  text: string;
  /** seconds relative to the start of the track */
  start: number;
  end: number;
}

export interface LyricLine {
  text: string;
  start: number;
  end: number;
  words: TimedWord[];
}

export interface DemoTrack {
  title: string;
  artist: string;
  durationSec: number;
  lines: LyricLine[];
}

export const LINE_DURATION = 4.6;

const LYRICS: string[] = [
  "i can hear the rain on the glass",
  "gold light falling on your hands",
  "we are water and we are fire",
  "hold my heart in your hands",
  "the sky is opening for you",
  "call me when the city sleeps",
  "every light is a door back home",
  "you are the gold in the water",
  "fire on the edge of the sky",
  "hold on, we are almost home",
];

const buildLine = (text: string, index: number): LyricLine => {
  const start = index * LINE_DURATION + 0.18;
  const end = start + LINE_DURATION - 0.62;
  const tokens = text.split(" ").filter(Boolean);
  const span = (end - start) / Math.max(tokens.length, 1);
  const words: TimedWord[] = tokens.map((token, i) => ({
    text: token,
    start: start + i * span,
    end: start + i * span + span + (i === tokens.length - 1 ? 0.28 : 0),
  }));
  return { text, start, end, words };
};

export const DEMO_TRACK: DemoTrack = {
  title: "Neon Hours",
  artist: "Vela Grey",
  durationSec: LYRICS.length * LINE_DURATION,
  lines: LYRICS.map(buildLine),
};

export const TRACK_LOOP = DEMO_TRACK.durationSec;
