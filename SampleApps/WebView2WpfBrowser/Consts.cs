namespace WebView2WpfBrowser;

public static class Consts
{
    /// <summary>
    /// Use icons for custom menu items.
    /// </summary>
    public const bool UseIcon = true;

    /// <summary>
    /// Load icons from assembly resources. If false, icons are loaded from file.
    /// </summary>
    public const bool UseIconFromResources = false;

    /// <summary>
    /// Pass a copy of the icon stream to the menu item. If false, the original stream is passed.
    /// </summary>
    public const bool CopyIcon = false;
}