import { AnimatePresence, motion } from "framer-motion";
import { RotateCcw } from "lucide-react";
import { useRef, type ReactNode } from "react";
import { argbToRgb, withArgbAlpha } from "../lib/color";
import { fontStack } from "../lib/styles";
import { useAppStore, type InspectorView } from "../store/useAppStore";

function Toggle({
  on,
  onChange,
  disabled = false,
}: {
  on: boolean;
  onChange: (value: boolean) => void;
  disabled?: boolean;
}) {
  return (
    <button
      role="switch"
      aria-checked={on}
      disabled={disabled}
      onClick={() => onChange(!on)}
      className="relative mt-0.5 h-[20px] w-[34px] shrink-0 rounded-full transition-colors duration-200"
      style={{
        background: on ? "#EA7B24" : "rgba(255,255,255,0.14)",
        opacity: disabled ? 0.45 : 1,
      }}
    >
      <motion.span
        layout
        transition={{ type: "spring", stiffness: 520, damping: 34 }}
        className="absolute top-[3px] h-[14px] w-[14px] rounded-full bg-white shadow-[0_1px_4px_rgba(0,0,0,0.5)]"
        style={{ left: on ? 17 : 3 }}
      />
    </button>
  );
}

function Row({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle?: string;
  children: ReactNode;
}) {
  return (
    <div className="flex items-start justify-between gap-3 py-[7px]">
      <div className="min-w-0">
        <p className="text-[12.5px] font-medium leading-tight text-white/88">{title}</p>
        {subtitle ? (
          <p className="mt-[3px] text-[10.5px] leading-tight text-white/36">{subtitle}</p>
        ) : null}
      </div>
      {children}
    </div>
  );
}

function SizeView() {
  const textScale = useAppStore((state) => state.overlay.textScale);
  const apply = useAppStore((state) => state.apply);
  const percent = Math.round(textScale * 100);
  const fill = ((percent - 60) / 140) * 100;

  return (
    <div>
      <p className="ui-label mb-3">Text size</p>
      <div className="flex items-center gap-3">
        <span className="text-[10px] font-semibold text-white/40">A</span>
        <input
          aria-label="Text size"
          type="range"
          min={60}
          max={200}
          step={1}
          value={percent}
          onChange={(event) => apply({ textScale: Number(event.target.value) / 100 })}
          className="range-amber"
          style={{
            background: `linear-gradient(to right, #EA7B24 0%, #EA7B24 ${fill}%, rgba(255,255,255,0.16) ${fill}%, rgba(255,255,255,0.16) 100%)`,
          }}
        />
        <span className="text-[18px] font-semibold leading-none text-white/40">A</span>
      </div>
      <div className="mt-3 flex items-center justify-between">
        <span className="text-[11px] font-medium tabular-nums text-white/55">{percent}%</span>
        <button
          onClick={() => apply({ textScale: 1 })}
          className="text-[11px] font-medium text-white/45 transition-colors hover:text-white"
        >
          Reset
        </button>
      </div>
    </div>
  );
}

function FontView() {
  const font = useAppStore((state) => state.overlay.font);
  const fonts = useAppStore((state) => state.overlay.fonts);
  const apply = useAppStore((state) => state.apply);

  return (
    <div>
      <p className="ui-label mb-2">Typeface</p>
      <div className="scroll-area -mr-1 max-h-[272px] overflow-y-auto pr-1">
        {fonts.map((name) => {
          const active = name === font;
          return (
            <button
              key={name}
              onClick={() => apply({ font: name })}
              className={`mb-[3px] flex w-full items-center justify-between rounded-[9px] px-2.5 py-[9px] text-left text-[14px] transition-colors duration-150 ${
                active ? "bg-white/10 text-white" : "text-white/68 hover:bg-white/[0.05]"
              }`}
              style={{ fontFamily: fontStack(name) }}
            >
              <span className="truncate">{name}</span>
              {active ? (
                <span className="ml-2 h-[5px] w-[5px] shrink-0 rounded-full bg-[#EA7B24]" />
              ) : null}
            </button>
          );
        })}
      </div>
    </div>
  );
}

const timingButton =
  "flex h-6 w-6 items-center justify-center rounded-[7px] bg-white/[0.07] text-[13px] leading-none text-white/70 transition-colors hover:bg-white/15 hover:text-white";

function ScreenView() {
  const overlay = useAppStore((state) => state.overlay);
  const apply = useAppStore((state) => state.apply);
  const shiftTiming = (delta: number) =>
    apply({ offsetMs: Math.max(-3000, Math.min(3000, overlay.offsetMs + delta)) });

  return (
    <div>
      <Row title="Show lyrics">
        <Toggle on={!overlay.hidden} onChange={(value) => apply({ hidden: !value })} />
      </Row>

      <div className="my-2 h-px w-full bg-white/[0.09]" />

      <p className="ui-label mb-2">Display mode</p>
      <div className="flex gap-1 rounded-[9px] bg-white/[0.04] p-1">
        {[
          { id: "desktopOnly", label: "Idle Desktop" },
          { id: "alwaysOnTop", label: "Over Any App" },
        ].map((mode) => {
          const active = overlay.displayMode === mode.id;
          return (
            <button
              key={mode.id}
              onClick={() => apply({ displayMode: mode.id })}
              className={`flex-1 rounded-[7px] py-[6px] text-[11.5px] font-medium transition-colors ${
                active ? "bg-white/12 text-white" : "text-white/45 hover:text-white/75"
              }`}
            >
              {mode.label}
            </button>
          );
        })}
      </div>
      <p className="mt-[6px] text-[10.5px] leading-tight text-white/36">
        {overlay.displayMode === "alwaysOnTop"
          ? "Floats above apps; clicks pass straight through."
          : "Only on the desktop; open apps stay above it."}
      </p>

      <Row title="Auto-hide in fullscreen apps & games" subtitle="Games and fullscreen video hide the lyrics">
        <Toggle
          on={overlay.hideInFullscreen}
          onChange={(value) => apply({ hideInFullscreen: value })}
        />
      </Row>

      <div className="my-2 h-px w-full bg-white/[0.09]" />

      <Row title="Keep the screen awake" subtitle="While music is playing">
        <Toggle on={overlay.keepAwake} onChange={(value) => apply({ keepAwake: value })} />
      </Row>
      <Row title="Full screen lyrics" subtitle="Centre the band over the whole work area">
        <Toggle on={overlay.fullScreen} onChange={(value) => apply({ fullScreen: value })} />
      </Row>
      <Row title="Lyric timing" subtitle="Slide every line earlier or later">
        <div className="flex items-center gap-1">
          <button onClick={() => shiftTiming(-100)} className={timingButton} aria-label="Lyrics earlier">
            −
          </button>
          <span className="w-[44px] text-center text-[11px] font-medium tabular-nums text-white/60">
            {overlay.offsetMs > 0 ? "+" : ""}
            {(overlay.offsetMs / 1000).toFixed(1)}s
          </span>
          <button onClick={() => shiftTiming(100)} className={timingButton} aria-label="Lyrics later">
            +
          </button>
        </div>
      </Row>
      <Row title="Start with Windows" subtitle="Open quietly into the tray at sign-in">
        <Toggle on={overlay.autoStart} onChange={(value) => apply({ autoStart: value })} />
      </Row>

      <div className="my-3 h-px w-full bg-white/[0.09]" />

      <p className="ui-label mb-2">Lyrics source</p>
      <div className="flex gap-1 rounded-[9px] bg-white/[0.04] p-1">
        {["lrc.red", "lrclib"].map((provider) => {
          const active = overlay.lyricsProvider === provider;
          return (
            <button
              key={provider}
              onClick={() => apply({ lyricsProvider: provider })}
              className={`flex-1 rounded-[7px] py-[6px] text-[11.5px] font-medium transition-colors ${
                active ? "bg-white/12 text-white" : "text-white/45 hover:text-white/75"
              }`}
            >
              {provider}
            </button>
          );
        })}
      </div>
    </div>
  );
}

function ColorRow({
  label,
  value,
  disabled,
  onChange,
}: {
  label: string;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <div className="flex items-center justify-between py-[7px]">
      <span
        className="text-[12.5px] font-medium transition-opacity"
        style={{ color: "rgba(255,255,255,0.78)", opacity: disabled ? 0.4 : 1 }}
      >
        {label}
      </span>
      <input
        type="color"
        aria-label={label}
        value={argbToRgb(value)}
        disabled={disabled}
        onChange={(event) => onChange(withArgbAlpha(value, event.target.value))}
        className="swatch-input"
        style={{ opacity: disabled ? 0.4 : 1, cursor: disabled ? "not-allowed" : "pointer" }}
      />
    </div>
  );
}

function AestheticsView() {
  const overlay = useAppStore((state) => state.overlay);
  const apply = useAppStore((state) => state.apply);

  return (
    <div>
      <Row title="Match the album cover" subtitle="Colors follow whatever is playing.">
        <Toggle on={overlay.matchArtwork} onChange={(value) => apply({ matchArtwork: value })} />
      </Row>

      <div className="my-2 h-px w-full bg-white/[0.09]" />

      <ColorRow
        label="Lyric text"
        value={overlay.textColor}
        disabled={overlay.matchArtwork}
        onChange={(value) => apply({ textColor: value })}
      />
      <ColorRow
        label="Highlight"
        value={overlay.highlightColor}
        disabled={overlay.matchArtwork}
        onChange={(value) => apply({ highlightColor: value })}
      />
      <ColorRow
        label="Secondary text"
        value={overlay.secondaryColor}
        disabled={overlay.matchArtwork}
        onChange={(value) => apply({ secondaryColor: value })}
      />

      <div className="mt-2 flex justify-end">
        <button
          onClick={() => apply({ resetColors: true })}
          disabled={overlay.matchArtwork}
          className="flex items-center gap-1.5 text-[11px] font-medium text-white/45 transition-colors hover:text-white disabled:cursor-not-allowed disabled:opacity-35"
        >
          <RotateCcw size={11} strokeWidth={2.2} /> Reset colors
        </button>
      </div>
    </div>
  );
}

export function InspectorCard() {
  const view = useAppStore((state) => state.inspector);
  const connected = useAppStore((state) => state.overlay.connected);
  const lastView = useRef<InspectorView>("size");
  if (view) lastView.current = view;
  const shown = view ?? lastView.current;

  return (
    <motion.aside
      layout
      initial={{ opacity: 0, y: -8, scale: 0.98 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, y: -8, scale: 0.98 }}
      transition={{ type: "spring", stiffness: 380, damping: 32 }}
      style={{ transformOrigin: "top right" }}
      className="glass fixed right-5 top-[56px] z-50 w-[270px] overflow-hidden rounded-[16px] p-4"
    >
      <AnimatePresence mode="wait" initial={false}>
        <motion.div
          key={shown}
          initial={{ opacity: 0, y: 4 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: -4 }}
          transition={{ duration: 0.18, ease: "easeOut" }}
        >
          {shown === "size" ? <SizeView /> : null}
          {shown === "font" ? <FontView /> : null}
          {shown === "screen" ? <ScreenView /> : null}
          {shown === "aesthetics" ? <AestheticsView /> : null}
        </motion.div>
      </AnimatePresence>

      {!connected ? (
        <p className="mt-3 border-t border-white/[0.07] pt-2.5 text-[10.5px] leading-tight text-white/30">
          Waiting for LyricOverlay — changes apply as soon as it connects.
        </p>
      ) : null}
    </motion.aside>
  );
}
