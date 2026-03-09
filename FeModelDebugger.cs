using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Model.Entities;
using System;
using System.Linq;

namespace ModuleGroupUnitAnalysis.Services.Utils
{
  public static class FeModelDebugger
  {
    public static void PrintSummary(FeModelContext context, PipelineLogger logger, bool pipelineDebug, bool verboseDebug)
    {
      if (!pipelineDebug) return;

      logger.LogInfo("\n==================================================");
      logger.LogInfo("           FE Model 로드 결과 요약 (Summary)      ");
      logger.LogInfo("==================================================");
      logger.LogInfo($" - Nodes (GRID)          : {context.Nodes.Count()} EA");
      logger.LogInfo($" - Elements (CBEAM)      : {context.Elements.Count()} EA");
      logger.LogInfo($" - Rigids (RBE2)         : {context.Rigids.Count()} EA");
      logger.LogInfo($" - PointMasses (CONM2)   : {context.PointMasses.Count()} EA");
      logger.LogInfo($" - Properties (PBEAML)   : {context.Properties.Count()} EA");
      logger.LogInfo($" - Materials (MAT1)      : {context.Materials.Count()} EA");
      logger.LogInfo("==================================================\n");

      if (verboseDebug)
      {
        logger.LogWarning(">>> [Verbose Mode] 전체 세부 데이터 리스트 출력 시작 <<<");

        logger.LogInfo("\n[1] MATERIALS");
        foreach (var m in context.Materials) logger.LogInfo($"  {m.Value.ToString()}");

        logger.LogInfo("\n[2] PROPERTIES");
        foreach (var p in context.Properties) logger.LogInfo($"  PropID: {p.Key} | {p.Value.ToString()}");

        logger.LogInfo("\n[3] NODES");
        foreach (var n in context.Nodes) logger.LogInfo($"  NodeID: {n.Key,-6} | X: {n.Value.X,-8:F2} Y: {n.Value.Y,-8:F2} Z: {n.Value.Z,-8:F2}");

        logger.LogInfo("\n[4] ELEMENTS (CBEAM)");
        foreach (var e in context.Elements) logger.LogInfo($"  ElemID: {e.Key,-6} | Nodes: [{string.Join(", ", e.Value.NodeIDs)}] | PropID: {e.Value.PropertyID}");

        logger.LogInfo("\n[5] RIGIDS (RBE2)");
        foreach (var r in context.Rigids) logger.LogInfo($"  RigidID: {r.Key,-6} | {r.Value.ToString()}");

        logger.LogInfo("\n[6] POINT MASSES (CONM2)");
        foreach (var pm in context.PointMasses) logger.LogInfo($"  MassID: {pm.Key,-6} | Node: {pm.Value.NodeID} | Mass: {pm.Value.Mass}");

        logger.LogWarning(">>> [Verbose Mode] 출력 종료 <<<\n");
      }
    }
  }
}
