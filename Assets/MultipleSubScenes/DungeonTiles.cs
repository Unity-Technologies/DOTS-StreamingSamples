using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[CreateAssetMenu]
public class DungeonTiles : ScriptableObject
{
    public SceneAsset       DefaultEditingRigScene;
    public float            TileSize = 3;
    public List<SceneAsset> SubScenes;
}
