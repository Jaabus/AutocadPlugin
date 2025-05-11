using System.Windows.Controls;

namespace AutocadPlugin
{
    public partial class RoadMarkingsGUI : UserControl
    {
        public RoadMarkingsGUI()
        {
            InitializeComponent();

            RoadMarkingCommands.LoadLinetypes(@"C:\Users\JAABUK\Desktop\prog\EESTI\jooned.lin");
        }
    }
}