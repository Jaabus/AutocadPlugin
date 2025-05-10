using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AutocadPlugin
{
    internal class CommandUtilites
    {
        internal static Point3d GetBlockReferencePosition(Transaction transaction, ObjectId blockRefId)
        {
            BlockReference blockRef = transaction.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
            return blockRef?.Position ?? Point3d.Origin;
        }

        internal static double GetObjectRotation(Transaction transaction, ObjectId objectId)
        {
            // Open the object for read
            BlockReference blockRef = transaction.GetObject(objectId, OpenMode.ForRead) as BlockReference;

            if (blockRef != null)
            {
                return blockRef.Rotation;
            }

            throw new InvalidOperationException("The provided ObjectId is not a BlockReference or is invalid.");
        }

        internal static void RegisterApplicationName(Transaction transaction, Database db, string appName)
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

        internal static (ObjectId blockRefId, ObjectId BlockDefId) InsertBlockFromDWG(Transaction transaction, Database targetDb, string DWGPath, Point3d location, double rotation, double scaleFactor)
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

                    // Set the scale of the block reference
                    blockRef.ScaleFactors = new Scale3d(scaleFactor);

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

        internal static ObjectId GetObjectIdFromHandle(Database db, string handleString)
        {
            Handle handle = new Handle(Convert.ToInt64(handleString, 16));
            ObjectId id = db.GetObjectId(false, handle, 0);
            return id;
        }

        internal static Point3d GetAttributePositionByTag(Transaction transaction, BlockReference blockRef, string tag)
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

        internal static Point3d PromptPoint(Editor editor, PromptPointOptions options)
        {
            var result = editor.GetPoint(options);
            if (result.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                throw new OperationCanceledException();
            }
            return result.Value;
        }

        internal static double PromtAngle(Editor editor, PromptAngleOptions options)
        {
            var result = editor.GetAngle(options);
            if (result.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nOperation canceled.");
                throw new OperationCanceledException();
            }
            return result.Value;
        }

        internal static void StoreVariable(Transaction transaction, Database db, string key, string value)
        {
            // Access the NamedObjectsDictionary
            DBDictionary namedObjectsDict = (DBDictionary)transaction.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            // Check if a custom dictionary for storing variables exists
            const string customDictName = "CustomVariables";
            DBDictionary customDict;

            if (!namedObjectsDict.Contains(customDictName))
            {
                // Create the custom dictionary if it doesn't exist
                namedObjectsDict.UpgradeOpen();
                customDict = new DBDictionary();
                namedObjectsDict.SetAt(customDictName, customDict);
                transaction.AddNewlyCreatedDBObject(customDict, true);
            }
            else
            {
                // Retrieve the existing custom dictionary
                customDict = (DBDictionary)transaction.GetObject(namedObjectsDict.GetAt(customDictName), OpenMode.ForWrite);
            }

            // Check if the key already exists
            if (customDict.Contains(key))
            {
                // Update the existing XRecord
                Xrecord xRecord = (Xrecord)transaction.GetObject(customDict.GetAt(key), OpenMode.ForWrite);
                xRecord.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, value));
            }
            else
            {
                // Create a new XRecord to store the value
                Xrecord xRecord = new Xrecord
                {
                    Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, value))
                };
                customDict.SetAt(key, xRecord);
                transaction.AddNewlyCreatedDBObject(xRecord, true);
            }
        }

        internal static string RetrieveVariable(Transaction transaction, Database db, string key)
        {
            // Access the NamedObjectsDictionary
            DBDictionary namedObjectsDict = (DBDictionary)transaction.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            const string customDictName = "CustomVariables";

            // Check if the custom dictionary exists
            if (namedObjectsDict.Contains(customDictName))
            {
                DBDictionary customDict = (DBDictionary)transaction.GetObject(namedObjectsDict.GetAt(customDictName), OpenMode.ForRead);

                // Check if the key exists in the custom dictionary
                if (customDict.Contains(key))
                {
                    Xrecord xRecord = (Xrecord)transaction.GetObject(customDict.GetAt(key), OpenMode.ForRead);
                    TypedValue[] data = xRecord.Data.AsArray();

                    // Return the stored value
                    return data[0].Value.ToString();
                }
            }

            // Return null if the key does not exist
            return null;
        }
    }
}
