import { useEffect, useRef, useState, type ComponentType } from "react";
import { useLyricFrame, type LyricFrame } from "../lib/clock";
import { argbToRgb } from "../lib/color";
import { fontStack } from "../lib/styles";
import { useAppStore } from "../store/useAppStore";
import { KineticEngine } from "../engines/KineticEngine";
import { FisheyeEngine } from "../engines/FisheyeEngine";
import { CodestyleEngine } from "../engines/CodestyleEngine";
import { BranchesEngine } from "../engines/BranchesEngine";
import { ConstellationEngine } from "../engines/ConstellationEngine";
import { BubbleEngine } from "../engines/BubbleEngine";
import { NativeDesignEngine } from "../engines/NativeDesignEngine";
import { KineticScatterEngine } from "../engines/KineticScatterEngine";
import type { EngineProps } from "../engines/types";

export type RenderVariant = "hero" | "card";

/** Every engine lays out in the same virtual space; hosts just scale it. */
const VIRTUAL: Record<RenderVariant, { w: number; h: number }> = {
  hero: { w: 1100, h: 560 },
  card: { w: 1000, h: 625 },
};

/** Styles that compose over the whole viewport instead of the virtual stage. */
const FULL_BLEED = new Set(["Kinetic Scatter"]);

/** Keyed by the style names the overlay reports. */
const ENGINES: Record<string, ComponentType<EngineProps>> = {
  Kinetic: KineticEngine,
  Fisheye: FisheyeEngine,
  "Code Style": CodestyleEngine,
  Vine: BranchesEngine,
  Constellation: ConstellationEngine,
  Bubble: BubbleEngine,
  "Native Design": NativeDesignEngine,
  "Kinetic Scatter": KineticScatterEngine,
};

interface Props {
  styleName: string;
  variant?: RenderVariant;
  frame?: LyricFrame;
  scaleOverride?: number;
  className?: string;
}

export function LyricRenderer({
  styleName,
  variant = "hero",
  frame,
  scaleOverride,
  className = "",
}: Props) {
  const textScale = useAppStore((state) => state.overlay.textScale);
  const textColor = useAppStore((state) => state.overlay.textColor);
  const highlightColor = useAppStore((state) => state.overlay.highlightColor);
  const secondaryColor = useAppStore((state) => state.overlay.secondaryColor);
  const fontFamily = useAppStore((state) => state.overlay.font);

  const Engine = ENGINES[styleName] ?? KineticEngine;
  const liveFrame = useLyricFrame(60, !frame);
  const activeFrame = frame ?? liveFrame;

  const hostRef = useRef<HTMLDivElement>(null);
  const [box, setBox] = useState({ w: 0, h: 0 });

  useEffect(() => {
    const node = hostRef.current;
    if (!node) return;
    const observer = new ResizeObserver((entries) => {
      const rect = entries[0].contentRect;
      setBox({ w: rect.width, h: rect.height });
    });
    observer.observe(node);
    return () => observer.disconnect();
  }, []);

  const virtual = VIRTUAL[variant];
  const fit = box.w > 0 && box.h > 0 ? Math.min(box.w / virtual.w, box.h / virtual.h) : 0;
  // The real overlay shrinks lyrics to fit its width; the preview caps the zoom so long
  // lines never clip. Size changes are still visible, and the overlay gets the exact value.
  const requested = scaleOverride ?? (variant === "card" ? 1 : Math.min(1.2, Math.max(0.6, textScale)));
  const applied = fit * requested;

  if (FULL_BLEED.has(styleName)) {
    // Zero dead space: the engine lays out against the measured stage, not a scaled box.
    return (
      <div ref={hostRef} className={`relative h-full w-full overflow-hidden ${className}`}>
        <Engine
          frame={activeFrame}
          colors={{
            lyricText: argbToRgb(textColor),
            highlight: argbToRgb(highlightColor),
            secondaryText: argbToRgb(secondaryColor),
          }}
          font={fontStack(fontFamily)}
          stage={{ width: box.w, height: box.h }}
        />
      </div>
    );
  }

  return (
    <div ref={hostRef} className={`relative h-full w-full overflow-hidden ${className}`}>
      <div
        style={{
          position: "absolute",
          left: "50%",
          top: "50%",
          width: virtual.w,
          height: virtual.h,
          transform: `translate(-50%, -50%) scale(${applied})`,
          transformOrigin: "center",
          opacity: applied > 0 ? 1 : 0,
        }}
      >
        <Engine
          frame={activeFrame}
          colors={{
            lyricText: argbToRgb(textColor),
            highlight: argbToRgb(highlightColor),
            secondaryText: argbToRgb(secondaryColor),
          }}
          font={fontStack(fontFamily)}
        />
      </div>
    </div>
  );
}
