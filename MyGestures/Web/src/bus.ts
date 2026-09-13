import { t } from "./i18n";

export const Methods = {
    Read: "getSettings", Save: "saveSettings", Suspend: "suspendGestures", Resume: "resumeGestures", Capture: "captureInputAction",
} as const;
type Method = typeof Methods[keyof typeof Methods];
type WebView = {
    postMessage(message: unknown): void;
    addEventListener(event: "message", listener: (event: MessageEvent) => void): void;
};
const webview = (window as Window & { chrome?: { webview?: WebView } }).chrome?.webview;
const pending = new Map<string, { resolve: (value: unknown) => void; reject: (error: Error) => void; timer: ReturnType<typeof setTimeout> }>();
const RequestTimeoutMilliseconds = 30_000;
const CaptureTimeoutMilliseconds = 24 * 60 * 60 * 1000;
const RequestIdPrefix = "settings-";
let sequence = 0;
webview?.addEventListener("message", (event) => {
    const response = event.data;
    const request = pending.get(response?.id);
    if (!request) return;
    clearTimeout(request.timer);
    pending.delete(response.id);
    if (response.error) request.reject(new Error(response.error));
    else request.resolve(response.result);
});
export const bus = {
    call<T = unknown>(method: Method, payload: unknown = {}): Promise<T> {
        if (!webview) return Promise.reject(new Error(t("Gestures.Web.HostUnavailable", "Open this page in MyGestures.")));
        const id = RequestIdPrefix + ++sequence;
        return new Promise<T>((resolve, reject) => {
            const timer = setTimeout(() => {
                pending.delete(id);
                reject(new Error(t("Gestures.Web.Timeout", "The operation timed out. Try again.")));
            }, method === Methods.Capture ? CaptureTimeoutMilliseconds : RequestTimeoutMilliseconds);
            pending.set(id, { resolve: (value) => resolve(value as T), reject, timer });
            webview.postMessage({ id, method, payload });
        });
    },
};
