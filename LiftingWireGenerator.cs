using System;
using System.Collections.Generic;
using System.Linq;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Modifiers
{
  public static class LiftingWireGenerator
  {
    /// <summary>
    /// # HookTrolley-09
    /// 계산된 정점에 새로운 Node를 생성하고, 기존 Lifting Point들과 CROD(와이어)로 연결합니다.
    /// 또한 모델 전체를 스캔하여 SPC가 전혀 없는 고립된 덩어리에 강제 경계조건을 주입합니다.
    /// </summary>
    public static void Run(List<LiftingGroup> liftingGroups, FeModelContext context, SpcAssignData spcData, PipelineLogger logger, bool debugPrint = true)
    {
      if (debugPrint) logger.LogInfo("\n[Stage 9] 권상 정점(Node) 및 가상 와이어(CROD) 네트워크 생성 시작");

      // 기존 모델과 ID가 겹치지 않도록 990만 번대역의 안전한 ID 사용
      int startId = 9900001;
      int currentGridId = startId;
      int currentElemId = startId;
      int propId = startId;
      int matId = startId;

      // 1. 와이어(Wire)용 재질(MAT1) 및 프로퍼티(PROD) 카드 생성 (강철 와이어 가정)
      spcData.GeneratedBulkCards.Add($"MAT1,{matId},2.1E5,,0.3,7.85E-9");
      spcData.GeneratedBulkCards.Add($"PROD,{propId},{matId},1963.5"); // 단면적 A=1963.5 (R=25mm 기준)

      // 2. 각 그룹별 정점(GRID)과 와이어(CROD) 생성
      foreach (var group in liftingGroups)
      {
        int topNodeId = currentGridId++;

        // Hook/Trolley 정점 노드 생성
        spcData.GeneratedBulkCards.Add($"GRID,{topNodeId},,{group.CalculatedTopPoint.X:F2},{group.CalculatedTopPoint.Y:F2},{group.CalculatedTopPoint.Z:F2}");

        // 정점이 허공으로 날아가지 않고 지지점이 되도록 123456(모든 자유도) 고정
        spcData.GeneratedBulkCards.Add($"SPC1,1,123456,{topNodeId}");

        // 정점과 대상 유닛 포인트들을 잇는 와어(CROD) 생성
        foreach (var node in group.Nodes)
        {
          spcData.GeneratedBulkCards.Add($"CROD,{currentElemId++},{propId},{topNodeId},{node.NodeID}");
        }

        if (debugPrint) logger.LogInfo($"  -> [Group {group.GroupId}] 정점 노드({topNodeId}) 및 권상 와이어 연결 완료");
      }

      // 3. Stage 8에서 확보한 해석 안정화용 SPC 카드 병합
      foreach (int pNode in spcData.PipeSpcNodes)
      {
        spcData.GeneratedBulkCards.Add($"SPC1,1,1,{pNode}");
      }
      foreach (int cNode in spcData.CogSpcNodes)
      {
        spcData.GeneratedBulkCards.Add($"SPC1,1,12,{cNode}");
      }

      // ====================================================================
      // ★ 4. [신규 안전망] 허공에 뜬 고립 덩어리(Disconnected Component) 추적 및 강제 구속
      // ====================================================================
      FixIsolatedGroups(context, liftingGroups, spcData, logger, debugPrint);

      if (debugPrint) logger.LogSuccess($"9단계 : 가상 와이어 네트워크 및 SPC 카드 텍스트 생성 완료");
    }

    /// <summary>
    /// Element와 RBE 망을 스캔하여 아무런 구속도 없는 덩어리를 찾아 강제로 핀을 꽂습니다.
    /// </summary>
    private static void FixIsolatedGroups(FeModelContext context, List<LiftingGroup> liftingGroups, SpcAssignData spcData, PipelineLogger logger, bool debugPrint)
    {
      var adj = new Dictionary<int, HashSet<int>>();
      var allNodes = new HashSet<int>();

      // 4-1. 모든 Element 연결 스캔
      foreach (var kvp in context.Elements)
      {
        var nids = kvp.Value.NodeIDs;
        foreach (var n in nids) allNodes.Add(n);

        for (int i = 0; i < nids.Count; i++)
        {
          if (!adj.ContainsKey(nids[i])) adj[nids[i]] = new HashSet<int>();
          for (int j = 0; j < nids.Count; j++)
          {
            if (i != j) adj[nids[i]].Add(nids[j]);
          }
        }
      }

      // 4-2. 모든 강체(RBE) 연결 스캔
      foreach (var kvp in context.Rigids)
      {
        var rbe = kvp.Value;
        int indep = rbe.IndependentNodeID;
        allNodes.Add(indep);
        if (!adj.ContainsKey(indep)) adj[indep] = new HashSet<int>();

        foreach (int dep in rbe.DependentNodeIDs)
        {
          allNodes.Add(dep);
          adj[indep].Add(dep);
          if (!adj.ContainsKey(dep)) adj[dep] = new HashSet<int>();
          adj[dep].Add(indep);
        }
      }

      // 4-3. BFS 알고리즘으로 독립된 덩어리(Cluster) 도출
      var visited = new HashSet<int>();
      var clusters = new List<HashSet<int>>();

      foreach (int startNode in allNodes)
      {
        if (visited.Contains(startNode)) continue;

        var cluster = new HashSet<int>();
        var q = new Queue<int>();

        q.Enqueue(startNode);
        visited.Add(startNode);
        cluster.Add(startNode);

        while (q.Count > 0)
        {
          int curr = q.Dequeue();
          if (adj.ContainsKey(curr))
          {
            foreach (int next in adj[curr])
            {
              if (!visited.Contains(next))
              {
                visited.Add(next);
                cluster.Add(next);
                q.Enqueue(next);
              }
            }
          }
        }
        clusters.Add(cluster);
      }

      // 4-4. 현재 안전하게 구속된 모든 노드 ID 수집
      var constrainedNodes = new HashSet<int>();
      foreach (int n in spcData.PipeSpcNodes) constrainedNodes.Add(n);
      foreach (int n in spcData.CogSpcNodes) constrainedNodes.Add(n);
      foreach (var group in liftingGroups)
      {
        // 와이어에 연결된 권상 대상 노드들은 허공에 뜨지 않음
        foreach (var n in group.Nodes) constrainedNodes.Add(n.NodeID);
      }

      // 4-5. 구속이 전혀 없는 덩어리 색출 및 강제 구속
      int isolatedGroupCount = 0;
      foreach (var cluster in clusters)
      {
        if (!cluster.Overlaps(constrainedNodes))
        {
          // 덩어리에 속한 노드 중 임의의 하나(가장 작은 번호)를 타겟으로 지정
          int nodeToFix = cluster.Min();

          spcData.GeneratedBulkCards.Add($"SPC1,1,123456,{nodeToFix}");
          isolatedGroupCount++;

          if (debugPrint)
          {
            logger.LogWarning($"  -> [안전망 작동] 메인 구조물과 끊어진 잉여 덩어리(노드 {cluster.Count}개) 발견! 비산 방지를 위해 노드({nodeToFix})에 강제 구속(SPC 123456)을 할당합니다.");
          }
        }
      }
    }
  }
}
