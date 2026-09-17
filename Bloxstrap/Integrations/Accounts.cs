using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Bloxstrap.Models;
using Bloxstrap.Models.APIs.Roblox;

namespace Bloxstrap.Integrations
{
    public class SavedAccount
    {
        public long UserId { get; set; }

        public string Username { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public string Cookie { get; set; } = "";

        public DateTime SavedAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public string Label => String.IsNullOrWhiteSpace(DisplayName) || DisplayName == Username
            ? Username
            : $"{DisplayName} (@{Username})";
    }

    public class AccountsFile
    {
        public List<SavedAccount> Accounts { get; set; } = new();

        public long ActiveUserId { get; set; }
    }

    public static class Accounts
    {
        private const string LOG_IDENT = "Accounts";

        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        public static string StorePath => Path.Combine(Paths.Base, "Accounts.json");

        private static readonly object FileLock = new();

        public static AccountsFile Load()
        {
            lock (FileLock)
            {
                try
                {
                    if (File.Exists(StorePath))
                        return JsonSerializer.Deserialize<AccountsFile>(File.ReadAllText(StorePath)) ?? new AccountsFile();
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not read the saved accounts");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }

                return new AccountsFile();
            }
        }

        private static void Save(AccountsFile file)
        {
            lock (FileLock)
            {
                try
                {
                    Directory.CreateDirectory(Paths.Base);
                    File.WriteAllText(StorePath, JsonSerializer.Serialize(file, WriteOptions));
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Could not save the accounts file");
                    App.Logger.WriteException(LOG_IDENT, ex);
                }
            }
        }

        public static async Task AutoSaveAsync()
        {
            if (!App.Settings.Prop.AutoSaveAccounts || !App.Settings.Prop.AllowCookieAccess)
                return;

            try
            {
                (bool ok, string message) = await SaveCurrentAsync();

                if (!ok)
                    App.Logger.WriteLine(LOG_IDENT, $"Automatic account save skipped: {message}");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Automatic account save failed");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public static bool IsRobloxRunning()
        {
            string[] names = { "RobloxPlayerBeta", "RobloxStudioBeta", "RobloxCrashHandler" };

            foreach (string name in names)
            {
                try
                {
                    if (Process.GetProcessesByName(name).Length > 0)
                        return true;
                }
                catch (Exception)
                {
                }
            }

            return false;
        }

        public static bool IsCookieReadable => App.Settings.Prop.AllowCookieAccess
            && App.Cookies.Loaded
            && !String.IsNullOrEmpty(App.Cookies.ExportAuthCookie());

        public static async Task<(bool Ok, string Message)> SaveCurrentAsync()
        {
            if (!App.Settings.Prop.AllowCookieAccess)
                return (false, "Catstrap needs permission to read your Roblox cookie. Turn on \"Access your Roblox account\" on the Behaviour tab.");

            try
            {
                if (!App.Cookies.Loaded)
                    await App.Cookies.LoadCookies();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not load the Roblox cookies");
                App.Logger.WriteException(LOG_IDENT, ex);

                return (false, "Could not read your Roblox cookies.");
            }

            string? cookie = App.Cookies.ExportAuthCookie();

            if (String.IsNullOrEmpty(cookie))
                return (false, "No Roblox account is signed in on this PC that Catstrap can read.");

            AuthenticatedUser? user;

            try
            {
                user = await App.Cookies.GetAuthenticated();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not ask Roblox who is signed in");
                App.Logger.WriteException(LOG_IDENT, ex);

                return (false, "Roblox wouldn't confirm who is signed in.");
            }

            if (user is null || user.Id == 0)
                return (false, "That cookie doesn't belong to a valid account any more.");

            string protectedCookie;

            try
            {
                protectedCookie = Protect(cookie);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not encrypt the cookie");
                App.Logger.WriteException(LOG_IDENT, ex);

                return (false, "Windows wouldn't encrypt the cookie, so it wasn't saved.");
            }

            var file = Load();

            SavedAccount? existing = file.Accounts.FirstOrDefault(x => x.UserId == user.Id);

            if (existing is null)
            {
                existing = new SavedAccount { UserId = user.Id };
                file.Accounts.Add(existing);
            }

            existing.Username = user.Username;
            existing.DisplayName = user.Displayname;
            existing.Cookie = protectedCookie;
            existing.SavedAt = DateTime.UtcNow;

            file.ActiveUserId = user.Id;

            Save(file);

            App.Logger.WriteLine(LOG_IDENT, $"Saved account {user.Id}");

            return (true, $"Saved {existing.Label}.");
        }

        public static void Remove(long userId)
        {
            var file = Load();

            file.Accounts.RemoveAll(x => x.UserId == userId);

            if (file.ActiveUserId == userId)
                file.ActiveUserId = 0;

            Save(file);
        }

        public static (bool Ok, string Message) SwitchTo(long userId)
        {
            if (IsRobloxRunning())
                return (false, "Close Roblox first - it rewrites its cookie file when it exits.");

            var file = Load();
            SavedAccount? account = file.Accounts.FirstOrDefault(x => x.UserId == userId);

            if (account is null)
                return (false, "That account isn't saved any more.");

            string cookie;

            try
            {
                cookie = Unprotect(account.Cookie);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not decrypt a saved cookie");
                App.Logger.WriteException(LOG_IDENT, ex);

                return (false, "Windows wouldn't decrypt that account's cookie. Save it again.");
            }

            string path = App.Cookies.CookieFilePath;

            if (!File.Exists(path))
                return (false, "Roblox hasn't stored any cookies yet. Sign in to Roblox once, then try again.");

            try
            {
                string content = File.ReadAllText(path);
                var cookies = JsonSerializer.Deserialize<RobloxCookies>(content);

                if (cookies is null || String.IsNullOrEmpty(cookies.Cookies))
                    return (false, "Roblox's cookie file isn't in a shape we understand.");

                byte[] encrypted = Convert.FromBase64String(cookies.Cookies);
                byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                string raw = Encoding.UTF8.GetString(plain);

                if (!CookiesManager.AuthCookiePattern.IsMatch(raw))
                    return (false, "Couldn't find a Roblox login in the cookie file.");

                if (CookiesManager.AuthCookiePattern.Match(raw).Groups[2].Value == cookie)
                    return (true, $"Already signed in as {account.Label}.");

                string backup = path + ".catstrap-backup";

                if (!File.Exists(backup))
                    File.Copy(path, backup);

                string replaced = CookiesManager.AuthCookiePattern.Replace(raw, m => m.Groups[1].Value + cookie + m.Groups[3].Value);

                byte[] reEncrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(replaced), null, DataProtectionScope.CurrentUser);

                cookies.Cookies = Convert.ToBase64String(reEncrypted);

                File.WriteAllText(path, JsonSerializer.Serialize(cookies, WriteOptions));
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, "Could not switch accounts");
                App.Logger.WriteException(LOG_IDENT, ex);

                return (false, "Windows wouldn't let us write Roblox's cookie file.");
            }

            file.ActiveUserId = userId;
            Save(file);

            App.Cookies.ForgetLoadedCookie();

            App.Logger.WriteLine(LOG_IDENT, $"Switched to account {userId}");

            return (true, $"Now signed in as {account.Label}. Launch Roblox to use it.");
        }

        private static string Protect(string cookie)
        {
            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(cookie), null, DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(encrypted);
        }

        private static string Unprotect(string stored)
        {
            byte[] encrypted = Convert.FromBase64String(stored);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plain);
        }

        public static SavedAccount? ActiveAccount()
        {
            var file = Load();

            return file.Accounts.FirstOrDefault(x => x.UserId == file.ActiveUserId);
        }
    }
}
