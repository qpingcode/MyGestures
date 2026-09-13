export enum ActionType { HotKey = "hotkey", Mouse = "mouse" }
export enum Direction { Up = "Up", Down = "Down", Left = "Left", Right = "Right" }
export type GestureConfig = {
    id: string;
    directions: Direction[];
    actionName: string;
    actionType: ActionType;
    hotKey?: string | null;
    mouseButton?: string | null;
    processNames: string[];
    isEnabled: boolean;
};
export type GestureSettings = {
    enabled: boolean;
    autoStart: boolean;
    gameMode: boolean;
    skipGestureRecordHint: boolean;
    locale: string;
    theme: string;
    gestures: GestureConfig[];
};
export type TriggerCaptureResult = {
    directions: Direction[];
    processName?: string | null;
};
