using UnityEditor;
using ExtendedUnityEngine.MaterialInstance;

namespace ExtendedUnityEditor.MaterialInstance
{
	public class MaterialInstanceModificationProcessor : UnityEditor.AssetModificationProcessor
	{
		private static AssetMoveResult OnWillMoveAsset(string sourcePath, string destinationPath)
		{
			if (AssetDatabase.GetMainAssetTypeAtPath(sourcePath) == typeof(MaterialInstanceContainer))
			{
				var container = AssetDatabase.LoadAssetAtPath<MaterialInstanceContainer>(sourcePath);
				EditorApplication.delayCall += () =>
				{
					container?.SyncInstanceName();
				};
			}
			return AssetMoveResult.DidNotMove;
		}
		private static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions removeAssetOptions)
		{
			if (AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(MaterialInstanceContainer))
			{
				var container = AssetDatabase.LoadAssetAtPath<MaterialInstanceContainer>(path);
				MaterialInstanceContainer.Manager.RemoveReference(container);
			}
			return AssetDeleteResult.DidNotDelete;
		}
		private static string[] OnWillSaveAssets(string[] paths)
		{
			foreach (string path in paths)
			{
				if (path.EndsWith(".mat"))
				{
					UnityEngine.Debug.Log("Sync before save");
					MaterialInstanceContainer.Manager.SyncDependentInstances(path);
				}
			}
			return paths;
		}
	}
}
