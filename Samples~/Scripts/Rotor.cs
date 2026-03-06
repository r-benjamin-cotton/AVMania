using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rotor : MonoBehaviour
{
    [SerializeField]
    private Vector3 speed = Vector3.zero;

    private void Update()
    {
        transform.localRotation *= Quaternion.Euler(Time.deltaTime * speed);
    }
}
