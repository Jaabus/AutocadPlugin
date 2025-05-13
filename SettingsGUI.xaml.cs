using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace AutocadPlugin
{
    public partial class SettingsGUI : UserControl
    {
        public SettingsGUI()
        {
            InitializeComponent();

            PopulateLanguageComboBox();
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
            // Get the selected language
            ComboBoxItem selectedLanguage = LanguageComboBox.SelectedItem as ComboBoxItem;
            if (selectedLanguage != null)
            {
                string cultureName = selectedLanguage.Tag as string;

                Thread.CurrentThread.CurrentUICulture = new CultureInfo(cultureName);

                // Reset GUI
                Commands.ResetGUI();
            }
        }
    }
}
