using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Pipeline.Modifiers;
using ModuleGroupUnitAnalysis.Pipeline.Preprocess;
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
    private readonly bool _forceRigidDof123456;
    private readonly bool _pipelineDebug;
    private readonly bool _verboseDebug;

    public HookTrolleyPipeline(string bdfPath, PipelineLogger logger, bool forceRigidDof123456, 
      bool pipelineDebug, bool verboseDebug)
    {
      _bdfPath = bdfPath;
      _context = FeModelContext.CreateEmpty();
      _logger = logger;
      _forceRigidDof123456 = forceRigidDof123456;
      _pipelineDebug = pipelineDebug;
      _verboseDebug = verboseDebug;
    }

    public void Run()
    {
      _logger.LogInfo("\n==================================================");
      _logger.LogInfo("      [Module Unit] Hook & Trolley 파이프라인 시작   ");
      _logger.LogInfo("==================================================");

      // [Phase 0] 원본 BDF 파일 파싱
      var parser = new NastranBdfParser(_context, _logger, _pipelineDebug, _verboseDebug);
      parser.Parse(_bdfPath);

      FeModelDebugger.PrintSummary(_context, _logger, _pipelineDebug, _verboseDebug);

      // ====================================================================
      // [Phase 1] BDF 모델 건전성 검사 및 힐링 (Sanity Check)
      // ====================================================================
      bool isModelSane = StructuralSanityInspector.Run(_context, _logger, _pipelineDebug, _verboseDebug);
      if (!isModelSane)
      {
        _logger.LogError("\n[Pipeline Aborted] 모델 내부 검증에서 치명적 결함이 발견되어 종료합니다.");
        return;
      }

      // ====================================================================
      // ★ [신규] [Phase 2] 초기 모델 유효성 평가 (Sanity Nastran Run)
      // ====================================================================
      bool isNastranPass = SanityNastranRunner.Run(_bdfPath, _context, _forceRigidDof123456, _logger, _pipelineDebug);
      if (!isNastranPass)
      {
        _logger.LogError("\n[Pipeline Aborted] 초기 모델 Nastran 해석 중 FATAL이 발생하여 파이프라인을 종료합니다.");
        return;
      }

      // ====================================================================
      // [Phase 3] Hook & Trolley 본 해석 준비
      // ====================================================================
      var (totalMass, cog) = CenterOfGravityCalculator.Calculate(_context, _logger.LogInfo);

      List<LiftingGroup> liftingGroups = LiftingInformationParser.Parse(_bdfPath, _context, _logger);
      if (liftingGroups.Count == 0) return;

      // [Stage 1] & [Stage 2] & [Stage 3]
      LiftingPointArranger.Run(liftingGroups, _logger, _pipelineDebug);
      LiftingPointShapeDetecter.Run(liftingGroups, _logger, _pipelineDebug);

      bool isShapeValid = LiftingPointVerifier.Run(liftingGroups, _logger, _pipelineDebug);
      if (!isShapeValid)
      {
        _logger.LogError("\n[Pipeline Aborted] 권상 포인트 기하학 형태 불량으로 파이프라인을 중단합니다.");
        return;
      }

      _logger.LogSuccess("\n▶ 현재까지 작성된 파이프라인이 성공적으로 완료되었습니다.");
    }
  }
}
