import { t } from "./i18n";

export const Methods = {
    Read: "getSettings", Save: "saveSettings", Suspend: "suspendGestures", Resume: "resumeGestures", Capture: "captureInputAction",
    UpdateInfo: "getUpdateInfo", CheckUpdates: "checkForUpdates", DownloadUpdate: "downloadUpdate", OpenReleases: "openReleases",
} as const;
export const HostEvents = { UpdateProgress: "updateProgress", CheckUpdates: "checkUpdates" } as const;
type Method = typeof Methods[keyof typeof Methods];
type WebView = {
    postMessage(message: unknown): void;
    addEventListener(event: "message", listener: (event: MessageEvent) => void): void;
};
const webview = (window as Window & { chrome?: { webview?: WebView } }).chrome?.webview;
const pending = new Map<string, { resolve: (value: unknown) => void; reject: (error: Error) => void; timer: ReturnType<typeof setTimeout> }>();
const RequestTimeoutMilliseconds = 30_000;
const CaptureTimeoutMilliseconds = 24 * 60 * 60 * 1000;
const CheckUpdatesTimeoutMilliseconds = 60_000;
const DownloadTimeoutMilliseconds = 10 * 60 * 1000;
const RequestIdPrefix = "settings-";
let sequence = 0;
const eventListeners = new Map<string, Set<(payload: { percent?: number }) => void>>();
webview?.addEventListener("message", (event) => {
    const response = event.data;
    if (response?.eventName) {
        eventListeners.get(response.eventName)?.forEach((listener) => listener(response));
        return;
    }
    const request = pending.get(response?.id);
    if (!request) return;
    clearTimeout(request.timer);
    pending.delete(response.id);
    if (response.error) request.reject(new Error(response.error));
    else request.resolve(response.result);
});
function timeoutFor(method: Method): number {
    if (method === Methods.Capture) return CaptureTimeoutMilliseconds;
    if (method === Methods.CheckUpdates) return CheckUpdatesTimeoutMilliseconds;
    if (method === Methods.DownloadUpdate) return DownloadTimeoutMilliseconds;
    return RequestTimeoutMilliseconds;
}
export const bus = {
    call<T = unknown>(method: Method, payload: unknown = {}): Promise<T> {
        if (!webview) return Promise.reject(new Error(t("Gestures.Web.HostUnavailable", "Open this page in MyGestures.")));
        const id = RequestIdPrefix + ++sequence;
        return new Promise<T>((resolve, reject) => {
            const timer = setTimeout(() => {
                pending.delete(id);
                reject(new Error(t("Gestures.Web.Timeout", "The operation timed out. Try again.")));
            }, timeoutFor(method));
            pending.set(id, { resolve: (value) => resolve(value as T), reject, timer });
            webview.postMessage({ id, method, payload });
        });
    },
    on(eventName: string, listener: (payload: { percent?: number }) => void): () => void {
        const listeners = eventListeners.get(eventName) ?? new Set<(payload: { percent?: number }) => void>();
        listeners.add(listener);
        eventListeners.set(eventName, listeners);
        return () => listeners.delete(listener);
    },
};
