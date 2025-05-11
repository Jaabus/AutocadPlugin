using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Internal;

namespace AutocadPlugin
{
    internal static class RoadMarkingCommands
    {
        private static Dictionary<string, double> lineWidths = new Dictionary<string, double>();

        internal static void LoadLinetypes(string linFilePath)
        {
            Document document = Application.DocumentManager.MdiActiveDocument;

            // Step 1: Read all line type names from the .lin file
            List<string> lineTypeNames = new List<string>();
            foreach (var line in System.IO.File.ReadLines(linFilePath))
            {
                if (line.StartsWith("*")) // Line type definition starts with "*"
                {
                    int commaIndex = line.IndexOf(',');
                    if (commaIndex > 1) // Ensure there's a valid name before the comma
                    {
                        string lineTypeName = line.Substring(1, commaIndex - 1).Trim();
                        lineTypeNames.Add(lineTypeName);
                    }
                }
            }

            using (Transaction transaction = document.TransactionManager.StartTransaction())
            {
                using (DocumentLock docLock = document.LockDocument())
                {
                    // Step 2: Access the LinetypeTable
                    LinetypeTable linetypeTable = (LinetypeTable)transaction.GetObject(document.Database.LinetypeTableId, OpenMode.ForRead);

                    foreach (string lineTypeName in lineTypeNames)
                    {
                        // Step 3: Check if the line type is already loaded
                        if (!linetypeTable.Has(lineTypeName))
                        {
                            // Step 4: Load the line type from the .lin file
                            document.Database.LoadLineTypeFile(lineTypeName, linFilePath);
                        }
                    }
                }
                transaction.Commit();
            }
        }

        internal static void LoadLineWidths(string WidthsFilePath)
        {
            // Step 1: Read all line width values from the .txt file
            foreach (var line in System.IO.File.ReadLines(WidthsFilePath))
            {
                if (line.StartsWith("*")) // Line width definition starts with "*"
                {
                    int commaIndex = line.IndexOf(',');
                    if (commaIndex > 1) // Ensure there's a valid name before the comma
                    {
                        string lineTypeName = line.Substring(1, commaIndex - 1).Trim();
                        if (double.TryParse(line.Substring(commaIndex + 1).Trim(), out double thickness))
                        {
                            lineWidths[lineTypeName] = thickness;
                        }
                    }
                }
            }
        }

        internal static void DrawRoadLine(string lineType)
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Editor editor = document.Editor;

            // Store original line type to restore after line is drawn
            ObjectId originalLineTypeId = document.Database.Celtype;

            CommandEventHandler commandEndHandler = null;

            commandEndHandler = (s, e) =>
            {
                // Unsubscribe from events
                document.CommandEnded -= commandEndHandler;
                document.CommandCancelled -= commandEndHandler;
                document.CommandFailed -= commandEndHandler;

                // Reset the line type and apply line width
                using (Transaction transaction = document.TransactionManager.StartTransaction())
                {
                    document.Database.Celtype = originalLineTypeId;

                    // Retrieve the last drawn polyline
                    ObjectId polyLineId = Utils.EntLast();

                    Polyline polyline = (Polyline)transaction.GetObject(polyLineId, OpenMode.ForWrite);

                    // Check if the line type has a width and apply it
                    if (lineWidths.TryGetValue(lineType, out double width))
                    {
                        for (int i = 0; i < polyline.NumberOfVertices; i++)
                        {
                            polyline.SetStartWidthAt(i, width);
                            polyline.SetEndWidthAt(i, width);
                        }
                    }

                    transaction.Commit();
                }
            };

            using (DocumentLock docLock = document.LockDocument())
            {
                using (Transaction transaction = document.TransactionManager.StartTransaction())
                {
                    LinetypeTable linetypeTable = (LinetypeTable)transaction.GetObject(document.Database.LinetypeTableId, OpenMode.ForRead);

                    // Check if the line type is loaded
                    if (!linetypeTable.Has(lineType))
                    {
                        editor.WriteMessage($"\nLine type '{lineType}' is not loaded. Please load it first.");
                        return;
                    }

                    // Set the current line type to the specified line type
                    ObjectId lineTypeId = linetypeTable[lineType];
                    document.Database.Celtype = lineTypeId;

                    transaction.Commit();
                }

                // Subscribe to command end events
                document.CommandEnded += commandEndHandler;
                document.CommandCancelled += commandEndHandler;
                document.CommandFailed += commandEndHandler;

                // Invoke the built-in PLINE command
                document.SendStringToExecute("_PLINE ", true, false, true);
            }
        }
    }
}
