import { reactive, watch } from "vue";
import { bus, Methods } from "./bus";
import { locale, setLocale } from "./i18n";
import { applyTheme } from "./theme";
import type { GestureConfig, GestureSettings } from "./types";

export const store = reactive({
    gestureConfigs: [] as GestureConfig[],
    enabled: false,
    autoStart: false,
    gameMode: true,
    skipGestureRecordHint: false,
    theme: "dark",
    searchQuery: "",
    loading: true,
    saving: false,
    capturing: false,
    dirty: false,
    error: "",
});
let revision = 0;
const SaveDebounceMilliseconds = 300;
const MaxSaveFailureRetries = 1;
let saveFailureRetries = 0;
let saveTimer: ReturnType<typeof setTimeout> | undefined;
function scheduleSave(): void {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => { void saveSettings(); }, SaveDebounceMilliseconds);
}
export function markGesturesDirty(): void { revision++; store.dirty = true; scheduleSave(); }
function adoptSavedGestures(saved: GestureConfig[]): void {
    const current = store.gestureConfigs;
    if (current.length !== saved.length) {
        store.gestureConfigs = saved;
        return;
    }
    for (let index = 0; index < saved.length; index += 1) {
        const local = current[index];
        const incoming = saved[index];
        if (local.id !== incoming.id) {
            store.gestureConfigs = saved;
            return;
        }
        Object.assign(local, incoming);
    }
}
function markIfReady(): void { if (!store.loading) markGesturesDirty(); }
export async function loadSettings(): Promise<void> {
    try {
        const settings = await bus.call<GestureSettings>(Methods.Read);
        store.gestureConfigs = settings.gestures;
        store.enabled = settings.enabled;
        store.autoStart = settings.autoStart;
        store.gameMode = settings.gameMode;
        store.skipGestureRecordHint = settings.skipGestureRecordHint;
        store.theme = settings.theme;
        applyTheme(settings.theme);
        await setLocale(settings.locale);
        store.dirty = false;
    } catch (error) { store.error = String(error instanceof Error ? error.message : error); }
    finally { store.loading = false; }
}
export async function saveSettings(): Promise<void> {
    if (store.saving || !store.dirty) return;
    store.saving = true;
    store.error = "";
    const savedRevision = revision;
    let succeeded = false;
    const payload = JSON.parse(JSON.stringify({
        gestures: store.gestureConfigs,
        enabled: store.enabled,
        autoStart: store.autoStart,
        gameMode: store.gameMode,
        skipGestureRecordHint: store.skipGestureRecordHint,
        locale: locale.value,
        theme: store.theme,
    }));
    try {
        const result = await bus.call<GestureSettings>(Methods.Save, payload);
        succeeded = true;
        saveFailureRetries = 0;
        if (revision === savedRevision) {
            adoptSavedGestures(result.gestures);
            store.dirty = false;
        }
    } catch (error) {
        store.error = String(error instanceof Error ? error.message : error);
        if (saveFailureRetries < MaxSaveFailureRetries) {
            saveFailureRetries++;
            scheduleSave();
        }
    }
    finally {
        store.saving = false;
        if (succeeded && store.dirty) scheduleSave();
    }
}
watch(() => store.enabled, markIfReady);
watch(() => store.autoStart, markIfReady);
watch(() => store.gameMode, markIfReady);
watch(() => store.skipGestureRecordHint, markIfReady);
watch(() => store.theme, (value) => { applyTheme(value); markIfReady(); });
