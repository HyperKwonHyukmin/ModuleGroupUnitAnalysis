using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Pipeline; // 파이프라인 네임스페이스 추가

namespace ModuleGroupUnitAnalysis
{
  class MainApp
  {
    static void Main(string[] args)
    {
      // 강체역학망 오류 케이스 
      //var bdfFile = @"C:\Coding\Csharp\Projects\ModuleGroupUnitAnalysis\KangSangHunCSV\MUnit\3515_35020\NastranTest\3515-35020.bdf";
      var bdfFile = @"C:\Coding\Csharp\Projects\ModuleGroupUnitAnalysis\KangSangHunCSV\MUnit\3515_35030\NastranTest\3515-35030.bdf";

      // 1. 디버그 및 로그 옵션 설정
      bool logExport = true;
      bool pipelineDebug = true;
      bool verboseDebug = false;

      // Sanity 체크용 Nastran을 실제로 구동할 것인가? (시간 절약을 위해 평소엔 false)
      bool runSanityNastranCheck = true;

      // Ubolt DOF 강제 "123456" 설정
      bool forceRigidDof123456 = false;

      // Nastran 해석 유무
      bool runNastranAnalysis = true;

      // 2. 로거 초기화 및 로그 파일 생성 경로 확정
      var logger = new PipelineLogger(logExport);
      logger.InitializeFile(bdfFile);

      // 3. 파이프라인 객체 생성 및 실행 (초깔끔!)
      var pipeline = new HookTrolleyPipeline(bdfFile, logger, runSanityNastranCheck, 
        forceRigidDof123456, runNastranAnalysis, pipelineDebug, verboseDebug);
      pipeline.Run();
    }
  }
}
