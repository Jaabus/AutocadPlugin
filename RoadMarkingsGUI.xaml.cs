using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AutocadPlugin.Properties;

namespace AutocadPlugin
{
    public partial class RoadMarkingsGUI : UserControl
    {
        public RoadMarkingsGUI()
        {
            InitializeComponent();

            RoadMarkingCommands.LoadLinetypes(@"C:\Users\JAABUK\Desktop\prog\EESTI\jooned.lin");

            RoadMarkingCommands.LoadLineWidths(@"C:\Users\JAABUK\Desktop\prog\EESTI\jooned_paksused.txt");

            PopulateLineTypeComboBox(@"C:\Users\JAABUK\Desktop\prog\EESTI\jooned.lin");
        }

        private void DrawLineButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the selected sign from the line type combo box
            string selectedLineType = (string)LineTypeComboBox.SelectedItem;
            if (selectedLineType != null)
            {
                RoadMarkingCommands.DrawRoadLine(selectedLineType);
            }
        }

        private void AddDoubleLineButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the selected sign from the line type combo box
            string selectedLineType = (string)LineTypeComboBox.SelectedItem;
            if (selectedLineType != null)
            {
                RoadMarkingCommands.AddDoubleLine(selectedLineType, 0.05);
            }
        }

        private void PopulateLineTypeComboBox(string lineTypeFilePath)
        {
            LineTypeComboBox.Items.Clear();

            // Get all line type names from the .lin file
            foreach (var line in System.IO.File.ReadLines(lineTypeFilePath))
            {
                if (line.StartsWith("*")) // Line type definition starts with "*"
                {
                    int commaIndex = line.IndexOf(',');
                    if (commaIndex > 1) // Ensure there's a valid name before the comma
                    {
                        string lineTypeName = line.Substring(1, commaIndex - 1).Trim();
                        LineTypeComboBox.Items.Add(lineTypeName);
                    }
                }
            }
        }
    }
}
