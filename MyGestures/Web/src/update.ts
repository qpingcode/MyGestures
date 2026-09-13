import { onMounted, onBeforeUnmount, reactive } from "vue";
import { bus, HostEvents, Methods } from "./bus";

export const UpdateStatus = {
    Idle: "idle",
    Checking: "checking",
    NoUpdate: "noUpdate",
    UpdateAvailable: "updateAvailable",
    Downloading: "downloading",
    NotInstalled: "notInstalled",
    Busy: "busy",
    Error: "error",
} as const;

export type UpdateInfo = { currentVersion: string; installed: boolean; releasesUrl: string };
export type UpdateCheckResult = { status: string; currentVersion: string; version?: string | null; installed: boolean };

export const update = reactive({
    currentVersion: "",
    installed: false,
    status: UpdateStatus.Idle as string,
    availableVersion: "",
    progress: 0,
    error: "",
});

export async function loadUpdateInfo(): Promise<void> {
    const info = await bus.call<UpdateInfo>(Methods.UpdateInfo);
    update.currentVersion = info.currentVersion;
    update.installed = info.installed;
}

export async function checkForUpdates(): Promise<void> {
    if (update.status === UpdateStatus.Checking || update.status === UpdateStatus.Downloading) return;
    update.status = UpdateStatus.Checking;
    update.error = "";
    update.progress = 0;
    try {
        const result = await bus.call<UpdateCheckResult>(Methods.CheckUpdates);
        update.currentVersion = result.currentVersion;
        update.installed = result.installed;
        update.availableVersion = result.version ?? "";
        update.status = result.status;
    } catch (error) {
        update.status = UpdateStatus.Error;
        update.error = String(error instanceof Error ? error.message : error);
    }
}

export async function downloadUpdate(): Promise<void> {
    if (update.status === UpdateStatus.Downloading) return;
    if (!update.installed) {
        await bus.call(Methods.OpenReleases);
        return;
    }
    update.status = UpdateStatus.Downloading;
    update.progress = 0;
    update.error = "";
    try {
        await bus.call(Methods.DownloadUpdate);
    } catch (error) {
        update.status = UpdateStatus.Error;
        update.error = String(error instanceof Error ? error.message : error);
    }
}

export async function openReleases(): Promise<void> {
    await bus.call(Methods.OpenReleases);
}

export function useUpdateEvents(showUpdatePanel: () => void): void {
    let stopProgress: (() => void) | undefined;
    let stopCheck: (() => void) | undefined;
    onMounted(async () => {
        stopProgress = bus.on(HostEvents.UpdateProgress, (payload) => {
            update.progress = payload.percent ?? 0;
            update.status = UpdateStatus.Downloading;
        });
        stopCheck = bus.on(HostEvents.CheckUpdates, () => {
            showUpdatePanel();
            void checkForUpdates();
        });
        try { await loadUpdateInfo(); }
        catch { /* The host is unavailable in a plain browser preview. */ }
    });
    onBeforeUnmount(() => {
        stopProgress?.();
        stopCheck?.();
    });
}
