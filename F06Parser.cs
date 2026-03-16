using System;
using System.IO;
using System.Linq;
using System.Globalization;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Postprocess
{
  public class F06Parser
  {
    private enum ParseState { None, Displacement, BeamStress, RodForce }

    // debugPrint 파라미터 추가 (기본값 true)
    public static F06ResultData Parse(string f06FilePath, PipelineLogger logger, bool debugPrint = true)
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
          // 빈 줄이나 페이지 번호 줄은 무시 (PAGE 때문에 헤더가 통째로 스킵되는 문제 해결)
          if (string.IsNullOrWhiteSpace(rawLine) || rawLine.Contains("PAGE")) continue;

          string line = rawLine;
          // Nastran 캐리지 제어 문자(0, 1, +) 처리
          if (line.Length > 0 && (line[0] == '0' || line[0] == '1' || line[0] == '+'))
          {
            line = " " + line.Substring(1);
          }

          string trimmed = line.Trim();
          if (trimmed.Length == 0) continue;

          // ==========================================================
          // 1. 상태 전환 트리거 (공백을 제거한 후 비교하여 정확도 향상)
          // ==========================================================
          string noSpaceUpper = trimmed.Replace(" ", "").ToUpper();

          if (noSpaceUpper.Contains("DISPLACEMENTVECTOR") || noSpaceUpper.Contains("DISPLACEMENTSVECTOR"))
          {
            state = ParseState.Displacement;
            continue;
          }
          else if (noSpaceUpper.Contains("STRESSESINBEAMELEMENTS"))
          {
            state = ParseState.BeamStress;
            continue;
          }
          else if (noSpaceUpper.Contains("FORCESINRODELEMENTS"))
          {
            state = ParseState.RodForce;
            continue;
          }
          // 반력(SPC Forces)이나 하중 등 다른 테이블이 나오면 즉시 변위 파싱 중단
          else if (noSpaceUpper.Contains("FORCESOFSINGLE-POINT") ||
                   noSpaceUpper.Contains("SPCFORCES") ||
                   noSpaceUpper.Contains("APPLIEDLOADS") ||
                   noSpaceUpper.Contains("LOADVECTOR") ||
                   noSpaceUpper.Contains("FORCESINBEAMELEMENTS"))
          {
            state = ParseState.None;
            continue;
          }

          // 데이터 라인이 아니면 스킵 (반드시 숫자나 '-', '.'으로 시작해야 함)
          if (!char.IsDigit(trimmed[0]) && trimmed[0] != '-' && trimmed[0] != '.') continue;

          var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
          if (parts.Length == 0) continue;

          // ==========================================================
          // 2. 파싱 로직
          // ==========================================================
          if (state == ParseState.Displacement)
          {
            // 변위 데이터 형식: NodeID, TYPE(G/S), T1, T2, T3, R1, R2, R3
            if (parts.Length >= 8 && (parts[1] == "G" || parts[1] == "S") && int.TryParse(parts[0], out int nid))
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
            // CBEAM 요소 ID 단독 라인 처리
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
              // Grid 또는 Station 정보 스킵
              if (!parts[tokenIdx].Contains(".") && int.TryParse(parts[tokenIdx], out _))
                tokenIdx++;

              tokenIdx++; // DIST/ Station 스킵

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
            // CROD 데이터 처리
            if (parts.Length >= 2 && int.TryParse(parts[0], out int eid1))
              result.RodForces.Add(new RodForceData { ElementID = eid1, AxialForce = ParseNastranDouble(parts[1]) });

            if (parts.Length >= 5 && int.TryParse(parts[3], out int eid2))
              result.RodForces.Add(new RodForceData { ElementID = eid2, AxialForce = ParseNastranDouble(parts[4]) });
          }
        }
        result.IsParsedSuccessfully = result.Displacements.Count > 0 || result.BeamStresses.Count > 0 || result.RodForces.Count > 0;
      }
      catch (Exception ex)
      {
        logger.LogError($"F06 파싱 중 오류 발생: {ex.Message}");
      }

      // ★ 파싱 성공 시 디버그 요약 정보 출력
      if (debugPrint && result.IsParsedSuccessfully)
      {
        PrintDebugSummary(result, logger);
      }

      return result;
    }

    private static double ParseNastranDouble(string val)
    {
      val = val.Trim();
      if (string.IsNullOrEmpty(val)) return 0.0;

      // Nastran 특유의 지수 표현 처리 (e.g., 1.234-5 -> 1.234E-5)
      int signPos = val.IndexOfAny(new[] { '+', '-' }, 1);
      if (signPos > 0 && !val.Contains("E") && !val.Contains("e"))
      {
        val = val.Insert(signPos, "E");
      }

      if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
      {
        return result;
      }
      return 0.0;
    }

    // ==========================================================
    // ★ 파싱 결과 디버깅 출력용 메서드
    // ==========================================================
    private static void PrintDebugSummary(F06ResultData result, PipelineLogger logger)
    {
      logger.LogInfo("\n=========================================================");
      logger.LogInfo("               [ F06 Parsing Debug Summary ]             ");
      logger.LogInfo("=========================================================");
      logger.LogInfo($" - 파싱된 Displacement 데이터 수: {result.Displacements.Count} 개");
      logger.LogInfo($" - 파싱된 Beam Stress 데이터 수 : {result.BeamStresses.Count} 개");
      logger.LogInfo($" - 파싱된 Rod Force 데이터 수   : {result.RodForces.Count} 개\n");

      if (result.Displacements.Count > 0)
      {
        logger.LogInfo(" [Top 3 Max Displacements]");
        var topDisps = result.Displacements.OrderByDescending(d => d.Magnitude).Take(3);
        foreach (var d in topDisps)
          logger.LogInfo($"   -> Node {d.NodeID,-8} | Mag: {d.Magnitude,8:F2} (T1:{d.T1,8:F2}, T2:{d.T2,8:F2}, T3:{d.T3,8:F2})");
      }

      if (result.BeamStresses.Count > 0)
      {
        logger.LogInfo("\n [Top 3 Max Beam Stresses]");
        var topStresses = result.BeamStresses.OrderByDescending(s => s.MaxAbsStress).Take(3);
        foreach (var s in topStresses)
          logger.LogInfo($"   -> Element {s.ElementID,-5} | MaxAbs: {s.MaxAbsStress,8:F2} (Max:{s.MaxStress,8:F2}, Min:{s.MinStress,8:F2})");
      }

      if (result.RodForces.Count > 0)
      {
        logger.LogInfo("\n [Rod Axial Forces (Wire Tension)]");
        foreach (var r in result.RodForces)
          logger.LogInfo($"   -> Element {r.ElementID,-5} | Axial Force: {r.AxialForce,10:F2} N");
      }
      logger.LogInfo("=========================================================\n");
    }
  }
}
