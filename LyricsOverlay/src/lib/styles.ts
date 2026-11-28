export interface StyleOption {
  name: string;
  description: string;
}

/** Mirrors StyleCatalog.All in the overlay; replaced by the live list when connected. */
export const FALLBACK_STYLES: StyleOption[] = [
  { name: "Kinetic", description: "Words fill one at a time, centred." },
  { name: "Fisheye", description: "A tight stack bulging through a lens." },
  {
    name: "Code Style",
    description:
      "A shell transcript: the last line as a muted // comment, the current line filling in after a red > prompt.",
  },
  {
    name: "Vine",
    description: "A wavy vine with a glowing tip; tendrils grow out as the tip reaches each word.",
  },
  {
    name: "Constellation",
    description: "Words as stars in a loose sky, drawn in time; the sung star flares.",
  },
  { name: "Bubble", description: "Recent lines as chat bubbles." },
  {
    name: "Native Design",
    description: "The song as a chat thread: charcoal and blue bubbles, the newest revealing as it is sung.",
  },
  {
    name: "Kinetic Scatter",
    description:
      "Full screen only: words scatter across the whole stage, each landing in its own zone in time.",
  },
];

/** Styles the overlay only offers with full-screen lyrics on. */
const FULLSCREEN_ONLY = new Set(["Kinetic Scatter"]);

export const isFullScreenOnly = (name: string) => FULLSCREEN_ONLY.has(name);

/** Mirrors StyleCatalog.Fonts in the overlay. */
export const FALLBACK_FONTS = [
  "SF Pro Display",
  "Segoe UI",
  "Segoe UI Variable Display",
  "Bahnschrift",
  "Georgia",
  "Consolas",
  "Cascadia Mono",
  "Cascadia Code",
];

export interface StyleLook {
  gradient: string;
  cardGradient: string;
  accent: string;
}

const mesh = (a: string, b: string, base: string) =>
  `radial-gradient(70% 85% at 22% 24%, ${a} 0%, transparent 62%),` +
  `radial-gradient(80% 90% at 82% 16%, ${b} 0%, transparent 64%),` +
  `radial-gradient(120% 120% at 50% 108%, ${base} 0%, #050507 78%)`;

const card = (a: string, b: string) =>
  `radial-gradient(90% 120% at 18% 12%, ${a} 0%, transparent 66%),` +
  `radial-gradient(110% 130% at 86% 90%, ${b} 0%, transparent 70%), #08080b`;

/** Studio ambience per real overlay style. */
const LOOKS: Record<string, StyleLook> = {
  Kinetic: {
    gradient: mesh("rgba(28,86,190,0.46)", "rgba(14,44,110,0.40)", "#071226"),
    cardGradient: card("rgba(34,94,205,0.55)", "rgba(12,30,70,0.5)"),
    accent: "#5B8FF9",
  },
  Fisheye: {
    gradient: mesh("rgba(138,52,120,0.46)", "rgba(96,34,74,0.38)", "#1b0c1a"),
    cardGradient: card("rgba(150,60,130,0.55)", "rgba(88,32,68,0.5)"),
    accent: "#D185C0",
  },
  "Code Style": {
    gradient: mesh("rgba(22,44,96,0.52)", "rgba(150,24,40,0.34)", "#070a14"),
    cardGradient: card("rgba(30,58,124,0.6)", "rgba(150,26,44,0.42)"),
    accent: "#FF3B30",
  },
  Vine: {
    gradient: mesh("rgba(14,104,84,0.46)", "rgba(10,74,78,0.36)", "#04120f"),
    cardGradient: card("rgba(18,124,96,0.55)", "rgba(10,70,74,0.5)"),
    accent: "#39D3A6",
  },
  Constellation: {
    gradient: mesh("rgba(46,52,158,0.48)", "rgba(26,32,92,0.40)", "#070817"),
    cardGradient: card("rgba(58,64,186,0.55)", "rgba(24,28,88,0.5)"),
    accent: "#7C86FF",
  },
  Bubble: {
    gradient: mesh("rgba(16,104,120,0.44)", "rgba(12,58,74,0.38)", "#04121a"),
    cardGradient: card("rgba(20,124,140,0.55)", "rgba(10,54,70,0.5)"),
    accent: "#4ECBDC",
  },
  "Native Design": {
    gradient: mesh("rgba(24,74,196,0.48)", "rgba(16,42,120,0.40)", "#060b18"),
    cardGradient: card("rgba(30,84,210,0.55)", "rgba(14,38,108,0.5)"),
    accent: "#5C8CFF",
  },
  "Kinetic Scatter": {
    gradient: mesh("rgba(120,124,136,0.34)", "rgba(60,62,70,0.32)", "#0a0a0c"),
    cardGradient: card("rgba(150,154,166,0.45)", "rgba(48,50,58,0.45)"),
    accent: "#F3F4F6",
  },
};

export const lookFor = (name: string): StyleLook => LOOKS[name] ?? LOOKS.Kinetic;

const FONT_STACKS: Record<string, string> = {
  "SF Pro Display": '"SF Pro Display", "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif',
  "Segoe UI": '"Segoe UI", "Segoe UI Variable Text", system-ui, sans-serif',
  "Segoe UI Variable Display": '"Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif',
  Bahnschrift: '"Bahnschrift", "Segoe UI", system-ui, sans-serif',
  Georgia: 'Georgia, "Times New Roman", serif',
  Consolas: 'Consolas, "Cascadia Mono", ui-monospace, monospace',
  "Cascadia Mono": '"Cascadia Mono", Consolas, ui-monospace, monospace',
  "Cascadia Code": '"Cascadia Code", "Cascadia Mono", Consolas, ui-monospace, monospace',
};

export const fontStack = (name: string) =>
  FONT_STACKS[name] ?? `"${name}", "Segoe UI", system-ui, sans-serif`;
