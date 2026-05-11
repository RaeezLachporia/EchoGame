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
   
   private float moveAmount;
   public float verticalInput;
   public float horizontalInput;

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
         playerControls.PlayerMovement.Camera.performed+= i => cameraInput = i.ReadValue<Vector2>();
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
      
      moveAmount = Mathf.Clamp01(Mathf.Abs(horizontalInput) + Mathf.Abs(verticalInput));
      animatorManager.updateAnimatorValues(0, moveAmount);
   }
}
