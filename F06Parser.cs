using System;
using System.IO;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Postprocess
{
  public class F06Parser
  {
    private enum ParseState { None, Displacement, BeamStress, RodForce }

    public static F06ResultData Parse(string f06FilePath, PipelineLogger logger)
    {
      var result = new F06ResultData();
      if (!File.Exists(f06FilePath)) return result;

      try
      {
        var lines = File.ReadAllLines(f06FilePath);
        ParseState state = ParseState.None;

        int currentElemId = 0;

        foreach (string rawLine in lines)
        {
          if (string.IsNullOrWhiteSpace(rawLine) || rawLine.Contains("PAGE")) continue;

          string line = rawLine;
          if (line.Length > 0 && (line[0] == '0' || line[0] == '1' || line[0] == '+'))
          {
            line = " " + line.Substring(1);
          }

          string trimmed = line.Trim();
          if (trimmed.Length == 0) continue;

          // ==========================================================
          // ★ 1. 상태 전환 트리거 (Nastran 헤더 완벽 인식)
          // ==========================================================
          if (trimmed.Contains("D I S P L A C E M E N T   V E C T O R")) { state = ParseState.Displacement; continue; }
          else if (trimmed.Contains("S T R E S S E S   I N   B E A M   E L E M E N T S")) { state = ParseState.BeamStress; continue; }
          else if (trimmed.Contains("F O R C E S   I N   R O D   E L E M E N T S")) { state = ParseState.RodForce; continue; }

          // ★ [수정됨] SPC Forces, Applied Loads 등 다른 블록이 나오면 무조건 파싱을 중단(None)합니다!
          else if (trimmed.Contains("F O R C E S   O F   S I N G L E - P O I N T") ||
                   trimmed.Contains("A P P L I E D   L O A D S") ||
                   trimmed.Contains("L O A D   V E C T O R") ||
                   trimmed.Contains("O U T P U T   F R O M") ||
                   trimmed.Contains("E L E M E N T   S T R A I N   E N E R G I E S"))
          {
            state = ParseState.None;
            continue;
          }

          // 데이터가 아닌 텍스트 라인은 건너뜀
          if (!char.IsDigit(trimmed[0]) && trimmed[0] != '-') continue;

          var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

          // ==========================================================
          // 2. 변위 파싱
          // ==========================================================
          if (state == ParseState.Displacement)
          {
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
          // ==========================================================
          // 3. 빔 응력(CBEAM) 파싱
          // ==========================================================
          else if (state == ParseState.BeamStress)
          {
            int leadingSpaces = 0;
            foreach (char c in line) { if (c == ' ') leadingSpaces++; else break; }

            int tokenIdx = 0;
            bool hasElementID = (leadingSpaces < 14);

            if (hasElementID)
            {
              if (int.TryParse(parts[tokenIdx], out int eid))
              {
                currentElemId = eid;
                tokenIdx++;
              }
            }

            if (tokenIdx + 5 < parts.Length)
            {
              if (!parts[tokenIdx].Contains(".") && int.TryParse(parts[tokenIdx], out _))
              {
                tokenIdx++;
              }

              tokenIdx++;

              if (tokenIdx + 5 < parts.Length && currentElemId > 0)
              {
                double smax = ParseNastranDouble(parts[tokenIdx + 4]);
                double smin = ParseNastranDouble(parts[tokenIdx + 5]);

                result.BeamStresses.Add(new BeamStressData
                {
                  ElementID = currentElemId,
                  MaxStress = smax,
                  MinStress = smin
                });
              }
            }
          }
          // ==========================================================
          // 4. 와이어 장력(CROD) 파싱
          // ==========================================================
          else if (state == ParseState.RodForce)
          {
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
        result.IsParsedSuccessfully = true;
      }
      catch (Exception ex)
      {
        logger.LogError($"F06 파싱 중 오류 발생: {ex.Message}");
      }
      return result;
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
