using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Services.Parsers;
using ModuleGroupUnitAnalysis.Services.Utils;
using System;

namespace ModuleGroupUnitAnalysis
{
  class MainApp
  {
    static void Main(string[] args)
    {
      var bdfFile = @"C:\Coding\Python\Projects\ModuleUnit\KangSangHun_GU_bdf\323K1_ori.bdf";

      // 1. 디버그 및 로그 옵션 설정
      bool logExport = true;
      bool pipelineDebug = true;
      bool verboseDebug = true;

      // 2. 로거 및 컨텍스트 초기화
      var logger = new PipelineLogger(logExport);

      // ★ [추가해야 할 코드] 여기서 BDF 파일 경로를 로거에 던져주어 저장 위치를 확정해야 합니다!
      logger.InitializeFile(bdfFile);

      var context = FeModelContext.CreateEmpty();

      // 3. 파싱 실행
      var parser = new NastranBdfParser(context, logger, pipelineDebug, verboseDebug);
      parser.Parse(bdfFile);

      // 4. 로드 완료된 모델 정보 디버깅 출력
      FeModelDebugger.PrintSummary(context, logger, pipelineDebug, verboseDebug);

      foreach(var pro in context.Properties)
      {
        Console.WriteLine(pro);
      }
    }
  }
}
