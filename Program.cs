using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Pipeline;

namespace ModuleGroupUnitAnalysis
{
  // 1. 해석 타입을 정의하는 Enum 추가
  public enum AnalysisType
  {
    ModuleUnit,
    GroupUnit
  }

  class MainApp
  {
    static void Main(string[] args)
    {
      var bdfFile = @"C:\Coding\Csharp\Projects\ModuleGroupUnitAnalysis\KangSangHunCSV\MUnit\3515_35030\NastranTest\3515-35030.bdf";

      // 1. 디버그 및 로그 옵션 설정
      bool logExport = true;
      bool pipelineDebug = true;
      bool verboseDebug = false;

      // =========================================================
      // [파이프라인 제어 스위치 모음]
      // =========================================================
      // ★ 2. 여기서 해석 타입을 결정합니다.
      AnalysisType analysisType = AnalysisType.ModuleUnit;

      bool runSanityNastranCheck = false;

      // Group Unit일 때는 무조건 123456을 적용하도록 연동
      //bool forceRigidDof123456 = (analysisType == AnalysisType.GroupUnit);
      bool forceRigidDof123456 = true;

      bool runNastranAnalysis = true;
      bool checkAnalysisResult = true;
      // =========================================================

      // 2. 로거 초기화 및 로그 파일 생성 경로 확정
      var logger = new PipelineLogger(logExport);
      logger.InitializeFile(bdfFile);

      // 3. 파이프라인 객체 생성 및 실행 (옵션 추가)
      var pipeline = new HookTrolleyPipeline(bdfFile, logger, analysisType, runSanityNastranCheck,
              forceRigidDof123456, runNastranAnalysis, checkAnalysisResult, pipelineDebug, verboseDebug);
      pipeline.Run();
    }
  }
}
