using System;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    public float minSize;
    public float maxSize;
    public float sensitivity;
    public float size;

    public float speed = 1f;

    public GameObject target;

    public MovementController mc;

    private Camera cam;

    private void Start()
    {
        cam = gameObject.GetComponent<Camera>();
        size = cam.orthographicSize;
    }

    void Update()
    {
        var tempSpeed = speed;
        Vector3 targetVector3;
        if (Input.GetMouseButton(1))
        {
            targetVector3 = cam.ScreenToWorldPoint(Input.mousePosition);
            targetVector3.x = Mathf.Clamp(targetVector3.x, 0,  MovementController.field.Count);
            targetVector3.y = Mathf.Clamp(targetVector3.y, -MovementController.field.Count, 0);
            tempSpeed /= 2;
        }
        else
        {
            targetVector3 = target.transform.position;
        }
        
        targetVector3.z = -100;
        var curPos = cam.transform.position;
        curPos.z = -100;
        if ((curPos - targetVector3).magnitude > 0.1f)
            cam.transform.position = Vector3.Lerp(transform.position, targetVector3, tempSpeed * Time.deltaTime);
        
        size += (Input.GetAxis("Mouse ScrollWheel") * sensitivity) * -1;
        size = Mathf.Clamp(size, minSize, maxSize);
        if (Mathf.Abs(cam.orthographicSize - size) > 0.1f)
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, speed * Time.deltaTime);
        
        // Debug.Log(Vector2.Angle(mc.lastMovementVector, ((Vector2)cam.ScreenToWorldPoint(Input.mousePosition)) - mc.curPos));
    }
}
