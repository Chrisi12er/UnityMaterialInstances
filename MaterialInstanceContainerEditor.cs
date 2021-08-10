using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditorInternal;
using ExtendedUnityEngine.MaterialInstance;

namespace ExtendedUnityEditor.MaterialInstance
{
	[CustomEditor(typeof(MaterialInstanceContainer))]
	public class MaterialInstanceContainerEditor : Editor
	{
		[InitializeOnLoad]
		public static class EditorEventListener
		{
			private static Object activeObject;
			private static SerializedObject activeSerializedObject;

			static EditorEventListener()
			{
				Selection.selectionChanged += SelectionChanged;
			}
			private static void SelectionChanged()
			{
				Object lastActiveObject = activeObject;
				activeObject = Selection.activeObject;

				//Sync all related to last active Object
				if (lastActiveObject is MaterialInstanceContainer)
				{
					MaterialInstanceContainer.Manager.SyncDependentInstances(lastActiveObject as MaterialInstanceContainer);
				}
				else if (lastActiveObject is Material)
				{
					MaterialInstanceContainer.Manager.SyncDependentInstances(lastActiveObject as Material);
				}

				//Sync now active Object
				if (activeObject is MaterialInstanceContainer)
				{
					(activeObject as MaterialInstanceContainer).SyncInstanceProperties();
				}
				else if (activeObject is Material)
				{
					if (!AssetDatabase.IsMainAsset(activeObject))
					{
						AssetDatabase.LoadAssetAtPath<MaterialInstanceContainer>(AssetDatabase.GetAssetPath(activeObject))?.SyncInstanceProperties();
					}
				}

				//Continuously Check for Changes if Selection is Material
				if (activeObject is Material && !(lastActiveObject is Material))
				{
					activeSerializedObject = new SerializedObject(activeObject);
					EditorApplication.update += Update;
				}
				else if (lastActiveObject is Material && !(activeObject is Material))
				{
					EditorApplication.update -= Update;
				}
			}
			private static void Update()
			{
				if(activeObject != null && activeObject is Material && activeSerializedObject != null)
				{
					if(activeSerializedObject.UpdateIfRequiredOrScript())
					{
						MaterialInstanceContainer.Manager.SyncDependentInstances(activeObject as Material);
					}
				}
			}
		}

		private readonly struct MaterialPropertyInfo
		{
			public readonly MaterialProperty property;
			public readonly MaterialInstanceContainer.OverridePropertyType type;
			public readonly bool isKeyword;
			public readonly string displayTypeName;

			public MaterialPropertyInfo(MaterialProperty materialProperty, Shader shader)
			{
				property = materialProperty;

				if(materialProperty.type == MaterialProperty.PropType.Float)
				{
					var attributes = shader.GetPropertyAttributes(shader.FindPropertyIndex(materialProperty.name));
					if(System.Array.IndexOf(attributes, "Toggle") != -1)
					{
						type = MaterialInstanceContainer.OverridePropertyType.Toggle;
						displayTypeName = "Toggle";

						var getShaderLocalKeywords = typeof(ShaderUtil).GetMethod("GetShaderLocalKeywords", BindingFlags.Static | BindingFlags.NonPublic);
						var keywords = (string[])getShaderLocalKeywords.Invoke(null, new object[]{shader});
						isKeyword = System.Array.IndexOf(keywords, materialProperty.name + "_ON") != -1;
					}
					else if(System.Array.IndexOf(attributes, "ToggleOff") != -1)
					{
						type = MaterialInstanceContainer.OverridePropertyType.ToggleOff;
						displayTypeName = "Toggle";

						var getShaderLocalKeywords = typeof(ShaderUtil).GetMethod("GetShaderLocalKeywords", BindingFlags.Static | BindingFlags.NonPublic);
						var keywords = (string[])getShaderLocalKeywords.Invoke(null, new object[]{shader});
						isKeyword = System.Array.IndexOf(keywords, materialProperty.name + "_OFF") != -1;
					}
					else
					{
						type = MaterialInstanceContainer.OverridePropertyType.Float;
						displayTypeName = type.ToString();
						isKeyword = false;
					}
				}
				else if(materialProperty.type == MaterialProperty.PropType.Color)
				{
					if((materialProperty.flags & MaterialProperty.PropFlags.HDR) != 0)
					{
						type = MaterialInstanceContainer.OverridePropertyType.HDRColor;
						displayTypeName = "HDR Color";
					}
					else
					{
						type = MaterialInstanceContainer.OverridePropertyType.Color;
						displayTypeName = type.ToString();
					}

					isKeyword = false;
				}
				else
				{
					type = (MaterialInstanceContainer.OverridePropertyType)materialProperty.type;
					displayTypeName = type.ToString();
					isKeyword = false;
				}
			}
		}


		private MaterialEditor materialEditor;
		private MaterialInstanceContainer container;
		private SerializedProperty overridesProp;
		private Dictionary<(string, MaterialInstanceContainer.OverridePropertyType), MaterialPropertyInfo> materialProperties; 
		private AnimBool foldoutOverrides;
		private AnimBool foldoutAvailable;
		private GUIStyle availableGUIStyle;
		private ReorderableList reorderableList;

		private string availableSearchInput;
		private GUIStyle availableSearchTextFieldStyle;
		private GUIStyle availableSearchCancelStyle;


		private void Awake()
		{
			EditorApplication.contextualPropertyMenu += OnPropertyContextMenu;
			container = (MaterialInstanceContainer)target;
			overridesProp = serializedObject.FindProperty("overrides");
			RefreshMaterialProperties();

			foldoutOverrides = new AnimBool(true);
			foldoutAvailable = new AnimBool(container.overrides.Count == 0);
			foldoutOverrides.valueChanged.AddListener(Repaint);
			foldoutAvailable.valueChanged.AddListener(Repaint);
			reorderableList = new ReorderableList(serializedObject, serializedObject.FindProperty("overrides"), true, false, true, true);
			reorderableList.showDefaultBackground = true;
			reorderableList.drawElementCallback += OnInspectorGUIOverrideElement;
			reorderableList.onAddDropdownCallback += OnOverrideAddDropdown;
			reorderableList.onRemoveCallback += OnOverrideRemove;

			materialEditor = (MaterialEditor)CreateEditor(container.instance);
		}
		private void OnDestroy()
		{
			EditorApplication.contextualPropertyMenu -= OnPropertyContextMenu;
		}
		public override void OnInspectorGUI()
		{
			if (availableGUIStyle == null)
			{
				availableGUIStyle = new GUIStyle(GUI.skin.GetStyle("RL FooterButton"));
				availableGUIStyle.alignment = TextAnchor.MiddleLeft;
			}

			serializedObject.Update();

			//Header
			//materialEditor?.DrawHeader();

			//Master Material
			EditorGUILayout.PropertyField(serializedObject.FindProperty("master"));
			if(serializedObject.ApplyModifiedProperties())
			{
				MaterialInstanceContainer.Manager.AddReference(container, true);

				if (materialEditor != null)
					DestroyImmediate(materialEditor);
				materialEditor = (MaterialEditor)CreateEditor(container.instance);

				RefreshMaterialProperties();

				serializedObject.Update();
			}

			if (container.master != null)
			{
				//Overrides
				foldoutOverrides.target = EditorGUILayout.BeginFoldoutHeaderGroup(foldoutOverrides.target, "Overrides");
				EditorGUILayout.EndFoldoutHeaderGroup();
				if (EditorGUILayout.BeginFadeGroup(foldoutOverrides.faded))
				{
					reorderableList.DoLayoutList();
				}
				EditorGUILayout.EndFadeGroup();

				//Available
				foldoutAvailable.target = EditorGUILayout.BeginFoldoutHeaderGroup(foldoutAvailable.target, "Available");
				EditorGUILayout.EndFoldoutHeaderGroup();
				if (EditorGUILayout.BeginFadeGroup(foldoutAvailable.faded))
				{
					if(availableSearchTextFieldStyle == null)
						availableSearchTextFieldStyle = GUI.skin.FindStyle("ToolbarSeachTextField");
					if(availableSearchCancelStyle == null)
						availableSearchCancelStyle = GUI.skin.FindStyle("ToolbarSeachCancelButton");


					var r0 = GUILayoutUtility.GetRect(new GUIContent(availableSearchInput), availableSearchTextFieldStyle);
					var r1 = new Rect(r0.xMax - r0.height, r0.y, r0.height, r0.height);
					if(Event.current.type != EventType.MouseDown || !r1.Contains(Event.current.mousePosition))
						availableSearchInput = GUI.TextField(r0, availableSearchInput, availableSearchTextFieldStyle);
					if(!string.IsNullOrEmpty(availableSearchInput))
					{
						if (GUI.Button(r1, "", availableSearchCancelStyle))
						{
							availableSearchInput = "";
							GUI.FocusControl(null);
						}
					}


					foreach (var prop in materialProperties)
					{
						if (container.overrides.FindIndex(x => x.name == prop.Value.property.name) == -1)
						{
							if(string.IsNullOrEmpty(availableSearchInput) 
							|| prop.Value.property.displayName.IndexOf(availableSearchInput, 0, System.StringComparison.CurrentCultureIgnoreCase) != -1
							|| (availableSearchInput.StartsWith(":") && prop.Value.displayTypeName.IndexOf(availableSearchInput.Remove(0, 1), 0, System.StringComparison.CurrentCultureIgnoreCase) != -1))

							if(GUILayout.Button(prop.Value.property.displayName + "  [" + prop.Value.displayTypeName + "]", availableGUIStyle))
							{
								AddOverride(overridesProp, prop.Value, container.instance);
								if (serializedObject.ApplyModifiedProperties())
								{
									container.SyncInstanceProperties();
								}
							}
						}
					}
				}
				EditorGUILayout.EndFadeGroup();
			}

			if (serializedObject.ApplyModifiedProperties())
			{
				container.SyncInstanceProperties();
			}
		}
		private void OnInspectorGUIOverrideElement(Rect rect, int index, bool isActive, bool isFocused)
		{
			Rect checkBoxRect = new Rect(rect.x, rect.y, 16, rect.height);
			Rect fieldRect = new Rect(rect.x + checkBoxRect.width + 4, rect.y, rect.width - checkBoxRect.width - 4, rect.height);

			var itemProp = overridesProp.GetArrayElementAtIndex(index);
			var name = itemProp.FindPropertyRelative("name").stringValue;
			var type = (MaterialInstanceContainer.OverridePropertyType)itemProp.FindPropertyRelative("type").enumValueIndex;
			if(!materialProperties.TryGetValue((name, type), out var matProp)) return;
			var displayName = new GUIContent(matProp.property.displayName);
			var activeProp = itemProp.FindPropertyRelative("active");

			EditorGUI.PropertyField(checkBoxRect, activeProp, GUIContent.none);
			GUI.enabled = activeProp.boolValue;
			switch(type)
			{
				case MaterialInstanceContainer.OverridePropertyType.Color:
					EditorGUI.PropertyField(fieldRect, itemProp.FindPropertyRelative("colorValue"), displayName);
					break;
				case MaterialInstanceContainer.OverridePropertyType.HDRColor:
					{
						var valueProp = itemProp.FindPropertyRelative("colorValue");
						EditorGUI.BeginProperty(fieldRect, displayName, valueProp);
						EditorGUI.BeginChangeCheck();
						var newValue = EditorGUI.ColorField(fieldRect, displayName, valueProp.colorValue, true, true, true);
						if(EditorGUI.EndChangeCheck())
							valueProp.colorValue = newValue;
						EditorGUI.EndProperty();
					}
					break;
				case MaterialInstanceContainer.OverridePropertyType.Vector:
					EditorGUI.PropertyField(fieldRect, itemProp.FindPropertyRelative("vectorValue"), displayName);
					break;
				case MaterialInstanceContainer.OverridePropertyType.Float:
					EditorGUI.PropertyField(fieldRect, itemProp.FindPropertyRelative("floatValue"), displayName);
					break;
				case MaterialInstanceContainer.OverridePropertyType.Range:
					{
						var valueProp = itemProp.FindPropertyRelative("floatValue");
						EditorGUI.BeginProperty(rect, displayName, valueProp);
						EditorGUI.BeginChangeCheck();
						var newValue = EditorGUI.Slider(fieldRect, displayName, valueProp.floatValue, matProp.property.rangeLimits.x, matProp.property.rangeLimits.y);
						if(EditorGUI.EndChangeCheck())
							valueProp.floatValue = newValue;
						EditorGUI.EndProperty();
					}
					break;
				case MaterialInstanceContainer.OverridePropertyType.Toggle:
				case MaterialInstanceContainer.OverridePropertyType.ToggleOff:
					{
						var valueProp = itemProp.FindPropertyRelative("floatValue");
						EditorGUI.BeginProperty(fieldRect, displayName, valueProp);
						EditorGUI.BeginChangeCheck();
						var newValue = EditorGUI.Toggle(fieldRect, displayName, valueProp.floatValue != 0);
						if(EditorGUI.EndChangeCheck())
							valueProp.floatValue = newValue ? 1f : 0f;
						EditorGUI.EndProperty();
					}
					break;
				case MaterialInstanceContainer.OverridePropertyType.Texture:
					var textureType = typeof(Texture);
					switch(matProp.property.textureDimension)
					{
						case TextureDimension.Tex2D: textureType = typeof(Texture2D); break;
						case TextureDimension.Tex3D: textureType = typeof(Texture3D); break;
						case TextureDimension.Cube: textureType = typeof(Cubemap); break;
					}
					EditorGUI.PropertyField(fieldRect, itemProp.FindPropertyRelative("textureValue"), displayName);
					break;
			}
			GUI.enabled = true;
		}
		private void OnOverrideAddDropdown(Rect buttonRect, ReorderableList list)
		{
			var menu = new GenericMenu();
			foreach(var prop in materialProperties)
			{
				if(container.overrides.FindIndex(x => x.name == prop.Value.property.name) == -1)
				{
					var cachedMatprop = prop.Value;
					menu.AddItem(new GUIContent(cachedMatprop.property.displayName + " [" + cachedMatprop.displayTypeName + "]"), false, () => 
					{
						AddOverride(overridesProp, in cachedMatprop, container.instance);
						if(serializedObject.ApplyModifiedProperties())
						{
							container.SyncInstanceProperties();
						}
					});
				}
			}
			menu.DropDown(buttonRect);
		}
		private void OnOverrideRemove(ReorderableList list)
		{
			SerializedProperty property = serializedObject.FindProperty("overrides");
			property.DeleteArrayElementAtIndex(list.index);
			if(serializedObject.ApplyModifiedProperties())
			{
				container.SyncInstanceProperties();
			}
		}
		private void OnPropertyContextMenu(GenericMenu menu, SerializedProperty property)
		{
			if (property.serializedObject.targetObject == target && property.propertyPath.StartsWith("overrides."))
			{
				string path = property.propertyPath;
				int indexStart = path.LastIndexOf("[");
				int indexEnd = path.LastIndexOf("].");
				if (indexStart != -1 && indexEnd != -1 && indexEnd > indexStart + 1)
				{
					string indexStr = path.Substring(indexStart + 1, indexEnd - indexStart - 1);
					int index;
					if (int.TryParse(indexStr, out index) && index > -1)
					{
						property = property.serializedObject.FindProperty("overrides");
						if (property != null && index < property.arraySize)
						{
							menu.AddItem(new GUIContent("Reset"), false, () =>
							{
								ResetOverride(property, index, container.master);
								if (property.serializedObject.ApplyModifiedProperties())
								{
									container.SyncInstanceProperties();
								}
							});
							menu.AddItem(new GUIContent("Remove Override"), false, () =>
							{
								property.DeleteArrayElementAtIndex(index);
								if (property.serializedObject.ApplyModifiedProperties())
								{
									container.SyncInstanceProperties();
								}
							});
						}
					}
				}
			}
		}


		public override bool HasPreviewGUI() => container.master != null;
		public override GUIContent GetPreviewTitle()
		{
			if (container.master != null)
				return materialEditor.GetPreviewTitle();
			return base.GetPreviewTitle();
		}
		public override void OnPreviewSettings()
		{
			if (container.master != null)
				materialEditor.OnPreviewSettings();
		}
		public override void OnPreviewGUI(Rect r, GUIStyle background)
		{
			if (container.master != null)
				materialEditor.OnPreviewGUI(r, background);
		}
		public override void OnInteractivePreviewGUI(Rect r, GUIStyle background)
		{
			if (container.master != null)
				materialEditor.OnInteractivePreviewGUI(r, background);
		}

		//public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
		//{
		//	return null;
		//	var matPreview = AssetPreview.GetAssetPreview(container.instance);
		//	if (matPreview == null) return null;
		//	while (matPreview == null)
		//		matPreview = AssetPreview.GetAssetPreview(container.instance);

		//	Texture2D tex = new Texture2D(width, height);
		//	EditorUtility.CopySerialized(matPreview, tex);

		//	EditorUtility.SetDirty(container);
		//	return tex;
		//}

		private void RefreshMaterialProperties()
		{
			if(materialProperties == null)
				materialProperties = new Dictionary<(string, MaterialInstanceContainer.OverridePropertyType), MaterialPropertyInfo>();
			else
				materialProperties.Clear();
			foreach(var prop in MaterialEditor.GetMaterialProperties(new Object[]{container.instance}))
			{
				if((prop.flags & MaterialProperty.PropFlags.HideInInspector) == 0)
				{
					var propInfo = new MaterialPropertyInfo(prop, container.instance.shader);
					materialProperties.Add((prop.name, propInfo.type), propInfo);
				}
			}
		}

		private static void AddOverride(SerializedProperty serProp, in MaterialPropertyInfo matProp, Material mat)
		{
			serProp.InsertArrayElementAtIndex(serProp.arraySize);
			var itemProp = serProp.GetArrayElementAtIndex(serProp.arraySize-1);
			itemProp.FindPropertyRelative("name").stringValue = matProp.property.name;
			itemProp.FindPropertyRelative("type").enumValueIndex = (int)matProp.type;
			itemProp.FindPropertyRelative("isKeyword").boolValue = matProp.isKeyword;
			itemProp.FindPropertyRelative("active").boolValue = true;
			switch(matProp.type)
			{
				case MaterialInstanceContainer.OverridePropertyType.Color:
				case MaterialInstanceContainer.OverridePropertyType.HDRColor:
					itemProp.FindPropertyRelative("colorValue").colorValue = mat.GetColor(matProp.property.name);
					break;
				case MaterialInstanceContainer.OverridePropertyType.Vector:
					itemProp.FindPropertyRelative("vectorValue").vector4Value = mat.GetVector(matProp.property.name);
					break;
				case MaterialInstanceContainer.OverridePropertyType.Float:
				case MaterialInstanceContainer.OverridePropertyType.Range:
				case MaterialInstanceContainer.OverridePropertyType.Toggle:
				case MaterialInstanceContainer.OverridePropertyType.ToggleOff:
					itemProp.FindPropertyRelative("floatValue").floatValue = mat.GetFloat(matProp.property.name);
					break;
				case MaterialInstanceContainer.OverridePropertyType.Texture:
					itemProp.FindPropertyRelative("textureValue").objectReferenceValue = mat.GetTexture(matProp.property.name);
					break;
			}
		}
		private static void ResetOverride(SerializedProperty serProp, int index, Material mat)
		{
			var itemProp = serProp.GetArrayElementAtIndex(index);
			var type = (MaterialInstanceContainer.OverridePropertyType)itemProp.FindPropertyRelative("type").enumValueIndex;
			var name = itemProp.FindPropertyRelative("name").stringValue;

			switch(type)
			{
				case MaterialInstanceContainer.OverridePropertyType.Color:
				case MaterialInstanceContainer.OverridePropertyType.HDRColor:
					itemProp.FindPropertyRelative("colorValue").colorValue = mat.GetColor(name);
					break;
				case MaterialInstanceContainer.OverridePropertyType.Vector:
					itemProp.FindPropertyRelative("vectorValue").vector4Value = mat.GetVector(name);
					break;
				case MaterialInstanceContainer.OverridePropertyType.Float:
				case MaterialInstanceContainer.OverridePropertyType.Range:
				case MaterialInstanceContainer.OverridePropertyType.Toggle:
				case MaterialInstanceContainer.OverridePropertyType.ToggleOff:
					itemProp.FindPropertyRelative("floatValue").floatValue = mat.GetFloat(name);
					break;
				case MaterialInstanceContainer.OverridePropertyType.Texture:
					itemProp.FindPropertyRelative("textureValue").objectReferenceValue = mat.GetTexture(name);
					break;
			}
		}
	
	}
}
