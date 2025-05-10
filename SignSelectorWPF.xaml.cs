using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AutocadPlugin
{
    public partial class SignSelectorWPF : UserControl
    {
        public SignSelectorWPF()
        {
            InitializeComponent();

            // Set the initial directory path, later i should get it from the user
            string signDirectoryPath = @"C:\Users\JAABUK\Desktop\prog\EESTI";

            // Check if the directory exists
            if (Directory.Exists(signDirectoryPath))
            {
                // Populate the ComboBox with folder names
                PopulateSignDirectoryComboBox(signDirectoryPath);
            }
            else
            {
                MessageBox.Show($"The directory {signDirectoryPath} does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PopulateSignDirectoryComboBox(string signDirectoryPath)
        {
            SignDirectoryComboBox.Items.Clear();

            // Get all folders in the specified directory
            string[] folderNames = Directory.GetDirectories(signDirectoryPath);
            foreach (string folderPath in folderNames)
            {
                ComboBoxItem folderItem = new ComboBoxItem
                {
                    Content = Path.GetFileName(folderPath), // Display just the folder name
                    Tag = folderPath // Store the full path in the Tag property
                };
                SignDirectoryComboBox.Items.Add(folderItem);
            }
        }

        private void SignDirectoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Clear the second ComboBox
            SignsComboBox.Items.Clear();

            // Get the selected folder from the first ComboBox
            ComboBoxItem selectedItem = SignDirectoryComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
            {
                string selectedFolder = selectedItem.Tag as string; // Retrieve the full path from the Tag

                if (!string.IsNullOrEmpty(selectedFolder) && Directory.Exists(selectedFolder))
                {
                    // Get all .dwg files in the selected folder
                    string[] dwgFiles = Directory.GetFiles(selectedFolder, "*.dwg");
                    foreach (string file in dwgFiles)
                    {
                        ComboBoxItem signItem = new ComboBoxItem
                        {
                            Content = Path.GetFileName(file), // Display just the file name
                            Tag = file // Store the full path in the Tag property
                        };
                        SignsComboBox.Items.Add(signItem);
                    }
                }
            }
        }

        private void InsertSignButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the selected sign from the second ComboBox
            ComboBoxItem selectedSignItem = SignsComboBox.SelectedItem as ComboBoxItem;
            if (selectedSignItem != null)
            {
                string selectedSignPath = selectedSignItem.Tag as string; // Retrieve the full path from the Tag
                if (!string.IsNullOrEmpty(selectedSignPath) && File.Exists(selectedSignPath))
                {
                    // Call the InsertSign method from Commands class
                    SignCommands.InsertSignButton(selectedSignPath);
                }
                else
                {
                    MessageBox.Show($"The file {selectedSignPath} does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}