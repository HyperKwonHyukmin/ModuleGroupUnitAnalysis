using System;
using System.IO;
using System.Text;
using MooringFitting2026.Results; // F06AnalysisResult 위치

namespace MooringFitting2026.Exporters
{
  public static class ResultExporter
  {
    public static void Export(F06AnalysisResult result, string outputDir, string filePrefix)
    {
      if (result == null || result.CaseResults.Count == 0) return;

      // 저장할 폴더 생성
      Directory.CreateDirectory(outputDir);

      // 1. Displacement Export
      ExportDisplacements(result, Path.Combine(outputDir, $"{filePrefix}_Displacement.csv"));

      // 2. Beam Forces Export
      ExportBeamForces(result, Path.Combine(outputDir, $"{filePrefix}_BeamForces.csv"));

      // 3. Beam Stresses Export
      ExportBeamStresses(result, Path.Combine(outputDir, $"{filePrefix}_BeamStresses.csv"));

      Console.WriteLine($"   -> [Result Export] Saved 3 CSV files to {outputDir}");
    }

    private static void ExportDisplacements(F06AnalysisResult result, string path)
    {
      var sb = new StringBuilder();
      sb.AppendLine("LoadCase,NodeID,T1(X),T2(Y),T3(Z)"); // Header

      foreach (var kvCase in result.CaseResults)
      {
        int lc = kvCase.Key;
        var loadCaseData = kvCase.Value;

        foreach (var kvNode in loadCaseData.Displacements)
        {
          int nodeId = kvNode.Key;
          var (x, y, z) = kvNode.Value;
          sb.AppendLine($"{lc},{nodeId},{x:E6},{y:E6},{z:E6}");
        }
      }
      File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void ExportBeamForces(F06AnalysisResult result, string path)
    {
      var sb = new StringBuilder();
      sb.AppendLine("LoadCase,ElementID,Axial,ShearA,ShearB,Torque,MomentA,MomentB"); // Header

      foreach (var kvCase in result.CaseResults)
      {
        int lc = kvCase.Key;
        foreach (var kvForce in kvCase.Value.BeamForces)
        {
          var f = kvForce.Value;
          sb.AppendLine($"{lc},{f.ElementID},{f.AxialForce:E6},{f.ShearA:E6},{f.ShearB:E6},{f.TotalTorque:E6},{f.MomentA:E6},{f.MomentB:E6}");
        }
      }
      File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void ExportBeamStresses(F06AnalysisResult result, string path)
    {
      var sb = new StringBuilder();
      sb.AppendLine("LoadCase,ElementID,MaxCombined,MinCombined,MarginOfSafety"); // Header

      foreach (var kvCase in result.CaseResults)
      {
        int lc = kvCase.Key;
        foreach (var kvStress in kvCase.Value.BeamStresses)
        {
          var s = kvStress.Value;
          sb.AppendLine($"{lc},{s.ElementID},{s.MaxStressCombined:E6},{s.MinStressCombined:E6},{s.MarginOfSafety:F4}");
        }
      }
      File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }
  }
}
