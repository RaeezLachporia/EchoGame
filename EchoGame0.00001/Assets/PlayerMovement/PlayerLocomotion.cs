using System;
using UnityEngine;

public class PlayerLocomotion : MonoBehaviour
{
   InputManager inputManager;
   AnimatorManager animatorManager;
   PlayerBasicCombat playerCombat;
   public Transform cameraObject;
   public Vector3 moveDirection;
   Rigidbody playerRigidBody;
   public float walkSpeed = 2f;
   public float runSpeed = 6f;
   public float sprintSpeed = 10f;
   public float rotationSpeed = 15;
   // Peak height (in metres) the collider's base reaches at the apex of a jump.
   // Launch velocity is derived from gravity — v = sqrt(2 * g * height) — so this
   // stays accurate if gravity changes. Set it just ABOVE the ledge you want to
   // clear: the jump animation is cosmetic and does not move the collider, so the
   // capsule only rises by this amount regardless of what the clip shows.
   public float jumpHeight = 1.5f;
   public float dodgeSpeed = 8f;
   public float dodgeDuration = 0.2f;
   // Must be descending faster than this (m/s) before the fall animation starts.
   public float fallTriggerSpeed = 1f;
   // Must drop at least this far below the take-off point before the fall plays,
   // so ordinary jumps (which land back at take-off height) stay on the jump clip.
   public float fallStartDrop = 0.3f;
   // Landing after dropping more than this far below take-off plays the hard
   // (crouch) landing; shallower drops blend straight back to locomotion.
   public float hardLandDropHeight = 2f;
   // Seconds the player is frozen after a hard landing while the crouch recovery
   // plays. Set this to roughly the hard-landing clip length.
   public float hardLandLockTime = 1.2f;
   private Vector3 rollDirection;
   public bool isGrounded { get; private set; }
   private float groundedTimer;
   private float jumpCooldown;
   private float dodgeTimer;
   private const float groundedGracePeriod = 0.15f;
   private const float jumpCooldownTime = 0.25f;
   private bool wasGrounded = true;
   private float takeoffY;
   private bool falling;
   private float hardLandTimer;

   private void Awake()
   {
      inputManager = GetComponent<InputManager>();
      animatorManager = GetComponent<AnimatorManager>();
      playerCombat = GetComponent<PlayerBasicCombat>();
      playerRigidBody = GetComponent<Rigidbody>();
      cameraObject = Camera.main.transform;
   }

   public void handleAllMovement()
   {
      checkGrounded();
      handleJump();
      handleAirborne();
      handleDodge();
      handleMovement();
      handleRotation();
   }

   private void checkGrounded()
   {
      if (jumpCooldown > 0f)
      {
         jumpCooldown -= Time.fixedDeltaTime;
         isGrounded = false;
         return;
      }

      if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, 0.65f))
         groundedTimer = groundedGracePeriod;
      else
         groundedTimer -= Time.fixedDeltaTime;

      isGrounded = groundedTimer > 0f;
   }

   private void handleJump()
   {
      if (!inputManager.jumpInput) return;
      inputManager.jumpInput = false;

      if (hardLandTimer > 0f) return;
      if (!isGrounded) return;

      animatorManager.playJumpAnimation();
      float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
      playerRigidBody.linearVelocity = new Vector3(playerRigidBody.linearVelocity.x, jumpVelocity, playerRigidBody.linearVelocity.z);
      jumpCooldown = jumpCooldownTime;
      isGrounded = false;
   }

   // Drives the fall/land animation from real physics: remember where we left the
   // ground, play the fall once we're descending below that point, and on landing
   // pick a hard (crouch) landing for a big drop or blend straight back otherwise.
   private void handleAirborne()
   {
      if (hardLandTimer > 0f)
         hardLandTimer -= Time.fixedDeltaTime;

      if (wasGrounded && !isGrounded)
      {
         takeoffY = transform.position.y;
         falling = false;
      }

      float descended = takeoffY - transform.position.y;

      if (!isGrounded && !falling
          && playerRigidBody.linearVelocity.y < -fallTriggerSpeed
          && descended > fallStartDrop)
      {
         falling = true;
         animatorManager.playFallAnimation();
      }

      if (!wasGrounded && isGrounded)
      {
         if (falling && descended > hardLandDropHeight)
         {
            animatorManager.playHardLandAnimation();
            hardLandTimer = hardLandLockTime;
         }
         falling = false;
      }

      animatorManager.setGrounded(isGrounded);
      wasGrounded = isGrounded;
   }

   public void tryInitiateDodge()
   {
      if (!inputManager.dodgeInput || dodgeTimer > 0f || hardLandTimer > 0f) return;
      inputManager.dodgeInput = false;

      rollDirection = inputManager.moveAmount > 0.1f
         ? new Vector3(moveDirection.normalized.x, 0f, moveDirection.normalized.z)
         : new Vector3(transform.forward.x, 0f, transform.forward.z);

      animatorManager.playRollAnimation();
      dodgeTimer = dodgeDuration;
      playerRigidBody.linearVelocity = new Vector3(rollDirection.x * dodgeSpeed, playerRigidBody.linearVelocity.y, rollDirection.z * dodgeSpeed);
   }

   private void handleDodge()
   {
      if (dodgeTimer <= 0f) return;
      dodgeTimer -= Time.fixedDeltaTime;
      playerRigidBody.linearVelocity = new Vector3(rollDirection.x * dodgeSpeed, playerRigidBody.linearVelocity.y, rollDirection.z * dodgeSpeed);
   }

   private void handleMovement()
   {
      if (dodgeTimer > 0f) return;

      if (hardLandTimer > 0f)
      {
         // Frozen during hard-landing recovery — kill horizontal drift, keep gravity.
         playerRigidBody.linearVelocity = new Vector3(0f, playerRigidBody.linearVelocity.y, 0f);
         return;
      }

      if (playerCombat.isAttacking)
      {
         playerRigidBody.linearVelocity = new Vector3(0f, playerRigidBody.linearVelocity.y, 0f);
         return;
      }

      float speed = inputManager.isSprinting ? sprintSpeed
                  : inputManager.moveAmount <= 0.5f ? walkSpeed
                  : runSpeed;

      moveDirection = cameraObject.forward * inputManager.verticalInput;
      moveDirection += cameraObject.right * inputManager.horizontalInput;
      moveDirection.Normalize();
      moveDirection.y = 0;
      moveDirection *= speed;

      if (isGrounded)
      {
         playerRigidBody.linearVelocity = moveDirection;
      }
      else
      {
         // Keep gravity/jump Y velocity, allow air steering
         playerRigidBody.linearVelocity = new Vector3(moveDirection.x, playerRigidBody.linearVelocity.y, moveDirection.z);
      }
   }

   private void handleRotation()
   {
      if (playerCombat.isAttacking || hardLandTimer > 0f) return;

      Vector3 targetDirection = cameraObject.forward * inputManager.verticalInput;
      targetDirection += cameraObject.right * inputManager.horizontalInput;
      targetDirection.Normalize();
      targetDirection.y = 0;

      if (targetDirection == Vector3.zero)
         targetDirection = transform.forward;

      Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
      transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
   }
}
