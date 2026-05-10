using System;
using UnityEngine;

public class PlayerLocomotion : MonoBehaviour
{
   InputManager inputManager;
   public Transform cameraObject;
   public Vector3 moveDirection;
   Rigidbody playerRigidBody;
   public float movementSpeed = 7;
   public float rotationSpeed = 15;
   private void Awake()
   {
      inputManager = GetComponent<InputManager>();
      playerRigidBody = GetComponent<Rigidbody>();
      cameraObject = Camera.main.transform;
   }

   public void handleAllMovement()
   {
      handleMovement();
      handleRotation();
   }
   private void handleMovement()
   {
      moveDirection = cameraObject.forward * inputManager.verticalInput;
      moveDirection = moveDirection +cameraObject.right * inputManager.horizontalInput;
      moveDirection.Normalize();
      moveDirection.y = 0;
      moveDirection = moveDirection* movementSpeed;
      
      Vector3 movementVelocity = moveDirection;
      playerRigidBody.linearVelocity = movementVelocity;
   }

   private void handleRotation()
   {
      Vector3 targetDirection = Vector3.zero;
      targetDirection.x = inputManager.horizontalInput;
      targetDirection.z = inputManager.verticalInput;
      targetDirection.Normalize();
      targetDirection.y = 0;

      if (targetDirection== Vector3.zero)
      {
         targetDirection = transform.forward;
      }
      Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
      transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
   }
}
