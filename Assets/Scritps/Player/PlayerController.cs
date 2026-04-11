using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private SO_Movement _movement;
    [SerializeField] private Rigidbody _rigidbody;
    [SerializeField] private Transform _cameraTransform;
    [SerializeField] private Transform _orientation;
    [SerializeField] private Transform _playerBody;
    private Vector3 _moveDir = Vector3.zero;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.maxLinearVelocity = _movement.moveSpeed;
    }

    // Update is called once per frame
    void Update()
    {
        InputUpdate();
    }
    private void FixedUpdate()
    {
        Movement();
    }
    private void InputUpdate() 
    {
        _orientation.forward = (transform.position - new Vector3(_cameraTransform.position.x, transform.position.y, _cameraTransform.position.z)).normalized;

        _moveDir = _orientation.forward * Input.GetAxis("Vertical") + _orientation.right * Input.GetAxis("Horizontal");
        _moveDir.Normalize();
        if (_moveDir != Vector3.zero) 
        {
            _playerBody.forward = Vector3.Slerp(_playerBody.forward,_moveDir,Time.deltaTime * 10);
        }
    }
    private void Movement() 
    {
        _rigidbody.AddForce(_moveDir * _movement.acceleration);
    }
}
