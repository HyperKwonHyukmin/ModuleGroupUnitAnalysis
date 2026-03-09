using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Pipeline; // 파이프라인 네임스페이스 추가

namespace ModuleGroupUnitAnalysis
{
  class MainApp
  {
    static void Main(string[] args)
    {
      // (테스트 시 본인 PC의 실제 BDF 경로로 변경해주세요)
      var bdfFile = @"C:\Coding\Python\Projects\ModuleUnit\KangSangHun_GU_bdf\323K1_ori.bdf";

      // 1. 디버그 및 로그 옵션 설정
      bool logExport = true;
      bool pipelineDebug = true;
      bool verboseDebug = true; // 너무 길면 false로 두세요.

      // 2. 로거 초기화 및 로그 파일 생성 경로 확정
      var logger = new PipelineLogger(logExport);
      logger.InitializeFile(bdfFile);

      // 3. 파이프라인 객체 생성 및 실행 (초깔끔!)
      var pipeline = new HookTrolleyPipeline(bdfFile, logger, pipelineDebug, verboseDebug);
      pipeline.Run();
    }
  }
}
