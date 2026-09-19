using System.Text;
using System.Threading.Channels;

namespace Bloxstrap
{
    public class Logger
    {
        private const int MaxHistoryLines = 8000;

        private const int HistoryTrimThreshold = 9000;

        private const int MaxQueuedLines = 8192;

        private const int MaxBatchLines = 512;

        private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(MaxQueuedLines)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        private readonly Task _writerTask;

        private FileStream? _filestream;

        public readonly List<string> History = new();

        public bool Initialized = false;
        public bool NoWriteMode = false;
        public string? FileLocation;

        public string AsDocument => String.Join('\n', History);

        public Logger()
        {
            _writerTask = Task.Run(WriteLoopAsync);
        }

        public void Initialize(bool useTempDir = false, string? suffix = null)
        {
            const string LOG_IDENT = "Logger::Initialize";

            string directory = useTempDir ? Path.Combine(Paths.TempLogs) : Path.Combine(Paths.Base, "Logs");
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
            string filename = $"{App.ProjectName}_{timestamp}{(suffix is null ? "" : $"_{suffix}")}.log";
            string location = Path.Combine(directory, filename);

            WriteLine(LOG_IDENT, $"Initializing at {location}");

            if (Initialized)
            {
                WriteLine(LOG_IDENT, "Failed to initialize because logger is already initialized");
                return;
            }

            Directory.CreateDirectory(directory);

            if (File.Exists(location))
            {
                WriteLine(LOG_IDENT, "Failed to initialize because log file already exists");
                return;
            }

            try
            {
                _filestream = File.Open(location, FileMode.Create, FileAccess.Write, FileShare.Read);
            }
            catch (IOException)
            {
                WriteLine(LOG_IDENT, "Failed to initialize because log file already exists");
                return;
            }
            catch (UnauthorizedAccessException)
            {
                if (NoWriteMode)
                    return;

                WriteLine(LOG_IDENT, $"Failed to initialize because Bloxstrap cannot write to {directory}");

                Frontend.ShowMessageBox(
                    String.Format(Strings.Logger_NoWriteMode, directory),
                    System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxButton.OK
                );

                NoWriteMode = true;

                return;
            }

            Initialized = true;

            if (History.Count > 0)
                WriteToLog(String.Join('\n', History));

            WriteLine(LOG_IDENT, "Finished initializing!");

            FileLocation = location;

            if (Paths.Initialized && Directory.Exists(Paths.Logs))
                CleanupOldLogs(LOG_IDENT);
        }

        private void CleanupOldLogs(string logIdent)
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-7);

            foreach (FileInfo log in new DirectoryInfo(Paths.Logs).GetFiles())
            {
                if (log.LastWriteTimeUtc > cutoff)
                    continue;

                WriteLine(logIdent, $"Cleaning up old log file '{log.Name}'");

                try
                {
                    log.Delete();
                }
                catch (Exception ex)
                {
                    WriteLine(logIdent, "Failed to delete log!");
                    WriteException(logIdent, ex);
                }
            }
        }

        private void WriteLine(string message)
        {
            string timestamp = DateTime.UtcNow.ToString("s");
            string outcon = $"{timestamp}Z {message}";
            string outlog = Paths.Initialized && outcon.Contains(Paths.UserProfile, StringComparison.OrdinalIgnoreCase)
                ? outcon.Replace(Paths.UserProfile, "%UserProfile%", StringComparison.OrdinalIgnoreCase)
                : outcon;

            Debug.WriteLine(outcon);

            History.Add(outlog);

            if (History.Count > HistoryTrimThreshold)
                History.RemoveRange(0, History.Count - MaxHistoryLines);

            if (Initialized)
                _queue.Writer.TryWrite(outlog);
        }

        public void WriteLine(string identifier, string message) => WriteLine($"[{identifier}] {message}");

        public void WriteException(string identifier, Exception ex)
        {
            CultureInfo previous = Thread.CurrentThread.CurrentUICulture;

            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            string hresult = "0x" + ex.HResult.ToString("X8");

            WriteLine($"[{identifier}] ({hresult}) {ex}");

            Thread.CurrentThread.CurrentUICulture = previous;
        }

        private void WriteToLog(string message)
        {
            if (Initialized)
                _queue.Writer.TryWrite(message);
        }

        public void Shutdown()
        {
            _queue.Writer.TryComplete();

            try
            {
                _writerTask.Wait(TimeSpan.FromMilliseconds(500));
            }
            catch (Exception)
            {
            }

            try
            {
                _filestream?.Flush();
                _filestream?.Dispose();
            }
            catch (Exception)
            {
            }

            _filestream = null;
        }

        private async Task WriteLoopAsync()
        {
            var builder = new StringBuilder(16 * 1024);
            var batch = new List<string>(MaxBatchLines);
            var reader = _queue.Reader;

            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                batch.Clear();

                while (batch.Count < MaxBatchLines && reader.TryRead(out string? line))
                    batch.Add(line);

                if (batch.Count == 0)
                    continue;

                FileStream? stream = _filestream;

                if (stream is null)
                    continue;

                builder.Clear();

                foreach (string line in batch)
                    builder.Append(line).Append("\r\n");

                byte[] bytes = Encoding.UTF8.GetBytes(builder.ToString());

                try
                {
                    await stream.WriteAsync(bytes).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
