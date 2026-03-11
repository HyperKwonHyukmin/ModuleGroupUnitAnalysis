using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Preprocess
{
  public static class SanityNastranRunner
  {
    /// <summary>
    /// 임시 BDF를 생성하고 Nastran을 구동하여 .f06 파일의 FATAL 에러 유무를 검사합니다.
    /// </summary>
    public static bool Run(string originalBdfPath, FeModelContext context, bool forceRigidDof123456, PipelineLogger logger, bool debugPrint)
    {
      if (debugPrint) logger.LogInfo("\n[Sanity Run] 초기 모델 유효성 검증용 Nastran 강제 구동 시작...");

      string dir = Path.GetDirectoryName(originalBdfPath) ?? "";
      string fileName = Path.GetFileNameWithoutExtension(originalBdfPath);
      string sanityBdfPath = Path.Combine(dir, fileName + "_sanity.bdf");
      string sanityF06Path = Path.Combine(dir, fileName + "_sanity.f06");

      // 1. 메모리의 체 DOF 변경 (옵션 켜진 경우)
      if (forceRigidDof123456)
      {
        logger.LogWarning("  -> [옵션 적용] RBE2 강체의 DOF를 '123456'으로 강제 고정합니다. (FATAL 방지)");
        context.Rigids.ForceAllDofs("123456");
      }

      // ★ 2. 모델이 날아가지 않도록 더미 SPC를 부여할 기준 노드 1개 추출
      int dummySpcNodeId = context.Nodes.Keys.FirstOrDefault();
      if (dummySpcNodeId == 0)
      {
        logger.LogError("  -> 모델에 유효한 노드가 하나도 없어 Nastran을 구동할 수 없습니다.");
        return false;
      }

      // 3. Sanity 전용 BDF 생성 (더미 하중/경계조건 주입)
      ExportSanityBdf(originalBdfPath, sanityBdfPath, context, forceRigidDof123456, dummySpcNodeId);

      // 4. Nastran 백그라운드 실행
      if (debugPrint) logger.LogInfo($"  -> Nastran 솔버 실행 중... (파일: {Path.GetFileName(sanityBdfPath)})");
      bool solveSuccess = ExecuteNastran(sanityBdfPath, dir);

      if (!solveSuccess)
      {
        logger.LogError("  -> Nastran 프로세스 실행 실패! 시스템 환경변수(PATH)에 'nastran'이 설정되어 있는지 확인하세요.");
        return false;
      }

      // 5. F06 파일 FATAL 검사
      if (!File.Exists(sanityF06Path))
      {
        logger.LogError("  -> 해석은 종료되었으나 .f06 파일이 생성되지 않았습니다.");
        return false;
      }

      bool hasFatal = false;
      foreach (var line in File.ReadLines(sanityF06Path))
      {
        if (line.Contains("FATAL", StringComparison.OrdinalIgnoreCase))
        {
          hasFatal = true;
          break;
        }
      }

      if (hasFatal)
      {
        logger.LogError("  [FATAL 탐지] Nastran 초기 해석 결과, F06 파일에서 치명적 오류(FATAL)가 발견되었습니다!");
        return false;
      }

      if (debugPrint) logger.LogSuccess("  [OK] Sanity Nastran 해석 통과! (FATAL 없음) 초기 모델 유효성 검사 완료.");
      return true;
    }

    private static void ExportSanityBdf(string originalPath, string exportPath, FeModelContext context, bool forceRigidDof, int dummySpcNodeId)
    {
      var lines = File.ReadAllLines(originalPath);
      using var writer = new StreamWriter(exportPath);

      bool isBulk = false;

      foreach (var line in lines)
      {
        string trimmed = line.Trim();
        string upperTrimmed = trimmed.ToUpper();

        if (string.IsNullOrWhiteSpace(line) || upperTrimmed.StartsWith("$"))
        {
          writer.WriteLine(line);
          continue;
        }

        // ====================================================================
        // ★ 핵심: Case Control에서 기존 SPC, LOAD를 무시하고 더미 ID(999999)로 교체
        // ====================================================================
        if (!isBulk)
        {
          // 기존 SPC, LOAD 선언은 건너뜀
          if ((upperTrimmed.StartsWith("SPC") && upperTrimmed.Contains("=")) ||
              (upperTrimmed.StartsWith("LOAD") && upperTrimmed.Contains("=")))
          {
            continue;
          }

          if (upperTrimmed.StartsWith("BEGIN BULK"))
          {
            // BEGIN BULK 직전에 글로벌로 더미 Case Control 강제 주입
            writer.WriteLine("  SPC = 999999");
            writer.WriteLine("  LOAD = 999999");
            writer.WriteLine(line); // BEGIN BULK 작성
            isBulk = true;

            // Bulk Data 최상단에 더미 구속/하중 및 에러 우회 파라미터 삽입
            writer.WriteLine("PARAM,AUTOSPC,YES"); // 강체 회전 방지 보조
            writer.WriteLine("PARAM,BAILOUT,-1");  // 사소한 에러는 강제 진행
            writer.WriteLine($"SPC1, 999999, 123456, {dummySpcNodeId}"); // 노드 1개를 완벽히 허공에 고정
            writer.WriteLine("GRAV, 999999, , 9800.0, 0.0, 0.0, -1.0"); // 해석 구동을 위한 더미 하중
            continue;
          }
        }
        else // Bulk Data 영역
        {
          string head = upperTrimmed;
          if (head.Contains(",")) head = head.Split(',')[0].Trim();
          else if (head.Length >= 8) head = head.Substring(0, 8).Trim();

          try
          {
            int id = 0;
            if (head == "GRID" || head == "CBEAM" || head == "CONM2" || head == "RBE2")
            {
              if (line.Contains(",")) id = int.Parse(line.Split(',')[1].Trim());
              else id = int.Parse(line.Substring(8, 8).Trim());
            }

            // 가비지 데이터 제외
            if (head == "GRID" && !context.Nodes.Contains(id)) continue;
            if (head == "CBEAM" && !context.Elements.Contains(id)) continue;
            if (head == "CONM2" && !context.PointMasses.Contains(id)) continue;

            // U-Bolt RBE2 DOF 덮어쓰기 로직
            if (head == "RBE2")
            {
              if (!context.Rigids.Contains(id)) continue;

              if (forceRigidDof)
              {
                if (line.Contains(",")) // CSV 포맷
                {
                  var parts = line.Split(',');
                  if (parts.Length >= 4) parts[3] = "123456";
                  writer.WriteLine(string.Join(",", parts));
                }
                else // 고정 폭 포맷
                {
                  string part1 = line.Length >= 24 ? line.Substring(0, 24) : line.PadRight(24);
                  string part2 = "  123456"; // 4번째 필드 (자유도)
                  string part3 = line.Length > 32 ? line.Substring(32) : "";
                  writer.WriteLine(part1 + part2 + part3);
                }
                continue;
              }
            }
          }
          catch { /* 예외 시 원본 줄 유지 */ }
        }

        writer.WriteLine(line);
      }
    }

    private static bool ExecuteNastran(string bdfPath, string workDir)
    {
      try
      {
        var processInfo = new ProcessStartInfo("nastran", $"\"{bdfPath}\"")
        {
          WorkingDirectory = workDir,
          CreateNoWindow = true,
          UseShellExecute = false
        };

        using var process = Process.Start(processInfo);
        process?.WaitForExit();
        return true;
      }
      catch { return false; }
    }
  }
}
