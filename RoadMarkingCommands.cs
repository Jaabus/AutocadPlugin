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
            Editor ed = document.Editor;

            using (DocumentLock docLock = document.LockDocument())
            {
                using (Transaction transaction = document.TransactionManager.StartTransaction())
                {
                    // Get modelspace BlockTableRecord
                    BlockTable blockTable = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Try to retrieve the text style; if the specified font is not found, default to the current style.
                    TextStyleTable textStyleTable = (TextStyleTable)transaction.GetObject(document.Database.TextStyleTableId, OpenMode.ForRead);
                    ObjectId textStyleId = document.Database.Textstyle;
                    if (textStyleTable.Has(font))
                    {
                        textStyleId = textStyleTable[font];
                    }

                    // Calculate total length of polyline and initialize interval
                    double totalLength = polyline.Length;
                    double nextIntervalDistance = interval;

                    // Calculate total number of intervals
                    int totalIntervals = (int)(totalLength / interval);
                    if (totalIntervals == 0)
                    {
                        ed.WriteMessage("\nThe polyline is too short for the specified interval.");
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
                            return;
                        }
                    }

                    // Loop from the first interval until the end of the polyline
                    while (nextIntervalDistance <= totalLength)
                    {
                        double cumulativeDistance = 0;
                        // Iterate through each segment of the polyline
                        for (int i = 0; i < polyline.NumberOfVertices - 1; i++)
                        {
                            // Calculate current segment length
                            Point3d pt1 = polyline.GetPoint3dAt(i);
                            Point3d pt2 = polyline.GetPoint3dAt(i + 1);
                            double segLength = pt1.DistanceTo(pt2);
                            double segmentArcLength = segLength; // default for straight segments

                            // Check if the segment is an arc (nonzero bulge)
                            double bulge = polyline.GetBulgeAt(i);
                            if (bulge != 0)
                            {
                                // Compute arc properties.
                                // The arc angle is 4 * atan(bulge)
                                double arcAngle = 4 * Math.Atan(bulge);
                                // The chord length is the straight segment length between pt1 and pt2.
                                double chordLength = segLength;
                                // Compute the radius of the arc.
                                double radius = chordLength / (2 * Math.Sin(Math.Abs(arcAngle) / 2));
                                // Total arc length is |arcAngle| * radius.
                                segmentArcLength = Math.Abs(arcAngle) * Math.Abs(radius);
                            }

                            // Check if next interval is on the current segment/arc
                            if (cumulativeDistance + segmentArcLength >= nextIntervalDistance)
                            {
                                Point3d position;
                                Vector3d tangent;

                                if (bulge == 0)
                                {
                                    // Linear interpolation for straight segment.
                                    double fraction = (nextIntervalDistance - cumulativeDistance) / segLength;
                                    position = new Point3d(
                                        pt1.X + (pt2.X - pt1.X) * fraction,
                                        pt1.Y + (pt2.Y - pt1.Y) * fraction,
                                        pt1.Z + (pt2.Z - pt1.Z) * fraction
                                    );
                                    tangent = (pt2 - pt1).GetNormal();
                                }
                                else
                                {
                                    // Interpolate along the arc.
                                    double arcAngle = 4 * Math.Atan(bulge);
                                    double chordLength = pt1.DistanceTo(pt2);
                                    // Compute radius 
                                    double radius = chordLength / (2 * Math.Sin(Math.Abs(arcAngle) / 2));
                                    // Calculate chord mid-point.
                                    Point3d midPoint = new Point3d(
                                        (pt1.X + pt2.X) / 2,
                                        (pt1.Y + pt2.Y) / 2,
                                        (pt1.Z + pt2.Z) / 2
                                    );
                                    // Determine the perpendicular direction (from pt1 to pt2) for finding the arc center.
                                    Vector3d chordDir = (pt2 - pt1).GetNormal();
                                    Vector3d perp;
                                    // According to AutoCAD conventions, a positive bulge means the arc is drawn counterclockwise from pt1 to pt2.
                                    perp = bulge > 0 ? chordDir.RotateBy(Math.PI / 2, Vector3d.ZAxis) : chordDir.RotateBy(-Math.PI / 2, Vector3d.ZAxis);
                                    // Distance from chord midpoint to arc center.
                                    double centerDist = (chordLength / 2) / Math.Tan(Math.Abs(arcAngle) / 2);
                                    Point3d center = midPoint + perp * centerDist;

                                    // Determine the fraction along the arc.
                                    double fraction = (nextIntervalDistance - cumulativeDistance) / (Math.Abs(arcAngle) * Math.Abs(radius));
                                    // Starting angle (from center to pt1).
                                    double startAngle = new Vector2d(pt1.X - center.X, pt1.Y - center.Y).Angle;
                                    // Target angle along the arc.
                                    double targetAngle = startAngle + fraction * arcAngle;
                                    // Interpolate Z linearly.
                                    double z = pt1.Z + fraction * (pt2.Z - pt1.Z);
                                    // Compute the interpolated point along the arc.
                                    position = new Point3d(
                                        center.X + Math.Abs(radius) * Math.Cos(targetAngle),
                                        center.Y + Math.Abs(radius) * Math.Sin(targetAngle),
                                        z
                                    );
                                    // Compute the tangent at the interpolated point.
                                    tangent = new Vector3d(-Math.Sin(targetAngle), Math.Cos(targetAngle), 0);
                                }

                                // For both arc and straight segment, offset the computed position perpendicularly
                                double offsetAngle = side.Equals("Left", StringComparison.OrdinalIgnoreCase) ? Math.PI / 2 : -Math.PI / 2;
                                Vector3d offsetDir = tangent.RotateBy(offsetAngle, Vector3d.ZAxis);
                                Point3d textPosition = position + (offsetDir * offsetDistance);

                                // Create the DBText entity for the annotation
                                DBText lineTypeText = new DBText
                                {
                                    Position = textPosition,
                                    TextString = $"{text}",
                                    Height = textHeight,
                                    TextStyleId = textStyleId
                                };

                                modelSpace.AppendEntity(lineTypeText);
                                transaction.AddNewlyCreatedDBObject(lineTypeText, true);

                                // Interval found; break out of the segment loop.
                                break;
                            }
                            cumulativeDistance += segmentArcLength;
                        }
                        nextIntervalDistance += interval;
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
