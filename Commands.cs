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
