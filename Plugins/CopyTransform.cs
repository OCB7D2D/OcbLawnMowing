using UnityEngine;

public class CopyTransform : MonoBehaviour
{
    public Vector3 Position = Vector3.zero;
    public Vector3 Scale = Vector3.one;
    public string Prefab;
    public string Path;
    public string Name;
    public bool Debug;
    private bool Added;

#if !UNITY_EDITOR
    private void Awake()
    {
        if (Added) return;
        GameObject asset = DataLoader.LoadAsset<GameObject>(Prefab);
        if (asset == null) { Log.Warning("Could not load asset <{0}>", Prefab); return; }
        if (Debug) Log.Out("Loaded asset {0} => {1}", Prefab, asset);
        Transform transform = asset.transform.Find(Path);
        if (transform == null) { Log.Warning("Could find transform <{0}>", Path); return; }
        if (Debug) Log.Out("Found transform {0} => {1}", Path, transform);
        GameObject cpy = Instantiate(transform.gameObject);
        cpy.name = string.IsNullOrEmpty(Name) ? transform.name : Name;
        cpy.transform.position = Position;
        cpy.transform.localScale = Scale;
        cpy.transform.parent = this.transform;
        Added = true;
    }
#endif

}
