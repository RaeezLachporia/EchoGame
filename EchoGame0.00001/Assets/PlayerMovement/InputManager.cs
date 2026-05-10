using System;
using UnityEngine;

public class InputManager : MonoBehaviour
{
   private PlayerControls playerControls;
   AnimatorManager animatorManager;
   
   public Vector2 movementInput;
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
      moveAmount = Mathf.Clamp01(Mathf.Abs(horizontalInput) + Mathf.Abs(verticalInput));
      animatorManager.updateAnimatorValues(0, moveAmount);
   }
}
