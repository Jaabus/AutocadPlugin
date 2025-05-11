using System.Windows.Controls;

namespace AutocadPlugin
{
    public partial class MainGUI : UserControl
    {
        public MainGUI()
        {
            InitializeComponent();
        }

        private void StreetSignsCategory_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            CategoryGUIContent.Content = new SignSelectorGUI();
        }

        private void RoadMarkingsCategory_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            CategoryGUIContent.Content = new RoadMarkingsGUI();
        }

        private void HatchesCategory_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            CategoryGUIContent.Content = new HatchesGUI();
        }
    }
}