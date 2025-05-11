using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AutocadPlugin
{
    internal static class RoadMarkingCommands
    {
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

        internal static void DrawRoadLine(string lineType)
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Editor editor = document.Editor;

            ObjectId originalLineTypeId = ObjectId.Null;

            CommandEventHandler commandEndHandler = null;

            commandEndHandler = (s, e) =>
            {
                // Unsubscribe from events
                document.CommandEnded -= commandEndHandler;
                document.CommandCancelled -= commandEndHandler;
                document.CommandFailed -= commandEndHandler;

                // Reset the line type
                using (Transaction transaction = document.TransactionManager.StartTransaction())
                {
                    document.Database.Celtype = originalLineTypeId;
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

                    // Store original line type
                    originalLineTypeId = document.Database.Celtype;
                    editor.WriteMessage(originalLineTypeId.ToString());

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
