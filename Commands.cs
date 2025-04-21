using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms.Integration;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
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
            var targetDb = document.Database;

            // Lock the document for editing
            using (DocumentLock docLock = document.LockDocument())
            {
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

                        // Insert the DWG as a block
                        ObjectId blockId = targetDb.Insert(Path.GetFileName(signPath), signDb, false);

                        // Create a block reference
                        using (BlockReference blockRef = new BlockReference(Point3d.Origin, blockId))
                        {
                            modelSpace.AppendEntity(blockRef);
                            transaction.AddNewlyCreatedDBObject(blockRef, true);

                            // Retrieve the block definition
                            BlockTableRecord blockDef = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);

                            // Collect attributes
                            List<AttributeData> attributes = new List<AttributeData>();
                            foreach (ObjectId attDefId in blockDef)
                            {
                                if (attDefId.ObjectClass == RXClass.GetClass(typeof(AttributeDefinition)))
                                {
                                    AttributeDefinition attDef = (AttributeDefinition)transaction.GetObject(attDefId, OpenMode.ForRead);
                                    if (!attDef.Constant)
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

                            // Open the modal window for editing attributes
                            AttributeEditorWindow editorWindow = new AttributeEditorWindow(attributes);
                            if (editorWindow.ShowDialog() == true)
                            {
                                // Apply updated attributes
                                foreach (var attribute in editorWindow.Attributes)
                                {
                                    AttributeDefinition attDef = (AttributeDefinition)transaction.GetObject(attribute.AttributeDefinitionId, OpenMode.ForRead);
                                    AttributeReference attRef = new AttributeReference();
                                    attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);
                                    attRef.TextString = attribute.Value;
                                    blockRef.AttributeCollection.AppendAttribute(attRef);
                                    transaction.AddNewlyCreatedDBObject(attRef, true);
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
        }
    }
}