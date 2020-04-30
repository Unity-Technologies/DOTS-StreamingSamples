using System;
using UnityEngine;
using UnityEditor;
using Random = UnityEngine.Random;

[RequireComponent(typeof(RoomSet))]
public class GenerateDungeon : MonoBehaviour
{
    public DungeonTiles tiles;

    private RoomSet roomSet;
    // Start is called before the first frame update
    private void Awake()
    {
        roomSet = GetComponent<RoomSet>();
        roomSet.LoadTileSet(tiles.SubScenes);
    }

    void Start()
    {
        var guid = new GUID(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tiles.SubScenes[Random.Range(0,tiles.SubScenes.Count - 1)])));
        roomSet.RequestGeneration(guid, tiles);
    }

    void OnDestroy()
    {
        roomSet.CleanupGeneration();
    }
}
