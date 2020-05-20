using System;
using UnityEngine;
using UnityEditor;
using Random = UnityEngine.Random;

[RequireComponent(typeof(RoomSet))]
public class GenerateDungeon : MonoBehaviour
{
    public DungeonTiles tiles;
    private RoomSet roomSet;
    private RoomSetEditor roomSetEditor;

    private void Awake()
    {
#if UNITY_EDITOR
        roomSet = GetComponent<RoomSet>();
        roomSet.LoadTileSet(tiles.SubScenes);
        roomSetEditor = (RoomSetEditor)EditorWindow.GetWindow(typeof(RoomSetEditor));
#endif
    }

    void Start()
    {
#if UNITY_EDITOR
        var tilePath = roomSetEditor.LastEditScene.isLoaded
            ? roomSetEditor.LastEditScene.path
            : AssetDatabase.GetAssetPath(tiles.SubScenes[Random.Range(0, tiles.SubScenes.Count - 1)]);

        var guid = new GUID(AssetDatabase.AssetPathToGUID(tilePath));
        roomSet.RequestGeneration(guid, tiles);
#endif
    }
}
