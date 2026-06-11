using System;
using UnityEngine;

public class PlayerLocomotion : MonoBehaviour
{
   InputManager inputManager;
   AnimatorManager animatorManager;
   public Transform cameraObject;
   public Vector3 moveDirection;
   Rigidbody playerRigidBody;
   public float walkSpeed = 2f;
   public float runSpeed = 6f;
   public float sprintSpeed = 10f;
   public float rotationSpeed = 15;
   public float jumpForce = 6f;
   public float dodgeSpeed = 8f;
   public float dodgeDuration = 0.2f;
   private Vector3 rollDirection;
   private bool isGrounded;
   private float groundedTimer;
   private float jumpCooldown;
   private float dodgeTimer;
   private const float groundedGracePeriod = 0.15f;
   private const float jumpCooldownTime = 0.25f;

   private void Awake()
   {
      inputManager = GetComponent<InputManager>();
      animatorManager = GetComponent<AnimatorManager>();
      playerRigidBody = GetComponent<Rigidbody>();
      cameraObject = Camera.main.transform;
   }

   public void handleAllMovement()
   {
      checkGrounded();
      handleJump();
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

      if (!isGrounded) return;

      animatorManager.playJumpAnimation();
      playerRigidBody.linearVelocity = new Vector3(playerRigidBody.linearVelocity.x, jumpForce, playerRigidBody.linearVelocity.z);
      jumpCooldown = jumpCooldownTime;
      isGrounded = false;
   }

   public void tryInitiateDodge()
   {
      if (!inputManager.dodgeInput || dodgeTimer > 0f) return;
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
