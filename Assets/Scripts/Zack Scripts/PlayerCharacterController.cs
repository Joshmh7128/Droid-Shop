using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Droid.Player
{
	[RequireComponent(typeof(CharacterController))]
	public class PlayerCharacterController : MonoBehaviour
	{
		#region Variables n' Stuff

		#region References

		[Header("References"), Tooltip("Main camera used by the player."), SerializeField]
		Camera playerCamera;

		// Character controller used to move the player.
		CharacterController controller;

		#endregion

		#region General

		[Header("General"), Tooltip("Physics layers used to check grounded state."), SerializeField]
		LayerMask groundCheckLayers = -1;

		[Tooltip("Distance used to check grounded state."), SerializeField]
		float groundCheckDistance = 0.05f;

		// Normal vector of the ground the player is standing on.
		Vector3 groundNormal;

		#endregion

		#region Movement

		[Header("Movement"), Tooltip("Maximum movement speed on the ground."), SerializeField]
		float maxSpeedOnGround = 10f;

		[Tooltip("Sharpness for grounded movement.\nHigh value increases acceleration speed."), SerializeField]
		float movementSharpnessOnGround = 15;

		[Tooltip("Percentage of maximum speed used when crouched."), SerializeField, Range(0, 1)]
		float maxSpeedCrouchedRatio = 0.5f;

		[Tooltip("Maximum movement speedwhen airborne."), SerializeField]
		float maxSpeedInAir = 10f;

		[Tooltip("Acceleration speed when in the air."), SerializeField]
		float accelerationSpeedInAir = 25f;

		// Current velocity of the player character.
		Vector3 characterVelocity;

		// Speed of the character the last time they touched the ground.
		Vector3 latestImpactSpeed;

		#endregion

		#region Lookin' Around

		[Header("Looking Around"), Tooltip("Rotation speed for moving the camera."), SerializeField]
		float rotationSpeed = 1f;

		// Angle to set the camera's vertical angle to.
		float cameraVerticalAngle = 0f;

		#endregion

		#region Jump

		[Header("Jump"), Tooltip("Maximum height of the player's jump."), SerializeField]
		float maxJumpHeight = 4;

		[Tooltip("Minimum height of the player's jump."), SerializeField]
		float minJumpHeight = 1;

		[Tooltip("Time it takes to reach the height of the player's jump."), SerializeField]
		float timeToJumpApex = 1f;

		// Acceleration of the player downwards.
		private float gravity;
		// Initial velocity needed for the character to reach the maximum jump height.
		private float maxJumpVelocity;
		// Initial velocity needed for the character to reach the minimum jump height.
		private float minJumpVelocity;

		// Is the player currently grounded?
		bool isGrounded;
		// Has the player jumped this frame?
		bool hasJumpedThisFrame;

		// Time in which the player last jumped.
		float lastTimeJumped = 0f;

		// Time spent not checking for ground, so as to not snap the player back.
		const float jumpGroundingPreventionTime = 0.2f;
		// How far should the raycast travel to find ground in the air?
		const float groundCheckDistanceInAir = 0.07f;

		#endregion

		#region Stance

		[Header("Stance"), Tooltip("Ratio (0-1) of the character height where the camera will rest at."), SerializeField]
		float cameraHeightRatio = 0.9f;

		[Tooltip("Height of the character when standing."), SerializeField]
		float capsuleHeightStanding = 1.2f;

		[Tooltip("Height of the character when crouching."), SerializeField]
		float capsuleHeightCrouching = 0.5f;

		[Tooltip("Speed of crouching transitions."), SerializeField]
		float crouchingSharpness = 10f;

		[Tooltip("Event that occurs when the player crouches."), SerializeField]
		UnityAction<bool> onStanceChanged;

		// Is the player currently crouching?
		bool isCrouching;

		// Current intended height of the character.
		float targetCharacterHeight;

		#endregion

		#endregion

		#region Methods

		#region Input Methods

		Vector2 i_Move;
		Vector2 i_Look;
		bool i_Jump;
		bool i_Sprint;

		public void OnMove(InputAction.CallbackContext context)
		{
			i_Move = context.ReadValue<Vector2>();
		}

		public void OnLook(InputAction.CallbackContext context)
		{
			i_Look = context.ReadValue<Vector2>();
		}

		public void OnJump(InputAction.CallbackContext context)
		{
			i_Jump = context.ReadValue<float>() == 1;
			if (context.started)
			{
				Jump();
			}
		}

		public void OnCrouch(InputAction.CallbackContext context)
		{
			if (context.started && isGrounded)
			{
				SetCrouchingState(!isCrouching, false);
			}
		}

		public void OnInteract(InputAction.CallbackContext context)
		{
			if (context.started)
			{
				Interact();
			}
		}

		#endregion

		#region Initiation Methods

		private void Start()
		{
			// Fetch us some components.
			controller = GetComponent<CharacterController>();

			// Honestly can't remember why this is here.
			controller.enableOverlapRecovery = true;

			// Do some math (PHYSICS YEAAH) to get us some necessary variables.
			gravity = -(2 * maxJumpHeight) / Mathf.Pow(timeToJumpApex, 2);
			maxJumpVelocity = Mathf.Abs(gravity) * timeToJumpApex;
			minJumpVelocity = Mathf.Sqrt(2 * Mathf.Abs(gravity) * minJumpHeight);

			// Make sure ground movement ain't slower. Unless you're into that.
			maxSpeedInAir = Mathf.Min(maxSpeedInAir, maxSpeedOnGround);

			// Force the crouch state to false when starting.
			SetCrouchingState(false, true);
			UpdateCharacterHeight(true);

			// Hide and lock our mouse. Standard stuff.
			Cursor.visible = false; Cursor.lockState = CursorLockMode.Locked;
		}

		#endregion

		private void Update()
		{
			// Frame's just started, of course you haven't jumped yet.
			hasJumpedThisFrame = false;

			// Yesterday's "is" is today's "was".
			bool wasGrounded = isGrounded;
			// Check to see if we're solidly planted on the earth.
			GroundCheck();

			// Stuff that happens upon landing.
			if (isGrounded && !wasGrounded)
			{
				// Land SFX
			}

			// 
			UpdateCharacterHeight(false);

			// 
			HandleCharacterMovement();
		}

		private void GroundCheck()
		{
			// Make sure ground check distance in air is small, to prevent sudden snapping to ground.
			float chosenGroundCheckDistance =
				isGrounded ? (controller.skinWidth + groundCheckDistance) : groundCheckDistanceInAir;

			// Reset values before the ground check.
			isGrounded = false;
			controller.stepOffset = 0;
			groundNormal = Vector3.up;

			// Don't detect ground right after jumping.
			if (Time.time >= lastTimeJumped + jumpGroundingPreventionTime)
			{
				// If we're grounded, collect about the ground normal with a downward capsule cast representing our character capsule.
				if (Physics.CapsuleCast(GetCapsuleBottomHemisphere(), GetCapsuleTopHemisphere(controller.height),
					controller.radius, Vector3.down, out RaycastHit hit, chosenGroundCheckDistance, groundCheckLayers,
					QueryTriggerInteraction.Ignore))
				{
					// Store the normal for the surface found.
					groundNormal = hit.normal;

					// Only consider this a valid ground hit if the ground normal goes in the same
					// direction as the character up and if the slope angle is lower than the
					// character controller's limit.
					if (Vector3.Dot(hit.normal, transform.up) > 0f &&
						IsNormalUnderSlopeLimit(groundNormal))
					{
						isGrounded = true;
						controller.stepOffset = 0.3f;

						// Handle snapping to the ground.
						if (hit.distance > controller.skinWidth)
							controller.Move(Vector3.down * hit.distance);
					}
				}
			}
		}

		private void HandleCharacterMovement()
		{
			// Horizontal character rotation.
			{
				// Rotate the transform with the input speed around its local Y axis.
				transform.Rotate(new Vector3(0f, i_Look.x * rotationSpeed, 0f), Space.Self);
			}

			// Vertical camera rotation.
			{
				// Add vertical inputs to the camera's vertical angle.
				cameraVerticalAngle -= i_Look.y * rotationSpeed;

				// Limit the camera's vertical angle to min/max.
				cameraVerticalAngle = Mathf.Clamp(cameraVerticalAngle, -89f, 89f);

				// Apply the vertical angle as a local rotation to the camera transform along its right axis (makes it pivot up and down).
				playerCamera.transform.localEulerAngles = new Vector3(cameraVerticalAngle, 0, 0);
			}

			// Character movement handling.
			{
				bool isSprinting = i_Sprint;
				if (isSprinting)
				{
					isSprinting = SetCrouchingState(false, false);
				}

				// Do some shit with this if you put in a sprint speed. |||TODO|||
				float speedModifier = 1f;

				// Reduce speed if crouching by crouch speed ratio.
				if (isCrouching)
					speedModifier *= maxSpeedCrouchedRatio;

				// Converts move input to a worldspace vector based on our character's transform orientation.
				Vector3 worldspaceMoveInput = transform.TransformVector(new Vector3(i_Move.x, 0, i_Move.y));

				// Handle grounded movement.
				if (isGrounded)
				{
					// Calculate the desired velocity from inputs, max speed, and current slope.
					Vector3 targetVelocity = worldspaceMoveInput * maxSpeedOnGround * speedModifier;

					targetVelocity = GetDirectionReorientedOnSlope(targetVelocity.normalized, groundNormal) * targetVelocity.magnitude;

					// Smoothly interpolate between our current velocity and the target velocity based on acceleration speed.
					characterVelocity = Vector3.Lerp(characterVelocity, targetVelocity, movementSharpnessOnGround * Time.deltaTime);

					// Jumping
					// If jumping event doesn't work, just put the function here.

					// Play footsteps sound.
					// |||TODO|||
				}
				// Handle air movement.
				else
				{
					// Add air acceleration.
					characterVelocity += worldspaceMoveInput * accelerationSpeedInAir * Time.deltaTime;

					// Limit horizontal air speed to a maximum.
					float verticalVelocity = characterVelocity.y;
					Vector3 horizontalVelocity = Vector3.ProjectOnPlane(characterVelocity, Vector3.up);
					horizontalVelocity = Vector3.ClampMagnitude(horizontalVelocity, maxSpeedInAir * speedModifier);

					// Limit vertical air speed to a minimum. Limit to max if the player lets go of jump.
					verticalVelocity = Mathf.Clamp(verticalVelocity, -3 * maxJumpVelocity, !i_Jump ? minJumpVelocity : maxJumpVelocity);

					// Combine horizontal and vertical velocity.
					characterVelocity = horizontalVelocity + (Vector3.up * verticalVelocity);

					// Apply the gravity to the velocity.
					characterVelocity -= Vector3.down * gravity * Time.deltaTime;
				}
			}

			// Apply the final calculated velocity value as a character movement.
			Vector3 capsuleBottomBeforeMove = GetCapsuleBottomHemisphere();
			Vector3 capsuleTopBeforeMove = GetCapsuleTopHemisphere(controller.height);
			controller.Move(characterVelocity * Time.deltaTime);

			// Detect obstructions to adjust velocity accordingly.
			latestImpactSpeed = Vector3.zero;
			if (Physics.CapsuleCast(capsuleBottomBeforeMove, capsuleTopBeforeMove, controller.radius,
				characterVelocity.normalized, out RaycastHit hit, characterVelocity.magnitude * Time.deltaTime, -1,
				QueryTriggerInteraction.Ignore))
			{
				// We remember the last impact speed because the fall damage logic might need it.
				latestImpactSpeed = characterVelocity;

				characterVelocity = Vector3.ProjectOnPlane(characterVelocity, hit.normal);
			}
		}

		private void Jump()
		{
			if (isGrounded)
			{
				// Force crouch state to false.
				if (SetCrouchingState(false, false))
				{
					// Start by cancelling out the vertical component of our velocity.
					characterVelocity = new Vector3(characterVelocity.x, 0f, characterVelocity.z);

					// Then, add the jumpSpeed value upwards.
					characterVelocity += Vector3.up * maxJumpVelocity;

					// Play sound.
					// |||TODO|||

					// Remember the last time we jumped because we need to prevent snapping to ground for a short time.
					lastTimeJumped = Time.time;
					hasJumpedThisFrame = true;

					// Force grounding to false.
					isGrounded = false;
					groundNormal = Vector3.up;
				}
			}
		}

		bool SetCrouchingState(bool crouched, bool ignoreObstructions)
		{
			// Set appropriate heights.
			if (crouched)
				targetCharacterHeight = capsuleHeightCrouching;
			else
			{
				// Detect obstructions.
				if (!ignoreObstructions)
				{
					Collider[] standingOverlaps = Physics.OverlapCapsule(
						GetCapsuleBottomHemisphere(),
						GetCapsuleTopHemisphere(capsuleHeightStanding),
						controller.radius, -1, QueryTriggerInteraction.Ignore);
					foreach (Collider c in standingOverlaps)
					{
						if (c != controller)
							return false;
					}
				}

				targetCharacterHeight = capsuleHeightStanding;
			}

			if (onStanceChanged != null)
				onStanceChanged.Invoke(crouched);

			isCrouching = crouched;
			return true;
		}

		private void UpdateCharacterHeight(bool force)
		{
			// Update height instantly.
			if (force)
			{
				controller.height = targetCharacterHeight;
				controller.center = Vector3.up * controller.height * 0.5f;
				playerCamera.transform.localPosition = Vector3.up * targetCharacterHeight * cameraHeightRatio;
			}
			// Update smooth height.
			else if (controller.height != targetCharacterHeight)
			{
				// Resize the capsule and adjust camera position.
				controller.height = Mathf.Lerp(controller.height, targetCharacterHeight, crouchingSharpness * Time.deltaTime);
				controller.center = Vector3.up * controller.height * 0.5f;
				playerCamera.transform.localPosition = Vector3.Lerp(playerCamera.transform.localPosition,
					Vector3.up * targetCharacterHeight * cameraHeightRatio, crouchingSharpness * Time.deltaTime);
			}
		}

		private void Interact()
		{
			// Do some shit here.
		}

		#region Miscellaneous Methods

		bool IsNormalUnderSlopeLimit(Vector3 normal)
		{
			return Vector3.Angle(transform.up, normal) <= controller.slopeLimit;
		}

		Vector3 GetCapsuleBottomHemisphere()
		{
			return transform.position + (transform.up * controller.radius);
		}

		Vector3 GetCapsuleTopHemisphere(float atHeight)
		{
			return transform.position + (transform.up * (atHeight - controller.radius));
		}

		Vector3 GetDirectionReorientedOnSlope(Vector3 direction, Vector3 slopeNormal)
		{
			Vector3 directionRight = Vector3.Cross(direction, transform.up);
			return Vector3.Cross(slopeNormal, directionRight).normalized;
		}

		#endregion

		#endregion
	}
}