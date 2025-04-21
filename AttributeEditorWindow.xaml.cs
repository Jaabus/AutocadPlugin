using System.Collections.Generic;
using System.Windows;
using Autodesk.AutoCAD.DatabaseServices;

namespace AutocadPlugin
{
    public partial class AttributeEditorWindow : Window
    {
        public List<AttributeData> Attributes { get; set; }

        public AttributeEditorWindow(List<AttributeData> attributes)
        {
            InitializeComponent();
            Attributes = attributes;
            AttributesDataGrid.ItemsSource = Attributes;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true; // Close the window and return success
        }
    }

    public class AttributeData
    {
        public string Tag { get; set; }
        public string Value { get; set; }
        public ObjectId AttributeDefinitionId { get; set; } // Store the ObjectId of the AttributeDefinition
    }
}
