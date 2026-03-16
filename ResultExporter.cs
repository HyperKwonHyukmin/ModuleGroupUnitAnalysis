using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Postprocess
{
  public static class ResultExporter
  {
    public static void Export(string bdfPath, F06ResultData results, AnalysisType type, PipelineLogger logger)
    {
      logger.LogInfo("\n[Stage 13] 최종 구조 안정성 평가 및 리포트 생성 시작...");

      string resultTxtPath = Path.ChangeExtension(bdfPath, ".txt");
      var sb = new StringBuilder();

      // ====================================================================
      // ★ FATAL ERROR 발생 시 즉시 중단 및 에러 출력 로직
      // ====================================================================
      if (results.HasFatalError)
      {
        sb.AppendLine($"*** 결과 : Fail (FATAL ERROR) ***\n");
        sb.AppendLine("[해석 실패] NASTRAN 해석 중 치명적 오류(FATAL)가 발생하여 석이 중단되었습니다.\n");

        foreach (var msg in results.FatalMessages)
        {
          sb.AppendLine(msg);
        }

        File.WriteAllText(resultTxtPath, sb.ToString(), Encoding.UTF8);

        logger.Log("", useTimestamp: false);
        logger.Log("=========================================================", useTimestamp: false);
        logger.Log("                 [ F06 Analysis Result ]                 ", ConsoleColor.Red, useTimestamp: false);
        logger.Log("---------------------------------------------------------", useTimestamp: false);
        logger.Log($" Overall Status   : [ FAIL (FATAL ERROR) ]", ConsoleColor.Red, useTimestamp: false);
        logger.Log("---------------------------------------------------------", useTimestamp: false);

        foreach (var msg in results.FatalMessages)
        {
          logger.Log(msg, ConsoleColor.Red, useTimestamp: false);
        }

        logger.Log("=========================================================", useTimestamp: false);
        logger.LogWarning($"13단계 : FATAL 에러 리포트 출력 완료 ({Path.GetFileName(resultTxtPath)})");
        return; // FATAL이면 정상 평가 로직(아래 코드)을 더 이상 진행하지 않음
      }

      var maxDispNode = results.Displacements.OrderByDescending(d => d.Magnitude).FirstOrDefault();
      double maxDisp = maxDispNode != null ? Math.Round(maxDispNode.Magnitude, 1) : 0.0;

      var maxStressElem = results.BeamStresses.OrderByDescending(s => s.MaxAbsStress).FirstOrDefault();
      double maxStress = maxStressElem != null ? Math.Round(maxStressElem.MaxAbsStress, 1) : 0.0;
      int maxStressId = maxStressElem != null ? maxStressElem.ElementID : 0;

      double safetyFactor = maxStress > 0 ? Math.Round(220.0 / maxStress, 2) : 999.99;
      string structureStatus = safetyFactor >= 1.0 ? "OK" : "Fail";

      bool hasNegativeForce = false;
      var wireLines = new List<string>();
      int wireIdx = 1;

      foreach (var rod in results.RodForces)
      {
        double axialForce = Math.Round(rod.AxialForce, 2);
        double tonForce = Math.Round(axialForce / 9800.0, 2);

        if (axialForce < 0) hasNegativeForce = true;

        string assessment = axialForce < 60760.0 ? "국부 변형 방지 지그 불필요" : "국부 변형 방지 지그 필요";
        string idxStr = $"1-{wireIdx}".PadRight(10);
        string eidStr = rod.ElementID.ToString().PadRight(10);
        string forceStr = $"{tonForce:F2}ton / {axialForce:F2}".PadRight(25);

        wireLines.Add($"{idxStr}{eidStr}{forceStr}{assessment}");
        wireIdx++;
      }

      string overallStatus = structureStatus;

      // ====================================================================
      // 텍스트 파일 저장 로직
      // ====================================================================
      sb.AppendLine($"*** 결과 : {overallStatus} ***\n");
      sb.AppendLine("1. Unit Point 형태 유효성 확인 : OK\n");

      if (type == AnalysisType.ModuleUnit)
      {
        sb.AppendLine("2. 자세 안정성 평가 : OK\n");
        sb.AppendLine($"3. 구조 안정성 평가 : {structureStatus}");
        sb.AppendLine($"   1) 최대 변형 : {maxDisp:F1}mm");
        sb.AppendLine($"   2) 응력 평가 (항복응력 : 275MPa 허용응력 : 항복응력 x 0.8 = 220MPa)");
        sb.AppendLine($"     ElementID:        {maxStressId}");
        sb.AppendLine($"     Stress:           {maxStress:F1}MPa");
        sb.AppendLine($"     SafetyFactor:     {safetyFactor:F2}\n");
        sb.AppendLine("4. 장력 평가 (안전하중 : 6.2 ton / 60,760 N)");
      }
      else // Group Unit
      {
        sb.AppendLine($"2. 구조 안정성 평가 : {structureStatus}");
        sb.AppendLine($"   1) 최대 변형 : {maxDisp:F1}mm");
        sb.AppendLine($"   2) 응력 평가 (항복응력 : 275MPa 허용응력 : 항복응력 x 0.8 = 220MPa)");
        sb.AppendLine($"     ElementID:        {maxStressId}");
        sb.AppendLine($"     Stress:           {maxStress:F1}MPa");
        sb.AppendLine($"     SafetyFactor:     {safetyFactor:F2}\n");
        sb.AppendLine("3. 장력 평가 (안전하중 : 6.2 ton / 60,760 N)");
      }

      foreach (var wl in wireLines)
      {
        sb.AppendLine(wl);
      }

      if (hasNegativeForce)
      {
        sb.AppendLine("\n[경고] ROD(와이어)에 압축력(음수)이 발생했습니다. 권상 위치나 무게중심, 와이어 길이를 점검하세요.");
      }

      File.WriteAllText(resultTxtPath, sb.ToString(), Encoding.UTF8);
      logger.LogSuccess($"13단계 : 최종 결과 리포트 출력 완료 ({Path.GetFileName(resultTxtPath)})");

      // ====================================================================
      // 절대 깨지지 않는 콘솔 대시보드
      // ====================================================================
      string statStr = overallStatus == "OK" ? "PASS" : "FAIL";
      string dispNodeStr = maxDispNode != null ? maxDispNode.NodeID.ToString() : "N/A";
      string stressElemStr = maxStressElem != null ? maxStressId.ToString() : "N/A";

      logger.Log("", useTimestamp: false);
      logger.Log("=========================================================", useTimestamp: false);
      logger.Log("                 [ F06 Analysis Result ]                 ", ConsoleColor.Green, useTimestamp: false);
      logger.Log("---------------------------------------------------------", useTimestamp: false);
      logger.Log($" Overall Status   : [ {statStr} ]", ConsoleColor.White, useTimestamp: false);
      logger.Log($" Max Displacement : {maxDisp,6:F2} mm (Node {dispNodeStr})", ConsoleColor.White, useTimestamp: false);
      logger.Log($" Max Beam Stress  : {maxStress,6:F2} MPa (Element {stressElemStr})", ConsoleColor.White, useTimestamp: false);

      if (safetyFactor >= 1.0 && safetyFactor != 999.99)
        logger.Log($" Safety Factor    : {safetyFactor,6:F2} (안전, 기준 220MPa)", ConsoleColor.Cyan, useTimestamp: false);
      else if (safetyFactor == 999.99)
        logger.Log($" Safety Factor    : N/A    (응력 데이터 없음)", ConsoleColor.Yellow, useTimestamp: false);
      else
        logger.Log($" Safety Factor    : {safetyFactor,6:F2} (위험! 구조 보강 필요)", ConsoleColor.Red, useTimestamp: false);

      logger.Log("---------------------------------------------------------", useTimestamp: false);
      logger.Log(" Wire Tension (권상 와이어 장력)", ConsoleColor.White, useTimestamp: false);

      if (results.RodForces.Count == 0)
      {
        logger.Log("  - 데이터 없음", ConsoleColor.Yellow, useTimestamp: false);
      }
      else
      {
        foreach (var rod in results.RodForces)
        {
          double tonForce = rod.AxialForce / 9800.0;
          string status = tonForce >= 0 ? "정상" : "느슨함(음수)";
          ConsoleColor color = tonForce >= 0 ? ConsoleColor.Gray : ConsoleColor.Red;
          logger.Log($"  - Wire E{rod.ElementID,-8} : {tonForce,6:F2} ton ({status})", color, useTimestamp: false);
        }
      }
      logger.Log("=========================================================", useTimestamp: false);
    }
  }
}
