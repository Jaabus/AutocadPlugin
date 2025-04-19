using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using System.Windows.Forms.Integration;
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
    }
}