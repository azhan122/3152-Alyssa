using System;
using System.Collections.Generic;
using EditorAttributes;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the player near scene origin by sliding the whole world back whenever they drift too
/// far, so 32-bit float positions never lose precision no matter how far the journey goes.
/// <para>
/// The player is never pinned to exactly zero every frame - that would mean teleporting every
/// rigidbody, particle and collider in the scene 60 times a second. Instead the world rebases
/// in one hop once the player passes <c>shiftThreshold</c>, which is invisible, costs a single
/// pass over the scene's root objects, and leaves physics and batching untouched in between.
/// </para>
/// <para>
/// Cost scales with the number of <em>root</em> objects, not total objects, so parent generated
/// content under a handful of chunk roots and a rebase stays in the microseconds.
/// </para>
/// <para>
/// Put this on its own root object. It skips its own root when shifting, so anything parented
/// under it, the player included, would silently get left behind.
/// </para>
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public class World_Origin : MonoBehaviour
{
	private static readonly List<GameObject> RootBuffer = new(128);

	public static World_Origin Current { get; private set; }

	/// <summary>
	/// Raised right after the world moves, with the delta every shifted object just took.
	/// Anything caching a world position, or holding physics state that a teleport would smear,
	/// should listen. Static, so unsubscribe in OnDisable or OnDestroy.
	/// </summary>
	public static event Action<Vector3> OriginShifted;

	[SerializeField, Required(fixMode: ReferenceFixMode.Scene)] private Transform anchor;

	[Title("Rebasing")]
	[HelpBox("Lower means tighter float precision, higher means fewer rebases. Anything from one chunk up to a few hundred metres is comfortable.", MessageMode.None)]
	[SerializeField, Suffix("m")] private float shiftThreshold = 256f;

	[Tooltip("Leave off for an ocean world so sea level stays put. Only useful for deep vertical worlds.")]
	[SerializeField] private bool shiftVertical;

	[Tooltip("Also rebase additively loaded scenes. Needed if island chunks stream in as scenes.")]
	[SerializeField] private bool includeAllLoadedScenes = true;

	private World_Coord offset = World_Coord.Zero;

	/// <summary>How far the scene has been slid so far. Add a local position to it to get a true world position.</summary>
	public World_Coord Offset => offset;

	[ShowInInspector] public int ShiftCount { get; private set; }
	[ShowInInspector] public string AnchorWorldPosition => anchor == null ? "no anchor" : ToWorld(anchor.position).ToString();
	[ShowInInspector] public string OriginOffset => offset.ToString();

	private void Awake()
	{
		if (Current != null && Current != this)
		{
			Debug.LogError($"A second {nameof(World_Origin)} exists, destroying it. The world can only have one origin.", this);
			Destroy(this);
			return;
		}

		Current = this;
	}

	private void OnDestroy()
	{
		if (Current == this)
			Current = null;
	}

	private void LateUpdate()
	{
		if (anchor == null)
			return;

		Vector3 drift = anchor.position;

		if (!shiftVertical)
			drift.y = 0f;

		if (drift.sqrMagnitude < shiftThreshold * shiftThreshold)
			return;

		ShiftBy(-drift);
	}

	/// <summary>Points the origin at a different object, for respawns or swapping the controlled character.</summary>
	public void SetAnchor(Transform newAnchor) => anchor = newAnchor;

	/// <summary>Turns a scene-space position into its true world position.</summary>
	public World_Coord ToWorld(Vector3 localPosition) => offset + localPosition;

	/// <summary>Turns a true world position back into a scene-space one. Only valid for nearby positions.</summary>
	public Vector3 ToLocal(World_Coord worldPosition) => worldPosition - offset;

	/// <summary>Null-safe lookup for code that may run before the origin exists.</summary>
	public static World_Coord WorldOf(Vector3 localPosition)
		=> Current != null ? Current.ToWorld(localPosition) : new World_Coord(localPosition);

	public static Vector3 LocalOf(World_Coord worldPosition)
		=> Current != null ? Current.ToLocal(worldPosition) : new Vector3((float)worldPosition.X, (float)worldPosition.Y, (float)worldPosition.Z);

	private void ShiftBy(Vector3 delta)
	{
		Transform selfRoot = transform.root;

		if (includeAllLoadedScenes)
		{
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);

				if (scene.isLoaded)
					ShiftScene(scene, delta, selfRoot);
			}
		}
		else
		{
			ShiftScene(gameObject.scene, delta, selfRoot);
		}

		// One sync for the whole rebase, so colliders and character controllers agree with the
		// transforms before anything queries them again.
		Physics.SyncTransforms();

		// The world moved by delta, so the point now at local zero sits delta further out.
		offset -= delta;
		ShiftCount++;

		OriginShifted?.Invoke(delta);
	}

	private static void ShiftScene(Scene scene, Vector3 delta, Transform selfRoot)
	{
		scene.GetRootGameObjects(RootBuffer);

		for (int i = 0; i < RootBuffer.Count; i++)
		{
			Transform root = RootBuffer[i].transform;

			if (root == selfRoot || RootBuffer[i].TryGetComponent(out World_OriginIgnore _))
				continue;

			root.position += delta;
		}
	}

	[Button("Rebase Now")]
	private void RebaseNow()
	{
		if (!Application.isPlaying || anchor == null)
			return;

		Vector3 drift = anchor.position;

		if (!shiftVertical)
			drift.y = 0f;

		ShiftBy(-drift);
	}
}
