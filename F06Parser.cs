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
          // 1. 상태 전환 트리거 (Nastran 고유 헤더만 정확히 매칭)
          // ==========================================================
          if (trimmed.Contains("D I S P L A C E M E N T   V E C T O R")) { state = ParseState.Displacement; continue; }
          else if (trimmed.Contains("S T R E S S E S   I N   B E A M   E L E M E N T S")) { state = ParseState.BeamStress; continue; }
          else if (trimmed.Contains("F O R C E S   I N   R O D   E L E M E N T S")) { state = ParseState.RodForce; continue; }

          // ★ 반력 등 명확히 다른 데이터 테이블이 나올 때만 파싱을 초기화합니다.
          else if (trimmed.Contains("F O R C E S   O F   S I N G L E - P O I N T") ||
                   trimmed.Contains("S P C   F O R C E S") ||
                   trimmed.Contains("A P P L I E D   L O A D S"))
          {
            state = ParseState.None;
            continue;
          }

          // 데이터 라인이 아니면 스킵 (반드시 숫자나 '-'로 시작)
          if (!char.IsDigit(trimmed[0]) && trimmed[0] != '-') continue;

          var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
          if (parts.Length == 0) continue;

          // ==========================================================
          // 2. 파싱 로직
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
          else if (state == ParseState.BeamStress)
          {
            // Element ID 단독 라인 처리
            if (parts.Length == 1 && int.TryParse(parts[0], out int onlyId))
            {
              currentElemId = onlyId;
              continue;
            }

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
                tokenIdx++; // Grid 스킵

              tokenIdx++; // Station 스킵

              if (tokenIdx + 5 < parts.Length && currentElemId > 0)
              {
                result.BeamStresses.Add(new BeamStressData
                {
                  ElementID = currentElemId,
                  MaxStress = ParseNastranDouble(parts[tokenIdx + 4]),
                  MinStress = ParseNastranDouble(parts[tokenIdx + 5])
                });
              }
            }
          }
          else if (state == ParseState.RodForce)
          {
            // Nastran CROD는 한 줄에 데이터가 2열로 나올 수 있음!
            if (parts.Length >= 2 && int.TryParse(parts[0], out int eid1))
              result.RodForces.Add(new RodForceData { ElementID = eid1, AxialForce = ParseNastranDouble(parts[1]) });

            if (parts.Length >= 5 && int.TryParse(parts[3], out int eid2))
              result.RodForces.Add(new RodForceData { ElementID = eid2, AxialForce = ParseNastranDouble(parts[4]) });
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
