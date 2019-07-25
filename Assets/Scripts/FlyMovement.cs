using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FlyMovement : MonoBehaviour
{
    #region Public Members

    public float Speed = 5.0f;

    public float RotationSpeed = 240.0f;

    public Joystick joystick;

    Camera _camera;

    #endregion

    void Start()
    {
        _camera = Camera.main;
    }

    void Update()
    {
        // Get Input for axis
        float h;
        float v;
        if (joystick)
        {
            h = joystick.Horizontal;
            v = joystick.Vertical;
        }
        else
        {
            h = Input.GetAxis("Horizontal");
            v = Input.GetAxis("Vertical");
        }

        // Calculate the forward vector
        Vector3 camForward_Dir = Vector3.Scale(_camera.transform.forward, new Vector3(1, 0, 1)).normalized;
        Vector3 move = v * camForward_Dir + h * _camera.transform.right;

        if (move.magnitude > 1f) move.Normalize();

        // Calculate the rotation for the player
        move = transform.InverseTransformDirection(move);

        // Get Euler angles
        float turnAmount = Mathf.Atan2(move.x, move.z);

        transform.Rotate(0, turnAmount * RotationSpeed * Time.deltaTime, 0);

        Vector3 moveDirection = transform.forward * move.magnitude;

        moveDirection *= Speed;

        transform.position += moveDirection * Time.deltaTime;
    }
}
