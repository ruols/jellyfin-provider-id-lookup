using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.ProviderIdLookup;

public sealed class Plugin : BasePlugin
{
    public Plugin()
    {
        // This Jellyfin build does not initialize BasePlugin attributes during loading.
        var assembly = typeof(Plugin).Assembly;
        SetAttributes(assembly.Location, Path.GetDirectoryName(assembly.Location)!, assembly.GetName().Version!);
    }

    public override string Name => "Provider ID Lookup";
    public override string Description => "Native Emby AnyProviderIdEquals compatibility for the pinned Jellyfin 12.2.0 testing build.";
    public override Guid Id => Guid.Parse("ead7d651-c6a7-4d6d-9fe1-d32b7cc152c6");
}
