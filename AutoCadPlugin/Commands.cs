using Autodesk.AutoCAD.Runtime;
using System;
using System.Linq;

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
  }
}