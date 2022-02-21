using System;

namespace SystemInfoWallpaper
{
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            using (var game = new SystemInfoWallpaperGame())
                game.Run();
        }
    }
}
