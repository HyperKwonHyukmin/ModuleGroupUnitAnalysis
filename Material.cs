using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ModuleGroupUnitAnalysis.Model.Entities
{
  public sealed class Material
  {
    public string Name { get; }
    public double E { get; }
    public double Nu { get; }
    public double Rho { get; }

    public Material(
      string name,
      double elasticModulus,
      double poissonRatio,
      double density)
    {
      if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("Material name is invalid.", nameof(name));

      Name = name;
      E = elasticModulus;
      Nu = poissonRatio;
      Rho = density;
    }

    public override string ToString()
    {
      return $"Material:{Name}, E:{E}, ν:{Nu}, ρ:{Rho}";
    }
  }

  public sealed class Materials : IEnumerable<KeyValuePair<int, Material>>
  {
    private int _nextMaterialID = 1;

    private readonly Dictionary<int, Material> _materials = new();
    private readonly Dictionary<string, int> _lookup = new();


    public Material this[int materialID]
    {
      get
      {
        if (!_materials.TryGetValue(materialID, out var mat))
          throw new KeyNotFoundException($"Material ID {materialID} does not exist.");
        return mat;
      }
    }

    public int AddOrGet(
      string name,
      double youngsModulus,
      double poissonRatio,
      double density)
    {
      string key = MakeKey(name, youngsModulus, poissonRatio, density);

      if (_lookup.TryGetValue(key, out int existingID))
        return existingID;

      int newID = _nextMaterialID++;
      _materials[newID] = new Material(
        name,
        youngsModulus,
        poissonRatio,
        density);

      _lookup[key] = newID;
      return newID;
    }

    /// <summary>
    /// 지정된 ID로 Material 객체를 생성하여 컬렉션에 강제로 추가합니다.
    /// (BDF 파싱 등 원본 ID를 유지해야 할 때 사용합니다.)
    /// </summary>
    public void AddWithID(
      int materialID,
      string name,
      double youngsModulus,
      double poissonRatio,
      double density)
    {
      string key = MakeKey(name, youngsModulus, poissonRatio, density);

      // 이미 동일한 ID가 있다면, 기존 Lookup 캐시를 지워 충돌을 방지합니다.
      if (_materials.TryGetValue(materialID, out var oldMat))
      {
        string oldKey = MakeKey(oldMat.Name, oldMat.E, oldMat.Nu, oldMat.Rho);
        _lookup.Remove(oldKey);
      }

      // 새로운 매테리얼 객체 생성 및 딕셔너리 할당
      _materials[materialID] = new Material(name, youngsModulus, poissonRatio, density);
      _lookup[key] = materialID;

      // 자동 채번 ID가 파싱된 ID와 겹치지 않도록 최대값 동기화
      if (materialID >= _nextMaterialID)
      {
        _nextMaterialID = materialID + 1;
      }
    }

    public void Remove(int materialID)
    {
      if (!_materials.TryGetValue(materialID, out var mat))
        throw new KeyNotFoundException($"Material ID {materialID} does not exist.");

      string key = MakeKey(
        mat.Name,
        mat.E,
        mat.Nu,
        mat.Rho);

      _materials.Remove(materialID);
      _lookup.Remove(key);
    }

    public bool Contains(int materialID)
      => _materials.ContainsKey(materialID);

    public int Count
      => _materials.Count;

    public IEnumerable<int> Keys
      => _materials.Keys;

    public IReadOnlyDictionary<int, Material> AsReadOnly()
      => _materials;

    private static string MakeKey(
      string name,
      double youngsModulus,
      double poissonRatio,
      double density)
    {
      return string.Join("|",
        name,
        youngsModulus.ToString("G17", CultureInfo.InvariantCulture),
        poissonRatio.ToString("G17", CultureInfo.InvariantCulture),
        density.ToString("G17", CultureInfo.InvariantCulture));
    }


    public IEnumerator<KeyValuePair<int, Material>> GetEnumerator()
      => _materials.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
      => GetEnumerator();

  }
}
