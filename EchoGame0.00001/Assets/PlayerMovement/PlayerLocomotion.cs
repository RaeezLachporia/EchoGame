using System;
using UnityEngine;

public class PlayerLocomotion : MonoBehaviour
{
   InputManager inputManager;
   public Transform cameraObject;
   public Vector3 moveDirection;
   Rigidbody playerRigidBody;
   public float walkSpeed = 2f;
   public float runSpeed = 6f;
   public float sprintSpeed = 10f;
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
      float speed = inputManager.isSprinting ? sprintSpeed
                  : inputManager.moveAmount <= 0.5f ? walkSpeed
                  : runSpeed;

      moveDirection = cameraObject.forward * inputManager.verticalInput;
      moveDirection += cameraObject.right * inputManager.horizontalInput;
      moveDirection.Normalize();
      moveDirection.y = 0;
      moveDirection *= speed;

      playerRigidBody.linearVelocity = moveDirection;
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
