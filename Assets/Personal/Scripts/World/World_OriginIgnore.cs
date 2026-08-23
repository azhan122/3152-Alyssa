using EditorAttributes;
using UnityEngine;

/// <summary>
/// Marks a root object that <see cref="World_Origin"/> must leave alone when it rebases.
/// <para>
/// Put this on anything that lives in screen or camera space rather than world space -
/// UI canvases, audio listeners parked at the origin, debug rigs. Everything else should
/// move with the world, including the player.
/// </para>
/// </summary>
[DisallowMultipleComponent]
public class World_OriginIgnore : MonoBehaviour
{
	[HelpBox("Only checked on root objects. On a child this does nothing, because the whole root moves as one.", MessageMode.Warning)]
	[SerializeField, ReadOnly] private bool isRootObject;

	private void OnValidate() => isRootObject = transform.parent == null;
}
