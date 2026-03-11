using ModuleGroupUnitAnalysis.Model.Geometry;
using System.Collections.Generic;

namespace ModuleGroupUnitAnalysis.Model.Entities
{
  /// <summary>
  /// 개별 권상 포인트(Node)의 정보를 담는 클래스
  /// </summary>
  public class LiftingNode
  {
    public int NodeID { get; set; }
    public Point3D Pos { get; set; }

    // 기존 Python 코드에서 계산하던 보조 수학 값 (정렬 및 위치 계산에 사용)
    public double SqrtVal { get; set; }
    public double PVal { get; set; }
  }

  /// <summary>
  /// 권상 포인트 그룹 1세트 (예: 4개의 포인트로 이루어진 1개의 Sling Belt 세트)
  /// </summary>
  public class LiftingGroup
  {
    public int GroupId { get; set; }

    // 해당 그룹에 속한 Node들의 리스트
    public List<LiftingNode> Nodes { get; set; } = new List<LiftingNode>();

    public double LineLength { get; set; }

    /// <summary>
    /// 0 = Hydro (Hook), 1 = Goliat (Trolley)
    /// </summary>
    public int LiftingMethod { get; set; }

    /// <summary>
    /// 다각형 형태 (예: "4개점 사각형 형태", "4개점 일직선 형태")
    /// </summary>
    public string ShapeType { get; set; } = "Unknown";
    public Point3D CalculatedTopPoint { get; set; }
  }
}
