using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Documents;
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
                // Check if a sign post block is selected; if yes, place sign at bottom/top of existing sings; if not, create a new post and attach sign;
                ObjectId? selectedSignPost = GetSelectedSignPost(editor);
                if (selectedSignPost != null)
                {
                    AddSignToSignPost(signPath, editor, targetDb, (ObjectId)selectedSignPost);
                }
                else
                {
                    InsertSignPostAndSign(signPath, editor, targetDb);
                }
            }
        }

        private void InsertSignPostAndSign(string signPath, Editor editor, Database targetDb)
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
            ObjectId selectedSignPost = InsertSignPostBlock(targetDb, signPostPath, signPostPointResult.Value, rotationInDegrees);

            // Prompt user to specify a location for a new sign
            PromptPointResult signPointResult = editor.GetPoint("\nSpecify location for the new sign:");


            if (signPointResult.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return;
            }

            // Get sign post rotation for sign
            double signRotationInDegrees;
            using (Transaction transaction = targetDb.TransactionManager.StartTransaction())
            {
                signRotationInDegrees = GetObjectRotation(selectedSignPost, transaction);
                transaction.Commit();
            }

            // Insert the sign and attach it to the sign post
            ObjectId signBlockId = InsertSignAtSignPost(signPath, targetDb, selectedSignPost, signPointResult.Value, signRotationInDegrees);

            // Connect sign to sign post with line
            using (Transaction transaction = targetDb.TransactionManager.StartTransaction())
            {
                ConnectSignToSignPost(transaction, signBlockId, selectedSignPost);
                transaction.Commit();
            }
        }

        private void AddSignToSignPost(string signPath, Editor editor, Database targetDb, ObjectId signPostId)
        {
            // Prompt user to specify whether to add sign to top of bottom of existing signs 
            PromptKeywordOptions options = new PromptKeywordOptions("\nDo you want to add the sign to the [Higher/Lower] position?")
            {
                AllowNone = false // Force the user to select one of the options
            };

            // Add keywords for "Higher" and "Lower"
            options.Keywords.Add("Higher");
            options.Keywords.Add("Lower");

            // Set the default keyword
            options.Keywords.Default = "Higher";
             
            // Prompt the user
            PromptResult result = editor.GetKeywords(options);

            // Check the result
            if (result.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                return; // Return null if the user cancels
            }

            using (Transaction transaction = targetDb.TransactionManager.StartTransaction())
            {
                // If user selected higher, insert sign on top of existing signs
                if (result.StringResult == "Higher")
                {
                    // Get the highest sign
                    ObjectId highestSign = FindHighestAttachedSign(signPostId, transaction);

                    // Get top middle point of highest sign
                    (Point3d middleBottom, Point3d middleTop) = GetMiddlePoints(highestSign, transaction);

                    // Get angle of highest sign
                    double signAngle = GetObjectRotation(highestSign, transaction);

                    // Adjust the angle by subtracting 90 degrees for clockwise rotation
                    signAngle -= 180;
                    // Ensure the angle stays within the range of 0 to 360 degrees
                    if (signAngle < 0)
                    {
                        signAngle += 360;
                    }

                    // Insert new sign at top middle point and adjust rotation
                    InsertSignAtSignPost(signPath, targetDb, signPostId, middleTop, signAngle);
                }
                else if (result.StringResult == "Lower")
                {
                    // Get the lowest sign
                    ObjectId lowestSign = FindLowestAttachedSign(signPostId, transaction);

                    // Get bottom middle point of lowest sign
                    (Point3d middleBottom, Point3d middleTop) = GetMiddlePoints(lowestSign, transaction);

                    // Get angle of lowest sign
                    double signAngle = GetObjectRotation(lowestSign, transaction);

                    // Adjust the angle by subtracting 90 degrees for clockwise rotation
                    signAngle -= 180;
                    // Ensure the angle stays within the range of 0 to 360 degrees
                    if (signAngle < 0)
                    {
                        signAngle += 360;
                    }

                    // Insert new sign at bottom middle point
                    ObjectId newSignId = InsertSignAtSignPost(signPath, targetDb, signPostId, middleBottom, signAngle);

                    // Move the new sign so its "CON" tag aligns with the insertion point
                    using (Transaction moveTransaction = targetDb.TransactionManager.StartTransaction())
                    {
                        BlockReference newSignRef = moveTransaction.GetObject(newSignId, OpenMode.ForWrite) as BlockReference;

                        // Get the "CON" tag position of the new sign
                        Point3d conTagPosition = GetConTagPosition(newSignRef, moveTransaction);

                        // Calculate the offset to move the "CON" tag to the desired insertion point
                        Vector3d offset = middleBottom.GetVectorTo(conTagPosition);

                        // Apply the offset to the new sign
                        newSignRef.TransformBy(Matrix3d.Displacement(-offset));

                        moveTransaction.Commit();
                    }
                }
                transaction.Commit();
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
            ObjectId SignPostId;
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

                        SignPostId = blockRef.ObjectId;

                        // Commit the transaction
                        transaction.Commit();
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
            return SignPostId;
        }

        private static ObjectId InsertSignAtSignPost(string signPath, Database targetDb, ObjectId signPostId, Point3d location, double angleInDegrees)
        {
            // Get document editor
            var document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var editor = document.Editor;

            // Create a sign database and try to load the file
            Database signDb = new Database(false, true);
            ObjectId signId;
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

                        signId = signRef.ObjectId;

                        // Adjust the angle by subtracting 90 degrees for clockwise rotation
                        angleInDegrees -= 180;
                        // Ensure the angle stays within the range of 0 to 360 degrees
                        if (angleInDegrees < 0)
                        {
                            angleInDegrees += 360;
                        }
                        // Apply angle to sign
                        signRef.Rotation = angleInDegrees * (Math.PI / 180);

                        // Retrieve the block definition
                        BlockTableRecord blockDef = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);

                        // Collect attributes, including CON tag for the block reference
                        List<AttributeData> attributesForBlock = new List<AttributeData>();
                        List<AttributeData> attributesForModal = new List<AttributeData>();

                        foreach (ObjectId attDefId in blockDef)
                        {
                            if (attDefId.ObjectClass == RXClass.GetClass(typeof(AttributeDefinition)))
                            {
                                AttributeDefinition attDef = (AttributeDefinition)transaction.GetObject(attDefId, OpenMode.ForRead);
                                if (!attDef.Constant)
                                {
                                    // Add all attributes to the block reference
                                    attributesForBlock.Add(new AttributeData
                                    {
                                        Tag = attDef.Tag,
                                        Value = attDef.TextString,
                                        AttributeDefinitionId = attDefId // Store the ObjectId
                                    });

                                    // Exclude "CON" tag from the modal window
                                    if (attDef.Tag != "CON")
                                    {
                                        attributesForModal.Add(new AttributeData
                                        {
                                            Tag = attDef.Tag,
                                            Value = attDef.TextString,
                                            AttributeDefinitionId = attDefId // Store the ObjectId
                                        });
                                    }
                                }
                            }
                        }

                        // Open the modal window for editing attributes, excluding "CON"
                        if (attributesForModal.Count != 0)
                        {
                            AttributeEditorWindow editorWindow = new AttributeEditorWindow(attributesForModal);

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

                        // Add all attributes (including "CON") to the block reference
                        foreach (var attribute in attributesForBlock)
                        {
                            AttributeDefinition attDef = (AttributeDefinition)transaction.GetObject(attribute.AttributeDefinitionId, OpenMode.ForRead);
                            AttributeReference attRef = new AttributeReference();
                            attRef.SetAttributeFromBlock(attDef, signRef.BlockTransform);
                            attRef.TextString = attribute.Value;
                            signRef.AttributeCollection.AppendAttribute(attRef);
                            transaction.AddNewlyCreatedDBObject(attRef, true);
                        }

                        // Add sign ObjectId to sign post's list of attached signs
                        AddSignToSignPostMapping(signPostId, signId, transaction);
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
            return signId;
        }

        private static void ConnectSignToSignPost(Transaction transaction, ObjectId signId, ObjectId signPostId)
        {
            // Get the position of the sign post
            Point3d signPostPosition = GetBlockReferencePosition(transaction, signPostId);

            // Get the position of the sign
            Point3d signPosition = GetBlockReferencePosition(transaction, signId);

            // Create a line to connect the sign and the sign post
            using (Line connectionLine = new Line(signPostPosition, signPosition))
            {
                // Add the line to the model space
                BlockTableRecord modelSpace = transaction.GetObject(signId.Database.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
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

        private double GetObjectRotation(ObjectId objectId, Transaction transaction)
        {
            // Open the object for read
            BlockReference blockRef = transaction.GetObject(objectId, OpenMode.ForRead) as BlockReference;

            if (blockRef != null)
            {
                // Return the rotation in degrees (convert from radians)
                return blockRef.Rotation * (180 / Math.PI);
            }

            throw new InvalidOperationException("The provided ObjectId is not a BlockReference or is invalid.");
        }

        private static void SaveSignPostMapping(ObjectId signPostId, List<ObjectId> attachedSigns, Transaction transaction)
        {
            // Serialize the list of attached sign ObjectIds into a string
            string serializedData = string.Join(",", attachedSigns.Select(id => id.Handle.ToString()));

            // Get the sign post object
            DBObject signPost = transaction.GetObject(signPostId, OpenMode.ForWrite);

            // Register the application name
            RegisterApplicationName(signPost.Database, "SignPostMapping");

            // Create a ResultBuffer to store the XData
            using (ResultBuffer xdata = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, "SignPostMapping"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, serializedData)))
            {
                // Attach the XData to the sign post
                signPost.XData = xdata;
            }
        }

        private static List<ObjectId> LoadSignPostMapping(ObjectId signPostId, Transaction transaction)
        {
            // Get the sign post object
            DBObject signPost = transaction.GetObject(signPostId, OpenMode.ForRead);

            // Retrieve the XData
            ResultBuffer xdata = signPost.XData;
            if (xdata == null) return new List<ObjectId>();

            // Parse the XData
            TypedValue[] data = xdata.AsArray();
            if (data.Length < 2 || data[0].Value.ToString() != "SignPostMapping") return new List<ObjectId>();

            // Deserialize the attached sign ObjectIds
            string serializedData = data[1].Value.ToString();
            return serializedData.Split(',')
                .Select(handle => GetObjectIdFromHandle(signPost.Database, handle))
                .Where(id => id != ObjectId.Null)
                .ToList();
        }

        private static void AddSignToSignPostMapping(ObjectId signPostId, ObjectId signId, Transaction transaction)
        {
            // Validate that the signId is a BlockReference
            BlockReference blockRef = transaction.GetObject(signId, OpenMode.ForRead) as BlockReference;
            if (blockRef == null)
            {
                throw new InvalidOperationException("Only BlockReference objects can be attached to a sign post.");
            }

            // Get already attached sign Ids
            List<ObjectId> attachedSigns = LoadSignPostMapping(signPostId, transaction);

            // Add new sign to attached signs list
            attachedSigns.Add(signId);

            // Save list with added sign to sign post
            SaveSignPostMapping(signPostId, attachedSigns, transaction);
        }

        private static void RemoveSignFromSignPostMapping(ObjectId signPostId, ObjectId signId, Transaction transaction)
        {
            // Get already attached sign Ids
            List<ObjectId> attachedSigns = LoadSignPostMapping(signPostId, transaction);

            // Chech if id is in signpost mapping; then remove
            if (attachedSigns.Contains(signId))
            {
                attachedSigns.Remove(signId);
                SaveSignPostMapping(signPostId, attachedSigns, transaction);
            }
            else
            {
                throw new InvalidOperationException("Sign Id not in attached sings list.");
            }
        }

        private static void RegisterApplicationName(Database db, string appName)
        {
            using (Transaction transaction = db.TransactionManager.StartTransaction())
            {
                // Get the RegAppTable
                RegAppTable regAppTable = (RegAppTable)transaction.GetObject(db.RegAppTableId, OpenMode.ForRead);

                // Check if the application name is already registered
                if (!regAppTable.Has(appName))
                {
                    // Register the application name
                    regAppTable.UpgradeOpen();
                    RegAppTableRecord regAppRecord = new RegAppTableRecord
                    {
                        Name = appName
                    };
                    regAppTable.Add(regAppRecord);
                    transaction.AddNewlyCreatedDBObject(regAppRecord, true);
                }

                transaction.Commit();
            }
        }

        private static ObjectId GetObjectIdFromHandle(Database db, string handleString)
        {
            Handle handle = new Handle(Convert.ToInt64(handleString, 16));
            ObjectId id = db.GetObjectId(false, handle, 0);
            return id;
        }

        private static ObjectId FindLowestAttachedSign(ObjectId signPostId, Transaction transaction)
        {
            return FindSignByHeight(signPostId, transaction, true);
        }

        private static ObjectId FindHighestAttachedSign(ObjectId signPostId, Transaction transaction)
        {
            return FindSignByHeight(signPostId, transaction, false);
        }

        private static ObjectId FindSignByHeight(ObjectId signPostId, Transaction transaction, bool lowestSign)
        {
            // Get list of signs attached to sign post
            List<ObjectId> attachedSigns = LoadSignPostMapping(signPostId, transaction);

            // Loop through signs to store their heights in a dictionary
            Dictionary<ObjectId, Double> signHeights = new Dictionary<ObjectId, double>();
            foreach (ObjectId sign in attachedSigns){
                if (sign.IsErased)
                {
                    RemoveSignFromSignPostMapping(signPostId, sign, transaction);
                    continue;
                }
                BlockReference signRef = transaction.GetObject(sign, OpenMode.ForRead) as BlockReference;
                Point3d signPosition = signRef.Position;
                Double signHeight = signPosition.Y;
                signHeights[sign] = signHeight;
            }

            // Return lowest/highest sign
            if (lowestSign == true)
            {
                return signHeights.Aggregate((x, y) => x.Value < y.Value ? x : y).Key;
            }
            else
            {
                return signHeights.Aggregate((x, y) => x.Value > y.Value ? x : y).Key;
            }
        }

        private (Point3d middleBottom, Point3d middleTop) GetMiddlePoints(ObjectId signId, Transaction transaction)
        {
            // Get the sign block reference
            BlockReference signRef = transaction.GetObject(signId, OpenMode.ForRead) as BlockReference;

            // Middle bottom is the origin point of the sign block
            Point3d middleBottom = signRef.Position;

            // Initialize the middle top point
            Point3d middleTop = Point3d.Origin;

            // Iterate through the attributes of the block reference
            foreach (ObjectId attId in signRef.AttributeCollection)
            {
                AttributeReference attRef = transaction.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                var document =
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                var editor = document.Editor;
                editor.WriteMessage("\n" + attRef.Tag);

                // Check if the attribute tag matches "CON" (case-insensitive)
                if (attRef != null && attRef.Tag.Equals("CON", StringComparison.OrdinalIgnoreCase))
                {
                    middleTop = attRef.Position;
                    break;
                }
            }

            return (middleBottom, middleTop);
        }

        private Point3d GetConTagPosition(BlockReference blockRef, Transaction transaction)
        {
            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                AttributeReference attRef = transaction.GetObject(attId, OpenMode.ForRead) as AttributeReference;

                // Check if the attribute tag matches "CON" (case-insensitive)
                if (attRef != null && attRef.Tag.Equals("CON", StringComparison.OrdinalIgnoreCase))
                {
                    return attRef.Position;
                }
            }

            throw new InvalidOperationException("The block reference does not contain a 'CON' tag.");
        }
    }
}
