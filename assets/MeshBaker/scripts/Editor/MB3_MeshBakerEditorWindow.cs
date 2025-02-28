//----------------------------------------------
//            MeshBaker
// Copyright © 2011-2012 Ian Deane
//----------------------------------------------
using UnityEditor;
using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using DigitalOpus.MB.Core;

public class MB3_MeshBakerEditorWindow : EditorWindow, MB3_MeshBakerEditorWindowInterface
{
	public MB3_MeshBakerRoot _target = null;
	public MonoBehaviour target{
		get{ return _target; }
		set{ _target = (MB3_MeshBakerRoot) value; }
	}
	GameObject targetGO = null;
	GameObject oldTargetGO = null;
	MB3_TextureBaker textureBaker;
	MB3_MeshBaker meshBaker;
	MB3_MeshBakerGrouperCore textureBakerGrouper;

	bool onlyStaticObjects = false;
	bool onlyEnabledObjects = false;
	bool excludeMeshesWithOBuvs = true;
	bool excludeMeshesAlreadyAddedToBakers = true;
	int lightmapIndex = -2;
	Material shaderMat = null;
	Material mat = null;
	
	bool tbFoldout = false;
	bool mbFoldout = false;
	
	string generate_AssetsFolder = "";

	List<List<GameObjectFilterInfo>> sceneAnalysisResults = new List<List<GameObjectFilterInfo>>();
	bool[] sceneAnalysisResultsFoldouts = new bool[0];

	MB3_MeshBakerEditorInternal mbe = new MB3_MeshBakerEditorInternal();
	MB3_TextureBakerEditorInternal tbe = new MB3_TextureBakerEditorInternal();

	const int NUM_FILTERS = 5;
	int[] groupByFilterIdxs = new int[NUM_FILTERS];
	string[] groupByOptionNames;
	IGroupByFilter[] groupByOptionFilters;

	Vector2 scrollPos = Vector2.zero;
	Vector2 scrollPos2 = Vector2.zero;

	int selectedTab = 0;
	GUIContent[] tabs = new GUIContent[]{new GUIContent("Analyse Scene & Generate Bakers"),new GUIContent("Search For Meshes To Add")};

	void OnGUI()
	{
		selectedTab = GUILayout.Toolbar(selectedTab,tabs);
		scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Width(position.width), GUILayout.Height(position.height));

		if (selectedTab == 0){
			drawTabAnalyseScene();
		} else {
			drawTabAddObjectsToBakers();
		}

		EditorGUILayout.EndScrollView();
	}

	void drawTabAnalyseScene(){

		//first time we are displaying collect the filters
		if (groupByOptionNames == null || groupByOptionNames.Length == 0){
			var type = typeof(IGroupByFilter);
			var types = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(s => s.GetTypes())
					.Where(p => type.IsAssignableFrom(p));

			List<string> filterNames = new List<string>();
			List<IGroupByFilter> filters = new List<IGroupByFilter>();
			filterNames.Add ("None");
			filters.Add (null);
			foreach (Type tt in types){
				if (!tt.IsAbstract && !tt.IsInterface){
					IGroupByFilter instance = (IGroupByFilter)Activator.CreateInstance(tt);
					filterNames.Add (instance.GetName());
					filters.Add (instance);
				}
			}
			groupByOptionNames = filterNames.ToArray();
			groupByOptionFilters = filters.ToArray ();

			//set filter initial values
			for (int i = 0; i < groupByOptionFilters.Length; i++){
				if (groupByOptionFilters[i] is GroupByShader){
					groupByFilterIdxs[0] = i;
					break;
				}
			}
			for (int i = 0; i < groupByOptionFilters.Length; i++){
				if (groupByOptionFilters[i] is GroupByRenderType){
					groupByFilterIdxs[2] = i;
					break;
				}
			}
			for (int i = 0; i < groupByOptionFilters.Length; i++){
				if (groupByOptionFilters[i] is GroupByOutOfBoundsUVs){
					groupByFilterIdxs[3] = i;
					break;
				}
			}
			groupByFilterIdxs[4] = 0; //none
		}
		if (groupByFilterIdxs == null || groupByFilterIdxs.Length < NUM_FILTERS){
			groupByFilterIdxs = new int[]{
				0,0,0,0,0
			};
		}
		EditorGUILayout.HelpBox("List shaders in scene prints a report to the console of shaders and which objects use them. This is useful for planning which objects to combine.", UnityEditor.MessageType.None);

		groupByFilterIdxs[0] = EditorGUILayout.Popup("Group By:",groupByFilterIdxs[0],groupByOptionNames);
		for (int i = 1; i < NUM_FILTERS; i++){
			groupByFilterIdxs[i] = EditorGUILayout.Popup("Then Group By:",groupByFilterIdxs[i],groupByOptionNames);
		}

		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button("Select Folder For Combined Material Assets") ){
			generate_AssetsFolder = EditorUtility.SaveFolderPanel("Create Combined Material Assets In Folder", "", "");	
			generate_AssetsFolder = "Assets" + generate_AssetsFolder.Replace(Application.dataPath, "") + "/";
		}
		EditorGUILayout.LabelField("Folder: " + generate_AssetsFolder);
		EditorGUILayout.EndHorizontal();
		
		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button("List Shaders In Scene")){
			EditorUtility.DisplayProgressBar("Analysing Scene","",.05f);
			try{
				listMaterialsInScene();
			} catch (Exception ex){
				Debug.LogError(ex.StackTrace);
			} finally {
				EditorUtility.ClearProgressBar();
			}
		}
		
		if (GUILayout.Button("Bake Every MeshBaker In Scene")){
			try{
				MB3_TextureBaker[] texBakers = (MB3_TextureBaker[]) FindObjectsOfType(typeof(MB3_TextureBaker));
				for (int i = 0; i < texBakers.Length; i++){
					texBakers[i].CreateAtlases(updateProgressBar, true, new MB3_EditorMethods());	
				}
				MB3_MeshBakerCommon[] mBakers = (MB3_MeshBakerCommon[]) FindObjectsOfType(typeof(MB3_MeshBakerCommon));
				for (int i = 0; i < mBakers.Length; i++){
					if (mBakers[i].textureBakeResults != null){
						MB3_MeshBakerEditorFunctions.BakeIntoCombined(mBakers[i]);	
					}
				}					
			} catch (Exception e) {
				Debug.LogError(e);
			}finally{
				EditorUtility.ClearProgressBar();
			}
		}
		EditorGUILayout.EndHorizontal();

		if (sceneAnalysisResults.Count > 0){
			float height = position.height - 150f;
			if (height < 500f) height = 500f;
			MB_EditorUtil.DrawSeparator();
			scrollPos2 = EditorGUILayout.BeginScrollView(scrollPos2,false,true); //(scrollPos2,, GUILayout.Width(position.width - 20f), GUILayout.Height(height));
			EditorGUILayout.LabelField("Shaders In Scene",EditorStyles.boldLabel);
			for(int i = 0; i < sceneAnalysisResults.Count; i++){
				List<GameObjectFilterInfo> gows = sceneAnalysisResults[i];
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button ("Generate Baker",GUILayout.Width(200))){
					createAndSetupBaker(gows,generate_AssetsFolder);
				}
				string descr = gows[0].GetDescription(GameObjectFilterInfo.filters,gows[0]);

				EditorGUILayout.LabelField(descr, EditorStyles.wordWrappedLabel);
				EditorGUILayout.EndHorizontal();
				sceneAnalysisResultsFoldouts[i] = EditorGUILayout.Foldout(sceneAnalysisResultsFoldouts[i],"");
				if (sceneAnalysisResultsFoldouts[i]){ 
					EditorGUI.indentLevel += 1;
					for (int j = 0; j < gows.Count; j++){
						if (gows[j].go != null){
							EditorGUILayout.LabelField(gows[j].go.name + "  " + gows[j].GetDescription(GameObjectFilterInfo.filters,gows[j]));
						}
					}
					EditorGUI.indentLevel -= 1;
				}

			}
			EditorGUILayout.EndScrollView();
			MB_EditorUtil.DrawSeparator();
		}
	}

	void drawTabAddObjectsToBakers(){
		EditorGUILayout.HelpBox("Select one or more objects in the hierarchy view. Child Game Objects with MeshRender will be added. Use the fields below to filter what is added.", UnityEditor.MessageType.None);
		target = (MB3_MeshBakerRoot) EditorGUILayout.ObjectField("Target to add objects to",target,typeof(MB3_MeshBakerRoot),true);
		
		if (target != null){
			targetGO = target.gameObject;
		} else {
			targetGO = null;	
		}
		
		if (targetGO != oldTargetGO && targetGO != null){
			textureBaker = targetGO.GetComponent<MB3_TextureBaker>();
			meshBaker = targetGO.GetComponent<MB3_MeshBaker>();
			tbe = new MB3_TextureBakerEditorInternal();
			mbe = new MB3_MeshBakerEditorInternal();
			oldTargetGO = targetGO;
		}

		EditorGUIUtility.labelWidth = 300;
		onlyStaticObjects = EditorGUILayout.Toggle("Only Static Objects", onlyStaticObjects);
		
		onlyEnabledObjects = EditorGUILayout.Toggle("Only Enabled Objects", onlyEnabledObjects);
		
		excludeMeshesWithOBuvs = EditorGUILayout.Toggle("Exclude meshes with out-of-bounds UVs", excludeMeshesWithOBuvs);

		excludeMeshesAlreadyAddedToBakers = EditorGUILayout.Toggle("Exclude GameObjects already added to bakers", excludeMeshesAlreadyAddedToBakers);

		mat = (Material) EditorGUILayout.ObjectField("Using Material",mat,typeof(Material),true);
		shaderMat = (Material) EditorGUILayout.ObjectField("Using Shader",shaderMat,typeof(Material),true);
		
		string[] lightmapDisplayValues = new string[257];
		int[] lightmapValues = new int[257];
		lightmapValues[0] = -2;
		lightmapValues[1] = -1;
		lightmapDisplayValues[0] = "don't filter on lightmapping";
		lightmapDisplayValues[1] = "not lightmapped";
		for (int i = 2; i < lightmapDisplayValues.Length; i++){
			lightmapDisplayValues[i] = "" + i;
			lightmapValues[i] = i;
		}
		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.LabelField("Using Lightmap Index ");
		lightmapIndex = EditorGUILayout.IntPopup(lightmapIndex,
		                                         lightmapDisplayValues,
		                                         lightmapValues);
		EditorGUILayout.EndHorizontal();
		EditorGUILayout.Separator();
		
		if (GUILayout.Button("Add Selected Meshes To Target")){
			addSelectedObjects();
		}
		
		if (textureBaker != null){
			MB_EditorUtil.DrawSeparator();
			tbFoldout = EditorGUILayout.Foldout(tbFoldout,"Texture Baker");
			if (tbFoldout){
				tbe.DrawGUI((MB3_TextureBaker) textureBaker, typeof(MB3_MeshBakerEditorWindow));
			}
			
		}
		if (meshBaker != null){
			MB_EditorUtil.DrawSeparator();
			mbFoldout = EditorGUILayout.Foldout(mbFoldout,"Mesh Baker");
			if (mbFoldout){
				mbe.DrawGUI((MB3_MeshBaker) meshBaker, typeof(MB3_MeshBakerEditorWindow));
			}
		}
	}
	
	List<GameObject> GetFilteredList(){
		List<GameObject> newMomObjs = new List<GameObject>();
		MB3_MeshBakerRoot mom = (MB3_MeshBakerRoot) target;
		if (mom == null){
			Debug.LogError("Must select a target MeshBaker to add objects to");
			return newMomObjs;
		}		
		GameObject dontAddMe = null;
		Renderer r = MB_Utility.GetRenderer(mom.gameObject);
		if (r != null){ //make sure that this MeshBaker object is not in list
			dontAddMe = r.gameObject;	
		}

		MB3_MeshBakerRoot[] allBakers = FindObjectsOfType<MB3_MeshBakerRoot>();
		HashSet<GameObject> objectsAlreadyIncludedInBakers = new HashSet<GameObject>();
		for (int i = 0; i < allBakers.Length; i++){
			List<GameObject> objsToCombine = allBakers[i].GetObjectsToCombine();
			for (int j = 0; j < objsToCombine.Count; j++){
				if (objsToCombine[j] != null) objectsAlreadyIncludedInBakers.Add(objsToCombine[j]);
			}
		}
		
		int numInSelection = 0;
		int numStaticExcluded = 0;
		int numEnabledExcluded = 0;
		int numLightmapExcluded = 0;
		int numOBuvExcluded = 0;
		int numMatExcluded = 0;
		int numShaderExcluded = 0;
		int numAlreadyIncludedExcluded = 0;

		GameObject[] gos = Selection.gameObjects;
		if (gos.Length == 0){
			Debug.LogWarning("No objects selected in hierarchy view. Nothing added. Try selecting some objects.");	
		}
		Dictionary<int,MB_Utility.MeshAnalysisResult> meshAnalysisResultsCache = new Dictionary<int, MB_Utility.MeshAnalysisResult>(); //cache results
		for (int i = 0; i < gos.Length; i++){
			GameObject go = gos[i];
			Renderer[] mrs = go.GetComponentsInChildren<Renderer>();
			for (int j = 0; j < mrs.Length; j++){
				if (mrs[j] is MeshRenderer || mrs[j] is SkinnedMeshRenderer){
					if (mrs[j].GetComponent<TextMesh>() != null){
						continue; //don't add TextMeshes
					}
					numInSelection++;
					if (!newMomObjs.Contains(mrs[j].gameObject)){
						bool addMe = true;
						if (!mrs[j].gameObject.isStatic && onlyStaticObjects){
							numStaticExcluded++;
							addMe = false;
							continue;
						}
						
						if (!mrs[j].enabled && onlyEnabledObjects){
							numEnabledExcluded++;
							addMe = false;
							continue;
						}
	
						if (lightmapIndex != -2){
							if (mrs[j].lightmapIndex != lightmapIndex){
								numLightmapExcluded++;
								addMe = false;
								continue;
							}
						}

						if (excludeMeshesAlreadyAddedToBakers && objectsAlreadyIncludedInBakers.Contains(mrs[j].gameObject)){
							numAlreadyIncludedExcluded++;
							addMe = false;
							continue;
						}
						
						Mesh mm = MB_Utility.GetMesh(mrs[j].gameObject);
						if (mm != null){
							MB_Utility.MeshAnalysisResult mar;
							if (!meshAnalysisResultsCache.TryGetValue(mm.GetInstanceID(),out mar)){
								Rect dummy = new Rect();
								MB_Utility.hasOutOfBoundsUVs(mm, ref dummy, ref mar);
								meshAnalysisResultsCache.Add (mm.GetInstanceID(),mar);
							}
							if (mar.hasOutOfBoundsUVs && excludeMeshesWithOBuvs){
								numOBuvExcluded++;
								addMe = false;
								continue;
							}
						}					
						
						if (shaderMat != null){
							Material[] nMats = mrs[j].sharedMaterials;
							bool usesShader = false;
							foreach(Material nMat in nMats){
								if (nMat != null && nMat.shader == shaderMat.shader){
									usesShader = true;	
								}
							}
							if (!usesShader){
								numShaderExcluded++;
								addMe = false;	
								continue;
							}
						}
						
						if (mat != null){
							Material[] nMats = mrs[j].sharedMaterials;
							bool usesMat = false;
							foreach(Material nMat in nMats){
								if (nMat == mat){
									usesMat = true;
								}
							}
							if (!usesMat){
								numMatExcluded++;
								addMe = false;
								continue;
							}
						}		
									
						if (addMe && mrs[j].gameObject != dontAddMe){
							if (!newMomObjs.Contains(mrs[j].gameObject)){
								newMomObjs.Add(mrs[j].gameObject);
							}
						}	
					}
				}
			}
		}
		Debug.Log( "Total objects in selection " + numInSelection);
		if (numStaticExcluded > 0) Debug.Log(numStaticExcluded + " objects were excluded because they were not static");
		if (numEnabledExcluded > 0) Debug.Log(numEnabledExcluded + " objects were excluded because they were disabled");
		if (numOBuvExcluded > 0) Debug.Log(numOBuvExcluded + " objects were excluded because they had out of bounds uvs");
		if (numLightmapExcluded > 0) Debug.Log(numLightmapExcluded + " objects did not match lightmap filter.");
		if (numShaderExcluded > 0) Debug.Log(numShaderExcluded + " objects were excluded because they did not use the selected shader.");
		if (numMatExcluded > 0) Debug.Log(numMatExcluded + " objects were excluded because they did not use the selected material.");
		if (numAlreadyIncludedExcluded > 0) Debug.Log(numAlreadyIncludedExcluded + " objects were excluded because they did were already included in other bakers.");

		return newMomObjs;
	}

	void addSelectedObjects(){
		MB3_MeshBakerRoot mom = (MB3_MeshBakerRoot) target;
		if (mom == null){
			Debug.LogError("Must select a target MeshBaker to add objects to");
			return;
		}
		List<GameObject> newMomObjs = GetFilteredList();
		
		MBVersionEditor.RegisterUndo(mom, "Add Objects");
		List<GameObject> momObjs = mom.GetObjectsToCombine();
		int numAdded = 0;
		for (int i = 0; i < newMomObjs.Count;i++){
			if (!momObjs.Contains(newMomObjs[i])){
				momObjs.Add(newMomObjs[i]);
				numAdded++;
			}
		}
		SerializedObject so = new SerializedObject(mom);
		so.SetIsDifferentCacheDirty();
		
		if (numAdded == 0){
			Debug.LogWarning("Added 0 objects. Make sure some or all objects are selected in the hierarchy view. Also check ths 'Only Static Objects', 'Using Material' and 'Using Shader' settings");
		} else {
			Debug.Log("Added " + numAdded + " objects to " + mom.name);
		}
	}

	void listMaterialsInScene(){
		if (!ValidateGroupByFields()) return;

		//Get All Objects Already In a list of objects to be combined
		MB3_MeshBakerRoot[] allBakers = FindObjectsOfType<MB3_MeshBakerRoot>();
		HashSet<GameObject> objectsAlreadyIncludedInBakers = new HashSet<GameObject>();
		for (int i = 0; i < allBakers.Length; i++){
			List<GameObject> objsToCombine = allBakers[i].GetObjectsToCombine();
			for (int j = 0; j < objsToCombine.Count; j++){
				if (objsToCombine[j] != null) objectsAlreadyIncludedInBakers.Add(objsToCombine[j]);
			}
		}

		List<GameObjectFilterInfo> gameObjects = new List<GameObjectFilterInfo>();
		Renderer[] rs = (Renderer[]) FindObjectsOfType(typeof(Renderer));
//		Profile.StartProfile("listMaterialsInScene1");
		EditorUtility.DisplayProgressBar("Analysing Scene","Collecting Renderers",.25f);
		for (int i = 0; i < rs.Length; i++){
			Renderer r = rs[i];
			if (r is MeshRenderer || r is SkinnedMeshRenderer){
				if (r.GetComponent<TextMesh>() != null){
					continue; //don't add TextMeshes
				}
				GameObjectFilterInfo goaw = new GameObjectFilterInfo(r.gameObject,objectsAlreadyIncludedInBakers);
				gameObjects.Add (goaw);
				EditorUtility.DisplayProgressBar("Analysing Scene","Collecting Renderer For " + r.name,.1f);
			}
		}

		Dictionary<int,MB_Utility.MeshAnalysisResult> meshAnalysisResultCache = new Dictionary<int, MB_Utility.MeshAnalysisResult>();
		int totalVerts = 0;
		for (int i = 0; i < gameObjects.Count; i++){
			string rpt = String.Format ("Processing {0} [{1} of {2}]",gameObjects[i].go.name,i,gameObjects.Count);
			EditorUtility.DisplayProgressBar("Analysing Scene",rpt + " A",.6f);
			Mesh mm = MB_Utility.GetMesh(gameObjects[i].go);
			int nVerts = 0;
			if (mm != null){
				nVerts += mm.vertexCount;
				MB_Utility.MeshAnalysisResult mar;
				if (!meshAnalysisResultCache.TryGetValue(mm.GetInstanceID(),out mar)){
					Rect dummy = new Rect();
					EditorUtility.DisplayProgressBar("Analysing Scene",rpt + " Check Out Of Bounds UVs",.6f);
					MB_Utility.hasOutOfBoundsUVs(mm,ref dummy,ref mar);
					MB_Utility.doSubmeshesShareVertsOrTris(mm,ref mar);
					meshAnalysisResultCache.Add (mm.GetInstanceID(),mar);
				}
				if (mar.hasOutOfBoundsUVs){
					int w = (int) mar.uvRect.width;
					int h = (int) mar.uvRect.height;
					gameObjects[i].outOfBoundsUVs = true;
					gameObjects[i].warning += " [WARNING: has uvs outside the range (0,1) tex is tiled " + w + "x" + h + " times]";
				}
				if (mar.hasOverlappingSubmeshVerts){
					gameObjects[i].submeshesOverlap = true;
					gameObjects[i].warning += " [WARNING: Submeshes share verts or triangles. 'Multiple Combined Materials' feature may not work.]";
				}
			}
			totalVerts += nVerts;
			EditorUtility.DisplayProgressBar("Analysing Scene",rpt + " Validate OBuvs Multi Material",.6f);
			Renderer mr = gameObjects[i].go.GetComponent<Renderer>();
			if (!MB_Utility.AreAllSharedMaterialsDistinct(mr.sharedMaterials)){
				gameObjects[i].warning += " [WARNING: Object uses same material on multiple submeshes. This may produce poor results when used with multiple materials or fix out of bounds uvs.]";
			}
		}

		Dictionary<GameObjectFilterInfo,List<GameObjectFilterInfo>> gs2bakeGroupMap = new Dictionary<GameObjectFilterInfo,List<GameObjectFilterInfo>>();
		List<GameObjectFilterInfo> objsNotAddedToBaker = new List<GameObjectFilterInfo>();		
		
		sortIntoBakeGroups3(gameObjects, gs2bakeGroupMap, objsNotAddedToBaker);

		sceneAnalysisResults = new List<List<GameObjectFilterInfo>>(); 
		sceneAnalysisResultsFoldouts = new bool[gs2bakeGroupMap.Keys.Count];
		int ii = 0;
		foreach (GameObjectFilterInfo gow in gs2bakeGroupMap.Keys){
			sceneAnalysisResultsFoldouts[ii++] = true;
			List<GameObjectFilterInfo> gows = gs2bakeGroupMap[gow];
			sceneAnalysisResults.Add (gows);
		}

//		if (generateMeshBakers){
//			createBakers(gs2bakeGroupMap, objsNotAddedToBaker);						
//		} else {
			Debug.Log( generateSceneAnalysisReport(gs2bakeGroupMap, objsNotAddedToBaker) );			
//		}		
	}

	string generateSceneAnalysisReport(Dictionary<GameObjectFilterInfo,List<GameObjectFilterInfo>> gs2bakeGroupMap, List<GameObjectFilterInfo> objsNotAddedToBaker){
		string outStr = "(Click me, if I am too big copy and paste me into a spreadsheet or text editor)\n";// Materials in scene " + shader2GameObjects.Keys.Count + " and the objects that use them:\n";
		outStr += "\t\tOBJECT NAME\tLIGHTMAP INDEX\tSTATIC\tOVERLAPPING SUBMESHES\tOUT-OF-BOUNDS UVs\tNUM MATS\tMATERIAL\tWARNINGS\n";
		int totalVerts = 0;
		string outStr2 = "";
		foreach(List<GameObjectFilterInfo> gos in gs2bakeGroupMap.Values){
			outStr2 = "";
			totalVerts = 0;
			gos.Sort();
			for (int i = 0; i < gos.Count; i++){
				totalVerts += gos[i].numVerts;
				string matStr = "";
				Renderer mr = gos[i].go.GetComponent<Renderer>();
				foreach(Material mmm in mr.sharedMaterials){
					matStr += "[" + mmm + "] ";
				}				
				outStr2 += "\t\t" + gos[i].go.name + " (" + gos[i].numVerts + " verts)\t" + gos[i].lightmapIndex + "\t" + gos[i].isStatic + "\t" + gos[i].submeshesOverlap + "\t" + gos[i].outOfBoundsUVs + "\t" + gos[i].numMaterials + "\t" + matStr + "\t" + gos[i].warning + "\n";	
			}
			outStr2 = "\t" + gos[0].shaderName + " (" + totalVerts + " verts): \n" + outStr2;
			outStr += outStr2;
		}
		if (objsNotAddedToBaker.Count > 0){
			outStr += "Other objects\n";
			string shaderName = "";
			totalVerts = 0;
			List<GameObjectFilterInfo> gos1 = objsNotAddedToBaker;
			gos1.Sort();
			outStr2 = "";
			for (int i = 0; i < gos1.Count; i++){
				if (!shaderName.Equals( objsNotAddedToBaker[i].shaderName )){
					outStr2 += "\t" + gos1[0].shaderName + "\n";
					shaderName = objsNotAddedToBaker[i].shaderName;	
				}
				totalVerts += gos1[i].numVerts;
				string matStr = "";
				Renderer mr = gos1[i].go.GetComponent<Renderer>();
				foreach(Material mmm in mr.sharedMaterials){
					matStr += "[" + mmm + "] ";
				}				
				outStr2 += "\t\t" + gos1[i].go.name + " (" + gos1[i].numVerts + " verts)\t" + gos1[i].lightmapIndex + "\t" + gos1[i].isStatic + "\t" + gos1[i].submeshesOverlap + "\t" + gos1[i].outOfBoundsUVs + "\t" + gos1[i].numMaterials + "\t" + matStr + "\t" + gos1[i].warning + "\n";	
			}
			outStr += outStr2;	
		}		
		return outStr;
	}
	
	bool MaterialsAreTheSame(GameObjectFilterInfo a, GameObjectFilterInfo b){
		HashSet<Material> aMats = new HashSet<Material>();
		for(int i = 0; i < a.materials.Length; i++) aMats.Add(a.materials[i]);
		HashSet<Material> bMats = new HashSet<Material>();
		for(int i = 0; i < b.materials.Length; i++) bMats.Add(b.materials[i]);
		return aMats.SetEquals(bMats);
	}

	bool ShadersAreTheSame(GameObjectFilterInfo a, GameObjectFilterInfo b){
		HashSet<Shader> aMats = new HashSet<Shader>();
		for(int i = 0; i < a.shaders.Length; i++) aMats.Add(a.shaders[i]);
		HashSet<Shader> bMats = new HashSet<Shader>();
		for(int i = 0; i < b.shaders.Length; i++) bMats.Add(b.shaders[i]);
		return aMats.SetEquals(bMats);
	}
	 
	void sortIntoBakeGroups3(List<GameObjectFilterInfo> gameObjects,
	                         Dictionary<GameObjectFilterInfo,List<GameObjectFilterInfo>> gs2bakeGroupMap,
	                         List<GameObjectFilterInfo> objsNotAddedToBaker){
		List<GameObjectFilterInfo> gos = gameObjects;
		if (gos.Count < 1) return;

		List<IGroupByFilter> gbfs = new List<IGroupByFilter>();
		for (int i = 0; i < groupByFilterIdxs.Length; i++){
			if (groupByFilterIdxs[i] != 0){
				gbfs.Add ( groupByOptionFilters[groupByFilterIdxs[i]] );
			}
		}
		GameObjectFilterInfo.filters = gbfs.ToArray();
	
		gos.Sort();
		List<GameObjectFilterInfo> l = null;
		GameObjectFilterInfo key = gos[0];
		Debug.Log ("-----------");
		for (int i = 0; i < gos.Count; i++){
			GameObjectFilterInfo goaw = gos[i];
			//compare with key and decide if we need a new list
			for (int j = 0; j < GameObjectFilterInfo.filters.Length; j++){
				if (GameObjectFilterInfo.filters[j] != null && GameObjectFilterInfo.filters[j].Compare(key,goaw) != 0) l = null; 
			}		
			if (l == null){
				l = new List<GameObjectFilterInfo>();
				gs2bakeGroupMap.Add(gos[i],l);
				key = gos[i];
			}
			l.Add(gos[i]);				
		}
	}
	
	void createBakers(Dictionary<GameObjectFilterInfo,List<GameObjectFilterInfo>> gs2bakeGroupMap, List<GameObjectFilterInfo> objsNotAddedToBaker){
		string s = "";
		int numBakers = 0;
		int numObjsAdded = 0;
		
		if (generate_AssetsFolder == null || generate_AssetsFolder == ""){
			Debug.LogError("Need to choose a folder for saving the combined material assets.");
			return;
		}
		
		List<GameObjectFilterInfo> singletonObjsNotAddedToBaker = new List<GameObjectFilterInfo>();
		foreach(List<GameObjectFilterInfo> gaw in gs2bakeGroupMap.Values){
			if (gaw.Count > 1){
				numBakers ++;
				numObjsAdded += gaw.Count;
				createAndSetupBaker(gaw, generate_AssetsFolder);
				s += "  Created meshbaker for shader=" + gaw[0].shaderName + " lightmap=" + gaw[0].lightmapIndex + " OBuvs=" + gaw[0].outOfBoundsUVs + "\n";
			} else {
				singletonObjsNotAddedToBaker.Add(gaw[0]);
			}
		}		
		s = "Created " + numBakers + " bakers. Added " + numObjsAdded + " objects\n" + s;
		Debug.Log(s);
		s = "Objects not added=" + objsNotAddedToBaker.Count + " objects that have unique material=" + singletonObjsNotAddedToBaker.Count + "\n";
		for (int i = 0; i < objsNotAddedToBaker.Count; i++){
			s += "    " + objsNotAddedToBaker[i].go.name + 
						" isStatic=" + objsNotAddedToBaker[i].isStatic + 
					    " submeshesOverlap" + objsNotAddedToBaker[i].submeshesOverlap + 
					    " numMats=" + objsNotAddedToBaker[i].numMaterials + "\n";
		}
		for (int i = 0; i < singletonObjsNotAddedToBaker.Count; i++){
			s += "    " + singletonObjsNotAddedToBaker[i].go.name + " single\n";
		}		
		Debug.Log(s);		
	}
	
	void createAndSetupBaker(List<GameObjectFilterInfo> gaws, string pthRoot){
		for (int i = gaws.Count - 1; i >= 0; i--){
			if (gaws[i].go == null) gaws.RemoveAt(i);
		}
		if (gaws.Count < 1){
			Debug.LogError ("No game objects.");
			return;
		}

		if (pthRoot == null || pthRoot == ""){
			Debug.LogError ("Folder for saving created assets was not set.");
			return;
		}
		
		int numVerts = 0;
		for (int i = 0; i < gaws.Count; i++){
			if (gaws[i].go != null){
				numVerts = gaws[i].numVerts;
			}
		}
		
		GameObject newMeshBaker = null;
		if (numVerts >= 65535){
			newMeshBaker = MB3_MultiMeshBakerEditor.CreateNewMeshBaker();
		} else {
			newMeshBaker = MB3_MeshBakerEditor.CreateNewMeshBaker();
		}
		
		newMeshBaker.name = ("MeshBaker-" + gaws[0].shaderName + "-LM" + gaws[0].lightmapIndex).ToString().Replace("/","-");
			
		MB3_TextureBaker tb = newMeshBaker.GetComponent<MB3_TextureBaker>();
		MB3_MeshBakerCommon mb = tb.GetComponentInChildren<MB3_MeshBakerCommon>();

		tb.GetObjectsToCombine().Clear();
		for (int i = 0; i < gaws.Count; i++){
			if (gaws[i].go != null){
				tb.GetObjectsToCombine().Add(gaws[i].go);
			}
		}

		if (gaws[0].numMaterials > 1){
			tb.doMultiMaterial = true;
			SerializedObject tbr = new SerializedObject(tb);
			SerializedProperty resultMaterials = tbr.FindProperty("resultMaterials");
			MB3_TextureBakerEditorInternal.ConfigureMutiMaterialsFromObjsToCombine (tb,resultMaterials,tbr);
		} else {
			string pthMat = AssetDatabase.GenerateUniqueAssetPath( pthRoot + newMeshBaker.name + ".asset" );
			MB3_TextureBakerEditorInternal.CreateCombinedMaterialAssets(tb, pthMat);
		}
		if (gaws[0].isMeshRenderer) {
			mb.meshCombiner.renderType = MB_RenderType.meshRenderer;  
		} else {
			mb.meshCombiner.renderType = MB_RenderType.skinnedMeshRenderer;
		}
	}
	
	void bakeAllBakersInScene(){
		MB3_MeshBakerRoot[] bakers =(MB3_MeshBakerRoot[]) FindObjectsOfType(typeof(MB3_MeshBakerRoot));	
		for (int i = 0; i < bakers.Length; i++){
			if (bakers[i] is MB3_TextureBaker){
				MB3_TextureBaker tb = (MB3_TextureBaker) bakers[i];
				tb.CreateAtlases(updateProgressBar, true, new MB3_EditorMethods());
			}
		}
		EditorUtility.ClearProgressBar();
	}

	public void BuildAllSource(MB3_TextureBaker mom) {
		SerializedObject tbr = new SerializedObject(mom);
		SerializedProperty resultMaterials = tbr.FindProperty("resultMaterials");
		MB3_TextureBakerEditorInternal.ConfigureMutiMaterialsFromObjsToCombine (mom,resultMaterials,tbr);
		tbr.UpdateIfDirtyOrScript();
		
	}

	public void updateProgressBar(string msg, float progress){
		EditorUtility.DisplayProgressBar("Combining Meshes", msg, progress);
	}

	bool ValidateGroupByFields(){
		bool foundNone = false;
		for (int i = 0; i < groupByFilterIdxs.Length; i++){
			if (groupByFilterIdxs[i] == 0) foundNone = true; //zero is the none selection
			if (foundNone && groupByFilterIdxs[i] != 0){
				Debug.LogError("All non-none values must be at the top of the group by list");
				return false;
			}
		}
		for (int i = 0; i < groupByFilterIdxs.Length; i++){
			for (int j = i+1; j < groupByFilterIdxs.Length; j++){
				if (groupByFilterIdxs[i] == groupByFilterIdxs[j] && groupByFilterIdxs[i] != 0){
					Debug.LogError("Two of the group by options are the same.");
					return false;
				}
			}
		}
		return true;
	}
}