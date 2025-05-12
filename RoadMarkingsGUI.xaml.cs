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

            // 2. Prompt for the display text.
            PromptStringOptions psoText = new PromptStringOptions("\nEnter display text: ");
            psoText.AllowSpaces = true;
            PromptResult textRes = editor.GetString(psoText);
            if (textRes.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }
            string displayText = textRes.StringResult;

            // 3. Prompt for the length interval.
            PromptDoubleOptions pdoInterval = new PromptDoubleOptions("\nEnter length interval (e.g., 20): ");
            pdoInterval.DefaultValue = 20.0;
            PromptDoubleResult intervalRes = editor.GetDouble(pdoInterval);
            if (intervalRes.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }
            double interval = intervalRes.Value;

            // 5. Prompt for the text height.
            PromptDoubleOptions pdoTextHeight = new PromptDoubleOptions("\nEnter text height: ");
            pdoTextHeight.DefaultValue = 5.0;
            PromptDoubleResult textHeightRes = editor.GetDouble(pdoTextHeight);
            if (textHeightRes.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }
            double textHeight = textHeightRes.Value;

            // 6. Prompt for the offset distance.
            PromptDoubleOptions pdoOffset = new PromptDoubleOptions("\nEnter offset distance: ");
            pdoOffset.DefaultValue = 1.0;
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
            RoadMarkingCommands.AnnotatePolyline(polyline, displayText, interval, "Arial", textHeight, offsetDistance, side);
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
