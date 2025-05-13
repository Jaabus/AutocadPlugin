using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Internal;
using Autodesk.AutoCAD.Geometry;

namespace AutocadPlugin
{
    internal static class RoadMarkingCommands
    {
        private static readonly Dictionary<string, double> lineWidths = new Dictionary<string, double>();
        internal static bool AnnotateRoadLines { get; set; } = false;

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

                    if (AnnotateRoadLines)
                    {
                        // Get number of road line from lineType ("number_name")
                        string[] parts = lineType.Split(new char[] { '_' }, 2);
                        string roadLineNumber = parts.Length > 0 ? parts[0] : string.Empty;

                        AnnotatePolyline(polyline, roadLineNumber, 30, "STANDARD", .5, .5, "Left");
                    }

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

        internal static void AddOffsetLine(string lineType, double offset)
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

        internal static void AnnotatePolyline(Polyline polyline, string text, double interval, string font, double textHeight, double offsetDistance, string side)
        {
            if (polyline == null)
                throw new ArgumentNullException(nameof(polyline));

            Document document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return; // Or throw exception
            Editor ed = document.Editor;
            Database db = document.Database;

            using (DocumentLock docLock = document.LockDocument())
            {
                using (Transaction transaction = db.TransactionManager.StartTransaction())
                {
                    BlockTable blockTable = (BlockTable)transaction.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    TextStyleTable textStyleTable = (TextStyleTable)transaction.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    ObjectId textStyleId = db.Textstyle; // Default to current style
                    if (textStyleTable.Has(font))
                    {
                        textStyleId = textStyleTable[font];
                    }
                    else
                    {
                        ed.WriteMessage($"\nFont style '{font}' not found. Using current style.");
                    }

                    double totalLength = polyline.Length;
                    if (totalLength < interval || interval <= 0) // Added check for interval <= 0
                    {
                        ed.WriteMessage("\nThe polyline is too short for the specified interval, or interval is invalid.");
                        transaction.Abort(); // Abort transaction as no changes will be made
                        return;
                    }

                    double nextIntervalDistance = interval;
                    int totalIntervals = (int)(totalLength / interval);

                    if (totalIntervals == 0 && totalLength >= interval) // handles case where only one label might be placed if totalLength is slightly >= interval
                    {
                        totalIntervals = 1;
                    }
                    else if (totalIntervals == 0)
                    {
                        ed.WriteMessage("\nThe polyline is too short for any interval markings.");
                        transaction.Abort();
                        return;
                    }


                    if (totalIntervals > 1000)
                    {
                        PromptKeywordOptions confirmOptions = new PromptKeywordOptions("\nMore than 1000 numbers will be drawn. Continue? [Yes/No]: ", "Yes No")
                        {
                            AllowNone = false
                        };
                        PromptResult confirmResult = ed.GetKeywords(confirmOptions);
                        if (confirmResult.Status != PromptStatus.OK || confirmResult.StringResult.Equals("No", StringComparison.OrdinalIgnoreCase))
                        {
                            ed.WriteMessage("\nOperation cancelled.");
                            transaction.Abort();
                            return;
                        }
                    }

                    for (int k = 0; k < totalIntervals; k++, nextIntervalDistance += interval)
                    {
                        // It's possible nextIntervalDistance might slightly exceed totalLength due to floating point arithmetic,
                        // especially for the last interval. Clamp it to totalLength if that's the case,
                        // or ensure the loop condition handles it (e.g. k < totalIntervals or nextIntervalDistance <= totalLength + tolerance)
                        if (nextIntervalDistance > totalLength + 1e-6) // Add small tolerance
                            break;


                        // The GetPointAtDist and GetFirstDerivative methods are simpler for getting point and tangent
                        Point3d position = polyline.GetPointAtDist(nextIntervalDistance);
                        Vector3d tangent3d = polyline.GetFirstDerivative(polyline.GetParameterAtDistance(nextIntervalDistance));

                        // We need the tangent in the XY plane for text rotation and 2D offset
                        Vector3d tangent = new Vector3d(tangent3d.X, tangent3d.Y, 0.0).GetNormal();

                        // If tangent has zero length in XY (e.g., vertical line segment in 3D polyline),
                        // Atan2(0,0) is 0. Default to X-axis direction for such cases if needed,
                        // but GetFirstDerivative should give a non-zero vector if the point is on the curve.
                        // If the polyline segment itself is vertical (dx=0, dy=0, dz!=0), tangent.X and tangent.Y will be 0.
                        // Rotation will be 0, which is standard for text on vertical lines (horizontal text).
                        if (tangent.IsZeroLength()) // Should not happen if point is valid on curve
                        {
                            // Fallback or skip, though GetFirstDerivative should be valid
                            // For a purely vertical segment (in Z), tangent X and Y would be 0.
                            // Defaulting to a horizontal tangent for rotation purposes:
                            if (polyline.NumberOfVertices > 1)
                            {
                                Point3d ptStart = polyline.StartPoint;
                                Point3d ptNext = polyline.GetPoint3dAt(1);
                                if (Math.Abs(ptStart.X - ptNext.X) < 1e-9 && Math.Abs(ptStart.Y - ptNext.Y) < 1e-9)
                                {
                                    tangent = Vector3d.XAxis; // Default for purely Z-axis lines
                                }
                                else // Use overall polyline direction if specific segment is vertical
                                {
                                    tangent = (polyline.EndPoint - polyline.StartPoint);
                                    if (tangent.Length > 1e-9)
                                    {
                                        tangent = new Vector3d(tangent.X, tangent.Y, 0.0).GetNormal();
                                        if (tangent.IsZeroLength()) tangent = Vector3d.XAxis;
                                    }
                                    else
                                    {
                                        tangent = Vector3d.XAxis;
                                    }
                                }
                            }
                            else
                            {
                                tangent = Vector3d.XAxis; // Default tangent if polyline is just a point
                            }
                        }

                        double rotationAngle = Math.Atan2(tangent.Y, tangent.X);

                        // Normalize angle to [0, 2*PI)
                        double normalizedAngle = rotationAngle;
                        while (normalizedAngle < 0)
                        {
                            normalizedAngle += 2 * Math.PI;
                        }
                        while (normalizedAngle >= 2 * Math.PI)
                        {
                            normalizedAngle -= 2 * Math.PI;
                        }

                        // Define the bounds for flipping (180 +/- 60 degrees, so 120 to 240 degrees)
                        // (PI +/- PI/3) which is (2*PI/3) to (4*PI/3)
                        double lowerFlipBound = (2.0 * Math.PI) / 3.0; // 120 degrees
                        double upperFlipBound = (4.0 * Math.PI) / 3.0; // 240 degrees

                        // If the text is oriented in the "upside down" range, flip it by 180 degrees (PI radians)
                        if (normalizedAngle > lowerFlipBound && normalizedAngle < upperFlipBound)
                        {
                            rotationAngle += Math.PI;
                        }

                        double offsetAngle = side.Equals("Left", StringComparison.OrdinalIgnoreCase) ? Math.PI / 2 : -Math.PI / 2;
                        // Offset direction should be perpendicular to the XY tangent, in the XY plane.
                        Vector3d offsetDir = tangent.RotateBy(offsetAngle, Vector3d.ZAxis); // ZAxis ensures XY plane rotation

                        // Text position is offset from the 3D point on the polyline, in the XY direction relative to the point
                        Point3d textPosition = position + offsetDir * offsetDistance;


                        DBText lineTypeText = new DBText
                        {
                            Position = textPosition,
                            TextString = text,
                            Height = textHeight,
                            TextStyleId = textStyleId,
                            Rotation = rotationAngle,
                            HorizontalMode = TextHorizontalMode.TextCenter, // Optional: Center text on position
                            VerticalMode = TextVerticalMode.TextVerticalMid,   // Optional: Center text on position
                            AlignmentPoint = textPosition // Optional: Use with Horizontal/Vertical mode
                        };
                        // If using AlignmentPoint, Position should be set to AlignmentPoint OR (0,0,0) if text is in its own UCS.
                        // For simplicity, if centering, set Position = textPosition and also AlignmentPoint = textPosition.

                        modelSpace.AppendEntity(lineTypeText);
                        transaction.AddNewlyCreatedDBObject(lineTypeText, true);
                    }
                    transaction.Commit();
                }
            }
        }

        internal static void ConvertPolylineToRoadMarking(string roadMarkingType)
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Editor editor = document.Editor;
            Polyline polyline = null;

            // Try to get a polyline from the current implied selection
            PromptSelectionResult selectionResult = editor.SelectImplied();
            if (selectionResult.Status == PromptStatus.OK && selectionResult.Value.Count == 1)
            {
                using (Transaction transaction = document.TransactionManager.StartTransaction())
                {
                    Entity ent = transaction.GetObject(selectionResult.Value[0].ObjectId, OpenMode.ForRead) as Entity;
                    polyline = ent as Polyline;
                    transaction.Commit();
                }
            }

            // If no polyline was found, prompt the user to select one.
            if (polyline == null)
            {
                PromptEntityOptions entityOptions = new PromptEntityOptions("\nSelect a polyline to convert: ");
                entityOptions.SetRejectMessage("\nOnly polylines are allowed.");
                entityOptions.AddAllowedClass(typeof(Polyline), true);

                PromptEntityResult entityResult = editor.GetEntity(entityOptions);
                if (entityResult.Status != PromptStatus.OK)
                {
                    editor.WriteMessage("\nOperation canceled.");
                    return;
                }

                using (Transaction transaction = document.TransactionManager.StartTransaction())
                {
                    polyline = transaction.GetObject(entityResult.ObjectId, OpenMode.ForRead) as Polyline;
                    transaction.Commit();
                }
            }

            // Check if the specified road marking linetype is loaded.
            using (Transaction transaction = document.TransactionManager.StartTransaction())
            {
                LinetypeTable linetypeTable = (LinetypeTable)transaction.GetObject(document.Database.LinetypeTableId, OpenMode.ForRead);
                if (!linetypeTable.Has(roadMarkingType))
                {
                    editor.WriteMessage($"\nLinetype '{roadMarkingType}' is not loaded. Please load it first.");
                    return;
                }
                transaction.Commit();
            }

            // Convert the polyline by setting its linetype and width.
            using (DocumentLock docLock = document.LockDocument())
            {
                using (Transaction transaction = document.TransactionManager.StartTransaction())
                {
                    Polyline pline = transaction.GetObject(polyline.ObjectId, OpenMode.ForWrite) as Polyline;
                    pline.Linetype = roadMarkingType;

                    // Apply the specified width if available; if not, apply a width of 0.
                    double appliedWidth = lineWidths.TryGetValue(roadMarkingType, out double width) ? width : 0;
                    for (int i = 0; i < pline.NumberOfVertices; i++)
                    {
                        pline.SetStartWidthAt(i, appliedWidth);
                        pline.SetEndWidthAt(i, appliedWidth);
                    }
                    // Enable linetype generation.
                    pline.Plinegen = true;

                    transaction.Commit();
                }
            }

            editor.WriteMessage("\nConverted polyline to road marking line.");
        }
    }
}
