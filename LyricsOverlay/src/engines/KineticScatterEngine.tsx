import { AnimatePresence, motion } from "framer-motion";
import { useEffect, useRef, useState } from "react";
import { hexA } from "../lib/color";
import { useAppStore } from "../store/useAppStore";
import type { EngineProps } from "./types";

/**
 * Kinetic Scatter: a full-viewport editorial word collage.
 *
 * The line is distributed over a stratified grid that spans the whole stage, clamped inside a
 * 6%..94% / 12%..88% safe area and settled by an iterative AABB repulsion pass, so no word ever
 * clips an edge and none sit on top of another. Layout is deterministic per line, so words never
 * jump between renders.
 */

const SAFE = { left: 0.06, right: 0.94, top: 0.12, bottom: 0.88 };
const CLEARANCE = 28;
const REPULSION_PASSES = 30;

const hash = (seed: number) => {
  const value = Math.sin(seed * 12.9898) * 43758.5453;
  return value - Math.floor(value);
};

interface Box {
  x: number;
  y: number;
  w: number;
  h: number;
}

interface Slot {
  x: number;
  y: number;
  size: number;
  rotation: number;
}

const clamp = (value: number, low: number, high: number) =>
  Math.min(Math.max(value, low), high);

const clampBox = (box: Box, left: number, top: number, right: number, bottom: number): Box => {
  const dx = box.x < left ? left - box.x : box.x + box.w > right ? right - box.x - box.w : 0;
  const dy = box.y < top ? top - box.y : box.y + box.h > bottom ? bottom - box.y - box.h : 0;
  return { x: box.x + dx, y: box.y + dy, w: box.w, h: box.h };
};

/** Deterministic cell order spanning every column and row (right column + bottom row included). */
const stratifiedOrder = (columns: number, rows: number, count: number, seed: number) => {
  const total = columns * rows;
  const pool = Array.from({ length: total }, (_, index) => index);

  for (let i = total - 1; i > 0; i -= 1) {
    const j = clamp(Math.floor(hash(seed * 97 + i * 13) * (i + 1)), 0, i);
    [pool[i], pool[j]] = [pool[j], pool[i]];
  }

  const chosen = pool.slice(0, Math.min(count, total));

  if (count >= 4) {
    const ensure = (match: (cell: number) => boolean) => {
      if (chosen.some(match)) return;
      const found = pool.find((cell) => match(cell) && !chosen.includes(cell));
      if (found !== undefined) chosen[chosen.length - 1] = found;
    };
    ensure((cell) => cell % columns === columns - 1);
    if (rows > 1) ensure((cell) => Math.floor(cell / columns) === rows - 1);
  }

  return Array.from({ length: count }, (_, i) => chosen[i % chosen.length]);
};

const resolveOverlaps = (boxes: Box[], left: number, top: number, right: number, bottom: number) => {
  for (let pass = 0; pass < REPULSION_PASSES; pass += 1) {
    let moved = false;

    for (let i = 0; i < boxes.length; i += 1) {
      for (let j = i + 1; j < boxes.length; j += 1) {
        const a = boxes[i];
        const b = boxes[j];
        const overlapX = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x) + CLEARANCE;
        const overlapY = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y) + CLEARANCE;
        if (overlapX <= 0 || overlapY <= 0) continue;

        moved = true;
        if (overlapX < overlapY) {
          const push = overlapX / 2;
          const direction = a.x + a.w / 2 <= b.x + b.w / 2 ? -1 : 1;
          boxes[i] = clampBox({ ...a, x: a.x + push * direction }, left, top, right, bottom);
          boxes[j] = clampBox({ ...b, x: b.x - push * direction }, left, top, right, bottom);
        } else {
          const push = overlapY / 2;
          const direction = a.y + a.h / 2 <= b.y + b.h / 2 ? -1 : 1;
          boxes[i] = clampBox({ ...a, y: a.y + push * direction }, left, top, right, bottom);
          boxes[j] = clampBox({ ...b, y: b.y - push * direction }, left, top, right, bottom);
        }
      }
    }

    if (!moved) break;
  }
};

/** Responsive type: clamp(2.4rem, 4.6vw, 6.2rem) scaled by the Size slider. */
const baseSize = (width: number, scalePercent: number) =>
  Math.min(Math.max(38.4, width * 0.046), 99.2) * (scalePercent / 100);

const density = (count: number) =>
  count > 18 ? 0.58 : count > 12 ? 0.7 : count > 7 ? 0.84 : 1;

const measureText = (() => {
  let context: CanvasRenderingContext2D | null = null;
  return (text: string, size: number, font: string) => {
    if (typeof document === "undefined") return text.length * size * 0.56;
    context ??= document.createElement("canvas").getContext("2d");
    if (!context) return text.length * size * 0.56;
    context.font = `700 ${size}px ${font}`;
    return context.measureText(text).width;
  };
})();

const layout = (
  texts: string[],
  width: number,
  height: number,
  lineIndex: number,
  scalePercent: number,
  font: string,
): Slot[] => {
  if (width <= 1 || height <= 1) return texts.map(() => ({ x: 0, y: 0, size: 0, rotation: 0 }));

  const left = width * SAFE.left;
  const right = width * SAFE.right;
  const top = height * SAFE.top;
  const bottom = height * SAFE.bottom;

  const count = Math.max(1, texts.length);
  const aspect = width / height;
  const columns = Math.max(2, Math.ceil(Math.sqrt(count * aspect)));
  const rows = Math.max(1, Math.ceil(count / columns));

  const cellWidth = (right - left) / columns;
  const cellHeight = (bottom - top) / rows;
  const order = stratifiedOrder(columns, rows, count, lineIndex);
  const size0 = baseSize(width, scalePercent) * density(count);
  const maxWidth = (right - left) * 0.94;

  const boxes: Box[] = [];
  const slots: Slot[] = [];

  texts.forEach((text, i) => {
    if (!text.trim()) {
      slots.push({ x: 0, y: 0, size: 0, rotation: 0 });
      return;
    }

    let size = Math.max(18, size0 * (0.88 + 0.27 * hash(i * 3 + lineIndex * 71)));
    const natural = measureText(text, size, font) + size * 0.48;
    if (natural > maxWidth) size *= maxWidth / natural;
    size = Math.max(18, size);

    const textWidth = measureText(text, size, font);
    const overhang = Math.max(textWidth + size * 0.48, size * 1.35) * 0.06;
    const w = textWidth + size * 0.48 + overhang;
    const h = size * 1.35 + size * 0.16 + overhang;

    const cell = order[i];
    const column = cell % columns;
    const row = Math.floor(cell / columns);
    // Organic jitter: +-20% of the cell, so the grid reads as scattered, not tabular.
    const jitterX = (hash(i * 7 + lineIndex * 53 + column * 11) - 0.5) * 0.4;
    const jitterY = (hash(i * 11 + lineIndex * 37 + row * 17) - 0.5) * 0.4;

    const centerX = left + (column + 0.5 + jitterX) * cellWidth;
    const centerY = top + (row + 0.5 + jitterY) * cellHeight;

    const box = clampBox(
      { x: centerX - w / 2, y: centerY - h / 2, w, h },
      left,
      top,
      right,
      bottom,
    );
    boxes.push(box);
    slots.push({
      x: box.x + box.w / 2,
      y: box.y + box.h / 2,
      size,
      rotation: (hash(i * 13 + lineIndex * 29) - 0.5) * 6,
    });
  });

  resolveOverlaps(boxes, left, top, right, bottom);

  let cursor = 0;
  texts.forEach((text, i) => {
    if (!text.trim()) return;
    const box = boxes[cursor];
    cursor += 1;
    if (!box) return;
    slots[i] = { ...slots[i], x: box.x + box.w / 2, y: box.y + box.h / 2 };
  });

  return slots;
};

/** Kinetic Scatter: full-viewport word collage, the current word cut out in white. */
export function KineticScatterEngine({ frame, colors, font, stage }: EngineProps) {
  const hostRef = useRef<HTMLDivElement>(null);
  const stageWidth = stage?.width ?? 0;
  const stageHeight = stage?.height ?? 0;
  const [box, setBox] = useState({ w: stageWidth, h: stageHeight });

  useEffect(() => {
    const apply = (w: number, h: number) =>
      setBox((prev) => (prev.w === w && prev.h === h ? prev : { w, h }));

    // The host measures the stage; the engine only measures when it was not given a size.
    if (stageWidth > 0 && stageHeight > 0) {
      apply(stageWidth, stageHeight);
      return;
    }

    const node = hostRef.current;
    if (!node) return;
    const observer = new ResizeObserver((entries) => {
      const rect = entries[0].contentRect;
      apply(rect.width, rect.height);
    });
    observer.observe(node);
    return () => observer.disconnect();
  }, [stageWidth, stageHeight]);

  const scalePercent = Math.round(useAppStore((state) => state.overlay.textScale) * 100);
  const words = frame.line.words;
  const slots = layout(
    words.map((word) => word.text),
    box.w,
    box.h,
    frame.lineIndex,
    scalePercent,
    font,
  );

  return (
    <div
      ref={hostRef}
      className="absolute inset-0"
      style={{
        fontFamily: `"Inter", "Plus Jakarta Sans", ${font}`,
        overflow: "visible",
      }}
    >
      <AnimatePresence initial={false}>
        <motion.div
          key={frame.lineIndex}
          initial={{ opacity: 0, scale: 0.96 }}
          animate={{ opacity: 1, scale: 1 }}
          exit={{ opacity: 0, scale: 0.96 }}
          transition={{ duration: 0.28, ease: "easeOut" }}
          style={{
            position: "absolute",
            inset: 0,
            overflow: "visible",
            transformOrigin: "center",
          }}
        >
          {words.map((word, i) => {
            const slot = slots[i];
            if (!slot || slot.size <= 0) return null;

            const sung = i <= frame.activeWord && word.start <= frame.time;
            const active = i === frame.activeWord;
            const opacity = active ? 1 : sung ? 0.88 : 0.4;

            return (
              <div
                key={`${frame.lineIndex}-${i}`}
                style={{
                  position: "absolute",
                  left: slot.x,
                  top: slot.y,
                  transform: "translate(-50%, -50%)",
                  overflow: "visible",
                }}
              >
                <motion.div
                  initial={{ opacity: 0, scale: 0.92 }}
                  animate={{ opacity: 1, scale: 1 }}
                  transition={{ duration: 0.28, ease: "easeOut", delay: i * 0.022 }}
                  style={{ transformOrigin: "center", overflow: "visible" }}
                >
                  <motion.div
                    animate={{ y: [0, -5, 0] }}
                    transition={{
                      duration: 5 + (i % 3) * 0.7,
                      repeat: Infinity,
                      ease: "easeInOut",
                    }}
                  >
                    <span
                      style={{
                        display: "inline-block",
                        padding: "0.08em 0.24em",
                        fontSize: slot.size,
                        fontWeight: 700,
                        lineHeight: 1.35,
                        letterSpacing: "-0.02em",
                        whiteSpace: "nowrap",
                        overflow: "visible",
                        color: active ? "#0A0A0A" : "#F3F4F6",
                        background: active ? "#FFFFFF" : "transparent",
                        opacity,
                        transform: `rotate(${slot.rotation}deg)`,
                        textShadow: active ? "none" : `0 0 26px ${hexA(colors.highlight, 0.3)}`,
                        transition:
                          "color 260ms ease, background-color 260ms ease, opacity 260ms ease",
                      }}
                    >
                      {word.text}
                    </span>
                  </motion.div>
                </motion.div>
              </div>
            );
          })}
        </motion.div>
      </AnimatePresence>
    </div>
  );
}

