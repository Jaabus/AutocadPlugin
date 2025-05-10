using AutocadPlugin;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

public static class PluginInitializer
{   
    static PluginInitializer()
    {
        // Subscribe to the DocumentActivated event
        Application.DocumentManager.DocumentActivated += OnDocumentActivated;
    }

    private static void OnDocumentActivated(object sender, DocumentCollectionEventArgs e)
    {
        // Get the active document
        Document activeDocument = e.Document;
        if (activeDocument == null) return;

        Database db = activeDocument.Database;

        // Initialize values in the database
        using (Transaction transaction = db.TransactionManager.StartTransaction())
        {
            // Example: Set default values for signScale and signPostScale
            CommandUtilites.StoreVariable(transaction, db, "signScale", Constants.defaultSignScale);
            CommandUtilites.StoreVariable(transaction, db, "signPostScale", Constants.defaultSignPostScale);

            transaction.Commit();
        }
    }
}
