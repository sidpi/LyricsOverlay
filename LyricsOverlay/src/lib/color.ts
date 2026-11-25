const clamp = (n: number) => Math.min(255, Math.max(0, Math.round(n)));

const parse = (color: string): [number, number, number] => {
  let value = color.trim().replace("#", "");
  if (value.length === 3) {
    value = value
      .split("")
      .map((c) => c + c)
      .join("");
  }
  if (value.length === 8) {
    value = value.slice(2);
  }
  if (value.length !== 6 || /[^0-9a-f]/i.test(value)) return [255, 255, 255];
  return [
    parseInt(value.slice(0, 2), 16),
    parseInt(value.slice(2, 4), 16),
    parseInt(value.slice(4, 6), 16),
  ];
};

export const hexA = (hex: string, alpha: number): string => {
  const [r, g, b] = parse(hex);
  return `rgba(${clamp(r)}, ${clamp(g)}, ${clamp(b)}, ${Math.min(1, Math.max(0, alpha))})`;
};

/** #AARRGGBB (overlay config) -> #RRGGBB for a colour input. */
export const argbToRgb = (value: string): string => {
  const raw = value.trim().replace("#", "");
  const rgb = raw.length === 8 ? raw.slice(2) : raw.slice(-6);
  return `#${(/^[0-9a-f]{6}$/i.test(rgb) ? rgb : "ffffff").toUpperCase()}`;
};

/** Keep the overlay's existing alpha when the studio edits the hue. */
export const withArgbAlpha = (original: string, rgb: string): string => {
  const raw = original.trim().replace("#", "");
  const alpha = raw.length === 8 ? raw.slice(0, 2) : "FF";
  return `#${alpha}${rgb.replace("#", "").toUpperCase()}`;
};
