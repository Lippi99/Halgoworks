namespace Halgoworks.Components.Lab;

public sealed record SegOption<T>(T Value, string Text, bool Disabled = false, string? Hint = null);
