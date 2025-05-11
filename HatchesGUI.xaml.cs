using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AutocadPlugin.Properties;

namespace AutocadPlugin
{
    public partial class HatchesGUI : UserControl
    {
        public HatchesGUI()
        {
            InitializeComponent();
        }

        private void HatchFromPolylineButton_Click(object sender, RoutedEventArgs e)
        {
            HatchCommands.HatchFromPolyline();
        }
    }
}
