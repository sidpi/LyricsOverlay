import { AnimatePresence, motion } from "framer-motion";
import { Check, ChevronLeft, ChevronRight, Wand2 } from "lucide-react";
import { isFullScreenOnly, lookFor } from "../lib/styles";
import { useAppStore } from "../store/useAppStore";
import { LyricRenderer } from "./LyricRenderer";

export function HeroStage() {
  const overlay = useAppStore((state) => state.overlay);
  const apply = useAppStore((state) => state.apply);
  const setInspector = useAppStore((state) => state.setInspector);

  const styles = overlay.styles;
  const index = Math.max(
    0,
    styles.findIndex((entry) => entry.name === overlay.style),
  );
  const style = styles[index] ?? { name: "Kinetic", description: "" };
  const look = lookFor(style.name);

  const step = (delta: number) => {
    if (styles.length === 0) return;
    const next = (index + delta + styles.length) % styles.length;
    apply({ style: styles[next].name });
  };

  return (
    <section className="relative h-[68vh] min-h-[520px] w-full overflow-hidden">
      <AnimatePresence initial={false}>
        <motion.div
          key={style.name}
          className="absolute inset-0"
          style={{ background: look.gradient }}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.6, ease: "easeInOut" }}
        >
          <div className="breathe absolute inset-0" style={{ background: look.gradient }} />
        </motion.div>
      </AnimatePresence>

      <div className="grain" />
      <div
        className="pointer-events-none absolute inset-x-0 bottom-0 h-[34%]"
        style={{ background: "linear-gradient(to bottom, rgba(5,5,7,0) 0%, #050507 92%)" }}
      />

      <div
        className={
          isFullScreenOnly(style.name)
            ? "absolute inset-0"
            : "absolute inset-x-0 top-[6%] bottom-[24%]"
        }
      >
        <AnimatePresence initial={false} mode="sync">
          <motion.div
            key={style.name}
            className="absolute inset-0"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0, scale: 0.96 }}
            transition={{ opacity: { duration: 0.35 }, scale: { duration: 0.25, ease: "easeOut" } }}
          >
            <LyricRenderer styleName={style.name} variant="hero" />
          </motion.div>
        </AnimatePresence>
      </div>

      <button
        onClick={() => step(-1)}
        aria-label="Previous style"
        className="absolute left-4 top-1/2 z-20 flex h-9 w-9 -translate-y-1/2 items-center justify-center rounded-full text-white/40 transition-all duration-200 hover:bg-white/10 hover:text-white"
      >
        <ChevronLeft size={22} strokeWidth={1.6} />
      </button>
      <button
        onClick={() => step(1)}
        aria-label="Next style"
        className="absolute right-4 top-1/2 z-20 flex h-9 w-9 -translate-y-1/2 items-center justify-center rounded-full text-white/40 transition-all duration-200 hover:bg-white/10 hover:text-white"
      >
        <ChevronRight size={22} strokeWidth={1.6} />
      </button>

      <div className="absolute bottom-9 left-8 z-20 max-w-[460px]">
        <AnimatePresence mode="wait" initial={false}>
          <motion.div
            key={style.name}
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -6 }}
            transition={{ duration: 0.28, ease: "easeOut" }}
          >
            <p className="mb-2 flex items-center gap-2 text-[10.5px] font-medium uppercase tracking-[0.18em] text-white/40">
              <span
                className="inline-block h-[5px] w-[5px] rounded-full"
                style={{ background: look.accent, boxShadow: `0 0 10px ${look.accent}` }}
              />
              {overlay.connected && overlay.playing && overlay.track
                ? `Now playing · ${overlay.track} — ${overlay.artist}`
                : overlay.connected
                  ? "Live on your desktop"
                  : "Preview · overlay offline"}
            </p>
            <h1 className="font-serif-display text-[46px] leading-[1.04] text-white">
              {style.name}
            </h1>
            <p className="mt-2.5 max-w-[380px] text-[12.5px] leading-[1.55] text-white/55">
              {style.description}
            </p>

            <div className="mt-5 flex items-center gap-2.5">
              <motion.button
                whileTap={{ scale: 0.97 }}
                onClick={() => apply({ style: style.name })}
                className="flex h-[34px] items-center gap-1.5 rounded-full bg-white px-4 text-[12px] font-semibold text-[#050507] shadow-[0_10px_30px_-12px_rgba(255,255,255,0.6)]"
              >
                <Check size={13} strokeWidth={2.8} /> In use
              </motion.button>

              <motion.button
                whileTap={{ scale: 0.94 }}
                onClick={() => setInspector("aesthetics")}
                aria-label="Style accents"
                className="glass-soft flex h-[38px] w-[38px] items-center justify-center rounded-full text-white/70 transition-colors hover:text-white"
              >
                <Wand2 size={15} strokeWidth={1.9} />
              </motion.button>
            </div>
          </motion.div>
        </AnimatePresence>
      </div>

      <div className="absolute bottom-6 left-1/2 z-20 flex -translate-x-1/2 items-center gap-[7px]">
        {styles.map((entry, i) => (
          <button
            key={entry.name}
            onClick={() => apply({ style: entry.name })}
            aria-label={entry.name}
            className="h-[5px] rounded-full transition-all duration-300"
            style={{
              width: i === index ? 16 : 5,
              background: i === index ? "#fff" : "rgba(255,255,255,0.28)",
            }}
          />
        ))}
      </div>
    </section>
  );
}
