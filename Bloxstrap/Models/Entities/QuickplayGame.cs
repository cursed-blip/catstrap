using System.ComponentModel;
using System.Windows.Media;

namespace Bloxstrap.Models.Entities
{
    public class QuickplayGame : INotifyPropertyChanged
    {
        private string _name = "";
        private string _iconUrl = "";
        private ImageSource? _icon;
        private bool _isFavorite;
        private DateTime _lastPlayed;
        private long _playtimeSeconds;
        private long _playingCount = -1;
        private double _playtimeShare;
        private string _regionSummary = "";
        private bool _hasShortcut;

        public event PropertyChangedEventHandler? PropertyChanged;

        public long PlaceId { get; set; } = 0;

        public long UniverseId { get; set; } = 0;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string IconUrl
        {
            get => _iconUrl;
            set
            {
                _iconUrl = value;
                OnPropertyChanged(nameof(IconUrl));
            }
        }

        [JsonIgnore]
        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(HasIcon));
            }
        }

        [JsonIgnore]
        public bool HasIcon => _icon is not null;

        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                _isFavorite = value;
                OnPropertyChanged(nameof(IsFavorite));
                OnPropertyChanged(nameof(FavoriteGlyph));
            }
        }

        [JsonIgnore]
        public string FavoriteGlyph => _isFavorite ? "★" : "☆";

        public DateTime LastPlayed
        {
            get => _lastPlayed;
            set
            {
                _lastPlayed = value;
                OnPropertyChanged(nameof(LastPlayed));
                OnPropertyChanged(nameof(LastPlayedText));
            }
        }

        [JsonIgnore]
        public long PlaytimeSeconds
        {
            get => _playtimeSeconds;
            set
            {
                _playtimeSeconds = value;
                OnPropertyChanged(nameof(PlaytimeSeconds));
                OnPropertyChanged(nameof(PlaytimeText));
                OnPropertyChanged(nameof(PlaytimeShortText));
                OnPropertyChanged(nameof(HasPlaytime));
            }
        }

        [JsonIgnore]
        public double PlaytimeShare
        {
            get => _playtimeShare;
            set
            {
                _playtimeShare = value;
                OnPropertyChanged(nameof(PlaytimeShare));
                OnPropertyChanged(nameof(PlaytimeRingValue));
                OnPropertyChanged(nameof(PlaytimeShareText));
            }
        }

        [JsonIgnore]
        public double PlaytimeRingValue => _playtimeSeconds <= 0 ? 0 : Math.Max(_playtimeShare, 3);

        [JsonIgnore]
        public bool HasPlaytime => _playtimeSeconds >= 60;

        [JsonIgnore]
        public string PlaytimeText => HasPlaytime
            ? $"{FormatPlaytime(_playtimeSeconds)} played"
            : "Not played yet";

        [JsonIgnore]
        public string PlaytimeShortText
        {
            get
            {
                if (!HasPlaytime)
                    return "";

                TimeSpan span = TimeSpan.FromSeconds(_playtimeSeconds);

                if (span.TotalHours >= 10)
                    return $"{(int)span.TotalHours}h";

                if (span.TotalHours >= 1)
                    return $"{span.TotalHours:0.#}h";

                return $"{(int)span.TotalMinutes}m";
            }
        }

        [JsonIgnore]
        public string PlaytimeShareText => _playtimeSeconds <= 0
            ? ""
            : $"{_playtimeShare:0.#}% of your playtime";

        [JsonIgnore]
        public long PlayingCount
        {
            get => _playingCount;
            set
            {
                _playingCount = value;
                OnPropertyChanged(nameof(PlayingCount));
                OnPropertyChanged(nameof(PlayingText));
                OnPropertyChanged(nameof(HasPlayingCount));
            }
        }

        [JsonIgnore]
        public bool HasPlayingCount => _playingCount >= 0;

        [JsonIgnore]
        public string PlayingText => _playingCount < 0 ? "" : $"{FormatCount(_playingCount)} playing now";

        public string RegionSummary
        {
            get => _regionSummary;
            set
            {
                _regionSummary = value;
                OnPropertyChanged(nameof(RegionSummary));
                OnPropertyChanged(nameof(HasRegionSummary));
            }
        }

        public DateTime RegionsFetchedAt { get; set; }

        [JsonIgnore]
        public bool HasRegionSummary => !String.IsNullOrWhiteSpace(_regionSummary);

        [JsonIgnore]
        public bool HasShortcut
        {
            get => _hasShortcut;
            set
            {
                _hasShortcut = value;
                OnPropertyChanged(nameof(HasShortcut));
            }
        }

        [JsonIgnore]
        public string DisplayName => String.IsNullOrWhiteSpace(_name) ? $"Place {PlaceId}" : _name;

        [JsonIgnore]
        public string LastPlayedText => _lastPlayed == default
            ? "Never played"
            : $"Last played {_lastPlayed:yyyy-MM-dd HH:mm}";

        [JsonIgnore]
        public string LaunchDeeplink => $"roblox://experiences/start?placeId={PlaceId}";

        private static string FormatPlaytime(long seconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(seconds);

            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours}h {span.Minutes}m";

            return $"{(int)span.TotalMinutes}m";
        }

        public static string FormatCount(long value)
        {
            if (value >= 1_000_000)
                return $"{value / 1_000_000.0:0.#}M";

            if (value >= 1_000)
                return $"{value / 1_000.0:0.#}k";

            return value.ToString();
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
