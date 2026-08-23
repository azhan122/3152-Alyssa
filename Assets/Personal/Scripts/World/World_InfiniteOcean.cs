using EditorAttributes;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the ocean surface and drives the <c>LowPolyWater/WaterShaded URP</c> material.
/// <para>
/// Everything the water needs to agree on lives here rather than half here and half on the
/// material: the triangle size comes from the mesh this builds, and wave length is only
/// meaningful next to that triangle size, so splitting them across two inspectors is how you end
/// up with a wave nobody can see. The material keeps the pure look settings - colour, glint,
/// foam texture, shore blend.
/// </para>
/// <para>
/// The mesh is generated at runtime and never saved, so there is no plane asset to keep in sync.
/// The renderer's bounds are widened to match, because the shader slides the sheet under the
/// camera in the vertex stage and the culler would otherwise still be testing where the mesh
/// nominally sits. Wave sampling is fed <see cref="World_Origin"/>'s accumulated offset so a
/// floating-origin rebase does not drag the whole swell sideways.
/// </para>
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class World_InfiniteOcean : MonoBehaviour
{
	private const string GeneratedMeshName = "Infinite Ocean (generated)";

	/// <summary>Caps a typo in Triangle Size at ~160k verts instead of locking up the editor.</summary>
	private const int MaxSegments = 400;

	private static readonly int WaveHeightId = Shader.PropertyToID("_WaveHeight");
	private static readonly int WaveLengthId = Shader.PropertyToID("_WaveLength");
	private static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
	private static readonly int WaveDirectionId = Shader.PropertyToID("_WaveDirection");
	private static readonly int WaveChaosId = Shader.PropertyToID("_WaveChaos");
	private static readonly int WaveFadeDistanceId = Shader.PropertyToID("_WaveFadeDistance");
	private static readonly int FoamId = Shader.PropertyToID("_Foam");
	private static readonly int BumpTilingId = Shader.PropertyToID("_BumpTiling");
	private static readonly int BumpDirectionId = Shader.PropertyToID("_BumpDirection");
	private static readonly int FoamFadeDistanceId = Shader.PropertyToID("_FoamFadeDistance");
	private static readonly int MeshExtentId = Shader.PropertyToID("_MeshExtent");
	private static readonly int FollowSnapId = Shader.PropertyToID("_FollowSnap");
	private static readonly int HorizonReachId = Shader.PropertyToID("_HorizonReach");
	private static readonly int HorizonFalloffId = Shader.PropertyToID("_HorizonFalloff");
	private static readonly int InfiniteOceanId = Shader.PropertyToID("_InfiniteOcean");
	private static readonly int WorldOriginOffsetId = Shader.PropertyToID("_WorldOriginOffset");

	[Title("Size")]
	[HelpBox("Colour, sun glint, foam texture and shore blending live on the material. Everything that has to match the mesh lives here. Edits made while playing are kept when you stop - save the scene to make them stick.", MessageMode.None)]
	[SerializeField, Suffix("m across")] private float oceanSize = 2000f;

	[Tooltip("How wide one triangle is. Smaller means chunkier detail and more vertices.")]
	[SerializeField, Suffix("m per triangle")] private float triangleSize = 10f;

	[Tooltip("Keeps the ocean centred on the camera so you can never sail off it. Turn off for a fixed pond.")]
	[SerializeField] private bool followCamera = true;

	[Title("Waves")]
	[SerializeField, Suffix("m tall")] private float waveHeight = 1.5f;

	[HelpBox("Sets the scale of both the swell and the chop. Keep it comfortably bigger than Triangle Size - below about 3x there are too few triangles per wave to trace its shape, and the surface breaks up into unreadable flicker rather than water.", MessageMode.None)]
	[SerializeField, Suffix("m between crests")] private float waveLength = 40f;

	[SerializeField, Suffix("m per second")] private float waveSpeed = 3f;

	[Tooltip("Compass direction the swell rolls towards.")]
	[SerializeField, Range(0f, 360f)] private float waveDirection = 45f;

	[Tooltip("0 is a clean rolling swell, which always reads a bit like a corrugated roof. 1 is broken, random, choppy water. Most open ocean wants 0.6 or above.")]
	[SerializeField, Range(0f, 1f)] private float waveChaos = 0.6f;

	[Title("Foam")]
	[SerializeField, Range(0f, 3f)] private float foamAmount = 0.6f;

	[Tooltip("How far up a wave the foam starts. 0 foams everything, 1 foams only the very peaks.")]
	[SerializeField, Range(0f, 1f)] private float foamOnCrests = 0.5f;

	[SerializeField, Suffix("m per foam tile")] private float foamScale = 50f;

	[SerializeField, Suffix("m per second")] private float foamSpeed = 1.5f;

	[Title("Distance")]
	[HelpBox("Reach 1 leaves the mesh completely alone: even grid, water locked to the world, rock steady. Above 1 it stretches the outer rings towards the horizon to fake extra distance - the far triangles get huge and the sheet has to slide rather than step, so the facets drift a little as you move. Raise it only if you need distance more than steadiness.", MessageMode.None)]
	[SerializeField, Clamp(1f, 20f)] private float horizonReach = 1f;

	[Tooltip("Ignored while Reach is 1. Above that, higher pulls more triangles in close to the camera.")]
	[SerializeField, Clamp(1f, 4f)] private float detailNearCamera = 2f;

	[Tooltip("Flattens waves past this distance so the horizon does not shimmer. 0 never flattens.")]
	[SerializeField, Suffix("m (0 = never)")] private float waveFadeDistance;

	[Tooltip("Fades foam out past this distance. 0 never fades.")]
	[SerializeField, Suffix("m (0 = never)")] private float foamFadeDistance;

	[Title("Status")]
	[ShowInInspector] public string Mesh => $"{Segments} x {Segments} tiles, {VertexCount:N0} verts, {CellSize:F1}m triangles";

	[ShowInInspector] public string WaveQuality => DescribeWaveQuality();

	[ShowInInspector] public string FloatingOrigin => World_Origin.Current == null ? "none in scene" : World_Origin.Current.Offset.ToString();

	private MeshRenderer meshRenderer;
	private MeshFilter meshFilter;

	[System.NonSerialized] private Mesh generatedMesh;
	[System.NonSerialized] private int builtSegments = -1;
	[System.NonSerialized] private float builtSize = -1f;
	[System.NonSerialized] private bool dirty = true;

	/// <summary>Tiles per side, derived from the two size fields rather than typed in directly.</summary>
	private int Segments => Mathf.Clamp(Mathf.RoundToInt(oceanSize / Mathf.Max(triangleSize, 0.1f)), 1, MaxSegments);

	private float CellSize => oceanSize / Segments;

	private int VertexCount => (Segments + 1) * (Segments + 1);

	private MeshRenderer Renderer
	{
		get
		{
			if (meshRenderer == null) TryGetComponent(out meshRenderer);
			return meshRenderer;
		}
	}

	private MeshFilter Filter
	{
		get
		{
			if (meshFilter == null) TryGetComponent(out meshFilter);
			return meshFilter;
		}
	}

	private void OnEnable()
	{
		AdoptExistingMesh();

#if UNITY_EDITOR
		RestorePlayModeEdits();
		UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif

		World_Origin.OriginShifted += OnOriginShifted;
		PushOriginOffset();

		dirty = true;
		Apply();
	}

	private void OnDisable()
	{
		World_Origin.OriginShifted -= OnOriginShifted;

#if UNITY_EDITOR
		UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
#endif
	}

	private void OnDestroy() => ReleaseMesh();

	// Deferred rather than applied straight away: OnValidate fires mid-inspector-edit, and
	// building a mesh from inside it is exactly the kind of thing Unity complains about.
	private void OnValidate() => dirty = true;

	private void Update()
	{
		if (dirty) Apply();
	}

	private void OnOriginShifted(Vector3 delta) => PushOriginOffset();

	[Button("Rebuild Ocean")]
	private void Rebuild()
	{
		builtSegments = -1;
		dirty = true;
		Apply();
	}

	private void Apply()
	{
		dirty = false;

		if (Segments != builtSegments || !Mathf.Approximately(oceanSize, builtSize))
			BuildMesh();

		PushToMaterial();
		ApplyBounds();
	}

	/// <summary>
	/// Rebuilds the grid. Vertices are shared between triangles - the pack used to split them so
	/// <c>RecalculateNormals</c> could produce flat shading, but the shader derives face normals
	/// from screen-space derivatives now, so the split would be ~6x the vertices for nothing.
	/// </summary>
	private void BuildMesh()
	{
		int segments = Segments;
		int lineVerts = segments + 1;
		float step = oceanSize / segments;
		float half = oceanSize * 0.5f;

		var vertices = new Vector3[lineVerts * lineVerts];
		var normals = new Vector3[vertices.Length];
		var triangles = new int[segments * segments * 6];

		int v = 0;

		for (int z = 0; z < lineVerts; z++)
		{
			for (int x = 0; x < lineVerts; x++)
			{
				vertices[v] = new Vector3(x * step - half, 0f, z * step - half);
				normals[v] = Vector3.up;
				v++;
			}
		}

		int t = 0;

		for (int z = 0; z < segments; z++)
		{
			for (int x = 0; x < segments; x++)
			{
				int corner = z * lineVerts + x;

				triangles[t++] = corner;
				triangles[t++] = corner + lineVerts;
				triangles[t++] = corner + 1;

				triangles[t++] = corner + lineVerts;
				triangles[t++] = corner + lineVerts + 1;
				triangles[t++] = corner + 1;
			}
		}

		if (generatedMesh == null)
		{
			generatedMesh = new Mesh
			{
				name = GeneratedMeshName,
				// Generated fresh on load, so it must never be written into the scene file.
				hideFlags = HideFlags.HideAndDontSave
			};
		}

		generatedMesh.Clear();
		generatedMesh.indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
		generatedMesh.SetVertices(vertices);
		generatedMesh.SetNormals(normals);
		generatedMesh.SetTriangles(triangles, 0);

		if (Filter != null) Filter.sharedMesh = generatedMesh;

		builtSegments = segments;
		builtSize = oceanSize;
	}

	private void PushToMaterial()
	{
		Material material = Renderer != null ? Renderer.sharedMaterial : null;
		if (material == null) return;

		material.SetFloat(WaveHeightId, waveHeight);
		material.SetFloat(WaveLengthId, Mathf.Max(waveLength, 0.01f));
		material.SetFloat(WaveSpeedId, waveSpeed);
		material.SetFloat(WaveDirectionId, waveDirection);
		material.SetFloat(WaveChaosId, waveChaos);
		material.SetFloat(WaveFadeDistanceId, Mathf.Max(waveFadeDistance, 0f));

		material.SetVector(FoamId, new Vector4(foamAmount, foamOnCrests, 0f, 0f));
		material.SetFloat(FoamFadeDistanceId, Mathf.Max(foamFadeDistance, 0f));

		// The shader samples the foam texture twice and multiplies the two together. Giving the
		// second layer a different scale and a contrary drift is what stops the result from
		// reading as one obviously repeating tile.
		float tiling = 1f / Mathf.Max(foamScale, 0.01f);
		material.SetVector(BumpTilingId, new Vector4(tiling, tiling, tiling * 0.6f, tiling * 0.6f));

		// Scroll is applied as _Time.x (seconds / 20) * direction, so cancel the 20 out to make
		// Foam Speed an honest metres per second.
		float drift = foamSpeed * 20f;
		material.SetVector(BumpDirectionId, new Vector4(drift, drift, drift, -drift * 1.67f));

		material.SetFloat(MeshExtentId, oceanSize * 0.5f);
		material.SetFloat(FollowSnapId, CellSize);
		material.SetFloat(HorizonReachId, Mathf.Max(horizonReach, 1f));
		material.SetFloat(HorizonFalloffId, Mathf.Max(detailNearCamera, 1f));

		material.SetFloat(InfiniteOceanId, followCamera ? 1f : 0f);

		if (followCamera)
			material.EnableKeyword("_INFINITE_OCEAN");
		else
			material.DisableKeyword("_INFINITE_OCEAN");

#if UNITY_EDITOR
		if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(material);
#endif
	}

	/// <summary>
	/// Widens the bounds to cover wherever the shader can push the sheet, so the culler stops
	/// deciding the ocean is off screen while it is in fact underneath the camera.
	/// </summary>
	private void ApplyBounds()
	{
		if (Renderer == null) return;

		// While following, the shader offsets the sheet by however far the camera has drifted from
		// this transform, which has no upper bound - so bounds sized to the mesh would still cull
		// it the moment you sailed far enough. An always-visible box is the honest answer: the
		// ocean is on screen in every frame anyway, and it casts no shadows for the size to bloat.
		float radius = followCamera ? 100000f : oceanSize * Mathf.Max(horizonReach, 1f) * 0.75f;
		float height = followCamera ? 100000f : waveHeight * 4f + 1f;

		Renderer.localBounds = new Bounds(Vector3.zero, new Vector3(radius * 2f, height, radius * 2f));
	}

	/// <summary>
	/// Hands the shader the scene-to-world offset so it can undo the rebase when it samples.
	/// Precision holds to roughly a thousand kilometres out, well past anything the world streams.
	/// </summary>
	private static void PushOriginOffset()
	{
		World_Coord offset = World_Origin.Current != null ? World_Origin.Current.Offset : World_Coord.Zero;

		Shader.SetGlobalVector(WorldOriginOffsetId, new Vector4((float)offset.X, (float)offset.Y, (float)offset.Z, 0f));
	}

	/// <summary>Reclaims a mesh left behind by a previous load, so reloads do not pile them up.</summary>
	private void AdoptExistingMesh()
	{
		if (generatedMesh != null || Filter == null) return;

		if (Filter.sharedMesh != null && Filter.sharedMesh.name == GeneratedMeshName)
			generatedMesh = Filter.sharedMesh;
	}

	private void ReleaseMesh()
	{
		if (generatedMesh == null) return;

		if (Application.isPlaying)
			Destroy(generatedMesh);
		else
			DestroyImmediate(generatedMesh);

		generatedMesh = null;
	}

#if UNITY_EDITOR
	/// <summary>
	/// Carries values tuned during play mode back into edit mode, so dialling the ocean in while
	/// sailing is not thrown away the moment you press stop.
	/// <para>
	/// The snapshot goes through SessionState rather than a field because this very component is
	/// destroyed on the way out - SessionState outlives both the teardown and the domain reload
	/// that follows, and clears itself when the editor closes.
	/// </para>
	/// </summary>
	private void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
	{
		// Fires while the play-mode objects are still alive, so there is still something to read.
		if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
			UnityEditor.SessionState.SetString(SnapshotKey, JsonUtility.ToJson(this));
	}

	private void RestorePlayModeEdits()
	{
		if (Application.isPlaying) return;

		string key = SnapshotKey;
		string json = UnityEditor.SessionState.GetString(key, string.Empty);

		if (string.IsNullOrEmpty(json)) return;

		UnityEditor.SessionState.EraseString(key);

		// JsonUtility rather than EditorJsonUtility: it writes back only this script's serialized
		// fields, leaving the m_Script and m_GameObject references of the edit-mode instance alone.
		JsonUtility.FromJsonOverwrite(json, this);

		UnityEditor.EditorUtility.SetDirty(this);

		// Flag the scene so the unsaved-changes dot appears - these values are only really kept
		// once the scene is saved, and silently losing them on the next reload would be worse
		// than not restoring them at all.
		if (gameObject.scene.IsValid())
			UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
	}

	/// <summary>Scene plus hierarchy path, which survives play mode without needing a stable id.</summary>
	private string SnapshotKey
	{
		get
		{
			string path = name;

			for (Transform parent = transform.parent; parent != null; parent = parent.parent)
				path = $"{parent.name}/{path}";

			return $"World_InfiniteOcean:{gameObject.scene.name}:{path}";
		}
	}
#endif

	/// <summary>Turns the wave-length-to-triangle-size ratio into something readable at a glance.</summary>
	private string DescribeWaveQuality()
	{
		float ratio = waveLength / Mathf.Max(CellSize, 0.001f);

		if (ratio < 2f) return $"Too coarse - {ratio:F1}x triangle size. Raise Wave Length or lower Triangle Size.";
		if (ratio < 3f) return $"Blocky - {ratio:F1}x triangle size.";
		if (ratio > 30f) return $"Very smooth - {ratio:F1}x triangle size. Lower Wave Length for more shape.";

		return $"Good - {ratio:F1}x triangle size.";
	}
}
