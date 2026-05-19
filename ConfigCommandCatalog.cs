namespace MouseClicker;

internal static class ConfigCommandCatalog
{
    public static IReadOnlyList<ConfigCommandInfo> All { get; } =
    [
        new("MoveTo 1000x1111", "MoveTo", "Move cursor to screen coordinates (instant)."),
        new("HoverNudge", "HoverNudge", "Tiny mouse move for hover detection; use after MoveTo if needed."),
        new("LeftClick", "LeftClick", "Single left mouse button click."),
        new("LeftClick Repeat", "LeftClick Repeat", "Repeat left clicks until stopped (F6 / F12)."),
        new("LeftClick Repeat 0 10000", "LeftClick Repeat …", "Repeat left clicks: delay ms, then total duration ms."),
        new("RightClick", "RightClick", "Single right mouse button click."),
        new("RightClick Repeat", "RightClick Repeat", "Repeat right clicks until stopped (F6 / F12)."),
        new("RightClick Repeat 0 10000", "RightClick Repeat …", "Repeat right clicks: delay ms, then total duration ms."),
        new("Sleep 1000", "Sleep", "Pause for the given milliseconds."),
        new("ScrollDown 500", "ScrollDown", "Scroll view down for about the given milliseconds."),
        new("ScrollUp 500", "ScrollUp", "Scroll view up for about the given milliseconds."),
    ];

    public static IEnumerable<ConfigCommandInfo> FilterForAutocomplete(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return All;
        }

        return All.Where(c =>
            c.InsertText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            c.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<ConfigCommandInfo> FilterForPalette(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return All;
        }

        return All.Where(c =>
            c.InsertText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            c.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            c.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class ConfigCommandInfo(string insertText, string label, string description)
{
    public string InsertText { get; } = insertText;
    public string Label { get; } = label;
    public string Description { get; } = description;

    public string ListDisplay => $"{Label} — {Description}";
}
