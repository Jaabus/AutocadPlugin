using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AutocadPlugin
{
    public partial class RoadMarkingsGUI : UserControl
    {
        public RoadMarkingsGUI()
        {
            InitializeComponent();

            string lineTypeFilePath = AutocadPlugin.Properties.Settings.Default.LineTypeFilePath;
            string lineWidthFilePath = AutocadPlugin.Properties.Settings.Default.LineWidthFilePath;

            RoadMarkingCommands.LoadLinetypes(lineTypeFilePath);

            RoadMarkingCommands.LoadLineWidths(lineWidthFilePath);

            PopulateLineTypeComboBox(lineTypeFilePath);
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

        private void ConvertLineButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the selected sign from the line type combo box
            string selectedLineType = (string)LineTypeComboBox.SelectedItem;
            if (selectedLineType != null)
            {
                RoadMarkingCommands.ConvertPolylineToRoadMarking(selectedLineType);
            }
        }

        private void AddOffsetLineButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the selected sign from the line type combo box
            string selectedLineType = (string)LineTypeComboBox.SelectedItem;
            if (selectedLineType != null)
            {
                RoadMarkingCommands.AddOffsetLine(selectedLineType, 0.05);
            }
        }

        private void AnnotatePolylineButton_Click(object sender, RoutedEventArgs e)
        {
            Document document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor editor = document.Editor;

            // 1. Prompt the user to select a polyline.
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect a polyline: ");
            peo.SetRejectMessage("\nOnly polylines are allowed.");
            peo.AddAllowedClass(typeof(Polyline), true);
            PromptEntityResult per = editor.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }

            // Retrieve the selected polyline.
            Polyline polyline = null;
            using (Transaction transaction = document.Database.TransactionManager.StartTransaction())
            {
                polyline = transaction.GetObject(per.ObjectId, OpenMode.ForRead) as Polyline;
                transaction.Commit();
            }
            if (polyline == null)
            {
                editor.WriteMessage("\nThe selected entity is not a valid polyline.");
                return;
            }

            // 2. Promt whether user wishes to specify settings
            PromptKeywordOptions pkoSettings = new PromptKeywordOptions("\nSpecify settings [Yes/No]: ");
            pkoSettings.Keywords.Add("Yes");
            pkoSettings.Keywords.Add("No");
            pkoSettings.Keywords.Default = ("No");
            PromptResult settingsRes = editor.GetKeywords(pkoSettings);
            if (settingsRes.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }
            string settings = settingsRes.StringResult;

            if (settings == "No")
            {
                // Try to get linetype number from the polyline's layer name (format: number_name)
                string displayNumber = null;
                string sourceName = polyline.Linetype;

                if (!string.IsNullOrEmpty(sourceName))
                {
                    var parts = sourceName.Split('_');

                    // Ensure there's at least one part before an underscore and that part is not empty.
                    if (parts.Length > 1 && !string.IsNullOrEmpty(parts[0]))
                    {
                        string firstPart = parts[0]; // This is the potential number or number-letter part

                        // Case 1: The first part is purely a number (e.g., "123")
                        if (int.TryParse(firstPart, out _))
                        {
                            displayNumber = firstPart;
                        }
                        // Case 2: The first part could be a number followed by a single letter (e.g., "523a")
                        // This part must be at least two characters long (e.g., "1a", not just "a").
                        else if (firstPart.Length > 1)
                        {
                            char lastChar = firstPart[firstPart.Length - 1];
                            // Get the segment of the string before the last character
                            string potentialNumberSegment = firstPart.Substring(0, firstPart.Length - 1);

                            // Check if the last character is a letter AND the segment before it is a valid integer
                            if (char.IsLetter(lastChar) && int.TryParse(potentialNumberSegment, out _))
                            {
                                displayNumber = firstPart;
                            }
                        }
                    }
                }

                // If no number found, prompt the user for display text
                if (string.IsNullOrEmpty(displayNumber))
                {
                    PromptStringOptions psoNumber = new PromptStringOptions("\nEnter display text: ");
                    psoNumber.AllowSpaces = true;
                    PromptResult numberRes = editor.GetString(psoNumber);
                    if (numberRes.Status != PromptStatus.OK)
                    {
                        editor.WriteMessage("\nOperation canceled.");
                        return;
                    }
                    displayNumber = numberRes.StringResult;
                }

                // Call AnnotatePolyline with default settings
                RoadMarkingCommands.AnnotatePolyline(polyline, displayNumber, 30, "STANDARD", 0.5, 0.5, "Right");
                return;
            }

            // 3. Prompt for the display text.
            PromptStringOptions psoText = new PromptStringOptions("\nEnter display text: ");
            psoText.AllowSpaces = true;
            PromptResult textRes = editor.GetString(psoText);
            if (textRes.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }
            string displayText = textRes.StringResult;

            // 4. Prompt for the length interval.
            PromptDoubleOptions pdoInterval = new PromptDoubleOptions("\nEnter length interval (e.g., 20): ");
            pdoInterval.DefaultValue = 30.0;
            PromptDoubleResult intervalRes = editor.GetDouble(pdoInterval);
            if (intervalRes.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }
            double interval = intervalRes.Value;

            // 5. Prompt for the text height.
            PromptDoubleOptions pdoTextHeight = new PromptDoubleOptions("\nEnter text height: ");
            pdoTextHeight.DefaultValue = 0.5;
            PromptDoubleResult textHeightRes = editor.GetDouble(pdoTextHeight);
            if (textHeightRes.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }
            double textHeight = textHeightRes.Value;

            // 6. Prompt for the offset distance.
            PromptDoubleOptions pdoOffset = new PromptDoubleOptions("\nEnter offset distance: ");
            pdoOffset.DefaultValue = 0.5;
            PromptDoubleResult offsetRes = editor.GetDouble(pdoOffset);
            if (offsetRes.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }
            double offsetDistance = offsetRes.Value;

            // 7. Prompt for the side (Left/Right)
            PromptKeywordOptions pkoSide = new PromptKeywordOptions("\nSpecify side for annotation [Left/Right]: ");
            pkoSide.Keywords.Add("Left");
            pkoSide.Keywords.Add("Right");
            pkoSide.AllowNone = false;
            PromptResult sideRes = editor.GetKeywords(pkoSide);
            if (sideRes.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }
            string side = sideRes.StringResult;

            // Finally, call the AnnotatePolyline function with the gathered parameters.
            RoadMarkingCommands.AnnotatePolyline(polyline, displayText, interval, "STANDARD", textHeight, offsetDistance, side);
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

        private void AnnotateRoadLinesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            RoadMarkingCommands.AnnotateRoadLines = true;
        }

        private void AnnotateRoadLinesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            RoadMarkingCommands.AnnotateRoadLines = false;
        }
    }
}
