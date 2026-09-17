using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Bloxstrap.Models.APIs.Roblox;
using Bloxstrap.RobloxInterfaces;
using CommunityToolkit.Mvvm.Input;

namespace Bloxstrap.UI.ViewModels.Settings
{
    public class WardrobeViewModel : NotifyPropertyChangedViewModel
    {
        private const string LOG_IDENT = "WardrobeViewModel";

        private const int PageSize = 24;

        public class WardrobeItem : NotifyPropertyChangedViewModel
        {
            public long AssetId { get; init; }

            public long OutfitId { get; init; }

            public bool IsOutfit => OutfitId > 0;

            public bool IsCreation { get; init; }

            public bool Owned { get; init; } = true;

            public string Name { get; init; } = "";

            public string Kind { get; init; } = "";

            private bool _worn;

            public bool Worn
            {
                get => _worn;
                private set
                {
                    _worn = value;
                    OnPropertyChanged(nameof(Worn));
                    OnPropertyChanged(nameof(WornVisibility));
                    OnPropertyChanged(nameof(Label));
                }
            }

            public Visibility WornVisibility => _worn ? Visibility.Visible : Visibility.Collapsed;

            public string Badge => IsOutfit ? "Wear" : (Owned ? "" : "Open");

            public Visibility BadgeVisibility => Badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            public string Label
            {
                get
                {
                    if (IsOutfit)
                        return $"Wear {Name}";

                    if (!Owned)
                        return Kind.Length > 0 ? $"Open {Name} ({Kind}) on Roblox" : $"Open {Name} on Roblox";

                    if (_worn)
                        return Kind.Length > 0 ? $"{Name} ({Kind}) - worn" : $"{Name} - worn";

                    return Kind.Length > 0 ? $"{Name} ({Kind})" : Name;
                }
            }

            private ImageSource? _thumbnail;

            public ImageSource? Thumbnail
            {
                get => _thumbnail;
                set
                {
                    _thumbnail = value;
                    OnPropertyChanged(nameof(Thumbnail));
                }
            }

            public void SetWorn(bool worn) => Worn = worn;
        }

        public class WardrobeGroup : NotifyPropertyChangedViewModel
        {
            public int TypeId { get; init; }

            public string Name { get; init; } = "";

            public List<WardrobeItem> All { get; } = new();

            public ObservableCollection<WardrobeItem> Visible { get; } = new();

            public bool HasMore => Visible.Count < All.Count;

            public Visibility MoreVisibility => HasMore ? Visibility.Visible : Visibility.Collapsed;

            public string MoreLabel => $"Show more ({All.Count - Visible.Count} left)";

            public string CountLabel => All.Count == 1 ? "1 item" : $"{All.Count} items";

            public void Fill()
            {
                Visible.Clear();

                foreach (WardrobeItem item in All.Take(PageSize))
                    Visible.Add(item);

                Refresh();
            }

            public List<WardrobeItem> ShowMore()
            {
                var added = All.Skip(Visible.Count).Take(PageSize).ToList();

                foreach (WardrobeItem item in added)
                    Visible.Add(item);

                Refresh();

                return added;
            }

            public void Refresh()
            {
                OnPropertyChanged(nameof(HasMore));
                OnPropertyChanged(nameof(MoreVisibility));
                OnPropertyChanged(nameof(MoreLabel));
                OnPropertyChanged(nameof(CountLabel));
            }
        }

        private readonly Dictionary<long, ImageSource> _images = new();

        private long _userId;

        private bool _loaded;

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

        public ObservableCollection<WardrobeGroup> Tabs { get; } = new();

        public Visibility GroupsVisibility => Tabs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        private WardrobeGroup? _selectedTab;

        public WardrobeGroup? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (ReferenceEquals(_selectedTab, value))
                    return;

                _selectedTab = value;

                OnPropertyChanged(nameof(SelectedTab));
                OnPropertyChanged(nameof(HasSelectedTab));

                if (value is not null)
                    _ = LoadThumbnailsAsync(value.Visible.ToList());
            }
        }

        public bool HasSelectedTab => _selectedTab is not null;

        private string _status = "";

        public string Status
        {
            get => _status;
            private set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(HasStatus));
            }
        }

        public bool HasStatus => !String.IsNullOrWhiteSpace(_status);

        public ICommand LoadCommand => new AsyncRelayCommand(() => LoadAsync(false));

        public ICommand RefreshCommand => new AsyncRelayCommand(() => LoadAsync(true));

        public ICommand ToggleCommand => new AsyncRelayCommand<WardrobeItem>(ToggleAsync);

        public ICommand ShowMoreCommand => new RelayCommand(ShowMore);

        public void Load(bool force = false) => _ = LoadAsync(force);

        private async Task LoadAsync(bool force)
        {
            if (IsBusy || (_loaded && !force))
                return;

            IsBusy = true;

            try
            {
                if (!AvatarEditor.CookieAccess)
                {
                    await LoadCreationsAsync(null);
                    _loaded = true;
                    return;
                }

                Status = "Loading your wardrobe...";

                AuthenticatedUser? user = await App.Cookies.GetAuthenticated();

                if (user is null || user.Id == 0)
                {
                    Status = "Roblox wouldn't confirm who's signed in, so the wardrobe can't load.";
                    return;
                }

                _userId = user.Id;

                AvatarResponse? avatar = await AvatarEditor.GetAvatarAsync();

                if (avatar is null)
                {
                    Status = "Couldn't read what you're wearing.";
                    return;
                }

                var worn = avatar.Assets.Select(x => x.Id).ToHashSet();

                List<Outfit> outfits = await AvatarEditor.GetOutfitsAsync(user.Id);
                List<CreationItem> creations = await AvatarEditor.GetCreationsAsync(user.Id);

                HashSet<long> ownedCreations = await AvatarEditor.GetOwnedAmongAsync(user.Id, creations);

                var owned = new List<(int Id, string Name, List<InventoryItem> Items)>();

                foreach ((int id, string name) in AvatarEditor.AccessoryTypes)
                {
                    List<InventoryItem> items = await AvatarEditor.GetOwnedAsync(user.Id, id);

                    if (items.Count > 0)
                        owned.Add((id, name, items));
                }

                var tabs = new List<WardrobeGroup>();

                if (outfits.Count > 0)
                {
                    var avatars = new WardrobeGroup { TypeId = 0, Name = "Avatars" };

                    foreach (Outfit outfit in outfits)
                        avatars.All.Add(new WardrobeItem
                        {
                            AssetId = outfit.Id,
                            OutfitId = outfit.Id,
                            Name = String.IsNullOrWhiteSpace(outfit.Name) ? "Saved avatar" : outfit.Name
                        });

                    tabs.Add(avatars);
                }

                foreach ((int id, string name, List<InventoryItem> items) in owned)
                {
                    var group = new WardrobeGroup { TypeId = id, Name = name };

                    foreach (InventoryItem item in items)
                    {
                        var entry = new WardrobeItem
                        {
                            AssetId = item.AssetId,
                            Name = String.IsNullOrWhiteSpace(item.AssetName) ? $"Item {item.AssetId}" : item.AssetName
                        };

                        entry.SetWorn(worn.Contains(item.AssetId));

                        group.All.Add(entry);
                    }

                    tabs.Add(group);
                }

                WardrobeGroup? published = BuildCreationsGroup(creations, ownedCreations, worn);

                if (published is not null)
                    tabs.Add(published);

                Show(tabs);

                if (tabs.Count == 0)
                {
                    Status = "Nothing in your inventory yet.";
                    return;
                }

                int total = owned.Sum(x => x.Items.Count);

                Status = $"{user.Username} - {tabs.Count} tabs, {total} items, {worn.Count} on.";

                _loaded = true;

                await LoadVisibleThumbnailsAsync();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not load the wardrobe");
                App.Logger.WriteException(LOG_IDENT, ex);

                Status = "Something went wrong loading your wardrobe.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadCreationsAsync(long? userId)
        {
            IsBusy = true;

            try
            {
                long id = userId ?? _userId;

                if (id <= 0)
                    id = AccountProfiles.FindSignedInUserId() ?? 0;

                if (id <= 0)
                {
                    Status = "Turn on account access above and your wardrobe will load here.";
                    return;
                }

                _userId = id;

                Status = "Loading what you've published...";

                List<CreationItem> creations = await AvatarEditor.GetCreationsAsync(id);

                HashSet<long> owned = await AvatarEditor.GetOwnedAmongAsync(id, creations);

                WardrobeGroup? published = BuildCreationsGroup(
                    creations,
                    owned,
                    new HashSet<long>());

                Show(published is null ? new List<WardrobeGroup>() : new List<WardrobeGroup> { published });

                if (published is null)
                {
                    Status = AvatarEditor.CookieAccess
                        ? "You haven't published any avatar items."
                        : "Turn on account access above to see and change what you're wearing.";

                    return;
                }

                _loaded = true;

                Status = AvatarEditor.CookieAccess
                    ? $"{published.All.Count} published items."
                    : $"{published.All.Count} published items. Turn on account access above for the rest of your wardrobe.";

                await LoadVisibleThumbnailsAsync();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not load the published items");
                App.Logger.WriteException(LOG_IDENT, ex);

                Status = "Something went wrong loading your published items.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static WardrobeGroup? BuildCreationsGroup(
            List<CreationItem> creations,
            HashSet<long> owned,
            HashSet<long> worn)
        {
            if (creations.Count == 0)
                return null;

            var group = new WardrobeGroup { TypeId = -1, Name = "Creations" };

            foreach (CreationItem creation in creations)
            {
                bool isOwned = owned.Contains(creation.Id);

                var entry = new WardrobeItem
                {
                    AssetId = creation.Id,
                    Name = String.IsNullOrWhiteSpace(creation.Name) ? $"Item {creation.Id}" : creation.Name,
                    Kind = AvatarEditor.DescribeAssetType(creation.AssetType),
                    IsCreation = true,
                    Owned = isOwned
                };

                if (isOwned)
                    entry.SetWorn(worn.Contains(creation.Id));

                group.All.Add(entry);
            }

            return group;
        }

        private void Show(List<WardrobeGroup> groups)
        {
            Tabs.Clear();

            foreach (WardrobeGroup group in groups)
            {
                group.Fill();
                Tabs.Add(group);
            }

            OnPropertyChanged(nameof(Tabs));
            OnPropertyChanged(nameof(GroupsVisibility));

            SelectedTab = Tabs.FirstOrDefault();
        }

        private void ShowMore()
        {
            if (SelectedTab is not WardrobeGroup group)
                return;

            List<WardrobeItem> added = group.ShowMore();

            _ = LoadThumbnailsAsync(added);
        }

        private async Task LoadVisibleThumbnailsAsync()
        {
            if (SelectedTab is not null)
                await LoadThumbnailsAsync(SelectedTab.Visible.ToList());
        }

        private async Task LoadThumbnailsAsync(List<WardrobeItem> items)
        {
            var missing = items.Where(x => x.Thumbnail is null && !_images.ContainsKey(x.AssetId)).ToList();

            if (missing.Count == 0)
            {
                Apply(items);
                return;
            }

            try
            {
                var urls = new Dictionary<long, string>();

                List<long> outfitIds = missing.Where(x => x.IsOutfit).Select(x => x.AssetId).ToList();
                List<long> itemIds = missing.Where(x => !x.IsOutfit).Select(x => x.AssetId).ToList();

                if (itemIds.Count > 0)
                {
                    foreach (var entry in await AvatarEditor.GetThumbnailUrlsAsync(itemIds))
                        urls[entry.Key] = entry.Value;
                }

                if (outfitIds.Count > 0)
                {
                    foreach (var entry in await AvatarEditor.GetOutfitThumbnailUrlsAsync(outfitIds))
                        urls[entry.Key] = entry.Value;
                }

                foreach (WardrobeItem item in missing)
                {
                    if (!urls.TryGetValue(item.AssetId, out string? url))
                        continue;

                    try
                    {
                        byte[] bytes = await App.HttpClient.GetByteArrayAsync(url);

                        ImageSource? image = Decode(bytes);

                        if (image is not null)
                            _images[item.AssetId] = image;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"No picture for {item.AssetId}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not load wardrobe pictures");
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            Apply(items);
        }

        private void Apply(List<WardrobeItem> items)
        {
            foreach (WardrobeItem item in items)
            {
                if (_images.TryGetValue(item.AssetId, out ImageSource? image))
                    item.Thumbnail = image;
            }
        }

        private async Task ToggleAsync(WardrobeItem? item)
        {
            if (item is null || IsBusy)
                return;

            if (!item.Owned)
            {
                Utilities.ShellExecute(AvatarEditor.GetItemUrl(item.AssetId));

                Status = $"Opened {item.Name} on Roblox.";
                return;
            }

            IsBusy = true;

            try
            {
                if (item.IsOutfit)
                {
                    (bool wornOk, string wornMessage) = await AvatarEditor.WearOutfitAsync(item.OutfitId);

                    Status = wornMessage;

                    if (!wornOk)
                        return;

                    await RefreshWornAsync();
                    return;
                }

                (bool ok, string message, bool nowWorn) = await AvatarEditor.ToggleAsync(item.AssetId);

                if (!ok)
                {
                    Status = message;
                    return;
                }

                item.SetWorn(nowWorn);

                foreach (WardrobeGroup group in Tabs)
                {
                    foreach (WardrobeItem other in group.All)
                    {
                        if (other.AssetId == item.AssetId)
                            other.SetWorn(nowWorn);
                    }
                }

                Status = nowWorn ? $"Wearing {item.Name}." : $"Took off {item.Name}.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshWornAsync()
        {
            AvatarResponse? avatar = await AvatarEditor.GetAvatarAsync();

            if (avatar is null)
                return;

            var worn = avatar.Assets.Select(x => x.Id).ToHashSet();

            int on = 0;

            foreach (WardrobeGroup group in Tabs)
            {
                foreach (WardrobeItem item in group.All)
                {
                    if (item.IsOutfit)
                        continue;

                    item.SetWorn(worn.Contains(item.AssetId));

                    if (item.Owned && worn.Contains(item.AssetId))
                        on++;
                }
            }

            Status = $"{on} items on.";
        }

        private static ImageSource? Decode(byte[] bytes)
        {
            if (bytes.Length == 0)
                return null;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.DecodePixelWidth = 150;
                image.StreamSource = new MemoryStream(bytes);
                image.EndInit();
                image.Freeze();

                return image;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not decode an item picture: {ex.Message}");
                return null;
            }
        }
    }
}
