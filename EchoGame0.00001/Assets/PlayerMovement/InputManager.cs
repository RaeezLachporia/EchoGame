using System;
using UnityEngine;

public class InputManager : MonoBehaviour
{
   private PlayerControls playerControls;
   public Vector2 movementInput;

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
}
