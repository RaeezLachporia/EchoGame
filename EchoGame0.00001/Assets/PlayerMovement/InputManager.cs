using System;
using UnityEngine;

public class InputManager : MonoBehaviour
{
   private PlayerControls playerControls;
   public Vector2 movementInput;
   public float verticalInput;
   public float horizontalInput;
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
   }
}
