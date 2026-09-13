using MyGestures.Localization;
using MyGestures.Models;
using MyGestures.Utils;

namespace MyGestures.Services;

public static class GestureDefaults
{
    public static List<GestureConfig> Create(LocalizationService localization)
    {
        return
        [
            new GestureConfig
            {
                Directions = [nameof(MoveDirection.Down), nameof(MoveDirection.Right)],
                ActionName = localization.GetCaption("Gestures.Default.CloseTab", "Close Tab"),
                ActionType = GestureConfig.HotKeyActionType,
                HotKey = "Control+W",
                ProcessNames = []
            },
            new GestureConfig
            {
                Directions = [nameof(MoveDirection.Down), nameof(MoveDirection.Right)],
                ActionName = localization.GetCaption("Gestures.Default.CloseTab", "Close Tab"),
                ActionType = GestureConfig.HotKeyActionType,
                HotKey = "Control+F4",
                ProcessNames = ["rider", "rider64", "devenv"]
            },
            new GestureConfig
            {
                Directions = [nameof(MoveDirection.Up), nameof(MoveDirection.Right)],
                ActionName = localization.GetCaption("Gestures.Default.CreateNew", "Create New"),
                ActionType = GestureConfig.HotKeyActionType,
                HotKey = "Control+N",
                ProcessNames = []
            },
            new GestureConfig
            {
                Directions = [nameof(MoveDirection.Up), nameof(MoveDirection.Right)],
                ActionName = localization.GetCaption("Gestures.Default.CreateNewTab", "Create New Tab"),
                ActionType = GestureConfig.HotKeyActionType,
                HotKey = "Control+T",
                ProcessNames = ["chrome", "firefox", "edge"]
            },
            new GestureConfig
            {
                Directions = [nameof(MoveDirection.Left)],
                ActionName = localization.GetCaption("Gestures.Default.Back", "Back"),
                ActionType = GestureConfig.MouseActionType,
                MouseButton = "XButton1",
                ProcessNames = []
            },
            new GestureConfig
            {
                Directions = [nameof(MoveDirection.Right)],
                ActionName = localization.GetCaption("Gestures.Default.Forward", "Forward"),
                ActionType = GestureConfig.MouseActionType,
                MouseButton = "XButton2",
                ProcessNames = []
            },
            new GestureConfig
            {
                Directions = [nameof(MoveDirection.Up), nameof(MoveDirection.Down)],
                ActionName = localization.GetCaption("Gestures.Default.RefreshPage", "Refresh Page"),
                ActionType = GestureConfig.HotKeyActionType,
                HotKey = "F5",
                ProcessNames = ["chrome", "firefox", "edge"]
            },
            new GestureConfig
            {
                Directions = [nameof(MoveDirection.Down), nameof(MoveDirection.Right), nameof(MoveDirection.Up), nameof(MoveDirection.Left)],
                ActionName = localization.GetCaption("Gestures.Default.CloseOtherTabs", "Close Other Tabs"),
                ActionType = GestureConfig.HotKeyActionType,
                HotKey = "Alt+Shift+O",
                ProcessNames = []
            },
            new GestureConfig
            {
                Directions = [nameof(MoveDirection.Left), nameof(MoveDirection.Right)],
                ActionName = localization.GetCaption("Gestures.Default.FullScreen", "Full Screen Switch"),
                ActionType = GestureConfig.HotKeyActionType,
                HotKey = "Control+Shift+F12",
                ProcessNames = ["rider", "rider64", "devenv"]
            },
            new GestureConfig
            {
                Directions = [nameof(MoveDirection.Right), nameof(MoveDirection.Left)],
                ActionName = localization.GetCaption("Gestures.Default.FullScreen", "Full Screen Switch"),
                ActionType = GestureConfig.HotKeyActionType,
                HotKey = "Control+Shift+F12",
                ProcessNames = ["rider", "rider64", "devenv"]
            },
        ];
    }
}