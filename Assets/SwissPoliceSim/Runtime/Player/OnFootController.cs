using UnityEngine;

namespace SwissPoliceSim.Player
{
    /// <summary>Walk / jog / sprint character on a CharacterController, movement relative to the camera yaw.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class OnFootController : MonoBehaviour
    {
        public float walkSpeed = 1.8f, jogSpeed = 3.6f, sprintSpeed = 5.6f;
        public Transform cameraYawSource;
        CharacterController _cc;
        float _vy;

        void Awake() { _cc = GetComponent<CharacterController>(); }

        void Update()
        {
            var input = GameInput.Move();
            Vector3 dir = Vector3.zero;
            if (input.sqrMagnitude > 0.01f && cameraYawSource != null)
            {
                var f = Vector3.ProjectOnPlane(cameraYawSource.forward, Vector3.up).normalized;
                var r = Vector3.ProjectOnPlane(cameraYawSource.right, Vector3.up).normalized;
                dir = (f * input.y + r * input.x).normalized;
            }
            float speed = GameInput.Run() ? sprintSpeed : jogSpeed;
            if (_cc.isGrounded && _vy < 0) _vy = -1f;
            _vy += Physics.gravity.y * Time.deltaTime;
            var move = dir * speed + Vector3.up * _vy;
            _cc.Move(move * Time.deltaTime);
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 12f * Time.deltaTime);
        }

        public void Teleport(Vector3 position)
        {
            _cc.enabled = false; transform.position = position; _cc.enabled = true; _vy = 0;
        }

        public static OnFootController Create(Vector3 position)
        {
            var go = new GameObject("Polizist");
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.35f;
            var dark = Util.MaterialUtil.Create(new Color(0.08f, 0.1f, 0.16f), 0.2f);
            var skin = Util.MaterialUtil.Create(new Color(0.85f, 0.68f, 0.55f), 0.2f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(go.transform, false); body.transform.localPosition = new Vector3(0, 0.85f, 0); body.transform.localScale = new Vector3(0.55f, 0.85f, 0.4f);
            body.GetComponent<Renderer>().sharedMaterial = dark; Destroy(body.GetComponent<Collider>());
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(go.transform, false); head.transform.localPosition = new Vector3(0, 1.65f, 0); head.transform.localScale = Vector3.one * 0.24f;
            head.GetComponent<Renderer>().sharedMaterial = skin; Destroy(head.GetComponent<Collider>());
            go.transform.position = position;
            return go.AddComponent<OnFootController>();
        }
    }
}
