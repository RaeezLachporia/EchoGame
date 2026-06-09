using System;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    InputManager inputManager;
    public Transform targetTransform; // target camera will follow
    public Transform cameraPivot; //object camera uses to pivot
    
    private Vector3 cameraFollowVelocity = Vector3.zero;
    public float cameraFollowSpeed = 0.2f;
    public float cameraLookSpeed = 2f;
    public float cameraPivotSpeed = 2f;
    public float lookAngle;
    public float pivotAngle;
    public float minimumPivotAngle = -35f; // how far the camera can look down
    public float maximumPivotAngle = 35f;  // how far the camera can look up
    private Vector3 offset;
    private void Awake()
    {
        inputManager = FindObjectOfType<InputManager>();
        targetTransform = FindObjectOfType<PlayerManager>().transform;
        
    }

    public void HandleAllCameraMovement()
    {
        followTarget();
        RotateCamera();
    }
    private void followTarget()
    {
        Vector3 targetPosition = Vector3.SmoothDamp
            (transform.position, targetTransform.position, ref cameraFollowVelocity, cameraFollowSpeed);
        transform.position = targetPosition;
    }
    
    private void RotateCamera()
    {
        lookAngle = lookAngle + (inputManager.cameraInputX * cameraLookSpeed);
        pivotAngle = pivotAngle - (inputManager.cameraInputY * cameraPivotSpeed);
        pivotAngle = Mathf.Clamp(pivotAngle, minimumPivotAngle, maximumPivotAngle); // stop the camera flipping over

        Vector3 rotation = Vector3.zero;
        rotation.y = lookAngle;
        Quaternion targetRotation = Quaternion.Euler(rotation);
        transform.rotation = targetRotation;
        
        rotation = Vector3.zero;
        rotation.x = pivotAngle;
        
        targetRotation = Quaternion.Euler(rotation);
        cameraPivot.localRotation = targetRotation;
    }
    
}
