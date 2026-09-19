using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Bloxstrap.Integrations;

namespace Bloxstrap.UI.Elements.Controls
{
    public class AnimatedGifImage : FrameworkElement
    {
        private const string LOG_IDENT = "AnimatedGifImage";

        private const int MaxSourceWidth = 1024;

        private const int MaxSourceFrames = 240;

        private const int MaxDecodedBytes = 72 * 1024 * 1024;

        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            nameof(Source),
            typeof(string),
            typeof(AnimatedGifImage),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

        public string Source
        {
            get => (string)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        private sealed record GifFrame(BitmapSource Image, int DelayMs);

        private readonly DispatcherTimer _timer;
        private List<GifFrame> _frames = new();
        private int _index;
        private int _generation;
        private bool _playing;

        public AnimatedGifImage()
        {
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.LowQuality);
            SnapsToDevicePixels = false;
            IsHitTestVisible = false;

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };

            _timer.Tick += (_, _) => Advance();

            Loaded += (_, _) =>
            {
                if (Window.GetWindow(this) is Window window)
                {
                    window.Activated += OnWindowActivated;
                    window.Deactivated += OnWindowDeactivated;
                }

                UpdatePlayState();
            };

            Unloaded += (_, _) =>
            {
                if (Window.GetWindow(this) is Window window)
                {
                    window.Activated -= OnWindowActivated;
                    window.Deactivated -= OnWindowDeactivated;
                }

                Stop();
            };
        }

        private void OnWindowActivated(object? sender, EventArgs e) => UpdatePlayState();

        private void OnWindowDeactivated(object? sender, EventArgs e) => UpdatePlayState();

        private static void OnSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is AnimatedGifImage image)
                _ = image.LoadAsync((string)e.NewValue);
        }

        private async Task LoadAsync(string source)
        {
            int generation = ++_generation;

            Stop();
            _frames = new List<GifFrame>();
            InvalidateVisual();

            source = source?.Trim() ?? "";

            if (source.Length == 0)
                return;

            string path;

            try
            {
                if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    path = await BackgroundGif.EnsureAsync(source);
                else if (File.Exists(source))
                    path = source;
                else
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Nothing to play at {source}");
                    return;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not get the background gif: {ex.Message}");
                return;
            }

            List<GifFrame>? frames = null;

            await Task.Run(() =>
            {
                try
                {
                    frames = Decode(path);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not read the background gif");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            });

            if (generation != _generation)
                return;

            if (frames is null || frames.Count == 0)
                return;

            _frames = frames;
            _index = 0;

            App.Logger.WriteLine(LOG_IDENT, $"Playing {frames.Count} frames from {path}");

            UpdatePlayState();
            InvalidateVisual();
        }

        private void UpdatePlayState()
        {
            bool shouldPlay = _frames.Count > 1 && IsLoaded && !App.Settings.Prop.ReduceVisualEffects;

            if (Window.GetWindow(this) is Window window && !window.IsActive)
                shouldPlay = false;

            if (shouldPlay)
                Start();
            else
                Stop();
        }

        private void Start()
        {
            if (_playing || _frames.Count == 0)
                return;

            _playing = true;
            _timer.Interval = TimeSpan.FromMilliseconds(_frames[_index].DelayMs);
            _timer.Start();
        }

        private void Stop()
        {
            _playing = false;
            _timer.Stop();
        }

        private void Advance()
        {
            if (!_playing || _frames.Count == 0)
            {
                Stop();
                return;
            }

            _index = (_index + 1) % _frames.Count;
            _timer.Interval = TimeSpan.FromMilliseconds(_frames[_index].DelayMs);

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (_frames.Count == 0)
                return;

            BitmapSource frame = _frames[_index].Image;

            Size size = RenderSize;

            if (size.Width <= 0 || size.Height <= 0)
                return;

            double scale = Math.Max(size.Width / frame.PixelWidth, size.Height / frame.PixelHeight);
            double width = frame.PixelWidth * scale;
            double height = frame.PixelHeight * scale;

            var rect = new Rect(
                (size.Width - width) / 2,
                (size.Height - height) / 2,
                width,
                height);

            drawingContext.DrawImage(frame, rect);
        }

        #region decoding

        private static List<GifFrame> Decode(string path)
        {
            var frames = new List<GifFrame>();

            using var stream = File.OpenRead(path);

            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
                return frames;

            int width = decoder.Frames[0].PixelWidth;
            int height = decoder.Frames[0].PixelHeight;

            if (width <= 0 || height <= 0)
                return frames;

            double scale = Math.Min(1d, (double)MaxSourceWidth / width);

            int targetWidth = scale >= 0.999 ? width : Math.Max(1, (int)Math.Round(width * scale));
            int targetHeight = scale >= 0.999 ? height : Math.Max(1, (int)Math.Round(height * scale));

            int frameBudget = Math.Max(1, MaxDecodedBytes / (targetWidth * targetHeight * 4));
            int count = Math.Min(decoder.Frames.Count, Math.Min(MaxSourceFrames, frameBudget));

            byte[] canvas = new byte[width * height * 4];
            byte[] restore = new byte[canvas.Length];
            byte[] scratch = Array.Empty<byte>();
            byte[] scaled = Array.Empty<byte>();

            WriteableBitmap? full = null;

            if (targetWidth != width)
            {
                full = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                scaled = new byte[targetWidth * targetHeight * 4];
            }

            int previousDisposal = 0, previousLeft = 0, previousTop = 0, previousWidth = 0, previousHeight = 0;

            for (int i = 0; i < count; i++)
            {
                BitmapFrame frame = decoder.Frames[i];
                BitmapMetadata? metadata = frame.Metadata as BitmapMetadata;

                if (i > 0)
                    ApplyDisposal(canvas, width, height, previousDisposal, previousLeft, previousTop, previousWidth, previousHeight, restore);

                int left = ReadInt(metadata, "/imgdesc/Left", 0);
                int top = ReadInt(metadata, "/imgdesc/Top", 0);
                int frameWidth = ReadInt(metadata, "/imgdesc/Width", frame.PixelWidth);
                int frameHeight = ReadInt(metadata, "/imgdesc/Height", frame.PixelHeight);
                int disposal = ReadInt(metadata, "/grctlext/Disposal", 0);
                int delay = ReadInt(metadata, "/grctlext/Delay", 10) * 10;

                if (disposal == 3)
                    Array.Copy(canvas, restore, canvas.Length);

                Blit(canvas, width, height, frame, left, top, frameWidth, frameHeight, ref scratch);

                frames.Add(new GifFrame(Store(canvas, width, height, targetWidth, targetHeight, full, scaled), Math.Clamp(delay, 20, 1000)));

                previousDisposal = disposal;
                previousLeft = left;
                previousTop = top;
                previousWidth = frameWidth;
                previousHeight = frameHeight;
            }

            return frames;
        }

        private static void ApplyDisposal(byte[] canvas, int width, int height, int disposal, int left, int top, int frameWidth, int frameHeight, byte[] restore)
        {
            if (disposal == 2)
            {
                for (int y = Math.Max(0, top); y < Math.Min(height, top + frameHeight); y++)
                {
                    for (int x = Math.Max(0, left); x < Math.Min(width, left + frameWidth); x++)
                    {
                        int offset = (y * width + x) * 4;

                        canvas[offset] = 0;
                        canvas[offset + 1] = 0;
                        canvas[offset + 2] = 0;
                        canvas[offset + 3] = 0;
                    }
                }
            }
            else if (disposal == 3)
            {
                Array.Copy(restore, canvas, canvas.Length);
            }
        }

        private static void Blit(byte[] canvas, int width, int height, BitmapFrame frame, int left, int top, int frameWidth, int frameHeight, ref byte[] scratch)
        {
            int sourceWidth = frame.PixelWidth;
            int sourceHeight = frame.PixelHeight;

            if (sourceWidth <= 0 || sourceHeight <= 0)
                return;

            int stride = sourceWidth * 4;
            int required = stride * sourceHeight;

            byte[] source = scratch;

            if (source.Length < required)
            {
                source = new byte[required];
                scratch = source;
            }

            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(source, stride, 0);

            int copyWidth = Math.Min(frameWidth, sourceWidth);
            int copyHeight = Math.Min(frameHeight, sourceHeight);

            int destinationLeft = Math.Max(0, left);
            int destinationTop = Math.Max(0, top);
            int destinationRight = Math.Min(width, left + copyWidth);
            int destinationBottom = Math.Min(height, top + copyHeight);

            if (destinationRight <= destinationLeft || destinationBottom <= destinationTop)
                return;

            int startX = destinationLeft - left;

            for (int y = destinationTop; y < destinationBottom; y++)
            {
                int from = (y - top) * stride + startX * 4;
                int to = (y * width + destinationLeft) * 4;

                for (int x = destinationLeft; x < destinationRight; x++, from += 4, to += 4)
                {
                    if (source[from + 3] == 0)
                        continue;

                    canvas[to] = source[from];
                    canvas[to + 1] = source[from + 1];
                    canvas[to + 2] = source[from + 2];
                    canvas[to + 3] = 255;
                }
            }
        }

        private static BitmapSource Store(byte[] canvas, int width, int height, int targetWidth, int targetHeight, WriteableBitmap? full, byte[] scaled)
        {
            if (full is null || (targetWidth == width && targetHeight == height))
                return CreateFreeze(canvas, width, height);

            full.WritePixels(new Int32Rect(0, 0, width, height), canvas, width * 4, 0);

            var transformed = new TransformedBitmap(full, new ScaleTransform((double)targetWidth / width, (double)targetHeight / height));
            transformed.CopyPixels(scaled, targetWidth * 4, 0);

            return CreateFreeze(scaled, targetWidth, targetHeight);
        }

        private static BitmapSource CreateFreeze(byte[] pixels, int width, int height)
        {
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            image.Freeze();

            return image;
        }

        private static int ReadInt(BitmapMetadata? metadata, string query, int fallback)
        {
            if (metadata is null)
                return fallback;

            try
            {
                object? value = metadata.GetQuery(query);

                return value switch
                {
                    byte b => b,
                    short s => s,
                    ushort u => u,
                    int i => i,
                    uint ui => (int)ui,
                    _ => fallback
                };
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        #endregion
    }
}
