using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RoomSetEditor : EditorWindow
{
    private DungeonTiles _TileSet;
    private const string kTileSetPrefs = "RoomSetEditor._TileSet";

    public Scene LastEditScene => _LastEditScene;

    private Scene _LastEditScene;
    private RoomSet _roomSet;

    [MenuItem("Dungeon/EditDungeon")]
    static void OpenWindow()
    {
        EditorWindow.GetWindow<RoomSetEditor>();
    }

    void Awake()
    {
        _TileSet = AssetDatabase.LoadAssetAtPath<DungeonTiles>(EditorPrefs.GetString(kTileSetPrefs));
    }

    void EditRoom(SceneAsset room)
    {
        var rigScenePath = AssetDatabase.GetAssetPath(_TileSet.DefaultEditingRigScene);
        var scene = EditorSceneManager.GetSceneByPath(rigScenePath);
        if (!scene.isLoaded || _roomSet == null)
        {
            EditorSceneManager.OpenScene(rigScenePath, OpenSceneMode.Single);
            var go = new GameObject();
            go.hideFlags = HideFlags.HideInHierarchy;
            _roomSet = go.AddComponent<RoomSet>();
            _roomSet.LoadTileSet(_TileSet.SubScenes);
        }

        CloseRoom();
        _LastEditScene = EditorSceneManager.OpenScene(AssetDatabase.GetAssetPath(room), OpenSceneMode.Additive);

        var guid = new GUID(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(room)));
        _roomSet.RequestGeneration(guid, _TileSet);
    }

    void CloseRoom()
    {
        if (!_LastEditScene.isLoaded)
            return;

        if (EditorSceneManager.SaveModifiedScenesIfUserWantsTo(new[] {_LastEditScene}))
            EditorSceneManager.CloseScene(_LastEditScene, true);

        _roomSet.CleanupGeneration();
    }

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        _TileSet = (DungeonTiles)EditorGUILayout.ObjectField("EditScenes", _TileSet, typeof(DungeonTiles), false);
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetString(kTileSetPrefs, AssetDatabase.GetAssetPath(_TileSet));

        if (_TileSet == null)
            return;
        foreach (var scene in _TileSet.SubScenes)
        {
            if (GUILayout.Button(scene.name))
            {
                EditRoom(scene);
            }
        }

        if (GUILayout.Button("Close"))
        {
            CloseRoom();
        }
    }
}
