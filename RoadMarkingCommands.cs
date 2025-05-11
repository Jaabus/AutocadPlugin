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
        private static readonly Dictionary<string, double> lineWidths = new Dictionary<string, double>();

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

            void commandEndHandler(object s, CommandEventArgs e)
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
                    // Enable line type generation
                    polyline.Plinegen = true;

                    transaction.Commit();
                }
            }

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

        internal static void AddDoubleLine(string lineType, double offset)
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Editor editor = document.Editor;

            bool isPolylineSelected = false;
            Polyline originalPolyline = null;

            // Get the current selection set
            PromptSelectionResult selectionResult = editor.SelectImplied();
            if (selectionResult.Status == PromptStatus.OK)
            {
                if (selectionResult.Value.Count == 1)
                {
                    // If only 1 entity is selected, check if the selected object is a Polyline
                    SelectedObject selectedObject = selectionResult.Value[0];

                    using (Transaction transaction = document.TransactionManager.StartTransaction())
                    {
                        Polyline polyline = transaction.GetObject(selectedObject.ObjectId, OpenMode.ForRead) as Polyline;
                        if (polyline != null)
                        {
                            originalPolyline = polyline;
                            isPolylineSelected = true;
                        }
                        transaction.Commit();
                    }
                }
            }

            // If polyline is not already selected, prompt the user to select a single polyline
            if (isPolylineSelected == false)
            {
                PromptEntityOptions entityOptions = new PromptEntityOptions("\nSelect a polyline: ");
                entityOptions.SetRejectMessage("\nOnly polylines are allowed.");
                entityOptions.AddAllowedClass(typeof(Polyline), true);

                PromptEntityResult entityResult = editor.GetEntity(entityOptions);

                if (entityResult.Status != PromptStatus.OK)
                {
                    editor.WriteMessage("\nOperation canceled or invalid selection.");
                    return;
                }

                using (Transaction transaction = document.TransactionManager.StartTransaction())
                {
                    // Get the selected polyline
                    originalPolyline = transaction.GetObject(entityResult.ObjectId, OpenMode.ForRead) as Polyline;
                }
            }

            // Prompt the user to specify the side for the offset
            PromptKeywordOptions sideOptions = new PromptKeywordOptions("\nSpecify the side to offset towards [Left/Right]: ", "Left Right")
            {
                AllowNone = false
            };

            PromptResult sideResult = editor.GetKeywords(sideOptions);
            if (sideResult.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }

            // Determine the offset direction based on the user's input
            offset = sideResult.StringResult.Equals("Left", StringComparison.OrdinalIgnoreCase) ? -offset : offset;

            // Calculate the adjusted line offset to account for line widths
            double adjustedOffset = CalculateAdjustedLineOffset(originalPolyline.Linetype, lineType, offset);

            using (DocumentLock docLock = document.LockDocument())
            {
                using (Transaction transaction = document.TransactionManager.StartTransaction())
                {
                    if (originalPolyline == null)
                    {
                        editor.WriteMessage("\nFailed to retrieve the selected polyline.");
                        return;
                    }

                    // Offset the polyline
                    DBObjectCollection offsetCurves = originalPolyline.GetOffsetCurves(adjustedOffset);

                    if (offsetCurves.Count == 0)
                    {
                        editor.WriteMessage("\nFailed to create an offset polyline.");
                        return;
                    }

                    // Add the offset polyline to the drawing
                    BlockTable blockTable = transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord blockTableRecord = transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    foreach (DBObject obj in offsetCurves)
                    {
                        if (obj is Polyline offsetPolyline)
                        {
                            // Add the offset polyline to the model space
                            blockTableRecord.AppendEntity(offsetPolyline);
                            transaction.AddNewlyCreatedDBObject(offsetPolyline, true);

                            // Apply the specified linetype to the offset polyline
                            LinetypeTable linetypeTable = transaction.GetObject(document.Database.LinetypeTableId, OpenMode.ForRead) as LinetypeTable;

                            if (linetypeTable.Has(lineType))
                            {
                                offsetPolyline.Linetype = lineType;
                            }
                            else
                            {
                                editor.WriteMessage($"\nLinetype '{lineType}' is not loaded. Please load it first.");
                            }

                            // Check if the line type has a width and apply it
                            if (lineWidths.TryGetValue(lineType, out double width))
                            {
                                for (int i = 0; i < offsetPolyline.NumberOfVertices; i++)
                                {
                                    offsetPolyline.SetStartWidthAt(i, width);
                                    offsetPolyline.SetEndWidthAt(i, width);
                                }
                            }

                            // Enable line type generation
                            offsetPolyline.Plinegen = true;
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        internal static double CalculateAdjustedLineOffset(string lineType1, string lineType2, double targetOffset)
        {
            // Check if the given linetypes have widths in the lineWidths dictionary
            bool hasWidth1 = lineWidths.TryGetValue(lineType1, out double width1);
            bool hasWidth2 = lineWidths.TryGetValue(lineType2, out double width2);

            // If neither linetype has a width, return the target offset
            if (!hasWidth1 && !hasWidth2)
            {
                return targetOffset;
            }

            // If either or both linetypes have widths, calculate the ideal offset
            double combinedWidth = (hasWidth1 ? width1 : 0) + (hasWidth2 ? width2 : 0);
            double adjustedOffset = targetOffset + (Math.Sign(targetOffset) * (combinedWidth / 2));

            return adjustedOffset;
        }

    }
}
