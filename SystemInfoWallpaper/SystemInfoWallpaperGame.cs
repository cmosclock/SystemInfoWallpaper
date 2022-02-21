using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Vanara.PInvoke;
using ButtonState = Microsoft.Xna.Framework.Input.ButtonState;
using Color = Microsoft.Xna.Framework.Color;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace SystemInfoWallpaper
{
    public class SystemInfoWallpaperGame : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private Texture2D? _texture;
        NotifyIcon _notifyIcon = new ();
        private SpriteFont _font;
        private PerformanceCounter _statsCpuPercentCounter;
        private PerformanceCounter _statsRamFreeCounter;
        private string _statsCpuPercent;
        private string _statsRamPercent;
        private ulong _statsTotalRamKb;
        private int _stripThickeness = 10;
        private int _mouseX;
        private int _mouseY;
        private Timer _statsTimer;

        public static User32.EnumWindowsProc EnumWindowsProcPtr { get; set; }

        public SystemInfoWallpaperGame()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            Window.IsBorderless = true;
            var content = new EmbeddedResourceContentManager(Services);
            content.RootDirectory = "Content";
            Content = content;
            // anti aliasing
            _graphics.PreferMultiSampling = true;

            // fps
            IsFixedTimeStep = false;
            InactiveSleepTime = TimeSpan.FromSeconds(0.015);
            TargetElapsedTime = TimeSpan.FromSeconds(1d / 60);
            
            // get hwnd to draw on
            const uint WM_SPAWN_WORKER = 0x052C; // undocumented message
            var progman_hwnd = User32.FindWindow("ProgMan", null);
            // Credits for logic for grabbing hwnd:
            // darclander - https://stackoverflow.com/questions/1683791/drawing-on-the-desktop-background-as-wallpaper-replacement-windows-c
            // https://github.com/Foohy/Wallpainter
            HWND workerw_hwnd = default;
            bool EnumWindowsProc(HWND hwnd, IntPtr lParam)
            {
                var p = User32.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (p != HWND.NULL)
                {
                    workerw_hwnd = User32.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                    return false;
                }
                return true;
            }
            EnumWindowsProcPtr = EnumWindowsProc;
            var discard = IntPtr.Zero;
            User32.SendMessageTimeout(progman_hwnd, WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero, User32.SMTO.SMTO_NORMAL, 1000, ref discard);
            User32.EnumWindows(EnumWindowsProcPtr, IntPtr.Zero);
            User32.ShowWindowAsync(workerw_hwnd, 0);

            var mainGameForm = (Form)Control.FromHandle(Window.Handle);
            mainGameForm.FormBorderStyle = FormBorderStyle.None;
            mainGameForm.TopMost = true;
            mainGameForm.WindowState = FormWindowState.Maximized;
            mainGameForm.ShowInTaskbar = false;
            MakeFullScreenOverlay(Window.Handle);
            // attach to wallpaper
            User32.SetParent(Window.Handle, progman_hwnd);
            _graphics.HardwareModeSwitch = false;
            _graphics.IsFullScreen = true;
            _graphics.PreferredBackBufferWidth = Screen.PrimaryScreen.Bounds.Width;
            _graphics.PreferredBackBufferHeight = Screen.PrimaryScreen.Bounds.Height;
            _graphics.ApplyChanges();
            mainGameForm.Hide();
        }

        public void MakeFullScreenOverlay(IntPtr hWnd, bool clickable = false)
        {
            var flag = 0
                       | User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE)
                       // hide in alt tab
                       | (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW 
                       | 0;
            if (!clickable)
            {
                // make entire window click through
                flag |= (int)User32.WindowStylesEx.WS_EX_TRANSPARENT | (int)User32.WindowStylesEx.WS_EX_LAYERED;
            }
            
            User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE, flag);
            User32.SetWindowPos(hWnd, HWND.HWND_TOPMOST, 0, 0, 0, 0, 0
                                                                     | User32.SetWindowPosFlags.SWP_NOSIZE
                                                                     | User32.SetWindowPosFlags.SWP_NOMOVE
                                                                     | 0);
            DwmApi.MARGINS margins = new DwmApi.MARGINS(-1);
            DwmApi.DwmExtendFrameIntoClientArea(Window.Handle, margins);
        }

        protected override void Initialize()
        {
            _notifyIcon.Icon = SystemIcons.Question;
            _notifyIcon.Text = nameof(SystemInfoWallpaper);
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(5000, $"{nameof(SystemInfoWallpaper)}", $"{nameof(SystemInfoWallpaper)} is now running",  ToolTipIcon.Info);
            _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
            var quitBtn = new ToolStripMenuItem("Exit");
            _notifyIcon.ContextMenuStrip.Items.Add(quitBtn);
            quitBtn.Click += (sender, args) =>
            {
                _notifyIcon.Visible = false;
                Exit();
            };

            _statsCpuPercentCounter = new PerformanceCounter("Processor Information", "% Processor Time", "_Total");
            _statsRamFreeCounter = new PerformanceCounter("Memory", "Available MBytes");
            Kernel32.MEMORYSTATUSEX lpBuffer = Kernel32.MEMORYSTATUSEX.Default;
            Kernel32.GlobalMemoryStatusEx(ref lpBuffer);
            _statsTotalRamKb = lpBuffer.ullTotalPhys;

            _statsTimer = new Timer();
            _statsTimer.Interval = 500;
            _statsTimer.Tick += (sender, args) =>
            {
                _statsCpuPercent = $"{_statsCpuPercentCounter.NextValue():0.00}%";
                var totalRamGb = _statsTotalRamKb / 1024.0 / 1024;
                _statsRamPercent = $"{100.0 * (totalRamGb - _statsRamFreeCounter.NextValue()) / totalRamGb:0.00}%";
            };
            _statsTimer.Start();
            
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _font = Content.Load<SpriteFont>("bin/Windows/Content/Folks-Bold");
            _texture = new Texture2D(_spriteBatch.GraphicsDevice, 1, 1, false, SurfaceFormat.Color);
            _texture.SetData(new []
            {
                Color.White
            });
        }

        protected override void Update(GameTime gameTime)
        {
            var mouse = Mouse.GetState();
            _mouseX = mouse.X;
            _mouseY = mouse.Y;

            base.Update(gameTime);
        }
        
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Transparent);

            
            _spriteBatch.Begin();
            _spriteBatch.Draw(_texture, new Rectangle(0, _mouseY - _stripThickeness / 2, Window.ClientBounds.Width, _stripThickeness), Color.White);
            _spriteBatch.Draw(_texture, new Rectangle(_mouseX - _stripThickeness / 2, 0, _stripThickeness, Window.ClientBounds.Height), Color.White);
            var yOffset = 0;
            var fontColor = Color.White;
            _spriteBatch.DrawString(_font, $"mouse x - {_mouseX}", new Vector2(_mouseX + _stripThickeness + 10, _mouseY + _stripThickeness + yOffset), fontColor, 0, new Vector2(0, 0), 1.0f, SpriteEffects.None, 1);
            yOffset += 40;
            _spriteBatch.DrawString(_font, $"mouse y - {_mouseY}", new Vector2(_mouseX + _stripThickeness + 10, _mouseY + _stripThickeness + yOffset), fontColor, 0, new Vector2(0, 0), 1.0f, SpriteEffects.None, 1);
            yOffset += 40;
            _spriteBatch.DrawString(_font, $"fps - {1 / gameTime.ElapsedGameTime.TotalSeconds:0.00}", new Vector2(_mouseX + _stripThickeness + 10, _mouseY + _stripThickeness + yOffset), fontColor, 0, new Vector2(0, 0), 1.0f, SpriteEffects.None, 1);
            yOffset += 40;
            _spriteBatch.DrawString(_font, $"cpu - {_statsCpuPercent}", new Vector2(_mouseX + _stripThickeness + 10, _mouseY + _stripThickeness + yOffset), fontColor, 0, new Vector2(0, 0), 1.0f, SpriteEffects.None, 1);
            yOffset += 40;
            _spriteBatch.DrawString(_font, $"ram - {_statsRamPercent}", new Vector2(_mouseX + _stripThickeness + 10, _mouseY + _stripThickeness + yOffset), fontColor, 0, new Vector2(0, 0), 1.0f, SpriteEffects.None, 1);
            _spriteBatch.End();

            base.Draw(gameTime);
        }
    }
}
