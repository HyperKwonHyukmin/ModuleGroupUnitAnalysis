using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Pipeline.Modifiers;
using ModuleGroupUnitAnalysis.Pipeline.Preprocess;
using ModuleGroupUnitAnalysis.Services.Parsers;
using ModuleGroupUnitAnalysis.Services.Utils;
using ModuleGroupUnitAnalysis.Utils;
using ModuleGroupUnitAnalysis.Pipeline.Postprocess;
using ModuleGroupUnitAnalysis.Exporter;
using System;
using System.Collections.Generic;

namespace ModuleGroupUnitAnalysis.Pipeline
{
  // 추출된 SPC 정보를 담을 작은 클래스
  public class SpcAssignData
  {
    public List<int> PipeSpcNodes { get; set; } = new List<int>();
    public List<int> CogSpcNodes { get; set; } = new List<int>();

    // ★ [신규] 생성된 Nastran Bulk Data 카드들을 저장 (GRID, CROD, PROD, SPC1 등)
    public List<string> GeneratedBulkCards { get; set; } = new List<string>();
  }

  /// <summary>
  /// Hook & Trolley 권상 해석 위한 전체 파이프라인 프로세스를 관장합니다.
  /// </summary>
  public class HookTrolleyPipeline
  {
    private readonly string _bdfPath;
    private readonly FeModelContext _context;
    private readonly PipelineLogger _logger;
    private readonly bool _runSanityNastranCheck;
    private readonly bool _forceRigidDof123456;
    private readonly bool _runNastranAnalysis;
    private readonly bool _pipelineDebug;
    private readonly bool _verboseDebug;

    public SpcAssignData SpcData { get; private set; } = new SpcAssignData();

    public HookTrolleyPipeline(string bdfPath, PipelineLogger logger, bool runSanityNastranCheck, 
      bool forceRigidDof123456, bool runNastranAnalysis, bool pipelineDebug, bool verboseDebug)
    {
      _bdfPath = bdfPath;
      _context = FeModelContext.CreateEmpty();
      _logger = logger;
      _runSanityNastranCheck = runSanityNastranCheck;
      _forceRigidDof123456 = forceRigidDof123456;
      _runNastranAnalysis = runNastranAnalysis; // ★ 맵핑
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

      // ====================================================================
      // ★ [이 부분을 BDF 파싱 직후에 밖으로 빼내야 합니다!] 
      // ====================================================================
      if (_forceRigidDof123456)
      {
        _logger.LogWarning("\n[옵션 적용] 모델 내 모든 RBE2 강체의 DOF를 '123456'으로 강제 고정합니다.");
        _context.Rigids.ForceAllDofs("123456");
      }

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

      // [Stage 9] 권상 정점 및 와이어(CROD) 가상 텍스트 생성
      LiftingWireGenerator.Run(liftingGroups, _context, this.SpcData, _logger, _pipelineDebug);

      // ====================================================================
      // ★ [신규 추가] [Stage 9-1] 슬링 각도 사전 검사
      // ====================================================================
      bool isAngleSafe = LiftingSlingAngleInspector.Run(liftingGroups, _logger, _pipelineDebug);

      // ====================================================================
      // ★ [신규 추가] [Stage 9-2] 와이어-구조물 간섭 검사
      // ====================================================================
      bool isInterferenceFree = LiftingInterferenceInspector.Run(liftingGroups, _context, _logger, _pipelineDebug);


      // [Stage 10] BDF 파일 최종 출력 (_r.bdf 생성)
      if (_pipelineDebug) _logger.LogDivider("STAGE 10: BDF 파일 생성 및 최종 요약");

      ModuleGroupUnitAnalysis.Exporter.BdfExporter.Export(_bdfPath, _context, this.SpcData);

      string dir = System.IO.Path.GetDirectoryName(_bdfPath) ?? "";
      string outputFileName = System.IO.Path.GetFileNameWithoutExtension(_bdfPath) + "_r.bdf";
      string exportPath = System.IO.Path.Combine(dir, outputFileName); // 해석 구동용 경로

      _logger.LogSuccess($"최종 BDF 내보내기 완료: {outputFileName}");

      // 대시보드 출력 상태 텍스트 변경 (DANGER -> WARNING)
      string angleStatus = isAngleSafe ? "OK (60도 이상 확보)" : "WARNING (60도 미만 구간 존재!)";
      string clashStatus = isInterferenceFree ? "Clear (관통 없음)" : "WARNING (구조물 간섭 존재!)"; // ★ 노란색에 맞게 DANGER -> WARNING 수정

      _logger.Log("", useTimestamp: false);
      _logger.Log("┌─────────────────────────────────────────────────────────────────┐", useTimestamp: false);
      _logger.Log("│                 [ Module Unit Lifting Summary ]                 │", ConsoleColor.Cyan, useTimestamp: false);
      _logger.Log("├───────────────────────────┬─────────────────────────────────────┤", useTimestamp: false);

      _logger.LogSummaryTable("Target Model", System.IO.Path.GetFileName(_bdfPath));
      _logger.LogSummaryTable("Total Mass", $"{totalMass:F2} ton");
      _logger.LogSummaryTable("Center of Gravity (COG)", $"X:{cog.X:F1}  Y:{cog.Y:F1}  Z:{cog.Z:F1}");
      _logger.LogSummaryTable("Lifting Method", liftingGroups.Any(g => g.LiftingMethod == 1) ? "Goliat (Trolley)" : "Hydro (Hook)");
      _logger.LogSummaryTable("Lifting Point Groups", $"{liftingGroups.Count} Groups");
      _logger.LogSummaryTable("Auto-SPC Stabilizers", $"Pipe: {pipeSpcs.Count} EA / Struc: {cogSpcs.Count} EA");
      _logger.Log("├───────────────────────────┼─────────────────────────────────────┤", useTimestamp: false);
      _logger.LogSummaryTable("Safety: Overturn", "Safe (무게중심 내 안정적)");
      _logger.LogSummaryTable("Safety: Sling Angle", angleStatus);
      _logger.LogSummaryTable("Safety: Wire Clash", clashStatus);
      _logger.Log("├───────────────────────────┼─────────────────────────────────────┤", useTimestamp: false);
      _logger.LogSummaryTable("Generated Output File", outputFileName);

      _logger.Log("└───────────────────────────┴─────────────────────────────────────┘", useTimestamp: false);

      if (isAngleSafe && isInterferenceFree)
        _logger.LogSuccess("BDF 출력 및 모든 파이프라인 처리가 성공적으로 완료되었습니다.");
      else
        _logger.LogWarning("BDF 파일은 생성되었으나, 일부 기하학적 안전 경고(각도/간섭)가 존재합니다.");

      // ====================================================================
      // ★ [신규 추가] [Stage 11] Nastran 본 해석 실행 (옵션)
      // ====================================================================
      if (_runNastranAnalysis)
      {
        _logger.LogDivider("STAGE 11: Nastran 솔버 구동");
        bool isSolved = NastranAnalysisRunner.Run(exportPath, _logger, _pipelineDebug);

        if (isSolved)
        {
          _logger.LogSuccess("▶ F06 / OP2 파일이 준비되었습니다. 다음 후처리(Post-Processing) 단계로 넘어갈 수 있습니다.");
        }
      }

      _logger.Log("", useTimestamp: false);
    }
  }
}
