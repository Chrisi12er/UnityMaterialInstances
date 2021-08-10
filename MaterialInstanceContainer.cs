using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ExtendedUnityEngine.MaterialInstance
{
	public class MaterialInstanceContainer : ScriptableObject
	{
		public enum OverridePropertyType
		{
			Color = 0,
			Vector = 1,
			Float = 2,
			Range = 3,
			Texture = 4,
			HDRColor = 5,
			Toggle = 6,
			ToggleOff = 7
		}
		[System.Serializable]
		public struct OverrideData
		{
			public string name;

			public OverridePropertyType type;
			public bool isKeyword;

			public Color colorValue;
			public Vector4 vectorValue;
			public float floatValue;
			public Texture textureValue;

			public bool active;
		}

		public Material master;
		public Material instance;
		public List<OverrideData> overrides = new List<OverrideData>();


		#if UNITY_EDITOR
		[InitializeOnLoad]
		public static class Manager
		{
			[SerializeField]
			private static Dictionary<GUID, HashSet<GUID>> references = new Dictionary<GUID, HashSet<GUID>>();

			static Manager()
			{
				string[] guids = AssetDatabase.FindAssets("t:" + typeof(MaterialInstanceContainer).Name);
				for(int i = 0; i < guids.Length; i++)
				{
					string path = AssetDatabase.GUIDToAssetPath(guids[i]);
					var container = AssetDatabase.LoadAssetAtPath<MaterialInstanceContainer>(path);
					if(container)
					{
						AddReference(container);
						container.SyncInstanceName();
						container.SyncInstanceProperties();
					}
				}
			}
			[MenuItem("Assets/Create/Material Instance", priority=301)]
			private static void CreateNewMaterialInstance()
			{
				if(Selection.activeObject is Material)
				{
					var master = Selection.activeObject as Material;
					string path = AssetDatabase.GetAssetPath(master);
					path = path.Replace(".mat", "Instance.asset");
					path = AssetDatabase.GenerateUniqueAssetPath(path);
					MaterialInstanceContainer asset = CreateInstance<MaterialInstanceContainer>();
					AssetDatabase.CreateAsset(asset, path);
					asset.Initialize(master);
					AssetDatabase.SaveAssets();
					Selection.activeObject = asset;
				}
				else if(Selection.activeObject is MaterialInstanceContainer)
				{
					var master = Selection.activeObject as MaterialInstanceContainer;
					string path = AssetDatabase.GetAssetPath(master);
					path = path.Replace(".asset", "Instance.asset");
					path = AssetDatabase.GenerateUniqueAssetPath(path);
					MaterialInstanceContainer asset = CreateInstance<MaterialInstanceContainer>();
					AssetDatabase.CreateAsset(asset, path);
					asset.Initialize(master.instance);
					AssetDatabase.SaveAssets();
					Selection.activeObject = asset;
				}
				else
				{
					string path = AssetDatabase.GetAssetPath(Selection.activeInstanceID);
					if(path.Contains("."))
						path = path.Remove(path.LastIndexOf('/'));
					path += "/MaterialInstance.asset";
					path = AssetDatabase.GenerateUniqueAssetPath(path);
					MaterialInstanceContainer asset = CreateInstance<MaterialInstanceContainer>();
					AssetDatabase.CreateAsset(asset, path);
					asset.Initialize(null);
					AssetDatabase.SaveAssets();
					Selection.activeObject = asset;
				}
			}

			public static void AddReference(MaterialInstanceContainer container, bool checkForOutdatedEntry = false)
			{
				if(container == null) return;

				GUID masterGUID = AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(container.master));
				GUID containerGUID = AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(container));
				AddReference(masterGUID, containerGUID, checkForOutdatedEntry);
			}
			public static void AddReference(GUID master, GUID container, bool checkForOutdatedEntry = false)
			{
				if(master.Empty() || container.Empty()) return;

				//Remove if found somewhere else
				if(checkForOutdatedEntry)
				{
					foreach(var containers in references)
					{
						if(containers.Key != master)
						{
							if(containers.Value.Remove(container))
							{
								
							}
						}
					}
				}

				//Add
				{
					HashSet<GUID> containers;
					if(!references.TryGetValue(master, out containers))
					{
						containers = new HashSet<GUID>();
						references.Add(master, containers);
					}
					containers.Add(container);
				}
				
			}
			public static void RemoveReference(MaterialInstanceContainer container)
			{
				if(container == null) return;

				GUID containerGUID = AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(container));
				RemoveReference(containerGUID);
			}
			public static void RemoveReference(GUID container)
			{
				if(container.Empty()) return;

				foreach(var containers in references)
				{
					containers.Value.Remove(container);
				}
			}

			public static void SyncDependentInstances(MaterialInstanceContainer master)
			{
				if(master == null) return;
				string masterPath = AssetDatabase.GetAssetPath(master.instance);
				SyncDependentInstances(masterPath);
			}
			public static void SyncDependentInstances(Material master)
			{
				if(master == null) return;
				string masterPath = AssetDatabase.GetAssetPath(master);
				SyncDependentInstances(masterPath);
			}
			public static void SyncDependentInstances(string master)
			{
				if(string.IsNullOrWhiteSpace(master)) return;
				GUID masterGUID = AssetDatabase.GUIDFromAssetPath(master);
				SyncDependentInstances(masterGUID);
			}
			public static void SyncDependentInstances(GUID master)
			{
				if(master.Empty()) return;

				HashSet<GUID> containers;
				if(references.TryGetValue(master, out containers))
				{
					foreach(var containerGUID in containers)
					{
						var container = AssetDatabase.LoadAssetAtPath<MaterialInstanceContainer>(AssetDatabase.GUIDToAssetPath(containerGUID));
						container?.SyncInstanceProperties();
					}
				}
			}
		}

		private void Initialize(Material master)
		{
			this.master = master;
			instance = (master == null) ? new Material(Shader.Find("Standard")) : new Material(master);
			instance.name = name;
			instance.hideFlags = HideFlags.NotEditable;
			AssetDatabase.AddObjectToAsset(instance, this);
			Manager.AddReference(this);
			SyncInstanceName();
			SyncInstanceProperties();
			EditorUtility.SetDirty(this);
			EditorUtility.SetDirty(instance);
		}
		public void SyncInstanceName()
		{
			if (instance != null && instance.name != name)
			{
				instance.name = name;
				instance.hideFlags = HideFlags.NotEditable;
				AssetDatabase.SetLabels(instance, new string[] {name});
				EditorUtility.SetDirty(instance);
			}
		}
		public void SyncInstanceProperties()
		{
			if(master == null || instance == null) return;

			//Check For Shader Change
			bool shaderChanged = false;
			if(instance.shader != master.shader)
			{
				shaderChanged = true;
				instance.shader = master.shader;
				var materialProperties = new Dictionary<(string, OverridePropertyType), MaterialProperty>();
				foreach (var prop in MaterialEditor.GetMaterialProperties(new Object[] {master}))
					materialProperties.Add((prop.name, (OverridePropertyType)prop.type), prop);
				Undo.RecordObject(instance, "Update Material");
				for(int i = overrides.Count - 1; i >= 0; i--)
				{
					if (!materialProperties.ContainsKey((overrides[i].name, overrides[i].type)))
						overrides.RemoveAt(i);
					EditorUtility.SetDirty(this);
					EditorUtility.SetDirty(instance);
				}
			}

			//Copy and override Properties if needed
			if(shaderChanged || InstanceHasUnappliedDifferences())
			{
				Undo.RecordObject(instance, "Update Material");
				instance.CopyPropertiesFromMaterial(master);
				instance.shaderKeywords = master.shaderKeywords;

				foreach (var overr in overrides)
				{
					if (overr.active)
					{
						switch (overr.type)
						{
							case OverridePropertyType.Color:
							case OverridePropertyType.HDRColor:
								instance.SetColor(overr.name, overr.colorValue);
								break;
							case OverridePropertyType.Vector:
								instance.SetVector(overr.name, overr.vectorValue);
								break;
							case OverridePropertyType.Float:
							case OverridePropertyType.Range:
							case OverridePropertyType.Toggle:
							case OverridePropertyType.ToggleOff:
								instance.SetFloat(overr.name, overr.floatValue);
								break;
							case OverridePropertyType.Texture:
								instance.SetTexture(overr.name, overr.textureValue);
								break;
						}

						if(overr.isKeyword)
						{
							bool enabled = overr.floatValue != 0;
							string keywordName = overr.name + (overr.type == OverridePropertyType.Toggle ? "_ON" : "OFF");

							if(enabled)
								instance.EnableKeyword(keywordName);
							else
								instance.DisableKeyword(keywordName);
						}
					}
				}
				EditorUtility.SetDirty(this);
				EditorUtility.SetDirty(instance);

				Manager.SyncDependentInstances(this);
			}
		}

		private bool InstanceHasUnappliedDifferences()
		{
			Shader s = master.shader;
			int propCount = ShaderUtil.GetPropertyCount(s);
			for(int i = 0; i < propCount; i++)
			{
				string propName = ShaderUtil.GetPropertyName(s, i);
				var propType = ShaderUtil.GetPropertyType(s, i);
				int propID = Shader.PropertyToID(propName);
				int overrideIndex = GetOverridingPropertyIndex(propName, true);

				if(overrideIndex != -1)
				{
					switch(propType)
					{
						case ShaderUtil.ShaderPropertyType.Range:
						case ShaderUtil.ShaderPropertyType.Float:
							if(instance.GetFloat(propID) != overrides[overrideIndex].floatValue) return true;
							break;
						case ShaderUtil.ShaderPropertyType.Color:
							if(instance.GetColor(propID) != overrides[overrideIndex].colorValue) return true;
							break;
						case ShaderUtil.ShaderPropertyType.Vector:
							if(instance.GetVector(propID) != overrides[overrideIndex].vectorValue) return true;
							break;
						case ShaderUtil.ShaderPropertyType.TexEnv:
							if(instance.GetTexture(propID) != overrides[overrideIndex].textureValue) return true;
							break;
					}
				}
				else
				{
					switch(propType)
					{
						case ShaderUtil.ShaderPropertyType.Range:
						case ShaderUtil.ShaderPropertyType.Float:
							if(instance.GetFloat(propID) != master.GetFloat(propID)) return true;
							break;
						case ShaderUtil.ShaderPropertyType.Color:
							if(instance.GetColor(propID) != master.GetColor(propID)) return true;
							break;
						case ShaderUtil.ShaderPropertyType.Vector:
							if(instance.GetVector(propID) != master.GetVector(propID)) return true;
							break;
						case ShaderUtil.ShaderPropertyType.TexEnv:
							if(instance.GetTexture(propID) != master.GetTexture(propID)) return true;
							break;
					}
				}
			}
			return false;
		}
		private int GetOverridingPropertyIndex(string name,bool onlyReturnIfActive = false)
		{
			for(int i = 0; i < overrides.Count; i++)
			{
				if(overrides[i].name == name && (!onlyReturnIfActive || overrides[i].active)) return i;
			}
			return -1;
		}
		#endif
	}

}

