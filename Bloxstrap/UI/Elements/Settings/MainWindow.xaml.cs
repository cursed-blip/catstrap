using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Mvvm.Contracts;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

using Bloxstrap.UI.ViewModels.Settings;
using Bloxstrap.UI.Elements.Settings.Pages;
using Bloxstrap.UI.Elements.Controls;

namespace Bloxstrap.UI.Elements.Settings
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : INavigationWindow
    {
        private Models.Persistable.WindowState _state => App.State.Prop.SettingsWindow;

        private static List<SearchBarItem>? _searchIndex;

        public MainWindow(bool showAlreadyRunningWarning)
        {
            var viewModel = new MainWindowViewModel();

            viewModel.RequestSaveNoticeEvent += (_, _) => SettingsSavedSnackbar.Show();
            viewModel.RequestCloseWindowEvent += (_, _) => Close();

            DataContext = viewModel;

            InitializeComponent();

            App.Logger.WriteLine("MainWindow", "Initializing settings window");

            if (showAlreadyRunningWarning)
                ShowAlreadyRunningSnackbar();

            gbs.Opacity = viewModel.GBSEnabled ? 1 : 0.5;
            gbs.IsEnabled = viewModel.GBSEnabled; // binding doesnt work as expected so we are setting it in here instead

            LoadState();
            ApplyBackgroundGif();

            string? lastPageName = App.State.Prop.LastPage;
            Type? lastPage = lastPageName is null ? null : Type.GetType(lastPageName);

            App.RemoteData.Subscribe((object? sender, EventArgs e) => {
                RemoteDataBase Data = App.RemoteData.Prop;

                AlertBar.Visibility = Data.AlertEnabled ? Visibility.Visible : Visibility.Collapsed;
                AlertBar.Message = Data.AlertContent;
                AlertBar.Severity = Data.AlertSeverity;

                if (Data.KillFlags)
                    fastflags.PageType = typeof(FastFlagsDisabled);
            });

            if (lastPage is not null && RootNavigation.Items.OfType<NavigationItem>().Any(x => x.PageType == lastPage))
                SafeNavigate(lastPage);
            else if (lastPage is not null)
                App.Logger.WriteLine("MainWindow", $"Last page '{lastPageName}' is no longer available, falling back to default");

            this.Loaded += (_, _) =>
            {
                if (_searchIndex is not null)
                    return;

                Dispatcher.InvokeAsync(EnsureSearchIndex, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };

            RootNavigation.Navigated += OnNavigation!;

            void OnNavigation(object? sender, RoutedNavigationEventArgs e)
            {
                INavigationItem? currentPage = RootNavigation.Current;

                App.State.Prop.LastPage = currentPage?.PageType.FullName!;
            }

        }

        public void ApplyBackgroundGif()
        {
            string url = App.Settings.Prop.BackgroundGifUrl?.Trim() ?? "";

            if (url.Length == 0 || App.Settings.Prop.ReduceVisualEffects)
            {
                BackgroundGifLayer.Source = "";
                BackgroundGifLayer.Opacity = 0;
                return;
            }

            BackgroundGifLayer.Opacity = Math.Clamp(App.Settings.Prop.BackgroundGifOpacity, 0.05, 1);
            BackgroundGifLayer.Source = url;
        }

        private void Notify(string message, bool good = true) =>
            Dispatcher.Invoke(() => NoticeSnackbar.Show(good ? "Accounts" : "Accounts", message));

        private void AccountsDropDown_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not Wpf.Ui.Controls.Button button)
                return;

            Integrations.AccountsFile file = Integrations.Accounts.Load();

            var menu = new System.Windows.Controls.ContextMenu
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };

            if (file.Accounts.Count == 0)
            {
                menu.Items.Add(new System.Windows.Controls.MenuItem
                {
                    Header = "No accounts saved yet",
                    IsEnabled = false
                });
            }
            else
            {
                foreach (Integrations.SavedAccount account in file.Accounts)
                {
                    var item = new System.Windows.Controls.MenuItem
                    {
                        Header = account.Label,
                        IsCheckable = true,
                        IsChecked = account.UserId == file.ActiveUserId,
                        StaysOpenOnClick = false
                    };

                    long id = account.UserId;
                    bool isCurrent = account.UserId == file.ActiveUserId;

                    item.Click += (_, _) =>
                    {
                        if (isCurrent)
                            return;

                        SwitchAccount(id);
                    };

                    menu.Items.Add(item);
                }
            }

            menu.Items.Add(new Separator());

            var save = new System.Windows.Controls.MenuItem { Header = "Save the signed-in account now" };
            save.Click += async (_, _) =>
            {
                (bool ok, string message) = await Integrations.Accounts.SaveCurrentAsync();

                Notify(message, ok);
            };

            menu.Items.Add(save);

            var manage = new System.Windows.Controls.MenuItem { Header = "Accounts and wardrobe" };
            manage.Click += (_, _) => Navigate(typeof(AccountsPage));
            menu.Items.Add(manage);

            menu.IsOpen = true;
        }

        private void SwitchAccount(long userId)
        {
            (bool ok, string message) = Integrations.Accounts.SwitchTo(userId);

            Notify(message, ok);
        }

        public void LoadState()
        {
            if (_state.Left > SystemParameters.VirtualScreenWidth)
                _state.Left = 0;

            if (_state.Top > SystemParameters.VirtualScreenHeight)
                _state.Top = 0;

            if (_state.Width > 0)
                this.Width = _state.Width;

            if (_state.Height > 0)
                this.Height = _state.Height;

            if (_state.Left > 0 && _state.Top > 0)
            {
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Left = _state.Left;
                this.Top = _state.Top;
            }
        }

        private async void SafeNavigate(Type page)
        {
            await Task.Delay(500); // same as below

            if (page == typeof(GlobalSettingsPage) && !App.GlobalSettings.Loaded)
                return; // prevent from navigating onto disabled page

            Navigate(page);
        }

        private async void ShowAlreadyRunningSnackbar()
        {
            await Task.Delay(500); // wait for everything to finish loading
            AlreadyRunningSnackbar.Show();
        }

        #region INavigationWindow methods

        public Frame GetFrame() => RootFrame;

        public INavigation GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(IPageService pageService) => RootNavigation.PageService = pageService;

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        private void WpfUiWindow_Closing(object sender, CancelEventArgs e)
        {
            if (App.FastFlags.Changed || App.PendingSettingTasks.Any())
            {
                var result = Frontend.ShowMessageBox(Strings.Menu_UnsavedChanges, MessageBoxImage.Warning, MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes)
                    e.Cancel = true;
            }

            _state.Width = this.Width;
            _state.Height = this.Height;

            _state.Top = this.Top;
            _state.Left = this.Left;

            App.State.Save();
        }

        private void WpfUiWindow_Closed(object sender, EventArgs e)
        {
            if (App.LaunchSettings.TestModeFlag.Active)
                LaunchHandler.LaunchRoblox(LaunchMode.Player);
            else
                App.SoftTerminate();
        }

        private void EnsureSearchIndex()
        {
            if (_searchIndex is not null)
                return;

            BuildSearchIndexAutomatically();
        }

        private void BuildSearchIndexAutomatically()
        {
            _searchIndex = new List<SearchBarItem>();

            var navItems = RootNavigation.Items.OfType<NavigationItem>();

            foreach (var item in navItems)
            {
                if (item.PageType == null)
                    continue;

                if (Activator.CreateInstance(item.PageType) is Page pageInstance)
                {
                    var optionControls = FindLogicalChildren<OptionControl>(pageInstance);

                    foreach (var optionControl in optionControls)
                    {
                        if (optionControl.Header is string headerText && !string.IsNullOrWhiteSpace(headerText))
                        {
                            _searchIndex.Add(new SearchBarItem
                            {
                                DisplayName = headerText,
                                PageType = item.PageType
                            });
                        }
                    }
                }
            }
        }

        private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null)
                yield break;

            foreach (object rawChild in LogicalTreeHelper.GetChildren(depObj))
            {
                if (rawChild is DependencyObject child)
                {
                    if (child is T t)
                    {
                        yield return t;
                    }

                    foreach (T childOfChild in FindLogicalChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        // should move to viewmodels but uhhh im kinda lazy
        private void AutoSuggestBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is AutoSuggestBox autoSuggestBox)
            {
                var currentText = autoSuggestBox.Text;

                if (string.IsNullOrWhiteSpace(currentText))
                {
                    autoSuggestBox.ItemsSource = null;
                    return;
                }

                EnsureSearchIndex();

                var selectedSetting = _searchIndex?.FirstOrDefault(x => x.DisplayName.Equals(currentText, StringComparison.OrdinalIgnoreCase));

                if (selectedSetting is not null)
                {
                    Navigate(selectedSetting.PageType);
                    return;
                }

                var query = currentText.ToLower();
                autoSuggestBox.ItemsSource = _searchIndex?
                    .Where(x => x.DisplayName.ToLower().Contains(query))
                    .Select(x => x.DisplayName)
                    .ToList();
            }
        }
    }
}