using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private PaletteSet _paletteSet;

        [CommandMethod("PS_DrawGUI")]
        public void DrawGUI()
        {
            if (_paletteSet == null)
            {
                // Create a new PaletteSet
                _paletteSet = new PaletteSet("Street signs and markings")
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

        public void InsertSignButton(string signPath)
        {
            // Get the current document, its editor database
            var document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var editor = document.Editor;
            var targetDb = document.Database;

            // Lock the document for editing
            using (DocumentLock docLock = document.LockDocument())
            {
                // Start transaction
                using (Transaction transaction = targetDb.TransactionManager.StartTransaction())
                {
                    // Check if a sign post block is selected; if yes, add sign to sign post; else insert new post and sign
                    ObjectId? selectedSignPost = GetSelectedSignPost(transaction, editor);
                    if (selectedSignPost != null)
                    {
                        AddSignToSignPost(transaction, signPath, editor, targetDb, (ObjectId)selectedSignPost);
                    }
                    else
                    {
                        InsertSignPostAndSign(transaction, signPath, editor, targetDb);
                    }

                    transaction.Commit();
                }
            }
        }

        private void InsertSignPostAndSign(Transaction transaction, string signPath, Editor editor, Database targetDb)
        {
            // Prompt user to specify a location for a new sign post
            Point3d signPostLocation = PromptPoint(editor, new PromptPointOptions("\nSpecify location for the new sign post:"));

            // Prompt user to specify an angle for sign post rotation
            PromptAngleOptions promtSignPostRotationOptions = new PromptAngleOptions("\nSpecify rotation for the new sign post:")
            {
                DefaultValue = 0,
                BasePoint = signPostLocation,
                UseBasePoint = true,
                UseDefaultValue = true,
            };
            double signPostRotation = PromtAngle(editor, promtSignPostRotationOptions);

            // Adjust rotation by 90 degrees anticlockwise to be more intuitive
            signPostRotation = signPostRotation + (Math.PI / 2);

            // Insert a new sign post block at the specified location
            string signPostPath = @"C:\Users\JAABUK\Desktop\prog\EESTI\Märkide elemendid\Silt.dwg";
            (ObjectId signPostRefId, ObjectId signPostDefId) = InsertBlockFromDWG(transaction, targetDb, signPostPath, signPostLocation, signPostRotation);

            // Prompt user to specify a location for a new sign
            Point3d signLocation = PromptPoint(editor, new PromptPointOptions("\nSpecify location for the new sign:"));

            // Insert the sign
            ObjectId signRefId = InsertSign(transaction, signPath, targetDb, signLocation, signPostRotation);

            // Add sign ObjectId to sign post's list of attached signs
            AddSignToSignPostMapping(transaction, signPostRefId, signRefId);

            // Connect sign to sign post with line
            ConnectSignToSignPost(transaction, signRefId, signPostRefId);
        }

        private void AddSignToSignPost(Transaction transaction, string signPath, Editor editor, Database targetDb, ObjectId signPostId)
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

            // If user selected higher, insert sign on top of existing signs
            if (result.StringResult == "Higher")
            {
                // Get the highest sign
                ObjectId highestSign = FindHighestAttachedSign(transaction, signPostId);

                // Get top middle point of highest sign
                (_, Point3d middleTop) = GetSignAnchorPoints(transaction, highestSign);

                // Get angle of highest sign
                double signAngle = GetObjectRotation(transaction, highestSign);

                // Insert new sign at top middle point and adjust rotation
                ObjectId signRefId = InsertSign(transaction, signPath, targetDb, middleTop, signAngle);

                // Add sign ObjectId to sign post's list of attached signs
                AddSignToSignPostMapping(transaction, signPostId, signRefId);
            }
            // If user selected higher, insert sign below existing signs
            else if (result.StringResult == "Lower")
            {
                // Get the lowest sign
                ObjectId lowestSign = FindLowestAttachedSign(transaction, signPostId);

                // Get bottom middle point of lowest sign
                (Point3d middleBottom, _) = GetSignAnchorPoints(transaction, lowestSign);

                // Get angle of lowest sign
                double signAngle = GetObjectRotation(transaction, lowestSign);

                // Insert new sign at bottom middle point
                ObjectId signRefId = InsertSign(transaction, signPath, targetDb, middleBottom, signAngle);

                // Add sign ObjectId to sign post's list of attached signs
                AddSignToSignPostMapping(transaction, signPostId, signRefId);

                // Move the new sign so its "CON" tag aligns with the insertion point
                BlockReference signRef = transaction.GetObject(signRefId, OpenMode.ForWrite) as BlockReference;

                // Get the "CON" tag position of the new sign
                Point3d conTagPosition = GetAttributePositionByTag(transaction, signRef, "CON");

                // Calculate the offset to move the "CON" tag to the desired insertion point
                Vector3d offset = middleBottom.GetVectorTo(conTagPosition);

                // Apply the offset to the new sign
                signRef.TransformBy(Matrix3d.Displacement(-offset));
            }
        }

        private ObjectId? GetSelectedSignPost(Transaction transaction, Editor editor)
        {
            // Get the current selection set
            PromptSelectionResult selectionResult = editor.SelectImplied();
            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value.Count != 1)
            {
                // No objects are selected or more than one object selected
                return null;
            }

            // Get selected object
            SelectedObject selectedObject = selectionResult.Value[0];
            // Check if the selected object is a BlockReference
            BlockReference blockRef = transaction.GetObject(selectedObject.ObjectId, OpenMode.ForRead) as BlockReference;
            if (blockRef != null && IsSignPostBlock(transaction, blockRef))
            {
                // Return the ObjectId of the sign post block
                return selectedObject.ObjectId;
            }

            // No valid sign post block found in the selection
            return null;
        }

        private static ObjectId InsertSign(Transaction transaction, string signPath, Database targetDb, Point3d location, double rotation)
        {
            // Insert sign from DWG
            (ObjectId signRefId, ObjectId signDefId) = InsertBlockFromDWG(transaction, targetDb, signPath, location, rotation);

            // Collect sign attributes, including CON tag for the block reference
            List<AttributeData> attributesForBlock = new List<AttributeData>();
            List<AttributeData> attributesForModal = new List<AttributeData>();

            BlockReference signRef = transaction.GetObject(signRefId, OpenMode.ForWrite) as BlockReference;
            BlockTableRecord signDef = transaction.GetObject(signDefId, OpenMode.ForRead) as BlockTableRecord;

            foreach (ObjectId attDefId in signDef)
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

            return signRefId;
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

        private bool IsSignPostBlock(Transaction transaction, BlockReference blockRef)
        {
            // Check the block name or other identifying properties
            BlockTableRecord blockDef = transaction.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            return blockDef != null && blockDef.Name.Equals("Silt", StringComparison.OrdinalIgnoreCase);
        }

        private static Point3d GetBlockReferencePosition(Transaction transaction, ObjectId blockRefId)
        {
            BlockReference blockRef = transaction.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
            return blockRef?.Position ?? Point3d.Origin;
        }

        private double GetObjectRotation(Transaction transaction, ObjectId objectId)
        {
            // Open the object for read
            BlockReference blockRef = transaction.GetObject(objectId, OpenMode.ForRead) as BlockReference;

            if (blockRef != null)
            {
                return blockRef.Rotation;
            }

            throw new InvalidOperationException("The provided ObjectId is not a BlockReference or is invalid.");
        }

        private static void SaveSignPostMapping(Transaction transaction, ObjectId signPostId, List<ObjectId> attachedSigns)
        {
            // Serialize the list of attached sign ObjectIds into a string
            string serializedData = string.Join(",", attachedSigns.Select(id => id.Handle.ToString()));

            // Get the sign post object
            DBObject signPost = transaction.GetObject(signPostId, OpenMode.ForWrite);

            // Register the application name
            RegisterApplicationName(transaction, signPost.Database, "SignPostMapping");

            // Create a ResultBuffer to store the XData
            using (ResultBuffer xdata = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, "SignPostMapping"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, serializedData)))
            {
                // Attach the XData to the sign post
                signPost.XData = xdata;
            }
        }

        private static List<ObjectId> LoadSignPostMapping(Transaction transaction, ObjectId signPostId)
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

        private static void AddSignToSignPostMapping(Transaction transaction, ObjectId signPostId, ObjectId signId)
        {
            // Validate that the signId is a BlockReference
            BlockReference blockRef = transaction.GetObject(signId, OpenMode.ForRead) as BlockReference;
            if (blockRef == null)
            {
                throw new InvalidOperationException("Only BlockReference objects can be attached to a sign post.");
            }

            // Get already attached sign Ids
            List<ObjectId> attachedSigns = LoadSignPostMapping(transaction, signPostId);

            // Add new sign to attached signs list
            attachedSigns.Add(signId);

            // Save list with added sign to sign post
            SaveSignPostMapping(transaction, signPostId, attachedSigns);
        }

        private static void RemoveSignFromSignPostMapping(Transaction transaction, ObjectId signPostId, ObjectId signId)
        {
            // Get already attached sign Ids
            List<ObjectId> attachedSigns = LoadSignPostMapping(transaction, signPostId);

            // Chech if id is in signpost mapping; then remove
            if (attachedSigns.Contains(signId))
            {
                attachedSigns.Remove(signId);
                SaveSignPostMapping(transaction, signPostId, attachedSigns);
            }
            else
            {
                throw new InvalidOperationException("Sign Id not in attached sings list.");
            }
        }

        private static void RegisterApplicationName(Transaction transaction, Database db, string appName)
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
        }

        private static (ObjectId blockRefId, ObjectId BlockDefId) InsertBlockFromDWG(Transaction transaction, Database targetDb, string DWGPath, Point3d location, double rotation)
        {
            // Create a database for the DWG file
            Database sourceDb = new Database(false, true);
            ObjectId blockDefId;

            try
            {
                // Load the DWG file
                sourceDb.ReadDwgFile(DWGPath, FileOpenMode.OpenForReadAndReadShare, true, null);

                // Get the block table from the target database
                BlockTable blockTable = (BlockTable)transaction.GetObject(targetDb.BlockTableId, OpenMode.ForRead);

                // Insert the block definition from the source database into the target database
                blockDefId = targetDb.Insert(Path.GetFileNameWithoutExtension(DWGPath), sourceDb, false);

                // Create a block reference for the inserted block
                using (BlockReference blockRef = new BlockReference(location, blockDefId))
                {
                    // Set the rotation of the block reference
                    blockRef.Rotation = rotation;

                    // Add the block reference to the model space
                    BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    modelSpace.AppendEntity(blockRef);
                    transaction.AddNewlyCreatedDBObject(blockRef, true);

                    // Return the ObjectId of the block reference
                    return (blockRef.ObjectId, blockDefId);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new InvalidOperationException("Failed to insert the block from the DWG file.", ex);
            }
            finally
            {
                // Dispose of the source database
                sourceDb.Dispose();
            }
        }

        private static ObjectId GetObjectIdFromHandle(Database db, string handleString)
        {
            Handle handle = new Handle(Convert.ToInt64(handleString, 16));
            ObjectId id = db.GetObjectId(false, handle, 0);
            return id;
        }

        private static ObjectId FindLowestAttachedSign(Transaction transaction, ObjectId signPostId)
        {
            return FindSignByHeight(transaction, signPostId, true);
        }

        private static ObjectId FindHighestAttachedSign(Transaction transaction, ObjectId signPostId)
        {
            return FindSignByHeight(transaction, signPostId, false);
        }

        private static ObjectId FindSignByHeight(Transaction transaction, ObjectId signPostId, bool lowestSign)
        {
            // Get list of signs attached to sign post
            List<ObjectId> attachedSigns = LoadSignPostMapping(transaction, signPostId);

            // Loop through signs to store their heights in a dictionary
            Dictionary<ObjectId, Double> signHeights = new Dictionary<ObjectId, double>();
            foreach (ObjectId sign in attachedSigns)
            {
                if (sign.IsErased)
                {
                    RemoveSignFromSignPostMapping(transaction, signPostId, sign);
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

        private (Point3d middleBottom, Point3d middleTop) GetSignAnchorPoints(Transaction transaction, ObjectId signId)
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

        private Point3d GetAttributePositionByTag(Transaction transaction, BlockReference blockRef, string tag)
        {
            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                AttributeReference attRef = transaction.GetObject(attId, OpenMode.ForRead) as AttributeReference;

                // Check if the attribute tag matches
                if (attRef != null && attRef.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase))
                {
                    return attRef.Position;
                }
            }

            throw new InvalidOperationException("The block reference does not contain an attribute with provided tag.");
        }

        private Point3d PromptPoint(Editor editor, PromptPointOptions options)
        {
            var result = editor.GetPoint(options);
            if (result.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                throw new OperationCanceledException();
            }
            return result.Value;
        }

        private double PromtAngle(Editor editor, PromptAngleOptions options)
        {
            var result = editor.GetAngle(options);
            if (result.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                throw new OperationCanceledException();
            }
            return result.Value;
        }
    }
}
