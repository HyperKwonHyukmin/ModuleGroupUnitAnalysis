using System;
using System.IO;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Postprocess
{
  public class F06Parser
  {
    public static F06ResultData Parse(string f06FilePath, PipelineLogger logger)
    {
      var result = new F06ResultData();
      if (!File.Exists(f06FilePath)) return result;

      try
      {
        using (var reader = new StreamReader(f06FilePath))
        {
          string line;
          while ((line = reader.ReadLine()) != null)
          {
            // 1. 변위 파싱
            if (line.Contains("D I S P L A C E M E N T   V E C T O R"))
            {
              reader.ReadLine(); reader.ReadLine(); // 헤더 스킵
              while ((line = reader.ReadLine()) != null && !IsEndOfSection(line))
              {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 8 && parts[1] == "G" && int.TryParse(parts[0], out int nid))
                {
                  result.Displacements.Add(new DisplacementData
                  {
                    NodeID = nid,
                    T1 = ParseNastranDouble(parts[2]),
                    T2 = ParseNastranDouble(parts[3]),
                    T3 = ParseNastranDouble(parts[4])
                  });
                }
              }
            }
            // 2. 빔 응력 (CBEAM) 파싱
            else if (line.Contains("S T R E S S E S   I N   B E A M   E L E M E N T S"))
            {
              int currentElemId = 0;
              while ((line = reader.ReadLine()) != null && !IsEndOfSection(line))
              {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || !char.IsDigit(parts[0][0])) continue;

                if (parts.Length == 1 && int.TryParse(parts[0], out int eidOnly))
                {
                  currentElemId = eidOnly;
                  continue;
                }

                if (parts.Length >= 8)
                {
                  int tokenIdx = 0;
                  if (line.StartsWith(" ") && line.Length > 14 && line.Substring(0, 14).Trim().Length > 0)
                  {
                    if (int.TryParse(parts[tokenIdx], out int eid))
                    {
                      currentElemId = eid;
                      tokenIdx++;
                    }
                  }

                  // S-MAX, S-MIN 위치 추정 (뒤에서 2번째, 3번째 등 포맷에 맞춰)
                  double smax = ParseNastranDouble(parts[parts.Length - 4]);
                  double smin = ParseNastranDouble(parts[parts.Length - 3]);

                  result.BeamStresses.Add(new BeamStressData
                  {
                    ElementID = currentElemId,
                    MaxStress = smax,
                    MinStress = smin
                  });
                }
              }
            }
            // 3. 와이어 장력 (CROD) 파싱
            else if (line.Contains("F O R C E S   I N   R O D   E L E M E N T S"))
            {
              reader.ReadLine(); reader.ReadLine(); // 헤더 스킵
              while ((line = reader.ReadLine()) != null && !IsEndOfSection(line))
              {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[0], out int eid))
                {
                  result.RodForces.Add(new RodForceData
                  {
                    ElementID = eid,
                    AxialForce = ParseNastranDouble(parts[1])
                  });
                }
              }
            }
          }
        }
        result.IsParsedSuccessfully = true;
      }
      catch { }
      return result;
    }

    private static bool IsEndOfSection(string line)
    {
      if (string.IsNullOrWhiteSpace(line)) return false;
      if (line.Contains("PAGE") || line.Contains("D I S P L A C E M E N T") || line.Contains("S T R E S S") || line.Contains("F O R C E S")) return true;
      return false;
    }

    private static double ParseNastranDouble(string val)
    {
      if (val.Contains("-") && !val.StartsWith("-") && !val.Contains("E-")) val = val.Replace("-", "E-");
      if (val.Contains("+") && !val.StartsWith("+") && !val.Contains("E+")) val = val.Replace("+", "E+");
      double.TryParse(val, out double result);
      return result;
    }
  }
}
