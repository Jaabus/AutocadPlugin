using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using static AutocadPlugin.CommandUtilites;

namespace AutocadPlugin
{
    internal class SignCommands
    {

        public static void InsertSignButton(string signPath)
        {
            // Get the current document, its editor database
            var document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog("No document is currently opened. Please open a document to proceed.");
                return;
            }
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

        public static void InsertSignPostAndSign(Transaction transaction, string signDwgPath, Editor editor, Database targetDb)
        {
            // Prompt user to specify a location for a new sign post
            if (!TryPromptPoint(editor, new PromptPointOptions("\nSpecify location for the new sign post:"), out Point3d signPostLocation))
            {
                editor.WriteMessage("\nSign post location not specified. Operation cancelled.");
                return;
            }

            string basePath = AutocadPlugin.Properties.Settings.Default.StreetSignFilePath;
            if (string.IsNullOrEmpty(basePath))
            {
                editor.WriteMessage("\nStreetSignFilePath setting is not configured.");
                return;
            }
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;
            string signPostDwgPath = Path.Combine(basePath, "Märkide elemendid", "Silt.dwg"); // Path to the SIGN POST DWG

            if (!File.Exists(signPostDwgPath))
            {
                editor.WriteMessage($"\nSign post DWG file not found: {signPostDwgPath}");
                Application.ShowAlertDialog($"Sign post DWG file not found:\n{signPostDwgPath}");
                return;
            }
            ObjectId signPostDefId = LoadBlockDefinition(transaction, targetDb, signPostDwgPath);
            if (signPostDefId.IsNull)
            {
                editor.WriteMessage($"\nFailed to load sign post block definition: {signPostDwgPath}");
                return;
            }

            // signDwgPath is for the SIGN itself (passed as argument). Check its existence.
            if (!File.Exists(signDwgPath))
            {
                editor.WriteMessage($"\nSign DWG file not found: {signDwgPath}");
                Application.ShowAlertDialog($"Sign DWG file not found:\n{signDwgPath}");
                return;
            }
            ObjectId signDefId = LoadBlockDefinition(transaction, targetDb, signDwgPath);
            if (signDefId.IsNull)
            {
                editor.WriteMessage($"\nFailed to load sign block definition: {signDwgPath}");
                return;
            }

            TransientManager transientManager = TransientManager.CurrentTransientManager;
            IntegerCollection mainTransientIntCol = new IntegerCollection();

            using (BlockReference previewSignPost = new BlockReference(signPostLocation, signPostDefId))
            {
                // Initial setup for sign post preview
                string initialPostScaleStr = RetrieveVariable(transaction, targetDb, "SignPostScale") ?? Constants.defaultSignPostScale;
                double.TryParse(initialPostScaleStr, out double initialPostScale);
                if (initialPostScale <= 0) initialPostScale = 1.0;

                previewSignPost.ScaleFactors = new Scale3d(initialPostScale);
                previewSignPost.Rotation = 0; // Default rotation
                transientManager.AddTransient(previewSignPost, TransientDrawingMode.DirectShortTerm, 128, mainTransientIntCol);

                double finalSignPostRotation = 0;
                double finalSignPostScale = initialPostScale;
                bool signPostParamsConfirmed = false;

                try // This try-finally ensures previewSignPost transient is cleaned up
                {
                    PromptAngleOptions pao = new PromptAngleOptions("\nSpecify rotation for the new sign post:")
                    {
                        DefaultValue = 0,
                        BasePoint = signPostLocation,
                        UseBasePoint = true,
                        UseDefaultValue = true,
                    };
                    PromptDoubleResult angleResult = editor.GetAngle(pao);

                    if (angleResult.Status == PromptStatus.OK)
                    {
                        finalSignPostRotation = angleResult.Value + (Math.PI / 2); // Original 90-degree adjustment
                        previewSignPost.Rotation = finalSignPostRotation;

                        // Re-fetch or confirm scale if it can change after rotation prompt or if user can input it
                        string signPostScaleString = RetrieveVariable(transaction, targetDb, "SignPostScale") ?? Constants.defaultSignPostScale;
                        if (!double.TryParse(signPostScaleString, out finalSignPostScale) || finalSignPostScale <= 0)
                        {
                            finalSignPostScale = 1.0; // Fallback
                            editor.WriteMessage($"\nWarning: Invalid SignPostScale. Using {finalSignPostScale}.");
                        }
                        previewSignPost.ScaleFactors = new Scale3d(finalSignPostScale);

                        transientManager.UpdateTransient(previewSignPost, mainTransientIntCol);
                        signPostParamsConfirmed = true;
                    }
                    else
                    {
                        editor.WriteMessage("\nSign post rotation cancelled. Operation aborted.");
                        return; // Exits method, finally for previewSignPost will run
                    }

                    if (signPostParamsConfirmed)
                    {
                        double actualSignScale = 1.0; // Default scale for the sign
                        string signScaleStr = RetrieveVariable(transaction, targetDb, "SignScale");
                        if (!string.IsNullOrEmpty(signScaleStr))
                        {
                            if (double.TryParse(signScaleStr, out double parsedScale) && parsedScale > 0)
                                actualSignScale = parsedScale;
                            else
                                editor.WriteMessage($"\nWarning: Invalid value for SignScale setting: '{signScaleStr}'. Using default {actualSignScale}.");
                        }

                        var signPreviewJig = new SignPreviewJig(
                            transientManager,
                            signDefId,
                            previewSignPost.Position, // Line starts from the (transient) sign post's position
                            finalSignPostRotation,    // Sign rotates with the post
                            actualSignScale           // Scale of the sign block itself
                        );

                        PointMonitorEventHandler pointMonitorHandler = (s, e_pm) =>
                        {
                            if (e_pm.Context.PointComputed) // Ensure a valid point is computed
                            {
                                signPreviewJig.Update(e_pm.Context.ComputedPoint);
                            }
                        };

                        PromptPointOptions signLocationPpo = new PromptPointOptions("\nSpecify location for the new sign:");
                        // signLocationPpo.Keywords.Add("Cancel"); // Optional: allow keyword cancel

                        Point3d finalSignLocation = Point3d.Origin;
                        bool signLocationSelected = false;

                        editor.PointMonitor += pointMonitorHandler;
                        try
                        {
                            PromptPointResult pprSign = editor.GetPoint(signLocationPpo);
                            if (pprSign.Status == PromptStatus.OK)
                            {
                                finalSignLocation = pprSign.Value;
                                signLocationSelected = true;
                            }
                            else
                            {
                                editor.WriteMessage("\nSign location not specified or cancelled.");
                            }
                        }
                        finally
                        {
                            editor.PointMonitor -= pointMonitorHandler; // Crucial: detach the monitor
                            signPreviewJig.Erase(); // Clean up transient sign and line from the jig
                        }

                        if (signLocationSelected)
                        {
                            // Erase the sign post's preview as we are about to insert the real one
                            transientManager.EraseTransient(previewSignPost, mainTransientIntCol);
                            // previewSignPost is disposed by its 'using' statement at the end of this scope.

                            // Insert the actual sign post
                            (ObjectId signPostRefId, _) = InsertBlockFromDWG(transaction, targetDb, signPostDwgPath, signPostLocation, finalSignPostRotation, finalSignPostScale);
                            if (signPostRefId.IsNull) { editor.WriteMessage("\nFailed to insert sign post."); return; }

                            AddSignPostToNOD(transaction, targetDb, signPostRefId);
                            AttachSignPostErasedEvent(transaction, targetDb, signPostRefId);

                            // Insert the actual sign
                            ObjectId signRefId = InsertSign(transaction, signDwgPath, targetDb, finalSignLocation, finalSignPostRotation);
                            if (signRefId.IsNull) { editor.WriteMessage("\nFailed to insert sign."); return; }

                            // Apply scale to the actual sign block if it's not the default (1.0)
                            // Assuming InsertSign inserted it with scale 1.0 or its inherent scale.
                            if (Math.Abs(actualSignScale - 1.0) > Tolerance.Global.EqualPoint) // Check if scaling is needed
                            {
                                using (BlockReference actualSignBr = transaction.GetObject(signRefId, OpenMode.ForWrite, false, true) as BlockReference)
                                {
                                    if (actualSignBr != null)
                                    {
                                        actualSignBr.ScaleFactors = new Scale3d(actualSignScale);
                                    }
                                }
                            }

                            AddSignToSignPostMapping(transaction, signPostRefId, signRefId);
                            ConnectSignToSignPost(transaction, signRefId, signPostRefId);
                            editor.WriteMessage("\nSign post and sign inserted successfully.");
                        }
                        else
                        {
                            editor.WriteMessage("\nSign insertion aborted as location was not selected.");
                            // previewSignPost transient will be cleaned by its 'finally' block
                        }
                    }
                }
                finally
                {
                    // This ensures the previewSignPost (transient sign post) is always cleaned up
                    // if it hasn't been explicitly erased earlier.
                    transientManager.EraseTransient(previewSignPost, mainTransientIntCol);
                }
            }
        }

        private static void AddSignToSignPost(Transaction transaction, string signPath, Editor editor, Database targetDb, ObjectId signPostId)
        {
            // Get the highest sign
            ObjectId highestSign = FindHighestAttachedSign(transaction, signPostId);

            // Get the lowest sign
            ObjectId lowestSign = FindLowestAttachedSign(transaction, signPostId);

            // If no signs are attached, insert the new sign at user specifed location
            if (highestSign == ObjectId.Null || lowestSign == ObjectId.Null)
            {
                if (!TryPromptPoint(editor, new PromptPointOptions("\nSpecify location for the new sign:"), out Point3d signLocation))
                {
                    return;
                }
                double signPostRotation = GetObjectRotation(transaction, signPostId);
                ObjectId signRefId = InsertSign(transaction, signPath, targetDb, signLocation, signPostRotation);
                AddSignToSignPostMapping(transaction, signPostId, signRefId);
                ConnectSignToSignPost(transaction, signRefId, signPostId);
                return; // Exit if no signs are attached
            }

            // Prompt user to specify whether to add sign to top of bottom of existing signs 
            PromptKeywordOptions options = new PromptKeywordOptions("\nSpecify sign position [Higher/Lower]")
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

        private static ObjectId? GetSelectedSignPost(Transaction transaction, Editor editor)
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
            // Get sign post scale factor from user preferences
            string signScaleString = RetrieveVariable(transaction, targetDb, "SignScale");
            if (string.IsNullOrEmpty(signScaleString))
            {
                // If no scale is set, use the default value
                signScaleString = Constants.defaultSignScale;
            }
            double signScale = Convert.ToDouble(signScaleString);

            // Insert sign from DWG
            (ObjectId signRefId, ObjectId signDefId) = InsertBlockFromDWG(transaction, targetDb, signPath, location, rotation, signScale);

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

        private static bool IsSignPostBlock(Transaction transaction, BlockReference blockRef)
        {
            // Check the block name or other identifying properties
            BlockTableRecord blockDef = transaction.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            return blockDef != null && blockDef.Name.Equals("Silt", StringComparison.OrdinalIgnoreCase);
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
            if (signPostId.IsErased) return new List<ObjectId>();
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

            // Attach an ObjectErased event listener to the sign
            blockRef.Erased += (sender, args) =>
            {
                if (!args.DBObject.IsErased) return;

                if (signPostId.IsErased) return;

                // Remove the sign from the sign post mapping
                using (Transaction innerTransaction = signId.Database.TransactionManager.StartTransaction())
                {
                    try
                    {
                        RemoveSignFromSignPostMapping(innerTransaction, signPostId, signId);
                        innerTransaction.Commit();
                    }
                    catch
                    {
                        innerTransaction.Abort();
                        throw;
                    }
                }
            };
        }

        private static void RemoveSignFromSignPostMapping(Transaction transaction, ObjectId signPostId, ObjectId signId)
        {
            if (signPostId.IsErased) return;

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

            // If no sign are attached, return null objectid
            if (signHeights.Count == 0)
            {
                return ObjectId.Null;
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

        private static (Point3d middleBottom, Point3d middleTop) GetSignAnchorPoints(Transaction transaction, ObjectId signId)
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

        private static void AddSignPostToNOD(Transaction transaction, Database db, ObjectId signPostId)
        {
            // Retrieve the custom dictionary from the NOD
            DBDictionary nod = (DBDictionary)transaction.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            const string customDictName = "SignPostList";
            DBDictionary customDict;

            if (!nod.Contains(customDictName))
            {
                // Create the custom dictionary if it doesn't exist
                nod.UpgradeOpen();
                customDict = new DBDictionary();
                nod.SetAt(customDictName, customDict);
                transaction.AddNewlyCreatedDBObject(customDict, true);
            }
            else
            {
                // Retrieve the existing custom dictionary
                customDict = (DBDictionary)transaction.GetObject(nod.GetAt(customDictName), OpenMode.ForWrite);
            }

            // Check if the XRecord exists
            const string xRecordKey = "SignPostIds";
            Xrecord xRecord;

            if (!customDict.Contains(xRecordKey))
            {
                // Create a new XRecord if it doesn't exist
                xRecord = new Xrecord
                {
                    Data = new ResultBuffer()
                };
                customDict.SetAt(xRecordKey, xRecord);
                transaction.AddNewlyCreatedDBObject(xRecord, true);
            }
            else
            {
                // Retrieve the existing XRecord
                xRecord = (Xrecord)transaction.GetObject(customDict.GetAt(xRecordKey), OpenMode.ForWrite);
            }

            // Deserialize the existing list of ObjectIds
            List<ObjectId> signPostIds = DeserializeObjectIds(xRecord.Data);

            // Add the new sign post ID
            if (!signPostIds.Contains(signPostId))
            {
                signPostIds.Add(signPostId);
            }

            // Serialize the updated list back into the XRecord
            xRecord.Data = SerializeObjectIds(signPostIds);
        }

        private static void RemoveSignPostFromNOD(Transaction transaction, Database db, ObjectId signPostId)
        {
            // Retrieve the custom dictionary from the NOD
            DBDictionary nod = (DBDictionary)transaction.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            const string customDictName = "SignPostList";

            if (!nod.Contains(customDictName)) return;

            DBDictionary customDict = (DBDictionary)transaction.GetObject(nod.GetAt(customDictName), OpenMode.ForWrite);

            const string xRecordKey = "SignPostIds";
            if (!customDict.Contains(xRecordKey)) return;

            // Retrieve the existing XRecord
            Xrecord xRecord = (Xrecord)transaction.GetObject(customDict.GetAt(xRecordKey), OpenMode.ForWrite);

            // Deserialize the existing list of ObjectIds
            List<ObjectId> signPostIds = DeserializeObjectIds(xRecord.Data);

            // Remove the sign post ID
            if (signPostIds.Contains(signPostId))
            {
                signPostIds.Remove(signPostId);
            }

            // Serialize the updated list back into the XRecord
            xRecord.Data = SerializeObjectIds(signPostIds);
        }

        private static List<ObjectId> GetAllSignPostsFromNOD(Transaction transaction, Database db)
        {
            // Retrieve the custom dictionary from the NOD
            DBDictionary nod = (DBDictionary)transaction.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            const string customDictName = "SignPostList";

            if (!nod.Contains(customDictName)) return new List<ObjectId>();

            DBDictionary customDict = (DBDictionary)transaction.GetObject(nod.GetAt(customDictName), OpenMode.ForRead);

            const string xRecordKey = "SignPostIds";
            if (!customDict.Contains(xRecordKey)) return new List<ObjectId>();

            // Retrieve the existing XRecord
            Xrecord xRecord = (Xrecord)transaction.GetObject(customDict.GetAt(xRecordKey), OpenMode.ForRead);

            // Deserialize and return the list of ObjectIds
            return DeserializeObjectIds(xRecord.Data);
        }

        private static ResultBuffer SerializeObjectIds(List<ObjectId> objectIds)
        {
            return new ResultBuffer(objectIds.Select(id => new TypedValue((int)DxfCode.Handle, id.Handle.ToString())).ToArray());
        }

        private static List<ObjectId> DeserializeObjectIds(ResultBuffer buffer)
        {
            if (buffer == null) return new List<ObjectId>();

            return buffer
                .AsArray()
                .Where(tv => tv.TypeCode == (int)DxfCode.Handle)
                .Select(tv => GetObjectIdFromHandle(Application.DocumentManager.MdiActiveDocument.Database, tv.Value.ToString()))
                .Where(id => id != ObjectId.Null)
                .ToList();
        }

        private static void AttachSignPostErasedEvent(Transaction transaction, Database db, ObjectId signPostId)
        {
            BlockReference signPost = transaction.GetObject(signPostId, OpenMode.ForWrite) as BlockReference;
            if (signPost != null)
            {
                signPost.Erased += (sender, args) =>
                {
                    if (args.DBObject.IsErased)
                    {
                        using (Transaction innerTransaction = db.TransactionManager.StartTransaction())
                        {
                            RemoveSignPostFromNOD(innerTransaction, db, signPostId);
                            innerTransaction.Commit();
                        }
                    }
                };
            }
        }

        internal static void GenerateSignReport()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database db = document.Database;

            using (Transaction transaction = db.TransactionManager.StartTransaction())
            {
                // Step 1: Retrieve all sign posts from the NamedObjectsDictionary
                List<ObjectId> signPostIds = GetAllSignPostsFromNOD(transaction, db);

                // Step 2: Collect all signs and their details
                List<(string Code, Point3d Location, ObjectId SignId)> signDetails = new List<(string, Point3d, ObjectId)>();

                foreach (ObjectId signPostId in signPostIds)
                {
                    // Get attached signs for the current sign post
                    List<ObjectId> attachedSigns = LoadSignPostMapping(transaction, signPostId);

                    foreach (ObjectId signId in attachedSigns)
                    {
                        // Get the sign block reference
                        BlockReference signRef = transaction.GetObject(signId, OpenMode.ForRead) as BlockReference;
                        if (signRef == null) continue;

                        // Get the sign post block reference
                        BlockReference signPostRef = transaction.GetObject(signPostId, OpenMode.ForRead) as BlockReference;
                        if (signRef == null) continue;

                        // Extract the sign's name
                        string signName = GetBlockName(transaction, signRef);

                        // Extract the sign's code (first part of the name before the space)
                        string signCode = signName.Split(' ')[0];

                        // Get the sign post's location
                        Point3d signLocation = signPostRef.Position;

                        // Add the details to the list
                        signDetails.Add((signCode, signLocation, signId));
                    }
                }

                // Step 3: Create a new layout and add a table
                CreateSignReportLayout(transaction, db, document, signDetails);

                transaction.Commit();
            }
        }

        private static string GetBlockName(Transaction transaction, BlockReference blockRef)
        {
            BlockTableRecord blockDef = transaction.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            return blockDef?.Name ?? string.Empty;
        }

        private static void CreateSignReportLayout(Transaction transaction, Database db, Document document, List<(string Code, Point3d Location, ObjectId SignId)> signDetails)
        {
            using (DocumentLock docLock = document.LockDocument())
            {
                try
                {
                    // Create a new layout
                    LayoutManager layoutManager = LayoutManager.Current;
                    string layoutName = "Sign Report";
                    if (layoutManager.LayoutExists(layoutName))
                    {
                        layoutManager.DeleteLayout(layoutName);
                    }
                    layoutManager.CreateLayout(layoutName);
                    Layout layout = transaction.GetObject(layoutManager.GetLayoutId(layoutName), OpenMode.ForWrite) as Layout;
                    document.Editor.WriteMessage("\nLayout created successfully");

                    // Get the block table record for the layout
                    BlockTableRecord layoutBlock = transaction.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite) as BlockTableRecord;

                    // Create a table with specific dimensions
                    Table table = new Table
                    {
                        TableStyle = db.Tablestyle,
                        Width = 290, // Total table width
                        Height = signDetails.Count > 0 ? (signDetails.Count + 1) * 20 : 100 // Scale height based on number of rows
                    };

                    // Set the number of rows and columns
                    table.SetSize(signDetails.Count + 2, 3);

                    table.Columns[0].Width = 80;  // Sign Code column (about 28% of width)
                    table.Columns[1].Width = 130; // Coordinates column (about 45% of width)
                    table.Columns[2].Width = 80;  // Preview column (about 28% of width)

                    // Set uniform row height
                    table.SetRowHeight(20);

                    // Set header rows
                    table.Cells[0, 0].TextString = "Sign Report";
                    table.Cells[1, 0].TextString = "Sign Code";
                    table.Cells[1, 1].TextString = "Coordinates";
                    table.Cells[1, 2].TextString = "Preview";

                    // Set header rows' alignemnt and text height
                    for (int col = 0; col < 3; col++)
                    {
                        table.Cells[0, col].Alignment = CellAlignment.MiddleCenter;
                        table.Cells[0, col].TextHeight = 5.0;
                        table.Cells[1, col].Alignment = CellAlignment.MiddleCenter;
                        table.Cells[1, col].TextHeight = 5.0;
                    }

                    // Populate the table
                    for (int i = 0; i < signDetails.Count; i++)
                    {
                        var (code, location, signId) = signDetails[i];

                        // Set cell alignments
                        table.Cells[i + 2, 0].Alignment = CellAlignment.MiddleCenter;
                        table.Cells[i + 2, 1].Alignment = CellAlignment.MiddleCenter;
                        table.Cells[i + 2, 2].Alignment = CellAlignment.MiddleCenter;

                        // Add sign code
                        table.Cells[i + 2, 0].TextString = code;

                        // Add coordinates
                        table.Cells[i + 2, 1].TextString = $"({location.X:F2}, {location.Z:F2}, {location.Y:F2})";

                        try
                        {
                            BlockReference signRef = transaction.GetObject(signId, OpenMode.ForRead) as BlockReference;
                            if (signRef != null)
                            {
                                table.Cells[i + 2, 2].BlockTableRecordId = signRef.BlockTableRecord;
                            }
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception ex)
                        {
                            document.Editor.WriteMessage($"\nError adding preview for sign {code}: {ex.Message}");
                            table.Cells[i + 2, 2].TextString = "N/A";
                        }
                    }

                    // Add the table to the layout
                    layoutBlock.AppendEntity(table);
                    transaction.AddNewlyCreatedDBObject(table, true);

                    // Make the new layout current
                    LayoutManager.Current.CurrentLayout = layoutName;

                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    document.Editor.WriteMessage($"\nError creating layout: {ex.Message}");
                    document.Editor.WriteMessage($"\nStack trace: {ex.StackTrace}");
                }
            }
        }
    }

    /// <summary>
    /// Helper class for managing transient graphics (sign and connecting line)
    /// during interactive placement.
    /// </summary>
    internal class SignPreviewJig
    {
        public TransientManager Tm { get; }
        public ObjectId SignBlockDefId { get; }
        public Point3d SignPostPreviewLocation { get; }
        public double SignRotation { get; }
        public double SignScale { get; }

        private BlockReference _transientSign = null;
        private Line _transientLine = null;
        private readonly IntegerCollection _intCol = new IntegerCollection();

        public SignPreviewJig(TransientManager tm, ObjectId signBlockDefId, Point3d signPostPreviewLocation, double signRotation, double signScale)
        {
            Tm = tm;
            SignBlockDefId = signBlockDefId;
            SignPostPreviewLocation = signPostPreviewLocation;
            SignRotation = signRotation;
            SignScale = signScale;
        }

        public void Update(Point3d currentSignLocation)
        {
            Erase(); // Erase previous transients

            // Create new transient sign preview
            _transientSign = new BlockReference(currentSignLocation, SignBlockDefId);
            _transientSign.Rotation = SignRotation;
            _transientSign.ScaleFactors = new Scale3d(SignScale);

            // Create new transient line preview
            _transientLine = new Line(SignPostPreviewLocation, currentSignLocation);
            _transientLine.ColorIndex = 2;

            Tm.AddTransient(_transientSign, TransientDrawingMode.DirectShortTerm, 128, _intCol);
            Tm.AddTransient(_transientLine, TransientDrawingMode.DirectShortTerm, 128, _intCol);
        }

        public void Erase()
        {
            if (_transientSign != null)
            {
                Tm.EraseTransient(_transientSign, _intCol);
                _transientSign.Dispose();
                _transientSign = null;
            }
            if (_transientLine != null)
            {
                Tm.EraseTransient(_transientLine, _intCol);
                _transientLine.Dispose();
                _transientLine = null;
            }
        }
    }
}
