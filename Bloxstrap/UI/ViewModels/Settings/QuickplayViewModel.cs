using System.Collections.ObjectModel;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;

using Bloxstrap.Integrations;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class QuickplayViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        private const string LOG_IDENT = "QuickplayViewModel";

        private string _newGameText = "";
        private string? _statusMessage;
        private bool _isBusy;
        private bool _isSubscribed;

        private readonly AsyncRelayCommand _addGameCommand;
        private readonly AsyncRelayCommand _refreshCommand;
        private readonly RelayCommand<QuickplayGame> _playGameCommand;
        private readonly RelayCommand<QuickplayGame> _toggleFavoriteCommand;
        private readonly RelayCommand<QuickplayGame> _removeGameCommand;
        private readonly RelayCommand<QuickplayGame> _createShortcutCommand;
        private readonly AsyncRelayCommand<QuickplayGame> _fetchRegionsCommand;

        public QuickplayViewModel()
        {
            _addGameCommand = new AsyncRelayCommand(AddGameAsync);
            _refreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
            _playGameCommand = new RelayCommand<QuickplayGame>(game => Quickplay.Launch(game!));
            _toggleFavoriteCommand = new RelayCommand<QuickplayGame>(game => Quickplay.ToggleFavorite(game!));
            _removeGameCommand = new RelayCommand<QuickplayGame>(game => Quickplay.Remove(game!));
            _createShortcutCommand = new RelayCommand<QuickplayGame>(game => CreateShortcut(game!));
            _fetchRegionsCommand = new AsyncRelayCommand<QuickplayGame>(game => FetchRegionsAsync(game!));

            Quickplay.ApplyPlaytime();
            Rebuild();
        }

        public void Load()
        {
            if (_isSubscribed)
                return;

            _isSubscribed = true;
            Quickplay.Changed += OnLibraryChanged;

            QuickplayRegions.RestoreFromLibrary();
            Quickplay.ApplyPlaytime();

            Rebuild();
        }

        public void Dispose()
        {
            if (_isSubscribed)
            {
                _isSubscribed = false;
                Quickplay.Changed -= OnLibraryChanged;
            }
        }

        public ObservableCollection<QuickplayGame> VisibleGames { get; } = new();

        public bool HasGames => VisibleGames.Count > 0;

        public string GameCountText
        {
            get
            {
                int total = Quickplay.Games.Count;
                int favorites = Quickplay.Games.Count(x => x.IsFavorite);

                return $"{total} {(total == 1 ? "game" : "games")} \u2022 {favorites} {(favorites == 1 ? "favourite" : "favourites")}";
            }
        }

        public string NewGameText
        {
            get => _newGameText;
            set
            {
                _newGameText = value;
                OnPropertyChanged(nameof(NewGameText));
            }
        }

        public string? StatusMessage
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

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
                _refreshCommand.NotifyCanExecuteChanged();
            }
        }

        public bool TrackRecentlyPlayedGames
        {
            get => App.Settings.Prop.TrackRecentlyPlayedGames;
            set
            {
                App.Settings.Prop.TrackRecentlyPlayedGames = value;
                OnPropertyChanged(nameof(TrackRecentlyPlayedGames));
            }
        }

        public bool ShowLivePlayerCounts
        {
            get => App.Settings.Prop.ShowLivePlayerCounts;
            set
            {
                App.Settings.Prop.ShowLivePlayerCounts = value;
                OnPropertyChanged(nameof(ShowLivePlayerCounts));

                if (value)
                    _ = Quickplay.RefreshLiveCountsAsync();
            }
        }

        public bool ShowServerRegions
        {
            get => App.Settings.Prop.ShowServerRegions;
            set
            {
                App.Settings.Prop.ShowServerRegions = value;
                OnPropertyChanged(nameof(ShowServerRegions));
            }
        }

        public ICommand AddGameCommand => _addGameCommand;

        public ICommand PlayGameCommand => _playGameCommand;

        public ICommand ToggleFavoriteCommand => _toggleFavoriteCommand;

        public ICommand RemoveGameCommand => _removeGameCommand;

        public ICommand RefreshCommand => _refreshCommand;

        public ICommand CreateShortcutCommand => _createShortcutCommand;

        public ICommand FetchRegionsCommand => _fetchRegionsCommand;

        public Task AddFromInputAsync() => AddGameAsync();

        public async void LoadDetails()
        {
            await Quickplay.PrefetchAsync();

            Rebuild();
        }

        private async Task AddGameAsync()
        {
            if (String.IsNullOrWhiteSpace(NewGameText))
            {
                StatusMessage = "Paste a Roblox game link or a place id first.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Adding...";

            try
            {
                QuickplayGame game = await Quickplay.AddAsync(NewGameText);

                StatusMessage = $"Added {game.DisplayName}";
                NewGameText = "";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                StatusMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }

            Rebuild();
        }

        private async Task RefreshAsync()
        {
            IsBusy = true;
            StatusMessage = "Refreshing game details...";

            try
            {
                await Quickplay.RefreshAllAsync();
                await Quickplay.RefreshLiveCountsAsync();
                StatusMessage = "Up to date";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                StatusMessage = "Could not refresh every game.";
            }
            finally
            {
                IsBusy = false;
            }

            Rebuild();
        }

        private void CreateShortcut(QuickplayGame game)
        {
            try
            {
                string path = Quickplay.CreateShortcut(game);

                StatusMessage = $"Shortcut created: {Path.GetFileName(path)} on your desktop";
                OnPropertyChanged(nameof(VisibleGames));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                StatusMessage = $"Could not create the shortcut: {ex.Message}";
            }
        }

        private async Task FetchRegionsAsync(QuickplayGame game)
        {
            if (!App.Settings.Prop.ShowServerRegions)
            {
                StatusMessage = "Server region sampling is turned off above.";
                return;
            }

            StatusMessage = $"Checking where {game.DisplayName}'s servers are...";

            try
            {
                string summary = await QuickplayRegions.GetSummaryAsync(game.PlaceId, forceRefresh: true);

                if (String.IsNullOrWhiteSpace(summary))
                {
                    StatusMessage = $"No server data for {game.DisplayName} right now.";
                    return;
                }

                game.RegionSummary = summary;
                game.RegionsFetchedAt = DateTime.Now;

                StatusMessage = $"{game.DisplayName}: {summary}";
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                StatusMessage = "Could not reach the server region API.";
            }
        }

        private void OnLibraryChanged(object? sender, EventArgs e) => Rebuild();

        private void Rebuild()
        {
            VisibleGames.Clear();

            foreach (QuickplayGame game in Quickplay.Games
                .OrderByDescending(x => x.IsFavorite)
                .ThenByDescending(x => x.LastPlayed))
            {
                VisibleGames.Add(game);
            }

            OnPropertyChanged(nameof(VisibleGames));
            OnPropertyChanged(nameof(HasGames));
            OnPropertyChanged(nameof(GameCountText));
        }
    }
}
