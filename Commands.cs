using System.Windows.Forms.Integration;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using WF = System.Windows.Forms;

namespace AutocadPlugin
{
    public class Commands
    {
        private static PaletteSet _paletteSet;

        [CommandMethod("PS_DrawGUI")]
        public static void DrawGUI()
        {
            if (_paletteSet == null)
            {
                // Create a new PaletteSet
                _paletteSet = new PaletteSet("Street signs and markings")
                {
                    Size = new System.Drawing.Size(500, 200)
                };

                // Add control to PaletteSet
                var mainGUI = new MainGUI();
                var host = new ElementHost
                {
                    Dock = WF.DockStyle.Fill,
                    Child = mainGUI
                };

                _paletteSet.Add("Sign Selector", host);
            }

            // Show the PaletteSet
            _paletteSet.Visible = true;
        }

        [CommandMethod("PS_ResetGUI")]

        public static void ResetGUI()
        {
            if (_paletteSet != null)
            {
                bool wasVisible = _paletteSet.Visible;

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
    }
}
