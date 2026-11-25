import { motion } from "framer-motion";
import { Minus, Play, RefreshCw, Square, X } from "lucide-react";
import type { ReactNode } from "react";
import { isHosted, windowControls } from "../lib/bridge";
import { useAppStore, type InspectorView } from "../store/useAppStore";

const TABS: { id: InspectorView; label: string }[] = [
  { id: "size", label: "Size" },
  { id: "font", label: "Font" },
  { id: "screen", label: "Screen" },
  { id: "aesthetics", label: "Aesthetics" },
];

function Tip({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="group relative flex">
      {children}
      <span className="pointer-events-none absolute right-0 top-[calc(100%+12px)] z-50 whitespace-nowrap rounded-md border border-white/10 bg-[#0c0c10]/96 px-2 py-1 text-[10.5px] font-medium text-white/70 opacity-0 shadow-[0_16px_40px_-12px_rgba(0,0,0,0.9)] transition-opacity duration-150 group-hover:opacity-100">
        {label}
      </span>
    </div>
  );
}

const iconButton =
  "relative flex h-7 w-7 items-center justify-center rounded-full text-white/60 transition-colors duration-150 hover:bg-white/10 hover:text-white";

export function TopBar() {
  const inspector = useAppStore((state) => state.inspector);
  const toggleInspector = useAppStore((state) => state.toggleInspector);
  const overlay = useAppStore((state) => state.overlay);
  const launch = useAppStore((state) => state.launch);
  const refresh = useAppStore((state) => state.refresh);

  // Hosted inside LyricOverlay itself: there is no separate app to launch.
  const hosted = isHosted();

  const status = !overlay.connected
    ? "Overlay offline"
    : overlay.playing && overlay.track
      ? `${overlay.track} — ${overlay.artist}`
      : "Overlay connected";

  return (
    <header
      data-drag-region
      className="fixed inset-x-0 top-0 z-40 flex h-16 items-center justify-between gap-6 px-5"
      style={{ background: "linear-gradient(to bottom, rgba(5,5,7,0.75), rgba(5,5,7,0))" }}
    >
      <div data-drag-region className="flex min-w-[190px] items-center gap-2">
        <img
          src="./logo.png"
          alt="Lyric Overlay"
          className="h-[18px] w-[18px] rounded-[5px] shadow-[0_0_14px_rgba(234,123,36,0.35)]"
        />
        <span data-drag-region className="text-[13px] font-semibold tracking-[0.01em] text-white">
          LyricsOverlay
        </span>
      </div>

      <div data-drag-region className="flex flex-1 justify-center">
        <div data-drag-region className="glass relative flex items-center gap-1 rounded-full p-1">
          {TABS.map((tab) => {
            const active = inspector === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => toggleInspector(tab.id)}
                className={`relative rounded-full px-4 py-[6px] text-[12.5px] font-medium transition-colors duration-200 ${
                  active ? "text-white" : "text-white/55 hover:text-white/85"
                }`}
              >
                {active ? (
                  <motion.span
                    layoutId="nav-pill"
                    transition={{ type: "spring", stiffness: 420, damping: 32 }}
                    className="absolute inset-0 rounded-full"
                    style={{ background: "rgba(255,255,255,0.14)" }}
                  />
                ) : null}
                <span className="relative">{tab.label}</span>
              </button>
            );
          })}
        </div>
      </div>

      <div className="flex items-center gap-2">
        <div className="glass flex items-center gap-1.5 rounded-full py-1 pl-3 pr-1">
          <span
            className="h-[6px] w-[6px] shrink-0 rounded-full"
            style={{
              background: overlay.connected ? "#1DB954" : "rgba(255,255,255,0.3)",
              boxShadow: overlay.connected ? "0 0 8px rgba(29,185,84,0.85)" : "none",
            }}
          />
          <span
            className="max-w-[168px] truncate text-[11px] font-medium text-white/62"
            title={status}
          >
            {status}
          </span>
          {hosted ? null : (
            <Tip label={overlay.connected ? "LyricOverlay is running" : "Open LyricOverlay"}>
              <button onClick={() => void launch()} className={iconButton} aria-label="Launch overlay">
                <Play size={13} strokeWidth={2.3} />
              </button>
            </Tip>
          )}
          <Tip label="Refresh from overlay">
            <button
              onClick={() => void refresh()}
              className={iconButton}
              aria-label="Refresh overlay state"
            >
              <RefreshCw size={13} strokeWidth={2.3} />
            </button>
          </Tip>
        </div>

        <div className="flex items-center gap-0.5 pl-1">
          <button
            onClick={() => void windowControls.minimize()}
            aria-label="Minimize"
            className="flex h-8 w-8 items-center justify-center rounded-lg text-white/55 transition-colors hover:bg-white/10 hover:text-white"
          >
            <Minus size={14} strokeWidth={2.4} />
          </button>
          <button
            onClick={() => void windowControls.toggleMaximize()}
            aria-label="Maximize"
            className="flex h-8 w-8 items-center justify-center rounded-lg text-white/55 transition-colors hover:bg-white/10 hover:text-white"
          >
            <Square size={11.5} strokeWidth={2.4} />
          </button>
          <button
            onClick={() => void windowControls.hide()}
            aria-label="Close"
            className="flex h-8 w-8 items-center justify-center rounded-lg text-white/55 transition-colors hover:bg-[#E5484D] hover:text-white"
          >
            <X size={14} strokeWidth={2.4} />
          </button>
        </div>
      </div>
    </header>
  );
}
