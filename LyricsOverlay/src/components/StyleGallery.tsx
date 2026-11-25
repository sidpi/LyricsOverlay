import { motion } from "framer-motion";
import { Check } from "lucide-react";
import { useLyricFrame } from "../lib/clock";
import { isFullScreenOnly, lookFor } from "../lib/styles";
import { useAppStore } from "../store/useAppStore";
import { LyricRenderer } from "./LyricRenderer";

export function StyleGallery() {
  const overlay = useAppStore((state) => state.overlay);
  const apply = useAppStore((state) => state.apply);
  const frame = useLyricFrame(24);
  // Full-screen-exclusive styles are only offered where they can render.
  const styles = overlay.styles.filter(
    (style) => overlay.fullScreen || !isFullScreenOnly(style.name),
  );

  return (
    <section className="relative z-10 px-8 pb-20 pt-4">
      <div className="mb-5 flex items-baseline justify-between">
        <h2 className="text-[20px] font-bold tracking-[-0.01em] text-white">All styles</h2>
        <span className="text-[11.5px] text-white/35">{styles.length} styles</span>
      </div>

      <div className="grid grid-cols-5 gap-[18px]">
        {styles.map((style) => {
          const active = style.name === overlay.style;
          const look = lookFor(style.name);
          return (
            <motion.button
              key={style.name}
              onClick={() => apply({ style: style.name })}
              whileHover={{ scale: 1.02 }}
              transition={{ duration: 0.2, ease: "easeOut" }}
              className="group text-left"
              title={style.description}
            >
              <div
                className="relative overflow-hidden rounded-[14px] border transition-colors duration-200"
                style={{
                  aspectRatio: "16 / 10",
                  borderColor: active ? "rgba(255,255,255,0.85)" : "rgba(255,255,255,0.09)",
                  background: look.cardGradient,
                  boxShadow: active
                    ? "0 18px 50px -26px rgba(255,255,255,0.35)"
                    : "0 18px 46px -30px rgba(0,0,0,0.9)",
                }}
              >
                <div className="absolute inset-0 opacity-90">
                  <LyricRenderer
                    styleName={style.name}
                    variant="card"
                    frame={frame}
                    scaleOverride={1}
                  />
                </div>

                <div
                  className="pointer-events-none absolute inset-0 opacity-0 transition-opacity duration-200 group-hover:opacity-100"
                  style={{ boxShadow: "inset 0 0 0 1.5px rgba(255,255,255,0.85)" }}
                />

                {active ? (
                  <span className="absolute right-2 top-2 flex items-center gap-1 rounded-full bg-white px-2 py-[3px] text-[9.5px] font-semibold text-[#050507] shadow-[0_6px_18px_-6px_rgba(0,0,0,0.8)]">
                    <Check size={9} strokeWidth={3.2} /> In use
                  </span>
                ) : null}
              </div>

              <div className="mt-2.5 flex items-baseline justify-between gap-2">
                <span className="text-[13px] font-semibold text-white/90">{style.name}</span>
                <span className="whitespace-nowrap text-[10.5px] text-white/35">
                  {active ? "Applied" : "Click to apply"}
                </span>
              </div>
            </motion.button>
          );
          })}
      </div>
    </section>
  );
}
