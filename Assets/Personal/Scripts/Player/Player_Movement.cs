using EditorAttributes;
using UnityEngine;

/// <summary>
/// Everything that moves the player: look, walk, sprint, crouch, jump and gravity, driven by a
/// CharacterController. Look and locomotion live together because look decides the facing that
/// locomotion moves along, and splitting them means one always lags a frame behind the other.
/// <para>
/// Reads <see cref="Game_Input"/> directly. <see cref="Player_Controller"/> gates it by toggling
/// this component, so a disabled movement script means the player is fully parked.
/// </para>
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class Player_Movement : MonoBehaviour
{
	[SerializeField, Required(fixMode: ReferenceFixMode.Children)] private Transform cameraPivot;

	[Title("Look")]
	[SerializeField, Suffix("deg/unit")] private float horizontalSensitivity = 0.1f;
	[SerializeField, Suffix("deg/unit")] private float verticalSensitivity = 0.1f;
	[SerializeField] private bool invertVertical;
	[SerializeField, MinMaxSlider(-90f, 90f)] private Vector2 pitchLimits = new(-85f, 85f);

	[Title("Speed")]
	[SerializeField, Suffix("m/s")] private float walkSpeed = 4f;
	[SerializeField, Suffix("m/s")] private float sprintSpeed = 7f;
	[SerializeField, Suffix("m/s")] private float crouchSpeed = 2f;
	[SerializeField, Suffix("m/s^2")] private float acceleration = 45f;
	[SerializeField, Suffix("m/s^2")] private float deceleration = 60f;
	[SerializeField, Clamp(0f, 1f)] private float airControl = 0.35f;

	[Title("Jump & Gravity")]
	[SerializeField, Suffix("m")] private float jumpHeight = 1.1f;
	[SerializeField, Suffix("m/s^2")] private float gravity = -22f;
	[HelpBox("Grace period after walking off a ledge where a jump still counts. Stops jumps being eaten on uneven ground.", MessageMode.None)]
	[SerializeField, Suffix("s")] private float coyoteTime = 0.12f;

	[Title("Crouch")]
	[SerializeField] private bool holdToCrouch = true;
	[SerializeField, Clamp(0.2f, 1f)] private float crouchHeightRatio = 0.55f;
	[SerializeField, Suffix("m/s")] private float crouchTransitionSpeed = 6f;
	[Tooltip("Fraction of the current capsule height the camera sits at, so crouching lowers the view automatically.")]
	[SerializeField, Clamp(0f, 1f)] private float eyeHeightRatio = 0.9f;

	[Title("Ground Check")]
	[HelpBox("The player's own capsule is filtered out, so leaving this as Everything is fine.", MessageMode.None)]
	[SerializeField] private LayerMask environmentLayers = ~0;
	[SerializeField, Suffix("m")] private float groundCheckDistance = 0.1f;

	private CharacterController controller;

	private float yaw;
	private float pitch;

	private Vector3 horizontalVelocity;
	private float verticalVelocity;
	private float coyoteTimer;
	private bool skipLookThisFrame;

	private float standingHeight;
	private Vector3 standingCenter;
	private float feetLocalY;

	private readonly Collider[] overlapResults = new Collider[8];

	[ShowInInspector] public bool IsGrounded { get; private set; }
	[ShowInInspector] public bool IsCrouching { get; private set; }
	[ShowInInspector] public float CurrentSpeed => horizontalVelocity.magnitude;

	/// <summary>Flat facing direction, safe to use as a heading for navigation and clue readouts.</summary>
	public Vector3 Facing => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

	public float Yaw => yaw;
	public float Pitch => pitch;

	private void Awake()
	{
		controller = GetComponent<CharacterController>();

		standingHeight = controller.height;
		standingCenter = controller.center;
		feetLocalY = standingCenter.y - standingHeight * 0.5f;

		yaw = transform.localEulerAngles.y;
	}

	private void OnEnable() => skipLookThisFrame = true;

	private void Update()
	{
		float deltaTime = Time.deltaTime;

		ApplyLook();

		IsGrounded = CheckGrounded();
		coyoteTimer = IsGrounded ? coyoteTime : Mathf.Max(0f, coyoteTimer - deltaTime);

		UpdateCrouch(deltaTime);
		UpdateHorizontalVelocity(deltaTime);
		UpdateVerticalVelocity(deltaTime);

		controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);
	}

	private void ApplyLook()
	{
		Vector2 lookInput = Game_Input.Look;

		// The cursor re-locks on the frame control comes back, and that warp can arrive as one
		// enormous delta. Swallow the first frame so the view never snaps on resume.
		if (skipLookThisFrame)
		{
			skipLookThisFrame = false;
			lookInput = Vector2.zero;
		}

		yaw += lookInput.x * horizontalSensitivity;

		float pitchDelta = lookInput.y * verticalSensitivity;
		pitch += invertVertical ? pitchDelta : -pitchDelta;
		pitch = Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);

		// Local rotation so the player still turns correctly while parented to something that moves, like a raft.
		transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

		if (cameraPivot != null)
			cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
	}

	private void UpdateCrouch(float deltaTime)
	{
		bool wantsCrouch = holdToCrouch
			? Game_Input.CrouchHeld
			: Game_Input.CrouchPressed ? !IsCrouching : IsCrouching;

		// Never stand up into geometry, otherwise the capsule pops through ceilings.
		IsCrouching = wantsCrouch || !HasHeadroom();

		float targetHeight = IsCrouching ? standingHeight * crouchHeightRatio : standingHeight;
		controller.height = Mathf.MoveTowards(controller.height, targetHeight, crouchTransitionSpeed * deltaTime);

		// Recentre around the feet so resizing never shoves the player up or into the floor.
		Vector3 center = standingCenter;
		center.y = feetLocalY + controller.height * 0.5f;
		controller.center = center;

		if (cameraPivot != null)
		{
			Vector3 eyePosition = cameraPivot.localPosition;
			eyePosition.y = feetLocalY + controller.height * eyeHeightRatio;
			cameraPivot.localPosition = eyePosition;
		}
	}

	private void UpdateHorizontalVelocity(float deltaTime)
	{
		Vector2 moveInput = Vector2.ClampMagnitude(Game_Input.Move, 1f);

		// Look only ever applies yaw to the body, so these stay flat.
		Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.y;

		float targetSpeed = IsCrouching ? crouchSpeed : Game_Input.SprintHeld ? sprintSpeed : walkSpeed;
		Vector3 targetVelocity = moveDirection * targetSpeed;

		float rate = moveInput.sqrMagnitude > 0f ? acceleration : deceleration;

		if (!IsGrounded)
			rate *= airControl;

		horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * deltaTime);
	}

	private void UpdateVerticalVelocity(float deltaTime)
	{
		if (IsGrounded && verticalVelocity < 0f)
			verticalVelocity = -2f; // Keeps the capsule pinned to slopes instead of bouncing down them.

		if (Game_Input.JumpPressed && coyoteTimer > 0f && !IsCrouching)
		{
			verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
			coyoteTimer = 0f;
		}

		verticalVelocity += gravity * deltaTime;
	}

	private bool CheckGrounded()
	{
		if (controller.isGrounded)
			return true;

		Vector3 sphereCenter = transform.position + controller.center
			+ Vector3.down * (controller.height * 0.5f - controller.radius + groundCheckDistance);

		return OverlapsEnvironment(Physics.OverlapSphereNonAlloc(
			sphereCenter, controller.radius * 0.95f, overlapResults, environmentLayers, QueryTriggerInteraction.Ignore));
	}

	private bool HasHeadroom()
	{
		float radius = controller.radius * 0.95f;
		Vector3 bottom = transform.position + new Vector3(standingCenter.x, feetLocalY + controller.radius, standingCenter.z);
		Vector3 top = transform.position + new Vector3(standingCenter.x, feetLocalY + standingHeight - controller.radius, standingCenter.z);

		return !OverlapsEnvironment(Physics.OverlapCapsuleNonAlloc(
			bottom, top, radius, overlapResults, environmentLayers, QueryTriggerInteraction.Ignore));
	}

	/// <summary>True if any of the last overlap's hits was something other than our own capsule.</summary>
	private bool OverlapsEnvironment(int hitCount)
	{
		for (int i = 0; i < hitCount; i++)
		{
			if (overlapResults[i] != controller)
				return true;
		}

		return false;
	}

	/// <summary>Zeroes momentum, for teleports, respawns and cutscene handoffs.</summary>
	public void ResetVelocity()
	{
		horizontalVelocity = Vector3.zero;
		verticalVelocity = 0f;
	}
}
