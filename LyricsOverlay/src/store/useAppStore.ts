import { create } from "zustand";
import { invokeCommand, listenEvent } from "../lib/bridge";
import { FALLBACK_FONTS, FALLBACK_STYLES, type StyleOption } from "../lib/styles";

export type InspectorView = "size" | "font" | "screen" | "aesthetics";

/** Exactly what the overlay reports over its control pipe. */
export interface OverlayState {
  connected: boolean;
  style: string;
  font: string;
  textScale: number;
  fullScreen: boolean;
  hidden: boolean;
  autoStart: boolean;
  offsetMs: number;
  keepAwake: boolean;
  matchArtwork: boolean;
  textColor: string;
  highlightColor: string;
  secondaryColor: string;
  lyricsProvider: string;
  displayMode: string;
  hideInFullscreen: boolean;
  playing: boolean;
  track: string;
  artist: string;
  positionMs: number;
  durationMs: number;
  styles: StyleOption[];
  fonts: string[];
}

export interface OverlayPatch {
  style?: string;
  font?: string;
  textScale?: number;
  fullScreen?: boolean;
  hidden?: boolean;
  autoStart?: boolean;
  offsetMs?: number;
  keepAwake?: boolean;
  matchArtwork?: boolean;
  textColor?: string;
  highlightColor?: string;
  secondaryColor?: string;
  lyricsProvider?: string;
  displayMode?: string;
  hideInFullscreen?: boolean;
  resetColors?: boolean;
}

export const FALLBACK_STATE: OverlayState = {
  connected: false,
  style: "Kinetic",
  font: "Segoe UI",
  textScale: 1,
  fullScreen: false,
  hidden: false,
  autoStart: false,
  offsetMs: 0,
  keepAwake: true,
  matchArtwork: true,
  textColor: "#A6FFFFFF",
  highlightColor: "#FFFFFFFF",
  secondaryColor: "#73FFFFFF",
  lyricsProvider: "lrc.red",
  displayMode: "desktopOnly",
  hideInFullscreen: true,
  playing: false,
  track: "",
  artist: "",
  positionMs: 0,
  durationMs: 0,
  styles: FALLBACK_STYLES,
  fonts: FALLBACK_FONTS,
};

interface AppStore {
  overlay: OverlayState;
  inspector: InspectorView | null;
  refresh: () => Promise<void>;
  apply: (patch: OverlayPatch) => void;
  launch: () => Promise<void>;
  setInspector: (view: InspectorView | null) => void;
  toggleInspector: (view: InspectorView) => void;
}

let pending: OverlayPatch = {};
let timer = 0;
let lastApplyAt = 0;

const mergeRemote = (remote: Partial<OverlayState> | null, fromApply = false) => {
  const current = useAppStore.getState().overlay;

  // A poll can arrive mid-drag; keep the optimistic value until the write lands.
  if (!fromApply && Date.now() - lastApplyAt < 450) {
    return;
  }
  // A missed poll (or a bridge that is still starting) keeps the last known state; the
  // studio must never flash "offline" while the overlay is simply busy or waking up.
  if (!remote || remote.connected === false) {
    return;
  }

  useAppStore.setState({
    overlay: {
      ...FALLBACK_STATE,
      ...remote,
      connected: true,
      styles: remote.styles && remote.styles.length > 0 ? remote.styles : FALLBACK_STYLES,
      fonts: remote.fonts && remote.fonts.length > 0 ? remote.fonts : FALLBACK_FONTS,
    },
  });
};

export const useAppStore = create<AppStore>((set, get) => ({
  overlay: FALLBACK_STATE,
  inspector: null,

  refresh: async () => {
    const remote = await invokeCommand<Partial<OverlayState>>("overlay_state");
    mergeRemote(remote);
  },

  launch: async () => {
    const remote = await invokeCommand<Partial<OverlayState>>("overlay_launch");
    mergeRemote(remote);
  },

  apply: (patch) => {
    const { resetColors, ...rest } = patch;
    const current = get().overlay;
    set({
      overlay: {
        ...current,
        ...rest,
        ...(resetColors ? { matchArtwork: false } : {}),
      },
    });

    lastApplyAt = Date.now();
    pending = { ...pending, ...patch };
    if (timer) window.clearTimeout(timer);
    timer = window.setTimeout(() => {
      const payload = pending;
      pending = {};
      timer = 0;
      void invokeCommand<Partial<OverlayState>>("overlay_apply", { patch: payload }).then((remote) =>
        mergeRemote(remote, true),
      );
    }, 90);
  },

  setInspector: (view) => set({ inspector: view }),
  toggleInspector: (view) => set({ inspector: get().inspector === view ? null : view }),
}));

let booted = false;

export const initApp = async () => {
  if (booted) return;
  booted = true;

  await useAppStore.getState().refresh();
  await listenEvent<Partial<OverlayState>>("overlay:state", mergeRemote);
};
