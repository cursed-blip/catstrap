using Bloxstrap.AppData;
using Bloxstrap.Integrations;

namespace Bloxstrap
{
    public class Watcher : IDisposable
    {
        private const int RejoinDelaySeconds = 5;

        private const int MaxRejoinAttempts = 3;

        private int _rejoinAttempts;

        private DateTime _lastDesktopReturn = DateTime.MinValue;

        private readonly InterProcessLock _lock = new("Watcher");

        private readonly WatcherData? _watcherData;
        
        private readonly NotifyIconWrapper? _notifyIcon;

        public readonly ActivityWatcher? ActivityWatcher;

        public readonly WindowManipulation? WindowManipulation;

        public readonly DiscordRichPresence? RichPresence;

        public Watcher()
        {
            const string LOG_IDENT = "Watcher";


            if (!_lock.IsAcquired)
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher instance already exists");
                return;
            }

            string? watcherDataArg = App.LaunchSettings.WatcherFlag.Data;

            if (String.IsNullOrEmpty(watcherDataArg))
            {
#if DEBUG
                string path = new RobloxPlayerData().ExecutablePath;
                if (!File.Exists(path))
                    throw new ApplicationException("Roblox player is not been installed");

                using var gameClientProcess = Process.Start(path);

                while (gameClientProcess.MainWindowHandle == IntPtr.Zero)
                    Thread.Sleep(100);

                _watcherData = new() { ProcessId = gameClientProcess.Id, Handle = gameClientProcess.MainWindowHandle.ToInt64() };
#else
                throw new Exception("Watcher data not specified");
#endif
            }
            else
            {
                _watcherData = JsonSerializer.Deserialize<WatcherData>(Encoding.UTF8.GetString(Convert.FromBase64String(watcherDataArg)));
            }

            if (_watcherData is null)
                throw new Exception("Watcher data is invalid");

            if (App.Settings.Prop.EnableWindowManipulation && _watcherData.Handle != 0)
                WindowManipulation = new(_watcherData.Handle, _watcherData.ProcessId);

            if (App.Settings.Prop.EnableActivityTracking)
            {
                ActivityWatcher = new(_watcherData.LogFile);

                if (App.Settings.Prop.UseDisableAppPatch)
                {
                    ActivityWatcher.OnAppClose += delegate
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Received desktop app exit, closing Roblox");
                        using var process = Process.GetProcessById(_watcherData.ProcessId);
                        process.CloseMainWindow();
                    };
                }

                if (App.Settings.Prop.UseDiscordRichPresence && !App.State.Prop.WatcherRunning)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Running rpc");
                    RichPresence = new(ActivityWatcher);
                }

                if (App.Settings.Prop.AutoRejoin)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Watching for disconnects to rejoin the last server");

                    ActivityWatcher.OnGameJoin += (_, _) => _rejoinAttempts = 0;
                    ActivityWatcher.OnGameLeave += (_, _) => TryRejoin();
                    ActivityWatcher.OnAppClose += (_, _) => _lastDesktopReturn = DateTime.UtcNow;
                }
            }

            _notifyIcon = new(this);
        }

        private bool ClientRunning()
        {
            if (_watcherData is null)
                return false;

            return Utilities.GetProcessesSafe().Any(x => x.Id == _watcherData.ProcessId);
        }

        private void TryRejoin()
        {
            const string LOG_IDENT = "Watcher::TryRejoin";

            if (_watcherData is null || ActivityWatcher is null)
                return;

            ActivityData? last = ActivityWatcher.History.FirstOrDefault();

            if (last is null || last.PlaceId <= 0 || String.IsNullOrEmpty(last.JobId))
                return;

            if (!ClientRunning())
            {
                App.Logger.WriteLine(LOG_IDENT, "The client is closed, so there is nothing to rejoin");
                return;
            }

            if (ActivityWatcher.InGame)
            {
                App.Logger.WriteLine(LOG_IDENT, "Already back in a game, so there is nothing to rejoin");
                return;
            }

            if (DateTime.UtcNow - _lastDesktopReturn < TimeSpan.FromSeconds(10))
            {
                App.Logger.WriteLine(LOG_IDENT, "That looked like a deliberate exit, so not rejoining");
                return;
            }

            if (_rejoinAttempts >= MaxRejoinAttempts)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Already tried rejoining {_rejoinAttempts} times, leaving it be");
                return;
            }

            _rejoinAttempts += 1;

            App.Logger.WriteLine(LOG_IDENT, $"Lost the connection to {last.PlaceId}, rejoining in {RejoinDelaySeconds} seconds (attempt {_rejoinAttempts})");

            _ = RejoinAsync(last);
        }

        private async Task RejoinAsync(ActivityData activity)
        {
            const string LOG_IDENT = "Watcher::RejoinAsync";

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(RejoinDelaySeconds));

                if (!ClientRunning() || ActivityWatcher is null || ActivityWatcher.InGame)
                    return;

                string path = new RobloxPlayerData().ExecutablePath;

                Process.Start(path, activity.GetInviteDeeplink(false, true));

                App.Logger.WriteLine(LOG_IDENT, $"Rejoined {activity.PlaceId}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not rejoin {activity.PlaceId}");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public void KillRobloxProcess() => CloseProcess(_watcherData!.ProcessId, true);

        public void CloseProcess(int pid, bool force = false)
        {
            const string LOG_IDENT = "Watcher::CloseProcess";

            try
            {
                using var process = Process.GetProcessById(pid);

                App.Logger.WriteLine(LOG_IDENT, $"Killing process '{process.ProcessName}' (pid={pid}, force={force})");

                if (process.HasExited)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"PID {pid} has already exited");
                    return;
                }

                if (force)
                    process.Kill();
                else
                    process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"PID {pid} could not be closed");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public async Task Run()
        {
            if (!_lock.IsAcquired || _watcherData is null)
                return;

            ActivityWatcher?.Start();
            WindowManipulation?.Start();

            while (ClientRunning())
                await Task.Delay(1000);

            if (_watcherData.AutoclosePids is not null)
            {
                foreach (int pid in _watcherData.AutoclosePids)
                    CloseProcess(pid);
            }

            if (App.LaunchSettings.TestModeFlag.Active)
                Process.Start(Paths.Process, "-settings -testmode");
        }

        public void Dispose()
        {
            App.Logger.WriteLine("Watcher::Dispose", "Disposing Watcher");

            _notifyIcon?.Dispose();
            RichPresence?.Dispose();

            App.State.Prop.WatcherRunning = false;

            GC.SuppressFinalize(this);
        }
    }
}
