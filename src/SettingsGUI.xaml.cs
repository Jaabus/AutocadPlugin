using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AutocadPlugin
{
    public partial class SettingsGUI : System.Windows.Controls.UserControl
    {
        private bool _isLoadingSettings = false; // Flag to indicate if settings are being loaded

        public SettingsGUI()
        {
            InitializeComponent();

            PopulateLanguageComboBox();

            LoadSettings();
        }

        private void PopulateLanguageComboBox()
        {
            ComboBoxItem languageItem = new ComboBoxItem
            {
                Content = "English", // Display name
                Tag = "en" // Culture name
            };
            LanguageComboBox.Items.Add(languageItem);

            languageItem = new ComboBoxItem
            {
                Content = "Eesti", // Display name
                Tag = "et" // Culture name
            };
            LanguageComboBox.Items.Add(languageItem);
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) // Check the flag
            {
                return; // Do nothing if settings are being loaded
            }

            if (LanguageComboBox.SelectedItem is ComboBoxItem selectedLanguage)
            {
                if (selectedLanguage.Tag is string cultureName)
                {
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo(cultureName);

                    SaveSettings();
                    Commands.ResetGUI();
                }
            }
        }

        private void LoadSettings()
        {
            _isLoadingSettings = true; // Set flag before changing ComboBox

            // Load File Paths
            StreetSignPathTextBox.Text = AutocadPlugin.Properties.Settings.Default.StreetSignFilePath ?? string.Empty;
            LinetypePathTextBox.Text = AutocadPlugin.Properties.Settings.Default.LineTypeFilePath ?? string.Empty;
            LineWidthPathTextBox.Text = AutocadPlugin.Properties.Settings.Default.LineWidthFilePath ?? string.Empty;

            // Load Language Preference
            string savedCultureName = AutocadPlugin.Properties.Settings.Default.SelectedLanguageCultureName;
            if (!string.IsNullOrEmpty(savedCultureName))
            {
                foreach (ComboBoxItem item in LanguageComboBox.Items)
                {
                    if (item.Tag is string cultureTag && cultureTag == savedCultureName)
                    {
                        LanguageComboBox.SelectedItem = item;
                        // Apply the culture immediately if found
                        Thread.CurrentThread.CurrentUICulture = new CultureInfo(savedCultureName);
                        break;
                    }
                }
            }

            _isLoadingSettings = false; // Clear flag after ComboBox changes are done
        }

        private void SaveSettings()
        {
            // Save File Paths
            AutocadPlugin.Properties.Settings.Default.StreetSignFilePath = StreetSignPathTextBox.Text;
            AutocadPlugin.Properties.Settings.Default.LineTypeFilePath = LinetypePathTextBox.Text;
            AutocadPlugin.Properties.Settings.Default.LineWidthFilePath = LineWidthPathTextBox.Text;

            // Save Language Preference
            if (LanguageComboBox.SelectedItem is ComboBoxItem selectedLanguageItem)
            {
                if (selectedLanguageItem.Tag is string cultureTag)
                {
                    AutocadPlugin.Properties.Settings.Default.SelectedLanguageCultureName = cultureTag;
                }
            }

            AutocadPlugin.Properties.Settings.Default.Save();
        }

        private void StreetSignBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                // Set the initial directory if a path is already saved
                if (!string.IsNullOrEmpty(StreetSignPathTextBox.Text) && Directory.Exists(StreetSignPathTextBox.Text))
                {
                    dialog.SelectedPath = StreetSignPathTextBox.Text;
                }
                dialog.Description = "Select Street Sign Folder";
                // ShowHelpButton is false by default, set to true if you have help info
                dialog.ShowNewFolderButton = true; // Allow user to create a new folder

                DialogResult result = dialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    StreetSignPathTextBox.Text = dialog.SelectedPath;
                    SaveSettings();
                }
            }
        }

        private void LinetypeBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
            {
                // Filter for .lin files
                Filter = "Linetype Files (*.lin)|*.lin|All Files (*.*)|*.*",
                Title = "Select Linetype File (.lin)"
            };

            if (!string.IsNullOrEmpty(LinetypePathTextBox.Text) && File.Exists(LinetypePathTextBox.Text))
            {
                dlg.InitialDirectory = Path.GetDirectoryName(LinetypePathTextBox.Text);
                dlg.FileName = Path.GetFileName(LinetypePathTextBox.Text);
            }


            bool? result = dlg.ShowDialog(); // For WPF OpenFileDialog
            if (result == true)
            {
                LinetypePathTextBox.Text = dlg.FileName;
                SaveSettings();
            }
        }

        private void LineWidthBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
            {
                // Filter for .txt files
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                Title = "Select Line Width File (.txt)"
            };

            if (!string.IsNullOrEmpty(LineWidthPathTextBox.Text) && File.Exists(LineWidthPathTextBox.Text))
            {
                dlg.InitialDirectory = Path.GetDirectoryName(LineWidthPathTextBox.Text);
                dlg.FileName = Path.GetFileName(LineWidthPathTextBox.Text);
            }

            bool? result = dlg.ShowDialog(); // For WPF OpenFileDialog
            if (result == true)
            {
                LineWidthPathTextBox.Text = dlg.FileName;
                SaveSettings();
            }
        }
    }
}
