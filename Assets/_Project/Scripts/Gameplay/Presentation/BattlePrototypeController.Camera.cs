using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>Tabletop camera: orbit, tilt, dolly zoom and panning.</summary>
    public sealed partial class BattlePrototypeController
    {        public void RequestCameraRotation(float degrees)
        {
            targetCameraRotation += degrees;
        }

        public void RequestCameraZoom(float scrollDelta)
        {
            targetCameraZoom = Mathf.Clamp(
                targetCameraZoom - scrollDelta * CameraZoomStep,
                MinimumCameraZoom,
                MaximumCameraZoom);
        }

        public void RequestCameraTilt(float degrees)
        {
            targetCameraPitch = Mathf.Clamp(
                targetCameraPitch + degrees,
                MinimumCameraPitch,
                MaximumCameraPitch);
        }

        public void RequestCameraPan(Vector3 worldDelta)
        {
            float xLimit = (GridWidth - 1) * CellSpacing * 0.5f;
            float zLimit = (GridHeight - 1) * CellSpacing * 0.5f;
            cameraPivot = new Vector3(
                Mathf.Clamp(cameraPivot.x + worldDelta.x, -xLimit, xLimit),
                DefaultCameraPivot.y,
                Mathf.Clamp(cameraPivot.z + worldDelta.z, -zLimit, zLimit));
            ApplyCameraTransform();
        }

        public void ResetCameraView()
        {
            cameraPivot = DefaultCameraPivot;
            targetCameraRotation = 0f;
            targetCameraPitch = DefaultCameraPitch;
            targetCameraZoom = DefaultCameraZoom;
            ApplyCameraTransform();
        }

        private void ConfigureCameraAndLight()
        {
            battleCamera = Camera.main;
            if (battleCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                battleCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            battleCamera.orthographic = false;
            battleCamera.fieldOfView = CameraFieldOfView;
            battleCamera.nearClipPlane = 0.3f;
            battleCamera.farClipPlane = 150f;
            cameraZoom = DefaultCameraZoom;
            targetCameraZoom = DefaultCameraZoom;
            cameraPitch = DefaultCameraPitch;
            targetCameraPitch = DefaultCameraPitch;
            cameraPivot = DefaultCameraPivot;
            battleCamera.backgroundColor = new Color(0.055f, 0.07f, 0.1f);
            ApplyCameraTransform();

            Light light = UnityEngine.Object.FindAnyObjectByType<Light>();
            if (light == null)
            {
                var lightObject = new GameObject("Directional Light");
                light = lightObject.AddComponent<Light>();
            }

            BattleEnvironment.ConfigureLighting(light, battleCamera);
        }

        private void UpdateCameraControls()
        {
            if (Input.GetKeyDown(KeyCode.Q)) RequestCameraRotation(-90f);
            if (Input.GetKeyDown(KeyCode.E)) RequestCameraRotation(90f);
            if (Input.GetKeyDown(KeyCode.PageDown)) RequestCameraTilt(-8f);
            if (Input.GetKeyDown(KeyCode.PageUp)) RequestCameraTilt(8f);
            if (Input.GetKeyDown(KeyCode.Home)) ResetCameraView();
            float scrollDelta = Input.mouseScrollDelta.y;
            if (!Mathf.Approximately(scrollDelta, 0f)) RequestCameraZoom(scrollDelta);

            if (Input.GetMouseButtonDown(1)) previousCameraPointer = Input.mousePosition;
            if (Input.GetMouseButton(1))
            {
                Vector2 currentPointer = Input.mousePosition;
                Vector2 pointerDelta = currentPointer - previousCameraPointer;
                targetCameraRotation += pointerDelta.x * 0.35f;
                RequestCameraTilt(pointerDelta.y * 0.22f);
                previousCameraPointer = currentPointer;
            }

            if (Input.GetMouseButtonDown(2)) previousCameraPointer = Input.mousePosition;
            if (Input.GetMouseButton(2) && battleCamera != null)
            {
                Vector2 currentPointer = Input.mousePosition;
                Vector2 pointerDelta = currentPointer - previousCameraPointer;
                float viewHeight = 2f * CameraDistance * Mathf.Tan(CameraFieldOfView * 0.5f * Mathf.Deg2Rad);
                float worldUnitsPerPixel = viewHeight / Mathf.Max(1f, Screen.height);
                Vector3 screenRight = Vector3.ProjectOnPlane(battleCamera.transform.right, Vector3.up).normalized;
                Vector3 screenUp = Vector3.ProjectOnPlane(battleCamera.transform.up, Vector3.up).normalized;
                RequestCameraPan(
                    (-screenRight * pointerDelta.x - screenUp * pointerDelta.y) * worldUnitsPerPixel);
                previousCameraPointer = currentPointer;
            }

            UpdateKeyboardCameraPan();

            float updatedRotation = Mathf.MoveTowardsAngle(
                cameraRotation,
                targetCameraRotation,
                240f * Time.unscaledDeltaTime);
            if (!Mathf.Approximately(updatedRotation, cameraRotation))
            {
                cameraRotation = updatedRotation;
                ApplyCameraTransform();
            }

            float updatedPitch = Mathf.MoveTowards(
                cameraPitch,
                targetCameraPitch,
                120f * Time.unscaledDeltaTime);
            if (!Mathf.Approximately(updatedPitch, cameraPitch))
            {
                cameraPitch = updatedPitch;
                ApplyCameraTransform();
            }

            float updatedZoom = Mathf.MoveTowards(
                cameraZoom,
                targetCameraZoom,
                CameraZoomSpeed * Time.unscaledDeltaTime);
            if (Mathf.Approximately(updatedZoom, cameraZoom)) return;
            cameraZoom = updatedZoom;
            ApplyCameraTransform();
        }

        private void UpdateKeyboardCameraPan()
        {
            if (battleCamera == null) return;
            float horizontal = 0f;
            float vertical = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontal -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontal += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vertical -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vertical += 1f;
            if (Mathf.Approximately(horizontal, 0f) && Mathf.Approximately(vertical, 0f)) return;

            Vector3 screenRight = Vector3.ProjectOnPlane(battleCamera.transform.right, Vector3.up).normalized;
            Vector3 screenUp = Vector3.ProjectOnPlane(battleCamera.transform.up, Vector3.up).normalized;
            Vector3 direction = screenRight * horizontal + screenUp * vertical;
            if (direction.sqrMagnitude > 1f) direction.Normalize();
            RequestCameraPan(direction * cameraZoom * CameraKeyboardPanSpeed * Time.unscaledDeltaTime);
        }

        private void ApplyCameraTransform()
        {
            if (battleCamera == null) return;
            Vector3 horizontalDirection = new Vector3(
                DefaultCameraOffset.x,
                0f,
                DefaultCameraOffset.z).normalized;
            horizontalDirection = Quaternion.AngleAxis(cameraRotation, Vector3.up) * horizontalDirection;
            float pitchRadians = cameraPitch * Mathf.Deg2Rad;
            Vector3 orbitOffset = horizontalDirection * (Mathf.Cos(pitchRadians) * CameraDistance) +
                                  Vector3.up * (Mathf.Sin(pitchRadians) * CameraDistance);
            battleCamera.transform.position = cameraPivot + orbitOffset;
            battleCamera.transform.LookAt(cameraPivot);
        }
    }
}
