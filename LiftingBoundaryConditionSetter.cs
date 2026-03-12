using System;
using System.Collections.Generic;
using System.Linq;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Utils;

namespace ModuleGroupUnitAnalysis.Pipeline.Modifiers
{
  public static class LiftingBoundaryConditionSetter
  {
    /// <summary>
    /// # HookTrolley-08
    /// 해석 중 모델이 날아가는 현상을 방지하기 위해 Pipe 및 COG 근처에 필수 경계조건(SPC) 노드를 탐색합니다.
    /// 반환값: (Pipe_SPC_노드리스트, COG_SPC_노드리스트)
    /// </summary>
    public static (List<int> pipeSpcNodes, List<int> cogSpcNodes) Run(FeModelContext context, Point3D cog, PipelineLogger logger, bool debugPrint = true)
    {
      if (debugPrint) logger.LogInfo("\n[Stage 8] 해석 안정성 확보를 위한 자동 경계조건(SPC) 할당 시작");

      var pipeSpcNodes = SetPipeSpc(context, cog, logger, debugPrint);
      var cogSpcNodes = SetCogSpc(context, cog, logger, debugPrint);

      if (debugPrint) logger.LogSuccess("8단계 : 자동 경계조건(SPC) 할당 노드 탐색 완료");

      return (pipeSpcNodes, cogSpcNodes);
    }

    // =======================================================================
    // 1. Pipe (TUBE) 그룹 1번 자유도(X) 구속 노드 탐색
    // =======================================================================
    private static List<int> SetPipeSpc(FeModelContext context, Point3D cog, PipelineLogger logger, bool debug)
    {
      var pipeSpcNodes = new List<int>();

      // 1. TUBE 부재를 구성하는 노드 추출
      var pipeNodes = new HashSet<int>();
      var pipeElements = new List<Element>();

      foreach (var kv in context.Elements)
      {
        if (context.Properties.TryGetValue(kv.Value.PropertyID, out var prop) && prop.Type == "TUBE")
        {
          pipeElements.Add(kv.Value);
          foreach (var n in kv.Value.NodeIDs) pipeNodes.Add(n);
        }
      }

      if (pipeNodes.Count == 0) return pipeSpcNodes; // TUBE 없으면 패스

      // 2. TUBE 노드들을 Union-Find로 그룹화
      var uf = new UnionFind(pipeNodes);

      foreach (var ele in pipeElements)
      {
        for (int i = 1; i < ele.NodeIDs.Count; i++)
        {
          uf.Union(ele.NodeIDs[0], ele.NodeIDs[i]);
        }
      }

      // 3. 강체(RBE2)에 의한 연결 및 서포트(Support) 강체 분류
      var supportRbes = new List<RigidInfo>();
      foreach (var kv in context.Rigids)
      {
        var rbe = kv.Value;
        var rbeNodes = new List<int> { rbe.IndependentNodeID };
        rbeNodes.AddRange(rbe.DependentNodeIDs);

        int pipeNodeCount = rbeNodes.Count(n => pipeNodes.Contains(n));

        if (pipeNodeCount == rbeNodes.Count && rbeNodes.Count > 1)
        {
          for (int i = 1; i < rbeNodes.Count; i++) uf.Union(rbeNodes[0], rbeNodes[i]);
        }
        else if (pipeNodeCount > 0)
        {
          supportRbes.Add(rbe);
        }
      }

      // ★ [수정됨] 모든 강체에 포함된 노드 해시 (.Values 에러 수정)
      var allRigidNodes = new HashSet<int>();
      foreach (var kv in context.Rigids)
      {
        var rbe = kv.Value; // KVP에서 꺼냄
        allRigidNodes.Add(rbe.IndependentNodeID);
        foreach (var d in rbe.DependentNodeIDs) allRigidNodes.Add(d);
      }

      // 4. 그룹별로 1번(X) 구속이 있는지 확인
      var clusters = uf.GetClusters();
      int groupIndex = 1;

      foreach (var cluster in clusters.Values)
      {
        var clusterSet = new HashSet<int>(cluster);
        bool hasDof1 = false;

        foreach (var rbe in supportRbes)
        {
          if (clusterSet.Contains(rbe.IndependentNodeID) || rbe.DependentNodeIDs.Any(n => clusterSet.Contains(n)))
          {
            if (rbe.Cm.Contains("1"))
            {
              hasDof1 = true;
              break;
            }
          }
        }

        // 5. 1번 구속이 없는 위험한 배관 그룹은 COG와 가장 가까운 노드에 SPC 부여
        if (!hasDof1)
        {
          var candidates = cluster.Where(n => !allRigidNodes.Contains(n)).ToList();
          if (candidates.Count > 0)
          {
            int closestNode = candidates.OrderBy(n => Point3dUtils.Dist(context.Nodes[n], cog)).First();
            pipeSpcNodes.Add(closestNode);
            if (debug) logger.LogWarning($"  -> [배관 그룹 {groupIndex}] DOF 1 구속이 없어 COG 근처 노드({closestNode})에 SPC(1)를 강제 할당합니다.");
          }
        }
        groupIndex++;
      }

      return pipeSpcNodes;
    }

    // =======================================================================
    // 2. 전체 COG 기준 구조물(H, L) 1, 2번 자유도 구속 노드 탐색
    // =======================================================================
    private static List<int> SetCogSpc(FeModelContext context, Point3D cog, PipelineLogger logger, bool debug)
    {
      var cogSpcNodes = new List<int>();

      // 1. H 또는 L 형강 중 단면 크기(Dim[0]) 상위 5개 PropertyID 추출
      var hlProps = context.Properties
          .Where(kv => kv.Value.Type == "H" || kv.Value.Type == "L")
          .OrderByDescending(kv => kv.Value.Dim.Count > 0 ? kv.Value.Dim[0] : 0.0)
          .Take(5)
          .Select(kv => kv.Key)
          .ToHashSet();

      if (hlProps.Count == 0) return cogSpcNodes;

      // 2. 상위 5개 형강으로 이루어진 노드 추출
      var hlNodes = new HashSet<int>();
      foreach (var kv in context.Elements)
      {
        if (hlProps.Contains(kv.Value.PropertyID))
        {
          foreach (var n in kv.Value.NodeIDs) hlNodes.Add(n);
        }
      }

      // ★ [수정됨] 모델 전체에서 가장 흔하게 나타나는 Z좌표 탐색 (.Values 에러 수정)
      var zCounts = new Dictionary<double, int>();
      foreach (var kv in context.Nodes)
      {
        var n = kv.Value; // KVP에서 꺼냄
        double zRounded = Math.Round(n.Z, 1);
        if (!zCounts.ContainsKey(zRounded)) zCounts[zRounded] = 0;
        zCounts[zRounded]++;
      }

      double mostCommonZ = zCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;

      // 4. 해당 Z 높이에 있는 H/L 빔 노드 필터링
      var candidates = hlNodes.Where(n => Math.Abs(Math.Round(context.Nodes[n].Z, 1) - mostCommonZ) <= 1.0).ToList();

      // 5. COG와 가장 가까운 노드 1개 선택하여 12 방향 구속
      if (candidates.Count > 0)
      {
        int closestNode = candidates.OrderBy(n => Point3dUtils.Dist(context.Nodes[n], cog)).First();
        cogSpcNodes.Add(closestNode);
        if (debug) logger.LogInfo($"  -> [메인 구조물] 모델 비산 방지를 위해 Z={mostCommonZ:F1} 높이의 COG 근처 노드({closestNode})에 SPC(12)를 할당합니다.");
      }
      else
      {
        if (debug) logger.LogWarning("  -> [메인 구조물] 적절한 H/L  노드를 찾지 못해 구조물 강제 구속을 건너뜁니다.");
      }

      return cogSpcNodes;
    }
  }
}
