using System;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    InputManager inputManager;
    CameraManager cameraManager;
    PlayerLocomotion playerLocomotion;
    private void Awake()
    {
        inputManager = GetComponent<InputManager>();
        cameraManager = FindObjectOfType<CameraManager>();
        playerLocomotion = GetComponent<PlayerLocomotion>();
        
    }

    private void Update()
    {
        inputManager.handleAllInput();
        playerLocomotion.tryInitiateDodge();
    }

    private void FixedUpdate()
    {
        playerLocomotion.handleAllMovement();
    }

    private void LateUpdate()
    {
        cameraManager.HandleAllCameraMovement();
    }
}

