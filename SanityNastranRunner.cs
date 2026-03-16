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
    public static bool Run(string originalBdfPath, FeModelContext context, bool forceRigidDof123456, PipelineLogger logger, bool debugPrint)
    {
      if (debugPrint) logger.LogInfo("\n[Sanity Run] 초기 모델 유효성 검증용 Nastran 강제 구동 시작...");

      string dir = Path.GetDirectoryName(originalBdfPath) ?? "";
      string fileName = Path.GetFileNameWithoutExtension(originalBdfPath);
      string sanityBdfPath = Path.Combine(dir, fileName + "_sanity.bdf");
      string sanityF06Path = Path.Combine(dir, fileName + "_sanity.f06");

      // ★ [버그 수정] 메모리의 원본 강체(context.Rigids) DOF를 덮어쓰는 코드를 삭제했습니다.
      // (대신 ExportSanityBdf 함수 내부에서 임시 텍스트 파일 생성 시에만 123456으로 덮어씁니다.)
      if (forceRigidDof123456)
      {
        logger.LogWarning("  -> [옵션 적용] Sanity 검증용 BDF 생성 시 임시로 RBE2 강체의 DOF를 '123456'으로 강제 고정합니다. (원본 유지)");
      }

      int dummySpcNodeId = context.Nodes.Keys.FirstOrDefault();
      if (dummySpcNodeId == 0)
      {
        logger.LogError("  -> 모델에 유효한 노드가 하나도 없어 Nastran을 구동할 수 없습니다.");
        return false;
      }

      ExportSanityBdf(originalBdfPath, sanityBdfPath, context, forceRigidDof123456, dummySpcNodeId);

      if (debugPrint) logger.LogInfo($"  -> Nastran 솔버 실행 중... (파일: {Path.GetFileName(sanityBdfPath)})");
      bool solveSuccess = ExecuteNastran(sanityBdfPath, dir);

      if (!solveSuccess)
      {
        logger.LogError("  -> Nastran 프로세스 실행 실패! 시스템 환경변수(PATH)에 'nastran'이 설정되어 있는지 확인세요.");
        return false;
      }

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

        if (!isBulk)
        {
          if ((upperTrimmed.StartsWith("SPC") && upperTrimmed.Contains("=")) ||
              (upperTrimmed.StartsWith("LOAD") && upperTrimmed.Contains("=")))
          {
            continue;
          }

          if (upperTrimmed.StartsWith("BEGIN BULK"))
          {
            writer.WriteLine("  SPC = 999999");
            writer.WriteLine("  LOAD = 999999");
            writer.WriteLine(line);
            isBulk = true;

            writer.WriteLine("PARAM,AUTOSPC,YES");
            writer.WriteLine("PARAM,BAILOUT,-1");
            writer.WriteLine($"SPC1, 999999, 123456, {dummySpcNodeId}");
            writer.WriteLine("GRAV, 999999, , 9800.0, 0.0, 0.0, -1.0");
            continue;
          }
        }
        else
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

            if (head == "GRID" && !context.Nodes.Contains(id)) continue;
            if (head == "CBEAM" && !context.Elements.Contains(id)) continue;
            if (head == "CONM2" && !context.PointMasses.Contains(id)) continue;

            if (head == "RBE2")
            {
              if (!context.Rigids.Contains(id)) continue;

              // ★ 여기서 Sanity 파일에 쓸 때만 임시로 123456 텍스트를 주입합니다. (메모리 원본 유지)
              if (forceRigidDof)
              {
                if (line.Contains(","))
                {
                  var parts = line.Split(',');
                  if (parts.Length >= 4) parts[3] = "123456";
                  writer.WriteLine(string.Join(",", parts));
                }
                else
                {
                  string part1 = line.Length >= 24 ? line.Substring(0, 24) : line.PadRight(24);
                  string part2 = "  123456";
                  string part3 = line.Length > 32 ? line.Substring(32) : "";
                  writer.WriteLine(part1 + part2 + part3);
                }
                continue;
              }
            }
          }
          catch { }
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
