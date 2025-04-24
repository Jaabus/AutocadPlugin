using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms.Integration;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using WF = System.Windows.Forms;

namespace AutocadPlugin
{
    public class Commands
    {
        [CommandMethod("PS_Hello")]
        public void Hello()
        {
            var document =
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var editor = document.Editor;
            editor.WriteMessage("\nHello World!");
        }

        private PaletteSet _paletteSet;

        [CommandMethod("PS_DrawGUI")]
        public void DrawGUI()
        {
            if (_paletteSet == null)
            {
                // Create a new PaletteSet
                _paletteSet = new PaletteSet("MainPalette")
                {
                    Size = new System.Drawing.Size(300, 200)
                };

                // Add control to PaletteSet
                var signSelector = new SignSelectorWPF();
                var host = new ElementHost
                {
                    Dock = WF.DockStyle.Fill,
                    Child = signSelector
                };

                _paletteSet.Add("Sign Selector", host);
            }

            // Show the PaletteSet
            _paletteSet.Visible = true;
        }

        [CommandMethod("PS_InsertSign")]
        public void InsertSign(string signPath)
        {
            // Get the current document and its database
            var document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var editor = document.Editor;
            var targetDb = document.Database;

            // Lock the document for editing
            using (DocumentLock docLock = document.LockDocument())
            {
                // Check if a sign post block is selected; if not, create a new post
                ObjectId? selectedSignPost = GetSelectedSignPost(editor);
                if (selectedSignPost == null)
                {
                    // Prompt user to specify a location for a new sign post
                    PromptPointResult signPostPointResult = editor.GetPoint("\nSpecify location for the new sign post:");
                    if (signPostPointResult.Status != PromptStatus.OK)
                    {
                        editor.WriteMessage("\nOperation canceled.");
                        return;
                    }

                    // Prompt user to specify a second location to calculate rotation
                    PromptPointResult rotationPointResult = editor.GetPoint("\nSpecify sign post rotation:");
                    if (signPostPointResult.Status != PromptStatus.OK)
                    {
                        editor.WriteMessage("\nOperation canceled.");
                        return;
                    }
                    // Calculate the rotation angle
                    double rotationInDegrees = CalculateRotationAngle(signPostPointResult.Value, rotationPointResult.Value);

                    // Insert a new sign post block at the specified location
                    string signPostPath = @"C:\Users\JAABUK\Desktop\prog\EESTI\Märkide elemendid\Silt.dwg";
                    selectedSignPost = InsertSignPostBlock(targetDb, signPostPath, signPostPointResult.Value, rotationInDegrees);
                }

                // Prompt user to specify a location for a new sign
                PromptPointResult signPointResult = editor.GetPoint("\nSpecify location for the new sign:");
                if (signPointResult.Status != PromptStatus.OK)
                {
                    editor.WriteMessage("\nOperation canceled.");
                    return;
                }

                // Insert the sign and attach it to the sign post
                InsertSignAtSignPost(signPath, targetDb, selectedSignPost.Value, signPointResult.Value);
            }
        }

        private ObjectId? GetSelectedSignPost(Editor editor)
        {
            // Get the current selection set
            PromptSelectionResult selectionResult = editor.SelectImplied();
            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value.Count != 1)
            {
                // No objects are selected or more than one object selected
                return null;
            }

            // Iterate through the selected objects
            using (Transaction transaction = editor.Document.Database.TransactionManager.StartTransaction())
            {
                // Get selected object
                SelectedObject selectedObject = selectionResult.Value[0];
                // Check if the selected object is a BlockReference
                BlockReference blockRef = transaction.GetObject(selectedObject.ObjectId, OpenMode.ForRead) as BlockReference;
                if (blockRef != null && IsSignPostBlock(blockRef, transaction))
                {
                    // Return the ObjectId of the sign post block
                    return selectedObject.ObjectId;
                }
            }

            // No valid sign post block found in the selection
            return null;
        }

        private bool IsSignPostBlock(BlockReference blockRef, Transaction transaction)
        {
            // Check the block name or other identifying properties
            BlockTableRecord blockDef = transaction.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            return blockDef != null && blockDef.Name.Equals("Silt", StringComparison.OrdinalIgnoreCase);
        }

        private ObjectId InsertSignPostBlock(Database targetDb, string signPostPath, Point3d location, double rotationInDegrees)
        {
            // Create a database for the sign post file
            Database signPostDb = new Database(false, true);
            try
            {
                // Load the sign post DWG file
                signPostDb.ReadDwgFile(signPostPath, FileOpenMode.OpenForReadAndReadShare, true, null);

                using (Transaction transaction = targetDb.TransactionManager.StartTransaction())
                {
                    // Define a block table and block table record
                    BlockTable blockTable = (BlockTable)transaction.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Insert the sign post block into the target database
                    ObjectId blockId = targetDb.Insert(Path.GetFileNameWithoutExtension(signPostPath), signPostDb, false);

                    // Create a block reference for the sign post
                    using (BlockReference blockRef = new BlockReference(location, blockId))
                    {
                        // Set the rotation of the block reference (convert degrees to radians)
                        blockRef.Rotation = rotationInDegrees * (Math.PI / 180);

                        // Add the block reference to the model space
                        modelSpace.AppendEntity(blockRef);
                        transaction.AddNewlyCreatedDBObject(blockRef, true);

                        // Commit the transaction
                        transaction.Commit();
                        return blockRef.ObjectId;
                    }
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new InvalidOperationException("Failed to insert the sign post block from the file.", ex);
            }
            finally
            {
                // Dispose of the sign post database
                signPostDb.Dispose();
            }
        }

        private static void InsertSignAtSignPost(string signPath, Database targetDb, ObjectId signPostId, Point3d location)
        {
            // Get document editor
            var document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var editor = document.Editor;

            // Create a sign database and try to load the file
            Database signDb = new Database(false, true);
            try
            {
                signDb.ReadDwgFile(signPath, FileOpenMode.OpenForReadAndReadShare, true, null);

                using (Transaction transaction = targetDb.TransactionManager.StartTransaction())
                {
                    // Define a block table and block table record
                    BlockTable blockTable = (BlockTable)transaction.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Load the sign block into document database
                    ObjectId blockId = targetDb.Insert(Path.GetFileName(signPath), signDb, false);

                    // Create a block reference
                    using (BlockReference signRef = new BlockReference(location, blockId))
                    {
                        // Insert the sign block near the sign post
                        modelSpace.AppendEntity(signRef);
                        transaction.AddNewlyCreatedDBObject(signRef, true);

                        // Connect the sign to the sign post with a line
                        ConnectSignToSignPost(transaction, signRef, signPostId);

                        // Retrieve the block definition
                        BlockTableRecord blockDef = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);

                        // Collect attributes, except CON tag
                        List<AttributeData> attributes = new List<AttributeData>();
                        foreach (ObjectId attDefId in blockDef)
                        {
                            if (attDefId.ObjectClass == RXClass.GetClass(typeof(AttributeDefinition)))
                            {
                                AttributeDefinition attDef = (AttributeDefinition)transaction.GetObject(attDefId, OpenMode.ForRead);
                                if (!attDef.Constant && attDef.Tag != "CON")
                                {
                                    attributes.Add(new AttributeData
                                    {
                                        Tag = attDef.Tag,
                                        Value = attDef.TextString,
                                        AttributeDefinitionId = attDefId // Store the ObjectId
                                    });
                                }
                            }
                        }

                        // Open the modal window for editing attributes if sign has attributes
                        if (attributes.Count != 0)
                        {
                            AttributeEditorWindow editorWindow = new AttributeEditorWindow(attributes);

                            if (editorWindow.ShowDialog() == true)
                            {
                                // Apply updated attributes
                                foreach (var attribute in editorWindow.Attributes)
                                {
                                    AttributeDefinition attDef = (AttributeDefinition)transaction.GetObject(attribute.AttributeDefinitionId, OpenMode.ForRead);
                                    AttributeReference attRef = new AttributeReference();
                                    attRef.SetAttributeFromBlock(attDef, signRef.BlockTransform);
                                    attRef.TextString = attribute.Value;
                                    signRef.AttributeCollection.AppendAttribute(attRef);
                                    transaction.AddNewlyCreatedDBObject(attRef, true);
                                }
                            }
                        }
                    }

                    // Commit the transaction
                    transaction.Commit();
                }
            }
            catch (System.Exception ex)
            {
                throw new InvalidOperationException("Failed to insert the DWG file.", ex);
            }
            finally
            {
                signDb.Dispose();
            }
        }

        private static void ConnectSignToSignPost(Transaction transaction, BlockReference signRef, ObjectId signPostId)
        {
            // Get the position of the sign post
            Point3d signPostPosition = GetBlockReferencePosition(transaction, signPostId);

            // Get the position of the sign
            Point3d signPosition = signRef.Position;

            // Create a line to connect the sign and the sign post
            using (Line connectionLine = new Line(signPostPosition, signPosition))
            {
                // Add the line to the model space
                BlockTableRecord modelSpace = transaction.GetObject(signRef.BlockId.Database.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                modelSpace.AppendEntity(connectionLine);
                transaction.AddNewlyCreatedDBObject(connectionLine, true);
            }
        }

        private static Point3d GetBlockReferencePosition(Transaction transaction, ObjectId blockRefId)
        {
            BlockReference blockRef = transaction.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
            return blockRef?.Position ?? Point3d.Origin;
        }

        private double CalculateRotationAngle(Point3d point1, Point3d point2)
        {
            // Calculate the angle in radians
            double angleInRadians = Math.Atan2(point2.Y - point1.Y, point2.X - point1.X);

            // Convert the angle to degrees
            double angleInDegrees = angleInRadians * (180 / Math.PI);

            // Adjust the angle by subtracting 90 degrees for clockwise rotation
            angleInDegrees -= 90;

            // Ensure the angle stays within the range of 0 to 360 degrees
            if (angleInDegrees < 0)
            {
                angleInDegrees += 360;
            }

            return angleInDegrees;
        }
    }
}