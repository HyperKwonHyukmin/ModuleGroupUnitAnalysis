using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ModuleGroupUnitAnalysis.Pipeline.Postprocess
{
  public static class ResultExporter
  {
    public static void ExportTxtReport(string f06Path, F06ResultData results)
    {
      string txtPath = Path.ChangeExtension(f06Path, ".txt");
      var sb = new StringBuilder();

      var maxDisp = results.Displacements.OrderByDescending(d => d.Magnitude).FirstOrDefault();
      var maxStress = results.BeamStresses.OrderByDescending(s => s.MaxAbsStress).FirstOrDefault();

      sb.AppendLine("==================================================");
      sb.AppendLine("           MODULE UNIT LIFTING ANALYSIS           ");
      sb.AppendLine("==================================================");
      sb.AppendLine($"1. Max Displacement : {(maxDisp != null ? maxDisp.Magnitude.ToString("F2") : "0")} mm");
      sb.AppendLine($"2. Max Beam Stress  : {(maxStress != null ? maxStress.MaxAbsStress.ToString("F2") : "0")} MPa");
      sb.AppendLine($"3. Safety Factor    : {(maxStress != null && maxStress.MaxAbsStress > 0 ? (220.0 / maxStress.MaxAbsStress).ToString("F2") : "N/A")}");
      sb.AppendLine("\n[Wire Tension Results]");

      foreach (var rod in results.RodForces)
      {
        double tonForce = Math.Round(rod.AxialForce / 9800.0, 2);
        sb.AppendLine($"- Wire E{rod.ElementID}: {tonForce} ton (Axial: {rod.AxialForce} N)");
      }

      File.WriteAllText(txtPath, sb.ToString(), Encoding.UTF8);
    }
  }
}
