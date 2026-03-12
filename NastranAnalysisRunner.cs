using System;
using System.Diagnostics;
using System.IO;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Postprocess
{
  public static class NastranAnalysisRunner
  {
    public static bool Run(string bdfPath, PipelineLogger logger, bool debugPrint)
    {
      if (debugPrint) logger.LogInfo($"\n[Nastran Run] 최종 해석 모델({Path.GetFileName(bdfPath)}) 솔버 구동 시작...");

      string dir = Path.GetDirectoryName(bdfPath) ?? "";
      string op2Path = Path.ChangeExtension(bdfPath, ".op2");
      string f06Path = Path.ChangeExtension(bdfPath, ".f06");

      // 이전 해석 결과 파일이 남아있다면 삭제 (최신 결과 덮어쓰기 보장)
      if (File.Exists(op2Path)) File.Delete(op2Path);
      if (File.Exists(f06Path)) File.Delete(f06Path);

      try
      {
        var processInfo = new ProcessStartInfo("nastran", $"\"{bdfPath}\"")
        {
          WorkingDirectory = dir,
          CreateNoWindow = true,
          UseShellExecute = false
        };

        using var process = Process.Start(processInfo);
        if (process != null)
        {
          process.WaitForExit(); // 해석이 끝날 때까지 대기
        }
        else
        {
          logger.LogError("  -> Nastran 프로세스 실행 실패! 시스템 환경변수(PATH)에 'nastran'이 설정되어 있는지 확인하세요.");
          return false;
        }
      }
      catch (Exception ex)
      {
        logger.LogError($"  -> Nastran 실행 중 운영체제 예외 발생: {ex.Message}");
        return false;
      }

      // 해석 성공/실패 여부 검증
      if (File.Exists(op2Path))
      {
        if (debugPrint) logger.LogSuccess("  [OK] Nastran 해석 완료! (.op2 파일 정상 생성됨)");
        return true;
      }
      else if (File.Exists(f06Path))
      {
        logger.LogError("  [FAIL] Nastran 해석 실패! (.op2 파일이 없습니다. .f06 파일의 FATAL 에러를 확인하세요)");
        return false;
      }
      else
      {
        logger.LogError("  [FAIL] Nastran 해석 구동 실패! 결과 파일(.op2, .f06)이 아예 생성되지 않았습니다.");
        return false;
      }
    }
  }
}
