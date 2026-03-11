using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Pipeline.Modifiers;
using ModuleGroupUnitAnalysis.Services.Parsers;
using ModuleGroupUnitAnalysis.Services.Utils;
using ModuleGroupUnitAnalysis.Utils;
using System;
using System.Collections.Generic;

namespace ModuleGroupUnitAnalysis.Pipeline
{
  /// <summary>
  /// Hook & Trolley 권상 해석을 위한 전체 파이프라인 프로세스를 관장합니다.
  /// </summary>
  public class HookTrolleyPipeline
  {
    private readonly string _bdfPath;
    private readonly FeModelContext _context;
    private readonly PipelineLogger _logger;
    private readonly bool _pipelineDebug;
    private readonly bool _verboseDebug;

    public HookTrolleyPipeline(string bdfPath, PipelineLogger logger, bool pipelineDebug, bool verboseDebug)
    {
      _bdfPath = bdfPath;
      _context = FeModelContext.CreateEmpty();
      _logger = logger;
      _pipelineDebug = pipelineDebug;
      _verboseDebug = verboseDebug;
    }

    public void Run()
    {
      _logger.LogInfo("\n==================================================");
      _logger.LogInfo("      [Module Unit] Hook & Trolley 파이프라인 시작   ");
      _logger.LogInfo("==================================================");

      // [Phase 0] 원본 BDF 파일 파싱 및 데이터 적재
      var parser = new NastranBdfParser(_context, _logger, _pipelineDebug, _verboseDebug);
      parser.Parse(_bdfPath);

      // 디버그 요약 정보 출력
      FeModelDebugger.PrintSummary(_context, _logger, _pipelineDebug, _verboseDebug);

      // 전체 모델 무게중심(COG) 계산
      var (totalMass, cog) = CenterOfGravityCalculator.Calculate(_context, _logger.LogInfo);

      // BDF 주석($$Hydro 등)에서 권상 초기 정보 추출
      List<LiftingGroup> liftingGroups = LiftingInformationParser.Parse(_bdfPath, _context, _logger);

      if (liftingGroups.Count == 0)
      {
        _logger.LogWarning("권상 정보($$Hydro / $$Goliat 등)를 찾을 수 없어 파이프라인을 종료합니다.");
        return;
      }

      // ====================================================================
      // 파이프라인 스테이지 순차 실행
      // ====================================================================

      // [Stage 1] 권상 포인트 초기 정렬 (Lifting Point Setting)
      LiftingPointArranger.Run(liftingGroups, _logger, _pipelineDebug);

      // [Stage 2] 유닛 포인트 형태 판별 (Shape Detector)
      LiftingPointShapeDetecter.Run(liftingGroups, _logger, _pipelineDebug);

      // [Stage 3] 유닛 포인트 형태 유효성 검증 (Verifier)
      bool isShapeValid = LiftingPointVerifier.Run(liftingGroups, _logger, _pipelineDebug);
      if (!isShapeValid)
      {
        _logger.LogError("\n[Pipeline Aborted] 권상 포인트의 기하학적 형태가 안전 기준을 통과하지 못해 해석 파이프라인을 중단합니다. 모델의 $$ 위치 지정을 확인하세요.");
        return; // 파이프라인 강제 종료
      }

      _logger.LogSuccess("\n▶ 현재까지 작성된 파이프라인(Stage 3)이 성공적으로 완료되었습니다.");
    }
  }
}
