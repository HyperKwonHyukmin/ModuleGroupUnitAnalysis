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
      bool logExport = true;       // 파일로 로그 저장 여부
      bool pipelineDebug = true;   // 기본 정보(개수, 과정) 출력
      bool verboseDebug = true;   // 세부적인 모든 내용 출력 (true로 바꾸면 전체 데이터 출력)

      // 2. 로거 및 컨텍스트 초기화
      var logger = new PipelineLogger(logExport);
      var context = FeModelContext.CreateEmpty();

      // 3. 파싱 실행 (오류 발생 시 로거가 자동으로 예외를 캐치하여 빨간불과 함께 로그 저장)
      var parser = new NastranBdfParser(context, logger, pipelineDebug, verboseDebug);
      parser.Parse(bdfFile);

      // 4. 로드 완료된 모델 정보 디버깅 출력
      FeModelDebugger.PrintSummary(context, logger, pipelineDebug, verboseDebug);
    }
  }
}
