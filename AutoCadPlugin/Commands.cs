using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Customization;
using System;
using System.Linq;
using System.Security.Cryptography;

namespace AutoCadPlugin
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

    [CommandMethod("PS_DrawToolbar")]
    public void DrawToolbar()
    {
      CustomizationSection cs = new CustomizationSection();
      Toolbar mainToolbar = new Toolbar("Main Toolbar", cs.MenuGroup);
      mainToolbar.ElementID = "EID_MainToolbar";
      mainToolbar.ToolbarOrient = ToolbarOrient.floating;
      mainToolbar.ToolbarVisible = ToolbarVisible.show;

      ToolbarButton button1 = new ToolbarButton(mainToolbar, 1);
      button1.MacroID = "ID_Pline";
    }
  }
}