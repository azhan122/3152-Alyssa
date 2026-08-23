using EditorAttributes;
using UnityEngine;

/// <summary>
/// The player hub. Owns the cursor, gates control between gameplay and UI, acts as the anchor
/// <see cref="World_Origin"/> rebases the world around, and is the one place that knows the
/// player's true position in the infinite world.
/// <para>
/// The transform position on this object is scene-space and stays near zero forever. Anything
/// that cares where the player actually is - generation, clue targets, save data - should read
/// <see cref="WorldPosition"/> instead.
/// </para>
/// </summary>
[RequireComponent(typeof(CharacterController), typeof(Player_Movement))]
[DisallowMultipleComponent]
public class Player_Controller : MonoBehaviour
{
	[SerializeField] private bool lockCursorOnStart = true;

	[Title("World Origin")]
	[HelpBox("With this on, the player becomes the point the world rebases around. Turn it off only if something else, like a boat, should be the anchor.", MessageMode.None)]
	[SerializeField] private bool actAsWorldAnchor = true;

	[Title("Control")]
	[Tooltip("Turn this off to hand control to a UI screen. Movement stops, input switches to the UI map and the cursor comes back.")]
	[SerializeField, OnValueChanged(nameof(ApplyControlState))] private bool controlEnabled = true;

	private CharacterController controller;
	private Player_Movement movement;

	public Player_Movement Movement => movement;

	/// <summary>The player's true position in the infinite world, unaffected by origin rebases.</summary>
	public World_Coord WorldPosition => World_Origin.WorldOf(transform.position);

	[ShowInInspector] public string DebugWorldPosition => WorldPosition.ToString();
	[ShowInInspector] public bool DebugControlEnabled => controlEnabled;

	private void Awake()
	{
		controller = GetComponent<CharacterController>();
		movement = GetComponent<Player_Movement>();
	}

	private void OnEnable() => World_Origin.OriginShifted += OnOriginShifted;

	private void OnDisable() => World_Origin.OriginShifted -= OnOriginShifted;

	private void Start()
	{
		if (actAsWorldAnchor && World_Origin.Current != null)
			World_Origin.Current.SetAnchor(transform);

		if (lockCursorOnStart)
			SetCursorLocked(true);

		ApplyControlState();
	}

	/// <summary>
	/// Stand-in for a pause menu. Escape hands control back to the desktop, clicking takes it back.
	/// When a real menu exists this moves into it, and this method goes away.
	/// </summary>
	private void Update()
	{
		if (controlEnabled)
		{
			if (Game_Input.PausePressed)
				SetControlEnabled(false);
		}
		else if (Game_Input.PointerClicked)
		{
			SetControlEnabled(true);
		}
	}

	/// <summary>Enables or disables player control, switching input maps and the cursor to match.</summary>
	public void SetControlEnabled(bool isEnabled)
	{
		controlEnabled = isEnabled;
		ApplyControlState();
	}

	/// <summary>Moves the player to a true world position, letting the origin work out where that lands in the scene.</summary>
	public void TeleportTo(World_Coord destination)
	{
		Vector3 localPosition = World_Origin.LocalOf(destination);

		// The controller caches its own position, so it has to be off across the jump or it
		// sweeps through everything in between.
		controller.enabled = false;
		transform.position = localPosition;
		controller.enabled = true;

		movement.ResetVelocity();
	}

	private void ApplyControlState()
	{
		// Also fires from the inspector toggle, which can happen before Awake or outside play mode.
		if (!Application.isPlaying || movement == null)
			return;

		movement.enabled = controlEnabled;
		Game_Input.SetGameplayEnabled(controlEnabled);
		SetCursorLocked(controlEnabled);
	}

	/// <summary>
	/// The world just slid underneath us. Our transform came along with it, but the controller
	/// keeps its own copy of that position, so make it re-read the transform.
	/// </summary>
	private void OnOriginShifted(Vector3 delta)
	{
		if (controller == null || !controller.enabled)
			return;

		controller.enabled = false;
		controller.enabled = true;
	}

	private void SetCursorLocked(bool locked)
	{
		Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
		Cursor.visible = !locked;
	}

	[Button("Toggle Control")]
	private void ToggleControl() => SetControlEnabled(!controlEnabled);
}
