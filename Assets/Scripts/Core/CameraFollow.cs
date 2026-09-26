using UnityEngine;

/// <summary>
/// Smooth-follow camera with screen shake. The TurnManager points it at the
/// active tank; Tank.Fire points it at the flying shell.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    public Transform target;
    public float smooth = 4f;
    public float minX = -45f;
    public float maxX = 45f;
    public float minY = 2f;

    float shake;

    void Awake() => Instance = this;

    public void Follow(Transform t) => target = t;

    public static void Shake(float amt)
    {
        if (Instance != null)
            Instance.shake = Mathf.Max(Instance.shake, amt);
    }

    void LateUpdate()
    {
        if (target != null)
        {
            Vector3 want = new Vector3(
                Mathf.Clamp(target.position.x, minX, maxX),
                Mathf.Max(target.position.y + 2f, minY),
                -10f);
            transform.position = Vector3.Lerp(transform.position, want,
                1f - Mathf.Exp(-smooth * Time.deltaTime));
        }
        if (shake > 0.01f)
        {
            transform.position += (Vector3)Random.insideUnitCircle * shake * 0.35f;
            shake = Mathf.Max(0f, shake - Time.deltaTime * 4f);
        }
    }
}
