using System;
using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Pipeline;

namespace ModuleGroupUnitAnalysis
{
  // 1. 해석 타입을 정의하는 Enum
  public enum AnalysisType
  {
    ModuleUnit,
    GroupUnit
  }

  class MainApp
  {
    static void Main(string[] args)
    {
      // =========================================================
      // [기본값 설정] (인자가 없을 때 작동하는 개발용 기본 환경)
      // =========================================================
      string bdfFile = @"C:\Coding\Csharp\Projects\ModuleGroupUnitAnalysis\KangSangHunCSV\MUnit\3515_35030\NastranTest\3515-35030.bdf";
      AnalysisType analysisType = AnalysisType.ModuleUnit;
      bool runNastranAnalysis = true;

      // 실제 적용(운영)에서는 false를 사용할 것이므로 기본값 false
      bool forceRigidDof123456 = false;

      // 로깅 및 디버그 제어 스위치
      bool logExport = true;
      bool pipelineDebug = true;
      bool verboseDebug = false;
      bool runSanityNastranCheck = true;
      bool checkAnalysisResult = true;

      // =========================================================
      // [명령줄 인자(Arguments) 파싱]
      // =========================================================
      if (args.Length > 0)
      {
        bdfFile = args[0]; // 1번째 인자: BDF 파일 경로
      }
      if (args.Length > 1)
      {
        // 2번째 인자: 해석 타입 (ModuleUnit 또는 GroupUnit)
        if (Enum.TryParse(args[1], true, out AnalysisType parsedType))
        {
          analysisType = parsedType;
        }
      }
      if (args.Length > 2)
      {
        // 3번째 인자: Nastran 본 해석 실행 여부 (true/false)
        bool.TryParse(args[2], out runNastranAnalysis);
      }
      if (args.Length > 3)
      {
        // 4번째 인자: 개발용 강제 DOF 고정 여부 (true/false)
        bool.TryParse(args[3], out forceRigidDof123456);
      }

      // =========================================================
      // [비즈니스 로직 제약 조건]
      // =========================================================
      // Group Unit일 때는 무조건 123456 고정을 해제(false)합니다.
      if (analysisType == AnalysisType.GroupUnit)
      {
        forceRigidDof123456 = false;
      }

      // =========================================================
      // [파이프라인 실행]
      // =========================================================
      var logger = new PipelineLogger(logExport);
      logger.InitializeFile(bdfFile);

      // 설정된 실행 환경 요약 출력
      if (pipelineDebug)
      {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[Config] 파일경로 : {bdfFile}");
        Console.WriteLine($"[Config] 해석타입 : {analysisType}");
        Console.WriteLine($"[Config] 솔버구동 : {runNastranAnalysis}");
        Console.WriteLine($"[Config] DOF 고정 : {forceRigidDof123456} (Sanity 구동 시엔 무조건 true 적용됨)");
        Console.ResetColor();
      }

      var pipeline = new HookTrolleyPipeline(bdfFile, logger, analysisType, runSanityNastranCheck,
              forceRigidDof123456, runNastranAnalysis, checkAnalysisResult, pipelineDebug, verboseDebug);
      pipeline.Run();
    }
  }
}
