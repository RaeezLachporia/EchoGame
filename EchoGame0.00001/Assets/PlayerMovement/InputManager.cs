using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
   private PlayerControls playerControls;
   AnimatorManager animatorManager;
   PlayerLocomotion playerLocomotion;
   
   public Vector2 movementInput;
   public Vector2 cameraInput;

   public float cameraInputX;
   public float cameraInputY;
   // Mouse delta and stick position are different units, so the camera has to
   // scale them differently. Tracks which device last drove the Camera action.
   public bool cameraInputIsMouse;
   
   public float moveAmount;
   public float verticalInput;
   public float horizontalInput;
   public bool isSprinting;
   public bool jumpInput;
   public bool dodgeInput;
   public bool attackInput;

   private void Awake()
   {
      animatorManager = GetComponent<AnimatorManager>();
      playerLocomotion = GetComponent<PlayerLocomotion>();
   }
   private void OnEnable()
   {
      if (playerControls == null)
      {
         playerControls = new PlayerControls();

         playerControls.PlayerMovement.Movement.performed += i => movementInput = i.ReadValue<Vector2>();
         playerControls.PlayerMovement.Camera.performed += i =>
         {
            cameraInput = i.ReadValue<Vector2>();
            cameraInputIsMouse = i.control.device is Mouse;
         };
         // Mouse delta never sends a "stop" value, so reset to zero when the action cancels.
         // Without this the last delta keeps being applied every frame and the camera drifts.
         playerControls.PlayerMovement.Camera.canceled += i => cameraInput = Vector2.zero;
         playerControls.PlayerMovement.Sprint.performed += i => isSprinting = true;
         playerControls.PlayerMovement.Sprint.canceled  += i => isSprinting = false;
         // Only accept jump/dodge presses when grounded — otherwise a press in
         // the air would buffer until the next FixedUpdate and fire on landing.
         playerControls.PlayerMovement.Jump.performed   += i => { if (playerLocomotion.isGrounded) jumpInput = true; };
         playerControls.PlayerMovement.Dodge.performed  += i => { if (playerLocomotion.isGrounded) dodgeInput = true; };
         playerControls.PlayerMovement.Attack.performed  += i => attackInput = true;
      }

      playerControls.Enable();
   }

   private void OnDisable()
   {
      playerControls.Disable();
   }
   
   public void handleAllInput()
   {
      handleMovementInput();
   }
   
   private void handleMovementInput()
   {
      verticalInput = movementInput.y;
      horizontalInput = movementInput.x;

      // The right stick is bound as a 2DVector of separate up/down/left/right
      // axes, which skips the deadzone the stick control would normally apply,
      // so a resting stick drifts the camera. Apply one here instead. A diagonal
      // reads (1,1), so clamp the magnitude too or diagonals turn 1.4x faster.
      Vector2 lookInput = cameraInput;
      if (!cameraInputIsMouse)
      {
         const float lookDeadzone = 0.2f;
         float magnitude = lookInput.magnitude;
         lookInput = magnitude < lookDeadzone
            ? Vector2.zero
            : lookInput / magnitude * Mathf.Min((magnitude - lookDeadzone) / (1f - lookDeadzone), 1f);
      }

      cameraInputY = lookInput.y;
      cameraInputX = lookInput.x;

      const float deadzone = 0.15f;
      if (Mathf.Abs(horizontalInput) < deadzone) horizontalInput = 0f;
      if (Mathf.Abs(verticalInput) < deadzone) verticalInput = 0f;

      moveAmount = Mathf.Clamp01(Mathf.Abs(horizontalInput) + Mathf.Abs(verticalInput));
      float animMoveAmount = isSprinting ? moveAmount * 2f : moveAmount;
      animatorManager.updateAnimatorValues(0, animMoveAmount, isSprinting);
   }
}
