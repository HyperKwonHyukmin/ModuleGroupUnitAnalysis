using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Pipeline;
using System;

namespace ModuleGroupUnitAnalysis
{
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
      // [기본값 설정]
      // =========================================================
      var bdfFile = @"C:\Coding\Csharp\Projects\ModuleGroupUnitAnalysis\KangSangHunCSV\MUnit\3515_35030\NastranTest\3515-35030.bdf";

      // ★ 해석 타입을 여기서 결정합니다.
      AnalysisType analysisType = AnalysisType.ModuleUnit;

      bool logExport = true;
      bool pipelineDebug = true;
      bool verboseDebug = false;

      // =========================================================
      // [파이프라인 제어 스위치 모음]
      // =========================================================
      bool runSanityNastranCheck = false;
      bool runNastranAnalysis = true;
      bool checkAnalysisResult = true;

      // ★ 개발 단계에서 임의 지정할 수 있는 옵션 (ModuleUnit의 경우 이 값을 무조건 따라감)
      bool forceRigidDof123456 = false;

      // =========================================================
      // [비즈니스 로직 제약 조건 적용]
      // =========================================================
      // Group Unit일 때는 개발 단계 지정값과 무관하게 무조건 123456(true)을 적용하도록 강제합니다.
      if (analysisType == AnalysisType.GroupUnit)
      {
        forceRigidDof123456 = true;
      }

      // =========================================================
      // [파이프라인 실행]
      // =========================================================
      var logger = new PipelineLogger(logExport);
      logger.InitializeFile(bdfFile);

      if (pipelineDebug)
      {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[Config] 해석타입 : {analysisType}");
        Console.WriteLine($"[Config] DOF 강제고정 여부 : {forceRigidDof123456}");
        Console.ResetColor();
      }

      var pipeline = new HookTrolleyPipeline(bdfFile, logger, analysisType, runSanityNastranCheck,
              forceRigidDof123456, runNastranAnalysis, checkAnalysisResult, pipelineDebug, verboseDebug);
      pipeline.Run();
    }
  }
}
