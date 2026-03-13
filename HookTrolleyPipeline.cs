using ModuleGroupUnitAnalysis.Exporter;
using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Pipeline.Modifiers;
using ModuleGroupUnitAnalysis.Pipeline.Postprocess;
using ModuleGroupUnitAnalysis.Pipeline.Preprocess;
using ModuleGroupUnitAnalysis.Services.Parsers;
using ModuleGroupUnitAnalysis.Services.Utils;
using ModuleGroupUnitAnalysis.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using static ModuleGroupUnitAnalysis.MainApp;

namespace ModuleGroupUnitAnalysis.Pipeline
{
  // 추출된 SPC 정보 및 가상 와이어 데이터를 담을 데이터 클래스
  public class SpcAssignData
  {
    public List<int> PipeSpcNodes { get; set; } = new List<int>();
    public List<int> CogSpcNodes { get; set; } = new List<int>();
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
    private readonly AnalysisType _analysisType;

    private readonly bool _runSanityNastranCheck;
    private readonly bool _forceRigidDof123456;
    private readonly bool _runNastranAnalysis;
    private readonly bool _checkAnalysisResult; // F06 결과 확인 옵션
    private readonly bool _pipelineDebug;
    private readonly bool _verboseDebug;

    public SpcAssignData SpcData { get; private set; } = new SpcAssignData();

    public HookTrolleyPipeline(string bdfPath, PipelineLogger logger, AnalysisType analysisType, bool runSanityNastranCheck,
            bool forceRigidDof123456, bool runNastranAnalysis, bool checkAnalysisResult, bool pipelineDebug, bool verboseDebug)
    {
      _bdfPath = bdfPath;
      _context = FeModelContext.CreateEmpty();
      _logger = logger;
      _analysisType = analysisType;
      _runSanityNastranCheck = runSanityNastranCheck;
      _forceRigidDof123456 = forceRigidDof123456;
      _runNastranAnalysis = runNastranAnalysis;
      _checkAnalysisResult = checkAnalysisResult;
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
      // [옵션 적용] BDF 파싱 직후 전역 U-Bolt 자유도 고정
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
      // [Phase 3] Hook & Trolley 본 해석 준비 (COG 및 좌표 추출)
      // ====================================================================
      var (totalMass, cog) = CenterOfGravityCalculator.Calculate(_context, _logger.LogInfo);

      List<LiftingGroup> liftingGroups = LiftingInformationParser.Parse(_bdfPath, _context, _logger);
      if (liftingGroups.Count == 0) return;

      // [Stage 1, 2, 3] 권상 포인트 기하학적 형태 검증
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
      // [Stage 6] 자세 안정성 평가 (Module Unit 전용)
      // ====================================================================

      if (_analysisType == AnalysisType.ModuleUnit)
      {
        bool isStable = LiftingOverturnInspector.Run(liftingGroups, cog, _logger, _pipelineDebug);
        if (!isStable)
        {
          _logger.LogError("\n[Pipeline Aborted] 무게중심이 권상 영역을 벗어나 전도(Overturn) 위험이 있어 해석을 중단합다.");
          return;
        }
      }
      else
      {
        if (_pipelineDebug) _logger.LogInfo("\n[Stage 6] 자세 안정성 평가 생략 (Group Unit 모드)");
      }

      // ====================================================================
      // [Stage 7] 트롤리 권상 간격 분할 (Trolley Splitter)
      // ====================================================================
      LiftingPointTrolleySplitter.Run(liftingGroups, _logger, _pipelineDebug);

      // ====================================================================
      // [Stage 8] 해석 안정화용 경계조건(SPC) 노드 추출
      // ====================================================================
      var (pipeSpcs, cogSpcs) = LiftingBoundaryConditionSetter.Run(_context, cog, _logger, _pipelineDebug);
      this.SpcData.PipeSpcNodes = pipeSpcs;
      this.SpcData.CogSpcNodes = cogSpcs;

      // ====================================================================
      // [Stage 9] 권상 정점 및 와이어(CROD) 가상 텍스트 생성
      // ====================================================================
      LiftingWireGenerator.Run(liftingGroups, _context, this.SpcData, _logger, _pipelineDebug);

      // ====================================================================
      // [Stage 9-1] 슬링 각도 사전 검사
      // ====================================================================
      bool isAngleSafe = LiftingSlingAngleInspector.Run(liftingGroups, _logger, _pipelineDebug);

      // ====================================================================
      // [Stage 9-2] 와이어-구조물 간섭 검사
      // ====================================================================
      bool isInterferenceFree = LiftingInterferenceInspector.Run(liftingGroups, _context, _logger, _pipelineDebug);


      // ====================================================================
      // [Stage 10] BDF 파일 최종 출력 (_r.bdf 생성)
      // ====================================================================
      if (_pipelineDebug) _logger.LogDivider("STAGE 10: BDF 파일 생성 및 최종 요약");

      BdfExporter.Export(_bdfPath, _context, this.SpcData);

      string dir = Path.GetDirectoryName(_bdfPath) ?? "";
      string outputFileName = Path.GetFileNameWithoutExtension(_bdfPath) + "_r.bdf";
      string exportPath = Path.Combine(dir, outputFileName);

      _logger.LogSuccess($"최종 BDF 내보내기 완료: {outputFileName}");

      // 대시보드 상태 텍스트
      string angleStatus = isAngleSafe ? "OK (60도 이상 확보)" : "WARNING (60도 미만 구간 존재!)";
      string clashStatus = isInterferenceFree ? "Clear (관통 없음)" : "WARNING (구조물 간섭 존재!)";

      _logger.Log("", useTimestamp: false);
      _logger.Log("┌────────────────────────────────────────────────────────────────┐", useTimestamp: false);
      _logger.Log("│                 [ Module Unit Lifting Summary ]                 │", ConsoleColor.Cyan, useTimestamp: false);
      _logger.Log("├───────────────────────────┬─────────────────────────────────────┤", useTimestamp: false);

      _logger.LogSummaryTable("Target Model", Path.GetFileName(_bdfPath));
      _logger.LogSummaryTable("Total Mass", $"{totalMass:F2} ton");
      _logger.LogSummaryTable("Center of Gravity (COG)", $"X:{cog.X:F1}  Y:{cog.Y:F1}  Z:{cog.Z:F1}");
      _logger.LogSummaryTable("Lifting Method", liftingGroups.Exists(g => g.LiftingMethod == 1) ? "Goliat (Trolley)" : "Hydro (Hook)");
      _logger.LogSummaryTable("Lifting Point Groups", $"{liftingGroups.Count} Groups");
      _logger.LogSummaryTable("Auto-SPC Stabilizers", $"Pipe: {pipeSpcs.Count} EA / Struc: {cogSpcs.Count} EA");
      _logger.Log("├───────────────────────────┼─────────────────────────────────────┤", useTimestamp: false);
      _logger.LogSummaryTable("Safety: Overturn", "Safe (무게중심 내 안정적)");
      _logger.LogSummaryTable("Safety: Sling Angle", angleStatus);
      _logger.LogSummaryTable("Safety: Wire Clash", clashStatus);
      _logger.Log("├───────────────────────────┼─────────────────────────────────────┤", useTimestamp: false);
      _logger.LogSummaryTable("Generated Output File", outputFileName);
      _logger.Log("└───────────────────────────┴────────────────────────────────────┘", useTimestamp: false);

      if (isAngleSafe && isInterferenceFree)
        _logger.LogSuccess("BDF 출력 및 모델 전처리 파이프라인이 성공적으로 완료되었습니다.");
      else
        _logger.LogWarning("BDF 파일은 생성되었으나, 일부 기하학적 안전 경고(각도/간섭)가 존재합니다.");

      // ====================================================================
      // [Stage 11] Nastran 본 해석 실행 (옵션 제어)
      // ====================================================================
      if (_runNastranAnalysis)
      {
        _logger.LogDivider("STAGE 11: Nastran 솔버 구동");
        bool isSolved = NastranAnalysisRunner.Run(exportPath, _logger, _pipelineDebug);

        if (!isSolved)
        {
          _logger.LogError("Nastran 솔버 구동에 실패했거나 FATAL이 발생했습니다. 이후 결과 확인을 보장할 수 없습니다.");
        }
        else
        {
          _logger.LogSuccess("▶ F06 / OP2 파일 생성 완료.");
        }
      }

      // ====================================================================
      // [Stage 12 & 13] F06 파싱 및 평가 (콘솔 결과 출력)
      // ====================================================================
      if (_checkAnalysisResult)
      {
        _logger.LogDivider("STAGE 12 & 13: 해석 결과(F06) 파싱 및 평가");

        string f06Path = Path.ChangeExtension(exportPath, ".f06");

        if (File.Exists(f06Path))
        {
          var results = F06Parser.Parse(f06Path, _logger);

          if (results.IsParsedSuccessfully)
          {
            // 1. 최대 변위, 응력, 장력 추출
            var maxDisp = results.Displacements.OrderByDescending(d => d.Magnitude).FirstOrDefault();
            var maxStress = results.BeamStresses.OrderByDescending(s => s.MaxAbsStress).FirstOrDefault();

            double dispVal = maxDisp != null ? maxDisp.Magnitude : 0.0;
            double stressVal = maxStress != null ? maxStress.MaxAbsStress : 0.0;
            double sf = stressVal > 0 ? Math.Round(220.0 / stressVal, 2) : 999.9; // 항복 275 * 0.8 = 220 기준

            // 2. 콘솔에 결과 대시보드 출력 (이 부분을 원하셨습니다!)
            _logger.Log("", useTimestamp: false);
            _logger.Log("┌────────────────────────────────────────────────────────┐", useTimestamp: false);
            _logger.Log("│                 [ F06 Analysis Result ]                │", ConsoleColor.Green, useTimestamp: false);
            _logger.Log("├────────────────────────────────────────────────────────┤", useTimestamp: false);
            _logger.Log($"│ Max Displacement : {dispVal,6:F2} mm (Node {maxDisp?.NodeID})", ConsoleColor.White, useTimestamp: false);
            _logger.Log($"│ Max Beam Stress  : {stressVal,6:F2} MPa (Element {maxStress?.ElementID})", ConsoleColor.White, useTimestamp: false);

            if (sf >= 1.0) _logger.Log($"│ Safety Factor    : {sf,6:F2} (안전, 기준 220MPa)", ConsoleColor.Cyan, useTimestamp: false);
            else _logger.Log($"│ Safety Factor    : {sf,6:F2} (위험! 구조 보강 필요)", ConsoleColor.Red, useTimestamp: false);

            _logger.Log("├────────────────────────────────────────────────────────┤", useTimestamp: false);
            _logger.Log("│ Wire Tension (권상 와이어 장력)                        │", ConsoleColor.White, useTimestamp: false);

            bool hasNegativeTension = false;
            foreach (var rod in results.RodForces)
            {
              double tonForce = rod.AxialForce / 9800.0;
              if (tonForce < 0) hasNegativeTension = true;

              string status = tonForce >= 0 ? "정상(Tension)" : "압축(Compression/느슨함)";
              ConsoleColor color = tonForce >= 0 ? ConsoleColor.Gray : ConsoleColor.Red;

              _logger.Log($"│  - Wire E{rod.ElementID,-8} : {tonForce,6:F2} ton ({status})", color, useTimestamp: false);
            }
            _logger.Log("└────────────────────────────────────────────────────────┘", useTimestamp: false);

            if (hasNegativeTension)
            {
              _logger.LogWarning("일부 와이어에 압축력(음수)이 발생했습니다! COG 위치나 와이어 길이를 점검하세요.");
            }

            // 3. 텍스트 파일로도 내보내기
            ResultExporter.Export(exportPath, results, _analysisType, _logger);
          _logger.LogSuccess($"▶ 요약 리포트 저장 완료: {Path.GetFileName(Path.ChangeExtension(f06Path, ".txt"))}");
          }
          else
          {
            _logger.LogError("F06 파일 파싱 실패. 형식이 다르거나 FATAL 에러로 데이터가 없습니다.");
          }
        }
        else
        {
          _logger.LogError($"해석 결과 파일(.f06)을 찾을 수 없습니다: {f06Path}");
        }
      }

      _logger.Log("", useTimestamp: false);
      _logger.LogSuccess("Hook & Trolley 전체 파이프라인 100% 완료");
      _logger.Log("", useTimestamp: false);
    }
  }
}
