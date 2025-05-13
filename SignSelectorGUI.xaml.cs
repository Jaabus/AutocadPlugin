using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AutocadPlugin
{
    public partial class SignSelectorGUI : UserControl
    {
        private const double MinScale = 0.001;

        private readonly Regex decimalValidationRegex =
            new Regex(@"^(([0-9]+(\.[0-9]*)?|\.[0-9]+))$", RegexOptions.Compiled);

        public SignSelectorGUI()
        {
            InitializeComponent();

            string signDirectoryPath = AutocadPlugin.Properties.Settings.Default.StreetSignFilePath;

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

            InitializeScaleTextBoxes();
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

        private void GenerateSignReportButton_Click(object sender, RoutedEventArgs e)
        {
            SignCommands.GenerateSignReport();
        }

        private void InitializeScaleTextBoxes()
        {
            // Get the current document and its database
            var document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                // No document is open
                return;
            }
            var db = document.Database;

            // Initialize variables
            string signScale;
            string signPostScale;

            // Retrieve values
            using (Transaction transaction = db.TransactionManager.StartTransaction())
            {
                signScale = CommandUtilites.RetrieveVariable(transaction, db, "signScale");
                signPostScale = CommandUtilites.RetrieveVariable(transaction, db, "signPostScale");

                // If either of the values are null, use the default values

                // Lock the document for editing
                using (DocumentLock docLock = document.LockDocument())
                {
                    if (signScale == null)
                    {
                        signScale = Constants.defaultSignScale;
                    }
                    if (signPostScale == null)
                    {
                        signPostScale = Constants.defaultSignPostScale;
                    }

                    transaction.Commit();
                }
            }

            // Set text boxes' content to match scale settings
            SignScale.Text = signScale;
            SignPostScale.Text = signPostScale;
        }

        private void Scale_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox == null) return;

            // Current text in the TextBox
            string currentText = textBox.Text;

            // Construct the text that would result if the input is accepted
            // This takes into account selected text (which would be replaced)
            string futureText = currentText.Remove(textBox.SelectionStart, textBox.SelectionLength)
                                           .Insert(textBox.SelectionStart, e.Text);

            // Check if the entire future text would match the regex
            if (!decimalValidationRegex.IsMatch(futureText))
            {
                // If it doesn't match, prevent the character from being entered
                e.Handled = true;
            }
        }

        private void Scale_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox == null) return;

            string textBoxName = textBox.Name;

            // If text box is empty, fill in with default value
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                if (textBoxName == "SignScale")
                {
                    textBox.Text = Constants.defaultSignScale;
                }
                if (textBoxName == "SignPostScale")
                {
                    textBox.Text = Constants.defaultSignPostScale;
                }
            }

            // If input is smaller than minimum, fill in with minimum value
            if (Convert.ToDouble(textBox.Text) < MinScale)
            {
                textBox.Text = MinScale.ToString();
            }

            // Save input to document database
            var document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (document == null)
                {
                    // No document is open
                    return;
                }
                var db = document.Database;

                using (Transaction transaction = db.TransactionManager.StartTransaction())
                {
                    using (DocumentLock docLock = document.LockDocument())
                    {
                        CommandUtilites.StoreVariable(transaction, db, textBoxName, textBox.Text);
                    }
                    transaction.Commit();
                }
        }
    }
}

