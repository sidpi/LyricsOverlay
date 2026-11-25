/**
 * Transport for the overlay control surface.
 *
 * The studio runs inside the LyricOverlay app (WPF + WebView2) and talks to the overlay
 * through `window.chrome.webview`. A plain browser preview has no host, so every call
 * turns into a no-op and the UI keeps its last known state.
 */

interface WebviewMessage {
  data: unknown;
}

interface WebviewHost {
  postMessage(message: unknown): void;
  addEventListener(type: "message", handler: (event: WebviewMessage) => void): void;
  removeEventListener(type: "message", handler: (event: WebviewMessage) => void): void;
}

interface HostReply {
  id?: number;
  ok?: boolean;
  state?: unknown;
  type?: string;
}

const webviewHost = (): WebviewHost | undefined =>
  (window as unknown as { chrome?: { webview?: WebviewHost } }).chrome?.webview;

/** True when the studio is hosted by the LyricOverlay desktop app. */
export const isHosted = (): boolean => Boolean(webviewHost());

let sequence = 0;
let wired = false;
const waiting = new Map<number, (reply: HostReply | null) => void>();

function ensureWired(host: WebviewHost): void {
  if (wired) return;
  wired = true;
  host.addEventListener("message", (event) => {
    const data = event.data as HostReply | null;
    if (!data || typeof data !== "object" || typeof data.id !== "number") return;
    const resolve = waiting.get(data.id);
    if (!resolve) return;
    waiting.delete(data.id);
    resolve(data);
  });
}

function sendHost(payload: Record<string, unknown>, timeoutMs = 2500): Promise<HostReply | null> {
  const host = webviewHost();
  if (!host) return Promise.resolve(null);

  ensureWired(host);
  const id = ++sequence;

  return new Promise((resolve) => {
    const timer = window.setTimeout(() => {
      waiting.delete(id);
      resolve(null);
    }, timeoutMs);

    waiting.set(id, (reply) => {
      window.clearTimeout(timer);
      resolve(reply);
    });

    host.postMessage({ id, ...payload });
  });
}

/** Studio command names map onto the host's control commands. */
const HOST_COMMANDS: Record<string, string> = {
  overlay_state: "state",
  overlay_apply: "apply",
  overlay_launch: "state",
};

export async function invokeCommand<T>(
  command: string,
  args?: Record<string, unknown>,
): Promise<T | null> {
  const reply = await sendHost({ cmd: HOST_COMMANDS[command] ?? command, ...(args ?? {}) });
  return reply && reply.state ? (reply.state as T) : null;
}

export async function listenEvent<T>(
  _name: string,
  handler: (payload: T) => void,
): Promise<() => void> {
  const host = webviewHost();
  if (!host) return () => {};

  const listener = (event: WebviewMessage) => {
    const data = event.data as { type?: string; state?: T } | null;
    if (!data || data.type !== "state" || !data.state) return;
    handler(data.state);
  };

  host.addEventListener("message", listener);
  return () => host.removeEventListener("message", listener);
}

const hostWindowAction = (action: string) => {
  void sendHost({ cmd: "window", action }, 600);
};

export const windowControls = {
  minimize: () => hostWindowAction("minimize"),
  toggleMaximize: () => hostWindowAction("maximize"),
  hide: () => hostWindowAction("close"),
};
