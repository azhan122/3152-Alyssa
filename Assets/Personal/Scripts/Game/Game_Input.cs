using EditorAttributes;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Game-wide input service over the generated <see cref="InputSystem_Actions"/> wrapper.
/// Bootstraps itself before the first scene loads, so anything can read <c>Game_Input.Move</c>
/// without wiring up a component. Action handles are cached once instead of walking the
/// wrapper's structs every frame.
/// </summary>
[DisallowMultipleComponent]
public class Game_Input : MonoBehaviour
{
	private static Game_Input instance;
	private static InputSystem_Actions actions;

	private static InputAction moveAction;
	private static InputAction lookAction;
	private static InputAction jumpAction;
	private static InputAction sprintAction;
	private static InputAction crouchAction;
	private static InputAction interactAction;
	private static InputAction attackAction;

	/// <summary>The generated wrapper, for anything the shorthand below does not cover.</summary>
	public static InputSystem_Actions Actions => actions;

	/// <summary>True while gameplay controls are live, false while the UI map has focus.</summary>
	public static bool GameplayEnabled { get; private set; }

	public static Vector2 Move => moveAction?.ReadValue<Vector2>() ?? Vector2.zero;

	/// <summary>Look delta for this frame. Mouse deltas are already frame-relative, so never scale this by deltaTime.</summary>
	public static Vector2 Look => lookAction?.ReadValue<Vector2>() ?? Vector2.zero;

	public static bool SprintHeld => sprintAction?.IsPressed() ?? false;
	public static bool CrouchHeld => crouchAction?.IsPressed() ?? false;
	public static bool AttackHeld => attackAction?.IsPressed() ?? false;

	/// <summary>
	/// Escape, read straight off the keyboard instead of through an action map, because it has to
	/// work while the gameplay map is disabled. Move this onto the UI map's Cancel action once
	/// there is a real pause menu to cancel out of.
	/// </summary>
	public static bool PausePressed => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

	/// <summary>Left mouse button, for clicking back into a released cursor.</summary>
	public static bool PointerClicked => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

	public static bool JumpPressed => jumpAction?.WasPressedThisFrame() ?? false;
	public static bool CrouchPressed => crouchAction?.WasPressedThisFrame() ?? false;
	public static bool InteractPressed => interactAction?.WasPressedThisFrame() ?? false;

	[ShowInInspector] public Vector2 DebugMove => Move;
	[ShowInInspector] public Vector2 DebugLook => Look;
	[ShowInInspector] public bool DebugSprint => SprintHeld;
	[ShowInInspector] public bool DebugCrouch => CrouchHeld;
	[ShowInInspector] public bool DebugGameplayEnabled => GameplayEnabled;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		// Statics survive a play session when domain reload is off, so always start from scratch.
		if (instance != null)
			Destroy(instance.gameObject);

		actions?.Dispose();
		actions = null;

		GameObject host = new(nameof(Game_Input));
		DontDestroyOnLoad(host);
		instance = host.AddComponent<Game_Input>();
	}

	private void Awake()
	{
		if (instance != null && instance != this)
		{
			Destroy(gameObject);
			return;
		}

		instance = this;
		actions = new InputSystem_Actions();

		moveAction = actions.Player.Move;
		lookAction = actions.Player.Look;
		jumpAction = actions.Player.Jump;
		sprintAction = actions.Player.Sprint;
		crouchAction = actions.Player.Crouch;
		interactAction = actions.Player.Interact;
		attackAction = actions.Player.Attack;

		SetGameplayEnabled(true);
	}

	private void OnDestroy()
	{
		if (instance != this)
			return;

		actions?.Dispose();
		actions = null;
		instance = null;
	}

	/// <summary>Swaps between the gameplay and UI action maps. Only one is ever live.</summary>
	public static void SetGameplayEnabled(bool isEnabled)
	{
		if (actions == null)
			return;

		GameplayEnabled = isEnabled;

		if (isEnabled)
		{
			actions.UI.Disable();
			actions.Player.Enable();
		}
		else
		{
			actions.Player.Disable();
			actions.UI.Enable();
		}
	}

	[Button("Toggle Gameplay Map")]
	private void ToggleGameplayMap() => SetGameplayEnabled(!GameplayEnabled);
}
