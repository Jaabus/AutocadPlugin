using System.Windows.Forms.Integration;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.ApplicationServices;
using WF = System.Windows.Forms;

namespace AutocadPlugin
{
    public class Commands
    {
        private static PaletteSet _paletteSet;

        [CommandMethod("RD_DrawGUI")]
        public static void DrawGUI()
        {
            if (_paletteSet == null)
            {
                // Create a new PaletteSet
                _paletteSet = new PaletteSet("Street signs and road markings")
                {
                };

                // Add control to PaletteSet
                var mainGUI = new MainGUI();
                var host = new ElementHost
                {
                    Dock = WF.DockStyle.Fill,
                    Child = mainGUI,
                };

                _paletteSet.Add("Sign Selector", host);
            }

            // Set paletteset properties
            _paletteSet.Visible = true;
            _paletteSet.Size = new System.Drawing.Size(450, 300);
        }

        [CommandMethod("RD_ResetGUI")]

        public static void ResetGUI()
        {
            if (_paletteSet != null)
            {
                bool wasVisible = _paletteSet.Visible;
                System.Drawing.Point location = _paletteSet.Location;
                System.Drawing.Size size = _paletteSet.Size;

                _paletteSet.Visible = false;
                _paletteSet.Dispose();
                _paletteSet = null;

                // Recreate the PaletteSet
                DrawGUI();

                // Restore visibility state
                if (!wasVisible)
                {
                    _paletteSet.Visible = false;
                }
            }
        }

        [CommandMethod("RD_ReadXData")]

        public static void ReadXData()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Editor editor = document.Editor;

            // Prompt user to select entity to read from
            if (!CommandUtilites.TryPromptEntity(editor, new PromptEntityOptions("\nSelect an entity to read xdata from: "), out ObjectId entityId))
            {
                editor.WriteMessage("\nNo entity selected.");
                return;
            }

            if (entityId == ObjectId.Null)
            {
                editor.WriteMessage("\nNo entity selected.");
                return;
            }

            using (Transaction transaction = document.Database.TransactionManager.StartTransaction())
            {
                try
                {
                    // Open the selected entity for read
                    Entity entity = transaction.GetObject(entityId, OpenMode.ForRead) as Entity;

                    if (entity == null)
                    {
                        editor.WriteMessage("\nSelected object is not a valid entity.");
                        return;
                    }

                    // Retrieve the xdata
                    ResultBuffer xdata = entity.XData;

                    if (xdata == null)
                    {
                        editor.WriteMessage("\nNo xdata found for the selected entity.");
                    }
                    else
                    {
                        // Print xdata to the editor
                        foreach (TypedValue value in xdata)
                        {
                            editor.WriteMessage($"\nType: {value.TypeCode}, Value: {value.Value}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    editor.WriteMessage($"\nError: {ex.Message}");
                }
                finally
                {
                    transaction.Commit();
                }
            }
        }
    }
}
