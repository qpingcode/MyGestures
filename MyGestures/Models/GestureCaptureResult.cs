namespace MyGestures.Models;

public sealed class GestureCaptureResult
{
    public string[] Directions { get; init; } = [];
    public string? ProcessName { get; init; }
}
