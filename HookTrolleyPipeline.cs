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
  // 추출된 SPC 정보를 담을 작은 클래스
  public class SpcAssignData
  {
    public List<int> PipeSpcNodes { get; set; } = new List<int>();
    public List<int> CogSpcNodes { get; set; } = new List<int>();
  }

  /// <summary>
  /// Hook & Trolley 권상 해석을 위한 전체 파이프라인 프로세스를 관장합니다.
  /// </summary>
  public class HookTrolleyPipeline
  {
    private readonly string _bdfPath;
    private readonly FeModelContext _context;
    private readonly PipelineLogger _logger;
    private readonly bool _runSanityNastranCheck;
    private readonly bool _forceRigidDof123456;
    private readonly bool _pipelineDebug;
    private readonly bool _verboseDebug;

    public SpcAssignData SpcData { get; private set; } = new SpcAssignData();

    public HookTrolleyPipeline(string bdfPath, PipelineLogger logger, bool runSanityNastranCheck, 
      bool forceRigidDof123456, bool pipelineDebug, bool verboseDebug)
    {
      _bdfPath = bdfPath;
      _context = FeModelContext.CreateEmpty();
      _logger = logger;
      _runSanityNastranCheck = runSanityNastranCheck;
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
      // [Phase 2] 초기 모델 유효성 평가 (선택적 구동)
      // ====================================================================
      if (_runSanityNastranCheck)
      {
        bool isNastranPass = SanityNastranRunner.Run(_bdfPath, _context, _forceRigidDof123456, _logger, _pipelineDebug);
        if (!isNastranPass)
        {
          _logger.LogError("\n[Pipeline Aborted] 초기 모델 Nastran 해석 중 FATAL이 발생하여 파이프라인을 종료합니다.");
          return;
        }
      }
      else
      {
        if (_pipelineDebug)
          _logger.LogInfo("\n[Phase 2] Sanity Nastran 해석 검증은 건너뜁니다. (옵션 Off)");
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

      // ====================================================================
      // [Stage 4] Hook / Trolley 3D 정점 좌표 역산
      // ====================================================================
      bool isCalcSuccess = LiftingPointCalculator.Run(liftingGroups, _logger, _pipelineDebug);
      if (!isCalcSuccess)
      {
        _logger.LogError("\n[Pipeline Aborted] Hook/Trolley 위치 수학적 계산에 실패하여 파이프라인을 중단합니다. (줄 길이를 확인하세요)");
        return;
      }

      // ====================================================================
      // [Stage 5] COG 기반 위치 미세 보정 (Hook to COG)
      // ====================================================================
      LiftingPointCogAdjuster.Run(liftingGroups, cog, _logger, _pipelineDebug);

      // ====================================================================
      // [Stage 6] 자세 안정성 평가 (Overturn Check)
      // ====================================================================
      bool isStable = LiftingOverturnInspector.Run(liftingGroups, cog, _logger, _pipelineDebug);
      if (!isStable)
      {
        _logger.LogError("\n[Pipeline Aborted] 무게중심이 권상 영역을 벗어나 전도(Overturn) 위험이 있어 해석을 중단합니다.");
        return;
      }

      // ====================================================================
      // [Stage 7] 트롤리 권상 간격 분할 (Trolley Splitter)
      // ====================================================================
      LiftingPointTrolleySplitter.Run(liftingGroups, _logger, _pipelineDebug);

      // ====================================================================
      // [Stage 8] 해석 안정화용 경계조건(SPC) 노드 추출
      // ====================================================================
      var (pipeSpcs, cogSpcs) = LiftingBoundaryConditionSetter.Run(_context, cog, _logger, _pipelineDebug);

      // 추출된 정보를 파이프라인 변수에 저장 (다음 Exporter 단계에서 꺼내 씀)
      this.SpcData.PipeSpcNodes = pipeSpcs;
      this.SpcData.CogSpcNodes = cogSpcs;

      _logger.LogSuccess("\n▶ 현재까지 작성된 파이프라인이 성공적으로 완료되었습니다.");
    }
  }
}
