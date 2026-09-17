using System.Windows;
using Bloxstrap.Resources;

namespace Bloxstrap.Utility
{
    internal static class Shortcut
    {
        private static GenericTriState _loadStatus = GenericTriState.Unknown;

        public static void Create(string exePath, string exeArgs, string lnkPath)
        {
            Create(exePath, exeArgs, lnkPath, null);
        }

        public static void Create(string exePath, string exeArgs, string lnkPath, string? iconPath, string? description = null)
        {
            const string LOG_IDENT = "Shortcut::Create";

            if (File.Exists(lnkPath))
                return;

            try
            {
                string workingDirectory = Path.GetDirectoryName(exePath) ?? "";

                var shortcut = String.IsNullOrEmpty(iconPath)
                    ? ShellLink.Shortcut.CreateShortcut(exePath, exeArgs, exePath, 0)
                    : ShellLink.Shortcut.CreateShortcut(exePath, exeArgs, workingDirectory, iconPath, 0);

                if (!String.IsNullOrEmpty(description))
                    shortcut.StringData.NameString = description;

                shortcut.WriteToFile(lnkPath);

                if (_loadStatus != GenericTriState.Successful)
                    _loadStatus = GenericTriState.Successful;
            }
            catch (FileNotFoundException ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to create a shortcut for {lnkPath}!");
                App.Logger.WriteException(LOG_IDENT, ex);

                if (_loadStatus == GenericTriState.Failed)
                    return;

                _loadStatus = GenericTriState.Failed;

                Frontend.ShowMessageBox(Strings.Dialog_CannotCreateShortcuts, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to create a shortcut for {lnkPath}!");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }
    }
}
