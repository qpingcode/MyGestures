import { bus, Methods } from "./bus";
import { store } from "./store";
import { ActionType } from "./types";
type InputActionValue = { kind: ActionType; hotKey?: string | null; mouseButton?: string | null };
export async function captureInputAction(options: { showKeyboard: boolean; showMouse: boolean; value: InputActionValue }): Promise<InputActionValue | null> {
    store.capturing = true;
    try { return await bus.call<InputActionValue | null>(Methods.Capture, options.value); }
    catch (error) { store.error = String(error instanceof Error ? error.message : error); return null; }
    finally { store.capturing = false; }
}
