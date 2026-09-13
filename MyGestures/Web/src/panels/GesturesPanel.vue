<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref } from "vue";
import type { InputInst } from "naive-ui";
import HighlightText from "../components/HighlightText.vue";
import TableToolbar from "../components/TableToolbar.vue";
import { bus, Methods } from "../bus";
import { captureInputAction } from "../capture-input-action";
import { t } from "../i18n";
import { markGesturesDirty, store } from "../store";
import { ActionType, Direction, type GestureConfig, type TriggerCaptureResult } from "../types";

const DIRECTION_ARROWS: Record<Direction, string> = {
    Up: "↑",
    Down: "↓",
    Left: "←",
    Right: "→",
};

const GESTURE_VISIBLE_DIRS = 4;
const UndoDismissMilliseconds = 8000;
const NewGestureHighlightMilliseconds = 3000;
const ImeKeyCode = 229;

const hintVisible = ref(false);
const skipHintAgain = ref(false);
let hintResolve: ((confirmed: boolean) => void) | null = null;

const gestures = computed(() => store.gestureConfigs || []);
const tableQuery = ref("");
const gestureBody = ref<HTMLElement | null>(null);
const highlightedGestureId = ref<string | null>(null);
let highlightTimer: ReturnType<typeof setTimeout> | undefined;
const highlightQuery = computed(() => tableQuery.value.trim() || store.searchQuery);


const headers = computed(() => ({
    action: t("Plugin.Settings.Gestures.HeaderAction", "Action Name"),
    actionTip: t("Plugin.Settings.Gestures.HeaderActionTip", "The name of this gesture action."),
    gesture: t("Plugin.Settings.Gestures.HeaderGesture", "Trigger Gesture"),
    gestureTip: t("Plugin.Settings.Gestures.HeaderGestureTip", "Click to record. Settings hide so you can click the target app, then hold the right mouse button and draw."),
    process: t("Plugin.Settings.Gestures.HeaderProcess", "Target Process"),
    processTip: t("Plugin.Settings.Gestures.HeaderProcessTip", "Only trigger this gesture in the specified processes. Recording a gesture fills this from the window you click. Leave empty to apply to all processes."),
    trigger: t("Plugin.Settings.Gestures.HeaderTrigger", "Action"),
    triggerTip: t("Plugin.Settings.Gestures.HeaderTriggerTip", "The action to run. Click to choose a keyboard shortcut or mouse button."),
    enabled: t("Plugin.Settings.Gestures.HeaderEnabled", "Enabled"),
    enabledTip: t("Plugin.Settings.Gestures.HeaderEnabledTip", "Enable or disable this gesture."),
}));

function directionsToArrows(dirs: Direction[]): string {
    return dirs.map((dir) => DIRECTION_ARROWS[dir] || dir).join(" ");
}

function formatGestureDisplay(dirs: Direction[]): { visible: string; full: string; truncated: boolean } {
    const full = directionsToArrows(dirs);
    if (dirs.length <= GESTURE_VISIBLE_DIRS) {
        return { visible: full, full, truncated: false };
    }
    return {
        visible: directionsToArrows(dirs.slice(0, GESTURE_VISIBLE_DIRS)) + " …",
        full,
        truncated: true,
    };
}

function formatMouseButtonShort(mouseButton: string): string {
    if (mouseButton === "XButton2") return t("Plugin.Settings.Gestures.MouseForwardShort", "Forward");
    if (mouseButton === "XButton1") return t("Plugin.Settings.Gestures.MouseBackShort", "Back");
    if (mouseButton === "Left") return t("Plugin.Settings.Gestures.MouseLeftShort", "Left");
    if (mouseButton === "Right") return t("Plugin.Settings.Gestures.MouseRightShort", "Right");
    if (mouseButton === "Middle") return t("Plugin.Settings.Gestures.MouseMiddleShort", "Middle");
    return mouseButton;
}

function formatActionDisplay(gesture: GestureConfig): { text: string; empty: boolean; title: string } {
    if (gesture.actionType === ActionType.Mouse) {
        const mouseLabel = gesture.mouseButton ? formatMouseButtonShort(gesture.mouseButton) : null;
        if (mouseLabel && gesture.mouseButton) {
            return { text: mouseLabel, empty: false, title: mouseLabel };
        }
    } else if (gesture.hotKey) {
        return { text: gesture.hotKey, empty: false, title: gesture.hotKey };
    }
    const none = t("Plugin.Settings.Gestures.NoAction", "Not set");
    return { text: none, empty: true, title: t("Plugin.Settings.Gestures.ClickToSetAction", "Click to set action") };
}

function gesturesConflict(a: GestureConfig, b: GestureConfig): boolean {
    if (!a.isEnabled || !b.isEnabled) return false;
    if (a.directions.length === 0 || a.directions.length !== b.directions.length) return false;
    if (a.directions.some((dir, index) => dir !== b.directions[index])) return false;
    const aAny = a.processNames.length === 0;
    const bAny = b.processNames.length === 0;
    if (aAny && bAny) return true;
    if (aAny || bAny) return false;
    return a.processNames.some((name) => b.processNames.includes(name));
}

function conflictMap(): Map<string, string> {
    const map = new Map<string, string>();
    const configs = store.gestureConfigs || [];
    for (let i = 0; i < configs.length; i += 1) {
        for (let j = i + 1; j < configs.length; j += 1) {
            const a = configs[i];
            const b = configs[j];
            if (!gesturesConflict(a, b)) continue;
            const aAny = a.processNames.length === 0;
            const bAny = b.processNames.length === 0;
            const aWins = !aAny && bAny;
            appendConflict(map, a, b, aWins);
            appendConflict(map, b, a, !aWins && !(aAny && !bAny));
        }
    }
    return map;
}

function appendConflict(map: Map<string, string>, self: GestureConfig, other: GestureConfig, selfWins: boolean): void {
    const otherName = other.actionName || t("Plugin.Settings.Gestures.Unnamed", "Unnamed");
    const msg = selfWins
        ? t("Plugin.Settings.Gestures.ConflictWins", "Conflicts with \"{{name}}\". This one takes priority.", { name: otherName })
        : t("Plugin.Settings.Gestures.ConflictLose", "Conflicts with \"{{name}}\", which will take priority.", { name: otherName });
    const existing = map.get(self.id);
    map.set(self.id, existing ? existing + "\n" + msg : msg);
}

const conflicts = computed(() => conflictMap());

const filteredGestures = computed(() => {
    const query = tableQuery.value.trim().toLowerCase();
    if (!query) return gestures.value;
    return gestures.value.filter((gesture) => {
        const action = formatActionDisplay(gesture).text;
        const gestureText = formatGestureDisplay(gesture.directions).full;
        const haystack = [
            gesture.actionName,
            action,
            gestureText,
            (gesture.processNames || []).join(" "),
            gesture.hotKey || "",
            gesture.mouseButton || "",
        ].join(" ").toLowerCase();
        return haystack.includes(query);
    });
});

type EditableField = "name" | "process";

const editing = ref<{ id: string; field: EditableField } | null>(null);
const editInputRef = ref<InputInst[]>([]);
const editComposing = ref(false);

function processText(gesture: GestureConfig): string {
    return (gesture.processNames || []).join(", ");
}

function isEditing(gesture: GestureConfig, field: EditableField): boolean {
    return editing.value?.id === gesture.id && editing.value.field === field;
}

async function startEdit(gesture: GestureConfig, field: EditableField): Promise<void> {
    editing.value = { id: gesture.id, field };
    await nextTick();
    editInputRef.value[0]?.focus();
}

function stopEdit(): void {
    editing.value = null;
    editComposing.value = false;
}

function isEditInputFocused(): boolean {
    const root = editInputRef.value[0]?.wrapperElRef;
    const active = document.activeElement;
    return !!(root && active && root.contains(active));
}

function onEditBlur(): void {
    const sessionId = editing.value?.id;
    const sessionField = editing.value?.field;
    requestAnimationFrame(() => {
        if (!editing.value || editComposing.value) return;
        if (editing.value.id !== sessionId || editing.value.field !== sessionField) return;
        if (!document.hasFocus() || isEditInputFocused()) return;
        stopEdit();
    });
}

function onEditKeydown(event: KeyboardEvent): void {
    editComposing.value = event.isComposing || event.keyCode === ImeKeyCode;
}

function onEditConfirm(event: KeyboardEvent): void {
    onEditKeydown(event);
    if (editComposing.value) return;
    stopEdit();
}

function markDirty(): void {
    markGesturesDirty();
}

async function addGesture(): Promise<void> {
    if (!store.gestureConfigs) store.gestureConfigs = [];
    const created: GestureConfig = {
        id: crypto.randomUUID(),
        directions: [],
        actionName: "",
        actionType: ActionType.HotKey,
        hotKey: null,
        mouseButton: null,
        processNames: [],
        isEnabled: true,
    };
    store.gestureConfigs.push(created);
    tableQuery.value = "";
    clearTimeout(highlightTimer);
    highlightedGestureId.value = created.id;
    highlightTimer = setTimeout(() => { highlightedGestureId.value = null; }, NewGestureHighlightMilliseconds);
    markDirty();
    await startEdit(created, "name");
    if (highlightedGestureId.value !== created.id) return;
    const body = gestureBody.value;
    if (body) body.scrollTop = body.scrollHeight;
}

type DeletedGesture = { gesture: GestureConfig; index: number };
const deleted = ref<DeletedGesture | null>(null);
let undoTimer: ReturnType<typeof setTimeout> | undefined;
const undoMessage = computed(() => {
    if (!deleted.value) return "";
    const name = deleted.value.gesture.actionName.trim();
    return name
        ? t("Plugin.Settings.Gestures.DeletedNamed", "Deleted \"{{name}}\".", { name })
        : t("Plugin.Settings.Gestures.Deleted", "Gesture deleted.");
});

function clearUndo(): void {
    clearTimeout(undoTimer);
    deleted.value = null;
}

function offerUndo(gesture: GestureConfig, index: number): void {
    clearTimeout(undoTimer);
    deleted.value = { gesture: JSON.parse(JSON.stringify(gesture)) as GestureConfig, index };
    undoTimer = setTimeout(() => { deleted.value = null; }, UndoDismissMilliseconds);
}

function undoDelete(): void {
    if (!deleted.value) return;
    if (!store.gestureConfigs) store.gestureConfigs = [];
    const insertAt = Math.min(Math.max(deleted.value.index, 0), store.gestureConfigs.length);
    store.gestureConfigs.splice(insertAt, 0, deleted.value.gesture);
    clearUndo();
    markDirty();
}

function removeGesture(gesture: GestureConfig): void {
    if (editing.value?.id === gesture.id) {
        stopEdit();
    }
    if (!store.gestureConfigs) return;
    const index = store.gestureConfigs.indexOf(gesture);
    if (index < 0) return;
    store.gestureConfigs.splice(index, 1);
    offerUndo(gesture, index);
    markDirty();
}

onBeforeUnmount(() => {
    clearUndo();
    clearTimeout(highlightTimer);
});

function onProcessChange(gesture: GestureConfig, value: string): void {
    gesture.processNames = value.split(",").map((item) => item.trim().toLowerCase()).filter(Boolean);
    markDirty();
}

async function setAction(gesture: GestureConfig): Promise<void> {
    const result = await captureInputAction({
        showKeyboard: true,
        showMouse: true,
        value: {
            kind: gesture.actionType === ActionType.Mouse ? ActionType.Mouse : ActionType.HotKey,
            hotKey: gesture.hotKey ?? null,
            mouseButton: gesture.mouseButton ?? null,
        },
    });
    if (!result) return;
    gesture.actionType = result.kind;
    gesture.hotKey = result.kind === ActionType.HotKey ? (result.hotKey ?? null) : null;
    gesture.mouseButton = result.kind === ActionType.Mouse ? (result.mouseButton ?? null) : null;
    markDirty();
}

function askRecordHint(): Promise<boolean> {
    if (store.skipGestureRecordHint) return Promise.resolve(true);
    skipHintAgain.value = false;
    hintVisible.value = true;
    return new Promise((resolve) => { hintResolve = resolve; });
}

function finishHint(confirmed: boolean): void {
    hintVisible.value = false;
    hintResolve?.(confirmed);
    hintResolve = null;
}

function confirmHint(): void {
    if (skipHintAgain.value) store.skipGestureRecordHint = true;
    finishHint(true);
}

function cancelHint(): void {
    finishHint(false);
}

async function startRecording(gesture: GestureConfig): Promise<void> {
    if (store.capturing) return;
    if (!(await askRecordHint())) return;
    store.capturing = true;
    try {
        const result = await bus.call<TriggerCaptureResult | null>(Methods.RecordTrigger);
        if (!result || result.directions.length === 0) return;
        gesture.directions = [...result.directions];
        const processName = (result.processName || "").trim().toLowerCase();
        gesture.processNames = processName ? [processName] : [];
        markDirty();
    } catch (error) {
        store.error = String(error instanceof Error ? error.message : error);
    } finally {
        store.capturing = false;
    }
}
</script>

<template>
    <div class="gestures-settings">
        <TableToolbar
            v-model="tableQuery"
            :placeholder="t('Plugin.Settings.Table.Search', 'Search')"
        >
            <n-button size="small" secondary @click="addGesture">
                <template #icon>
                    <i class="mdi mdi-plus"></i>
                </template>
                {{ t("Plugin.Settings.Table.Add", "Add") }}
            </n-button>
        </TableToolbar>
        <div v-if="gestures.length === 0" class="empty">
            {{ t("Plugin.Settings.Gestures.Empty", "No gestures configured") }}
        </div>
        <div v-else class="gesture-panel">
            <div class="gesture-header">
                <div class="col-name" :title="headers.actionTip">{{ headers.action }}</div>
                <div class="col-gesture" :title="headers.gestureTip">{{ headers.gesture }}</div>
                <div class="col-process" :title="headers.processTip">{{ headers.process }}</div>
                <div class="col-trigger" :title="headers.triggerTip">{{ headers.trigger }}</div>
                <div class="col-enabled" :title="headers.enabledTip">{{ headers.enabled }}</div>
                <div class="col-actions"></div>
            </div>
            <div ref="gestureBody" class="gesture-body">
                <div
                    v-for="(gesture, index) in filteredGestures"
                    :key="gesture.id || index"
                    class="gesture-row"
                    :class="{ 'new-gesture': highlightedGestureId === gesture.id }"
                    :data-gesture-id="gesture.id"
                    :style="{ '--new-gesture-highlight-duration': `${NewGestureHighlightMilliseconds}ms` }"
                >
                    <div class="col-name">
                        <i
                            v-if="conflicts.get(gesture.id)"
                            class="mdi mdi-alert conflict-icon"
                            :title="conflicts.get(gesture.id)"
                        ></i>
                        <n-input
                            v-if="isEditing(gesture, 'name')"
                            ref="editInputRef"
                            :value="gesture.actionName"
                            :placeholder="t('Plugin.Settings.Gestures.NamePlaceholder', 'e.g. Close Tab')"
                            size="small"
                            @update:value="
                                gesture.actionName = String($event || '');
                                markDirty();
                            "
                            @compositionstart="editComposing = true"
                            @compositionend="editComposing = false"
                            @blur="onEditBlur"
                            @keydown="onEditKeydown"
                            @keydown.enter.prevent="onEditConfirm"
                            @keydown.esc.prevent="onEditConfirm"
                        />
                        <button
                            v-else
                            type="button"
                            class="flat-display"
                            :class="{ empty: !gesture.actionName }"
                            :title="gesture.actionName || t('Plugin.Settings.Gestures.NamePlaceholder', 'e.g. Close Tab')"
                            @click="startEdit(gesture, 'name')"
                        >
                            <HighlightText
                                v-if="gesture.actionName"
                                :text="gesture.actionName"
                                :query="highlightQuery"
                            />
                            <span v-else>{{ t("Plugin.Settings.Gestures.NamePlaceholder", "e.g. Close Tab") }}</span>
                        </button>
                    </div>
                    <div class="col-gesture">
                        <button
                            type="button"
                            class="flat-display"
                            :class="{ empty: gesture.directions.length === 0 }"
                            :title="
                                gesture.directions.length === 0
                                    ? t('Plugin.Settings.Gestures.ClickToRecord', 'Click to record in the target app')
                                    : formatGestureDisplay(gesture.directions).full
                            "
                            @click="startRecording(gesture)"
                        >
                            <span v-if="gesture.directions.length === 0">
                                {{ t("Plugin.Settings.Gestures.NoGesture", "Not set") }}
                            </span>
                            <HighlightText
                                v-else
                                :text="formatGestureDisplay(gesture.directions).visible"
                                :query="highlightQuery"
                            />
                        </button>
                    </div>
                    <div class="col-process">
                        <n-input
                            v-if="isEditing(gesture, 'process')"
                            ref="editInputRef"
                            :value="processText(gesture)"
                            :placeholder="t('Plugin.Settings.Gestures.ProcessPlaceholder', 'Any')"
                            :title="t('Plugin.Settings.Gestures.ProcessHint', 'Comma-separated process names')"
                            size="small"
                            @update:value="onProcessChange(gesture, String($event || ''))"
                            @compositionstart="editComposing = true"
                            @compositionend="editComposing = false"
                            @blur="onEditBlur"
                            @keydown="onEditKeydown"
                            @keydown.enter.prevent="onEditConfirm"
                            @keydown.esc.prevent="onEditConfirm"
                        />
                        <button
                            v-else
                            type="button"
                            class="flat-display"
                            :class="{ empty: !processText(gesture) }"
                            :title="processText(gesture) || t('Plugin.Settings.Gestures.ProcessHint', 'Comma-separated process names')"
                            @click="startEdit(gesture, 'process')"
                        >
                            <HighlightText
                                v-if="processText(gesture)"
                                :text="processText(gesture)"
                                :query="highlightQuery"
                            />
                            <span v-else>{{ t("Plugin.Settings.Gestures.ProcessPlaceholder", "Any") }}</span>
                        </button>
                    </div>
                    <div class="col-trigger">
                        <button
                            type="button"
                            class="flat-display"
                            :class="{ empty: formatActionDisplay(gesture).empty }"
                            :title="formatActionDisplay(gesture).title"
                            @click="setAction(gesture)"
                        >
                            {{ formatActionDisplay(gesture).text }}
                        </button>
                    </div>
                    <div class="col-enabled">
                        <n-checkbox
                            :aria-label="headers.enabled"
                            :checked="gesture.isEnabled"
                            @update:checked="
                                gesture.isEnabled = !!$event;
                                markDirty();
                            "
                        />
                    </div>
                    <div class="col-actions">
                        <button
                            type="button"
                            class="icon-delete-btn"
                            :title="t('Plugin.Settings.Gestures.Delete', 'Delete')"
                            @click="removeGesture(gesture)"
                        >
                            <i class="mdi mdi-trash-can-outline delete-icon"></i>
                        </button>
                    </div>
                </div>
            </div>
        </div>
        <div
            v-if="hintVisible"
            class="record-hint-overlay"
            role="dialog"
            aria-modal="true"
            aria-labelledby="record-hint-title"
            tabindex="0"
            @keydown.esc.prevent="cancelHint"
        >
            <div class="record-hint-card">
                <h2 id="record-hint-title">{{ t("Plugin.Settings.Gestures.RecordHintTitle", "Record a gesture") }}</h2>
                <p>{{ t("Plugin.Settings.Gestures.RecordHintIntro", "Settings will hide so you can switch to the app you want.") }}</p>
                <ol>
                    <li>{{ t("Plugin.Settings.Gestures.RecordHintStepClick", "Click the application window this gesture should apply to.") }}</li>
                    <li>{{ t("Plugin.Settings.Gestures.RecordHintStepDraw", "Hold the right mouse button and draw the gesture.") }}</li>
                    <li>{{ t("Plugin.Settings.Gestures.RecordHintStepRelease", "Release the right mouse button to return here. The gesture and target process will be filled in.") }}</li>
                </ol>
                <n-checkbox v-model:checked="skipHintAgain">
                    {{ t("Plugin.Settings.Gestures.RecordHintSkip", "Don't show this again") }}
                </n-checkbox>
                <div class="record-hint-actions">
                    <n-button @click="cancelHint">{{ t("Plugin.Settings.Gestures.RecordHintCancel", "Cancel") }}</n-button>
                    <n-button type="primary" @click="confirmHint">{{ t("Plugin.Settings.Gestures.RecordHintContinue", "Continue") }}</n-button>
                </div>
            </div>
        </div>
        <div
            v-if="deleted"
            class="undo-bar"
            role="status"
            aria-live="polite"
        >
            <span class="undo-message">{{ undoMessage }}</span>
            <n-button size="small" type="primary" tertiary @click="undoDelete">
                {{ t("Plugin.Settings.Gestures.Undo", "Undo") }}
            </n-button>
            <button
                type="button"
                class="undo-dismiss"
                :title="t('Plugin.Settings.Gestures.UndoDismiss', 'Dismiss')"
                :aria-label="t('Plugin.Settings.Gestures.UndoDismiss', 'Dismiss')"
                @click="clearUndo"
            >
                <i class="mdi mdi-close"></i>
            </button>
        </div>
    </div>
</template>

<style scoped>
.gestures-settings {
    display: flex;
    flex-direction: column;
    flex: 1;
    min-height: 0;
}

.gestures-settings > :deep(.table-toolbar) {
    flex-shrink: 0;
}

.gesture-panel {
    display: flex;
    flex-direction: column;
    flex: 1;
    min-height: 0;
}

.gesture-body {
    position: relative;
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    scrollbar-gutter: stable;
}

.empty {
    padding: 24px 0;
    text-align: center;
    opacity: 0.6;
}

.gesture-header,
.gesture-row {
    display: flex;
    align-items: center;
    gap: 6px;
}

.gesture-row.new-gesture {
    border-radius: 8px;
    animation: new-gesture-highlight var(--new-gesture-highlight-duration) ease-out both;
}

@keyframes new-gesture-highlight {
    0%, 50% { background-color: rgba(59, 130, 246, 0.22); box-shadow: inset 3px 0 #3b82f6; }
    100% { background-color: transparent; box-shadow: inset 3px 0 transparent; }
}

@media (prefers-reduced-motion: reduce) {
    .gesture-row.new-gesture {
        animation: none;
        background-color: rgba(59, 130, 246, 0.22);
        box-shadow: inset 3px 0 #3b82f6;
    }
}

.gesture-header {
    flex-shrink: 0;
    overflow-y: auto;
    scrollbar-gutter: stable;
    padding: 8px 0 10px;
    border-bottom: 1px solid var(--mt-border, #404040);
    font-size: var(--mt-font-size-small, 12px);
    font-weight: 600;
    color: var(--mt-text-tertiary, #aaaaaa);
}

.gesture-row {
    padding: 10px 0;
    border-bottom: 1px solid var(--mt-border, #404040);
}

.col-name {
    width: 140px;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    gap: 4px;
    min-width: 0;
}

.col-name > .flat-display,
.col-name :deep(.n-input),
.col-process > .flat-display,
.col-process :deep(.n-input) {
    flex: 1 1 auto;
    width: 100%;
    min-width: 0;
}

.col-gesture {
    width: 110px;
    flex-shrink: 0;
}

.col-process {
    width: 140px;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    min-width: 0;
}

.col-trigger {
    width: 110px;
    flex-shrink: 0;
}

.col-enabled,
.col-actions {
    width: 48px;
    flex-shrink: 0;
    display: flex;
    justify-content: center;
}

.icon-delete-btn {
    width: 28px;
    height: 28px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: none;
    border-radius: 8px;
    background: transparent;
    color: var(--mt-text-tertiary, #aaaaaa);
    cursor: pointer;
    transition: background-color 140ms ease, color 140ms ease, transform 120ms ease;
}

.icon-delete-btn:hover {
    background: rgba(239, 68, 68, 0.14);
    color: #ef4444;
}

.icon-delete-btn:active {
    transform: scale(0.96);
}

.delete-icon {
    font-size: 16px;
    line-height: 1;
}

.flat-display {
    width: 100%;
    border: none;
    background: transparent;
    color: var(--mt-text, #fff);
    text-align: left;
    padding: 6px 8px;
    border-radius: 8px;
    cursor: pointer;
    font: inherit;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.flat-display:hover {
    background: var(--mt-surface-hover, #3a3a3a);
}

.flat-display.empty {
    font-style: italic;
    opacity: 0.6;
}

.record-hint-overlay {
    position: fixed;
    inset: 0;
    z-index: 300;
    background: rgba(0, 0, 0, 0.45);
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 24px;
}

.record-hint-card {
    width: min(520px, 100%);
    padding: 22px 24px 20px;
    border-radius: 14px;
    background: var(--mt-surface, #1d1d1d);
    border: 1px solid var(--mt-border, #2c2c2c);
    box-shadow: 0 16px 40px var(--mt-shadow, rgba(0, 0, 0, 0.28));
    color: var(--mt-text, #f3f1ec);
}

.record-hint-card h2 {
    margin: 0 0 10px;
    font-size: 18px;
    font-weight: 600;
}

.record-hint-card p,
.record-hint-card li {
    color: var(--mt-text-secondary, #b3aea4);
    line-height: 1.55;
    font-size: 13px;
}

.record-hint-card p {
    margin: 0 0 12px;
}

.record-hint-card ol {
    margin: 0 0 16px;
    padding-left: 20px;
}

.record-hint-card li + li {
    margin-top: 6px;
}

.record-hint-actions {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    margin-top: 18px;
}

.conflict-icon {
    color: #f44336;
}

.undo-bar {
    position: fixed;
    left: 50%;
    bottom: 24px;
    z-index: 280;
    transform: translateX(-50%);
    display: flex;
    align-items: center;
    gap: 12px;
    max-width: calc(100% - 32px);
    padding: 10px 12px 10px 16px;
    border-radius: 12px;
    background: var(--mt-surface, #1d1d1d);
    border: 1px solid var(--mt-border, #2c2c2c);
    box-shadow: 0 10px 28px var(--mt-shadow, rgba(0, 0, 0, 0.28));
    color: var(--mt-text, #f3f1ec);
}

.undo-message {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 13px;
}

.undo-dismiss {
    width: 28px;
    height: 28px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: none;
    border-radius: 8px;
    background: transparent;
    color: var(--mt-text-tertiary, #8a857c);
    cursor: pointer;
}

.undo-dismiss:hover {
    background: var(--mt-surface-hover, #2a2a2a);
    color: var(--mt-text, #f3f1ec);
}
</style>
