using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework.Content;

namespace SystemInfoWallpaper;

/// <summary>
/// Credit:
/// https://gamedev.stackexchange.com/questions/48619/resourcecontentmanager-with-folders
/// </summary>
class EmbeddedResourceContentManager : ContentManager
{
    public EmbeddedResourceContentManager(IServiceProvider serviceProvider)
        : base(serviceProvider) { }

    protected override Stream OpenStream(string assetName)
    {
        var path = Path.Combine(nameof(SystemInfoWallpaper), RootDirectory);
        path = Path.Combine(path, assetName);
        path = path.Replace('\\', '.');
        path = path.Replace('/', '.');
        assetName = path + ".xnb";
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(assetName);
        return stream;
    }
}