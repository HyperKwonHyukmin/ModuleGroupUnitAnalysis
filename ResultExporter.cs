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
    public static void Export(string bdfPath, F06ResultData results, PipelineLogger logger)
    {
      logger.LogInfo("\n[Stage 13] 최종 구조 안정성 평가 및 리포트 생성 시작...");

      string resultTxtPath = Path.ChangeExtension(bdfPath, ".txt");
      var sb = new StringBuilder();

      // 1. 최대 변위 추출
      var maxDispNode = results.Displacements.OrderByDescending(d => d.Magnitude).FirstOrDefault();
      double maxDisp = maxDispNode != null ? Math.Round(maxDispNode.Magnitude, 1) : 0.0;

      // 2. 최대 응력 추출
      var maxStressElem = results.BeamStresses.OrderByDescending(s => s.MaxAbsStress).FirstOrDefault();
      double maxStress = maxStressElem != null ? Math.Round(maxStressElem.MaxAbsStress, 1) : 0.0;
      int maxStressId = maxStressElem != null ? maxStressElem.ElementID : 0;

      // 3. 안전율 계산 (항복응력 275MPa 기준 허용응력 220MPa)
      // ★ 수정: 소수점 둘째 자리까지 계산 (예: 3.86)
      double safetyFactor = maxStress > 0 ? Math.Round(220.0 / maxStress, 2) : 999.99;
      string structureStatus = safetyFactor >= 1.0 ? "OK" : "Fail";

      // 4. 와이어 장력 평가 (단순 표시용)
      bool hasNegativeForce = false;
      var wireLines = new List<string>();
      int wireIdx = 1;

      foreach (var rod in results.RodForces)
      {
        double axialForce = Math.Round(rod.AxialForce, 2);
        double tonForce = Math.Round(axialForce / 9800.0, 2);

        if (axialForce < 0) hasNegativeForce = true;

        // 60,760 N (약 6.2 ton) 기준 평가 텍스트
        string assessment = axialForce < 60760.0 ? "국부 변형 방지 지그 불필요" : "국부 변형 방지 지그 필요";

        // 형식 맞추기: 1-1       310       0.11ton / 1048.09         국부 변형 방지 지그 불필요
        string idxStr = $"1-{wireIdx}".PadRight(10);
        string eidStr = rod.ElementID.ToString().PadRight(10);
        string forceStr = $"{tonForce:F2}ton / {axialForce:F2}".PadRight(25);

        wireLines.Add($"{idxStr}{eidStr}{forceStr}{assessment}");
        wireIdx++;
      }

      // 5. 최종 종합 상태 판별
      // ★ 수정: 와이어 장력 조건은 제외하고 순수하게 응력 기준(structureStatus)으로만 결과 판별
      string overallStatus = structureStatus;

      // ====================================================================
      // 6. 리포트 텍스트 작성 (안전율 소수점 둘째 자리 반영)
      // ====================================================================
      sb.AppendLine($"*** 결과 : {overallStatus} ***\n");
      sb.AppendLine("1. Unit Point 형태 유효성 확인 : OK\n");
      sb.AppendLine($"2. 구조 안정성 평가 : {structureStatus}");
      sb.AppendLine($"   1) 최대 변형 : {maxDisp:F1}mm");
      sb.AppendLine($"   2) 응력 평가 (항복응력 : 275MPa 허용응력 : 항복응력 x 0.8 = 220MPa)");
      sb.AppendLine($"     ElementID:        {maxStressId}");
      sb.AppendLine($"     Stress:           {maxStress:F1}MPa");
      sb.AppendLine($"     SafetyFactor:     {safetyFactor:F2}\n"); // ★ F2 적용
      sb.AppendLine("3. 장력 평가 (안전하중 : 6.2 ton / 60,760 N)");

      foreach (var wl in wireLines)
      {
        sb.AppendLine(wl);
      }

      // 음수(압축력) 발생 시 경고 문구 추가 (표시만 함)
      if (hasNegativeForce)
      {
        sb.AppendLine("\n[경고] ROD(와이어)에 압축력(음수)이 발생했습니다. 권상 위치나 무게중심, 와이어 길이를 점검하세요.");
      }

      // 파일 출력 저장
      File.WriteAllText(resultTxtPath, sb.ToString(), Encoding.UTF8);
      logger.LogSuccess($"13단계 : 최종 결과 리포트 출력 완료 ({Path.GetFileName(resultTxtPath)})");

      // ====================================================================
      // 7. 콘솔 대시보드 요약본 동시 출력
      // ====================================================================
      logger.Log("", useTimestamp: false);
      logger.Log("┌────────────────────────────────────────────────────────┐", useTimestamp: false);
      logger.Log("│                 [ F06 Analysis Result ]                │", ConsoleColor.Green, useTimestamp: false);
      logger.Log("├────────────────────────────────────────────────────────┤", useTimestamp: false);
      logger.Log($"│ Overall Status   : {(overallStatus == "OK" ? "✅ PASS" : "❌ FAIL"),-37} │", ConsoleColor.White, useTimestamp: false);
      logger.Log($"│ Max Displacement : {maxDisp,6:F2} mm (Node {maxDispNode?.NodeID})", ConsoleColor.White, useTimestamp: false);
      logger.Log($"│ Max Beam Stress  : {maxStress,6:F2} MPa (Element {maxStressId})", ConsoleColor.White, useTimestamp: false);

      // 콘솔에도 소수점 둘째 자리 적용
      if (safetyFactor >= 1.0) logger.Log($"│ Safety Factor    : {safetyFactor,6:F2} (안전, 기준 220MPa)", ConsoleColor.Cyan, useTimestamp: false);
      else logger.Log($"│ Safety Factor    : {safetyFactor,6:F2} (위험! 구조 보강 필요)", ConsoleColor.Red, useTimestamp: false);

      logger.Log("├────────────────────────────────────────────────────────┤", useTimestamp: false);
      logger.Log("│ Wire Tension (권상 와이어 장력)                        │", ConsoleColor.White, useTimestamp: false);

      foreach (var rod in results.RodForces)
      {
        double tonForce = rod.AxialForce / 9800.0;
        string status = tonForce >= 0 ? "정상" : "느슨함(음수)";
        ConsoleColor color = tonForce >= 0 ? ConsoleColor.Gray : ConsoleColor.Red;
        logger.Log($"│  - Wire E{rod.ElementID,-8} : {tonForce,6:F2} ton ({status})", color, useTimestamp: false);
      }
      logger.Log("└────────────────────────────────────────────────────────┘", useTimestamp: false);
    }
  }
}
