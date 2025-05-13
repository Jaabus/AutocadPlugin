using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AutocadPlugin
{
    internal class HatchCommands
    {
        internal static void HatchFromPolyline()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Editor editor = document.Editor;

            bool isPolylineSelected = false;
            Polyline polyline = null;

            using (DocumentLock docLock = document.LockDocument())
            {
                using (Transaction transaction = document.TransactionManager.StartTransaction())
                {
                    BlockTableRecord currentSpace = (BlockTableRecord)transaction.GetObject(document.Database.CurrentSpaceId, OpenMode.ForWrite);

                    // Get the current selection set
                    PromptSelectionResult selectionResult = editor.SelectImplied();
                    if (selectionResult.Status == PromptStatus.OK)
                    {
                        if (selectionResult.Value.Count == 1)
                        {
                            // If only 1 entity is selected, check if the selected object is a Polyline
                            SelectedObject selectedObject = selectionResult.Value[0];

                            Polyline selectedPolyline = transaction.GetObject(selectedObject.ObjectId, OpenMode.ForWrite) as Polyline;
                            if (selectedPolyline != null)
                            {
                                polyline = selectedPolyline;
                                isPolylineSelected = true;
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

                        // Get the selected polyline
                        polyline = transaction.GetObject(entityResult.ObjectId, OpenMode.ForWrite) as Polyline;
                    }

                    // Promt the user to specify offset distance
                    PromptDoubleOptions offsetOptions = new PromptDoubleOptions("\nSpecify hatch width: ")
                    {
                        AllowNegative = false,
                        AllowZero = false,
                        DefaultValue = 1
                    };
                    PromptDoubleResult offsetResult = editor.GetDouble(offsetOptions);
                    if (offsetResult.Status != PromptStatus.OK)
                    {
                        editor.WriteMessage("\nOperation canceled or invalid offset distance.");
                        return;
                    }
                    double offsetDistance = offsetResult.Value / 2;



                    if (polyline == null)
                    {
                        editor.WriteMessage("\nFailed to retrieve the selected polyline.");
                        return;
                    }

                    // Offset the polyline
                    DBObjectCollection positiveOffsetDbObjects = polyline.GetOffsetCurves(offsetDistance);
                    DBObjectCollection negativeOffsetDbObjects = polyline.GetOffsetCurves(-offsetDistance);

                    if (positiveOffsetDbObjects.Count == 0 || negativeOffsetDbObjects.Count == 0)
                    {
                        editor.WriteMessage("\nFailed to create an offset polyline.");
                        return;
                    }

                    Curve positiveCurve = positiveOffsetDbObjects[0] as Curve;
                    Curve negativeCurve = negativeOffsetDbObjects[0] as Curve;

                    // Add the positive offset curve to the database
                    currentSpace.AppendEntity(positiveCurve);
                    transaction.AddNewlyCreatedDBObject(positiveCurve, true);

                    // Reverse the negative curve and add to database
                    negativeCurve.ReverseCurve();

                    currentSpace.AppendEntity(negativeCurve);
                    transaction.AddNewlyCreatedDBObject(negativeCurve, true);

                    // Create capping lines to close the loop between the two offset curves
                    // Cap 1: Connects EndPoint of Positive Curve to StartPoint of Reversed Negative Curve
                    Line capLine1 = new Line(positiveCurve.EndPoint, negativeCurve.StartPoint);
                    // Cap 2: Connects EndPoint of Reversed Negative Curve to StartPoint of Positive Curve
                    Line capLine2 = new Line(negativeCurve.EndPoint, positiveCurve.StartPoint);

                    currentSpace.AppendEntity(capLine1);
                    transaction.AddNewlyCreatedDBObject(capLine1, true);
                    currentSpace.AppendEntity(capLine2);
                    transaction.AddNewlyCreatedDBObject(capLine2, true);

                    // Create an ObjectIdCollection for the hatch boundary
                    ObjectIdCollection boundaryIds = new ObjectIdCollection();
                    boundaryIds.Add(positiveCurve.ObjectId);
                    boundaryIds.Add(capLine1.ObjectId);
                    boundaryIds.Add(negativeCurve.ObjectId);
                    boundaryIds.Add(capLine2.ObjectId);

                    //Create a new hatch object
                    using (Hatch hatch = new Hatch())
                    {
                        hatch.SetDatabaseDefaults();
                        hatch.AppendLoop(HatchLoopTypes.Default, boundaryIds);
                        hatch.EvaluateHatch(true);
                        // Add the hatch to the current space
                        currentSpace.AppendEntity(hatch);
                        transaction.AddNewlyCreatedDBObject(hatch, true);
                    }

                    // Ask if user wishes to keep the original polyline
                    PromptKeywordOptions promptPolylineOptions = new PromptKeywordOptions("\nKeep the original polyline? [Yes/No]: ", "Yes No")
                    {
                        AllowNone = false
                    };
                    PromptResult polylineKeywordResult = editor.GetKeywords(promptPolylineOptions);

                    if (polylineKeywordResult.Status == PromptStatus.OK && polylineKeywordResult.StringResult == "No")
                    {
                        // If the user chooses "No", erase the original polyline
                        polyline.Erase();
                    }

                    // Ask if user wishes to keep the outline polyline
                    PromptKeywordOptions promptOutlineOptions = new PromptKeywordOptions("\nKeep the hatch outline polyline? [Yes/No]: ", "Yes No")
                    {
                        AllowNone = false
                    };
                    PromptResult outlineKeywordResult = editor.GetKeywords(promptOutlineOptions);

                    if (polylineKeywordResult.Status == PromptStatus.OK && outlineKeywordResult.StringResult == "No")
                    {
                        // If the user chooses "No", erase the original polyline
                        positiveCurve.Erase();
                        negativeCurve.Erase();
                        capLine1.Erase();
                        capLine2.Erase();
                    }

                    transaction.Commit();
                }
            }
        }
    }
}

