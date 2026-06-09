using System;
using UnityEngine;

public class CameraManager : MonoBehaviour
{
    InputManager inputManager;
    public Transform targetTransform; // target camera will follow
    public Transform cameraPivot;     // object camera uses to pivot
    private Transform cameraTransform; // the actual camera child

    private Vector3 cameraFollowVelocity = Vector3.zero;
    public float cameraFollowSpeed = 0.2f;
    public float cameraLookSpeed = 2f;
    public float cameraPivotSpeed = 2f;
    public float lookAngle;
    public float pivotAngle;
    public float minimumPivotAngle = -35f;
    public float maximumPivotAngle = 35f;

    // Camera collision
    public float cameraCollisionRadius = 0.2f;   // how large the sphere is
    public float cameraCollisionOffset = 0.2f;   // minimum gap between camera and surface
    public LayerMask collisionLayers = -1;        // what to collide with (-1 = everything)
    private float defaultCameraZOffset;           // original z distance stored on start
    private float targetCameraZOffset;            // z offset we are moving toward

    private void Awake()
    {
        inputManager = FindObjectOfType<InputManager>();
        targetTransform = FindObjectOfType<PlayerManager>().transform;
        cameraTransform = Camera.main.transform;
        defaultCameraZOffset = cameraTransform.localPosition.z;
        targetCameraZOffset  = defaultCameraZOffset;
    }

    public void HandleAllCameraMovement()
    {
        followTarget();
        RotateCamera();
        HandleCameraCollision();
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

    private void HandleCameraCollision()
    {
        targetCameraZOffset = defaultCameraZOffset;

        RaycastHit hit;
        // Cast a sphere from the pivot outward toward the camera's desired position
        if (Physics.SphereCast(cameraPivot.position, cameraCollisionRadius, -cameraPivot.forward,
            out hit, Mathf.Abs(defaultCameraZOffset), collisionLayers))
        {
            // Pull camera in to just in front of whatever was hit
            float hitDistance = Vector3.Distance(cameraPivot.position, hit.point);
            targetCameraZOffset = -(hitDistance - cameraCollisionOffset);
        }

        // Never let the camera clip into the pivot itself
        if (Mathf.Abs(targetCameraZOffset) < cameraCollisionOffset)
            targetCameraZOffset = -cameraCollisionOffset;

        // Snap in fast when hitting, ease out slowly when clearing
        Vector3 localPos = cameraTransform.localPosition;
        float smoothSpeed = targetCameraZOffset < localPos.z ? 0.02f : 0.15f;
        localPos.z = Mathf.Lerp(localPos.z, targetCameraZOffset, smoothSpeed);
        cameraTransform.localPosition = localPos;
    }

}
