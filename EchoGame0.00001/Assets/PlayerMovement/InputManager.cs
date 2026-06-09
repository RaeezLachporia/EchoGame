using System;
using UnityEngine;

public class InputManager : MonoBehaviour
{
   private PlayerControls playerControls;
   AnimatorManager animatorManager;
   
   public Vector2 movementInput;
   public Vector2 cameraInput;

   public float cameraInputX;
   public float cameraInputY;
   
   public float moveAmount;
   public float verticalInput;
   public float horizontalInput;
   public bool isSprinting;

   private void Awake()
   {
      animatorManager = GetComponent<AnimatorManager>();
   }
   private void OnEnable()
   {
      if (playerControls == null)
      {
         playerControls = new PlayerControls();
         
         playerControls.PlayerMovement.Movement.performed += i => movementInput = i.ReadValue<Vector2>();
         playerControls.PlayerMovement.Camera.performed += i => cameraInput = i.ReadValue<Vector2>();
         // Mouse delta never sends a "stop" value, so reset to zero when the action cancels.
         // Without this the last delta keeps being applied every frame and the camera drifts.
         playerControls.PlayerMovement.Camera.canceled += i => cameraInput = Vector2.zero;
         playerControls.PlayerMovement.Sprint.performed += i => isSprinting = true;
         playerControls.PlayerMovement.Sprint.canceled  += i => isSprinting = false;
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

      cameraInputY = cameraInput.y;
      cameraInputX = cameraInput.x;

      const float deadzone = 0.15f;
      if (Mathf.Abs(horizontalInput) < deadzone) horizontalInput = 0f;
      if (Mathf.Abs(verticalInput) < deadzone) verticalInput = 0f;

      moveAmount = Mathf.Clamp01(Mathf.Abs(horizontalInput) + Mathf.Abs(verticalInput));
      float animMoveAmount = isSprinting ? moveAmount * 2f : moveAmount;
      animatorManager.updateAnimatorValues(0, animMoveAmount, isSprinting);
   }
}
