using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Bloxstrap.Integrations;
using Bloxstrap.RobloxInterfaces;
using CommunityToolkit.Mvvm.Input;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class AccountsViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "AccountsViewModel";

        private long _currentId;

        public AccountsViewModel()
        {
        }

        public WardrobeViewModel Wardrobe { get; } = new();

        public bool CookieAccess
        {
            get => App.Settings.Prop.AllowCookieAccess;
            set
            {
                if (App.Settings.Prop.AllowCookieAccess == value)
                    return;

                App.Settings.Prop.AllowCookieAccess = value;

                if (value)
                    _ = App.Cookies.LoadCookies();

                OnPropertyChanged(nameof(CookieAccess));
                OnPropertyChanged(nameof(CookieHint));
                OnPropertyChanged(nameof(AccessPromptVisibility));

                Wardrobe.Load(force: true);
            }
        }

        public Visibility AccessPromptVisibility => CookieAccess ? Visibility.Collapsed : Visibility.Visible;

        public string CookieHint => CookieAccess
            ? "Catstrap can read your Roblox login. That's what the wardrobe needs, and it's what lets it remember the accounts you switch between. Cookies are encrypted with Windows and never leave this PC."
            : "Turn this on to use the wardrobe and to have Catstrap remember your accounts. Nothing is sent anywhere, and the cookies stay encrypted with Windows.";

        private bool _isBusy;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
            }
        }

        private string _statusMessage = "";

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }

        public bool HasStatusMessage => !String.IsNullOrWhiteSpace(_statusMessage);

        public bool HasProfile => _currentId > 0;

        public string DisplayName { get; private set; } = "";

        public string UsernameLine { get; private set; } = "";

        public string MetaLine { get; private set; } = "";

        public bool IsBanned { get; private set; }

        public ImageSource? BustImage { get; private set; }

        public ImageSource? FullBodyImage { get; private set; }

        public Brush BannerBrush { get; private set; } = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2C));

        public void Load()
        {
            _ = LoadAsync();

            Wardrobe.Load();
        }

        private async Task LoadAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;

            try
            {
                if (_currentId <= 0)
                {
                    StatusMessage = "Finding your account...";

                    long? id = AccountProfiles.FindSignedInUserId();

                    if (id is null)
                    {
                        StatusMessage = "Couldn't tell which account this PC is signed into. Sign in to Roblox, then reopen this tab.";
                        return;
                    }

                    await ShowProfileAsync(id.Value, false);
                    return;
                }

                await ShowProfileAsync(_currentId, false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ShowProfileAsync(long id, bool forceRefresh)
        {
            AccountProfile? profile = await AccountProfiles.FetchAsync(id, forceRefresh);

            if (profile is null)
            {
                StatusMessage = "Roblox didn't return that profile.";
                return;
            }

            byte[]? fullBody = await AccountProfiles.DownloadImageAsync(profile.FullBodyUrl);
            byte[]? bust = await AccountProfiles.DownloadImageAsync(profile.BustUrl);

            ImageSource? fullBodyImage = ToImage(fullBody, 320);
            ImageSource? bustImage = ToImage(bust ?? fullBody, 220);
            Brush banner = BuildBannerBrush(fullBody ?? bust);

            DateTime created = profile.Created;

            Dispatcher? dispatcher = Application.Current?.Dispatcher;

            void Apply()
            {
                _currentId = profile.Id;

                DisplayName = profile.DisplayName;
                UsernameLine = "@" + profile.Username;
                MetaLine = $"Joined {created.ToLocalTime():d MMMM yyyy}   •   {FormatCount(profile.Followers)} followers   •   {FormatCount(profile.Following)} following";
                IsBanned = profile.IsBanned;

                FullBodyImage = fullBodyImage;
                BustImage = bustImage;
                BannerBrush = banner;

                StatusMessage = profile.IsBanned ? "This account is banned." : "";

                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(UsernameLine));
                OnPropertyChanged(nameof(MetaLine));
                OnPropertyChanged(nameof(IsBanned));
                OnPropertyChanged(nameof(FullBodyImage));
                OnPropertyChanged(nameof(BustImage));
                OnPropertyChanged(nameof(BannerBrush));
                OnPropertyChanged(nameof(HasProfile));
            }

            if (dispatcher is null || dispatcher.CheckAccess())
                Apply();
            else
                dispatcher.Invoke(Apply);
        }

        private static string FormatCount(int count)
        {
            if (count >= 1_000_000)
                return (count / 1_000_000d).ToString("0.#") + "M";

            if (count >= 1_000)
                return (count / 1_000d).ToString("0.#") + "K";

            return count.ToString();
        }

        private static ImageSource? ToImage(byte[]? bytes, int decodeWidth)
        {
            if (bytes is null || bytes.Length == 0)
                return null;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.DecodePixelWidth = decodeWidth;
                image.StreamSource = new MemoryStream(bytes);
                image.EndInit();
                image.Freeze();

                return image;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not decode an avatar image: {ex.Message}");
                return null;
            }
        }

        private static Brush BuildBannerBrush(byte[]? png)
        {
            Color top = Color.FromRgb(0x1F, 0x1F, 0x2A);
            Color bottom = Color.FromRgb(0x3B, 0x35, 0x50);

            if (png is null || png.Length == 0)
                return MakeBrush(top, bottom);

            try
            {
                var frame = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;

                byte[] pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                double r = 0, g = 0, b = 0, weight = 0;

                for (int i = 0; i + 3 < pixels.Length; i += 16)
                {
                    if (pixels[i + 3] < 160)
                        continue;

                    byte pb = pixels[i];
                    byte pg = pixels[i + 1];
                    byte pr = pixels[i + 2];

                    int max = Math.Max(pr, Math.Max(pg, pb));
                    int min = Math.Min(pr, Math.Min(pg, pb));

                    double saturation = max == 0 ? 0 : (double)(max - min) / max;
                    double w = 0.15 + saturation * saturation;

                    r += pr * w;
                    g += pg * w;
                    b += pb * w;
                    weight += w;
                }

                if (weight > 0)
                {
                    Color accent = Color.FromRgb((byte)(r / weight), (byte)(g / weight), (byte)(b / weight));

                    top = Scale(accent, 0.30);
                    bottom = Scale(accent, 0.62);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not work out a banner colour: {ex.Message}");
            }

            return MakeBrush(top, bottom);
        }

        private static Brush MakeBrush(Color top, Color bottom)
        {
            var brush = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0.4, 1));
            brush.Freeze();

            return brush;
        }

        private static Color Scale(Color color, double factor) =>
            Color.FromRgb((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));
    }
}
