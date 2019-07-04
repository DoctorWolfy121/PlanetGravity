using UnityEngine;
using System.Collections.Generic;

public class GravitySource : MonoBehaviour
{
    [Tooltip("How much gravity force to apply to objects within range")]
    public float gravity = 9.8f;

    [Tooltip("The maximum distance from the surface of the gravity source that is still affected by gravity")]
    public float radius = 5.0f;

    [SerializeField, Space(5), Tooltip("Enable Debug rays and lines to help visualise the gravity.")]
    private bool enableDebug;

    private Collider[] _gravityColliders;

    private const float MaxRaycastDistance = 100.0f;

    private List<Rigidbody> _objectsInRange = new List<Rigidbody>();

    private void OnDrawGizmos()
    {
        if (!enableDebug) return;

        if (Camera.current == null)
            return;

        // Visualize gravity radius 
        Gizmos.color = Color.magenta;
        for (int i = 0; _gravityColliders != null && i < _gravityColliders.Length; ++i)
        {
            var col = _gravityColliders[i];
            DrawLine(col, transform.up);
            DrawLine(col, transform.right);
            DrawLine(col, transform.forward);
        }
    }

    private void DrawLine(Collider collider, Vector3 dir)
    {
        var raycastFrom = collider.transform.position + dir * 1000.0f;
        var raycastDir = (collider.transform.position - raycastFrom).normalized;
        var ray = new Ray(raycastFrom, raycastDir);
        if (collider.Raycast(ray, out var hitInfo, 2000.0f))
        {
            Gizmos.DrawLine(hitInfo.point, hitInfo.point + hitInfo.normal * (-radius * 2));
        }
    }

    private void Awake()
    {
        _gravityColliders = GetComponents<Collider>();

        if (_gravityColliders == null || _gravityColliders.Length == 0)
        {
            Debug.LogWarning("GravitySource has no colliders, will not be functional.");
        }

        SetItemsInRadius();
    }

    private void SetItemsInRadius()
    {
        var overlapColliders = Physics.OverlapSphere(transform.position, radius);

        foreach (var c in overlapColliders)
        {
            var component = c.GetComponent<GravityItem>();
            if (component != null)
            {
                OnTriggerEnter(c);
            }
        }
    }

    private void OnTriggerEnter(Collider c)
    {
        var rb = c.GetComponent<Rigidbody>();
        if (rb == null || _objectsInRange.Contains(rb)) return;

        _objectsInRange.Add(rb);

        var item = rb.GetComponent<GravityItem>() ?? rb.gameObject.AddComponent<GravityItem>();
        ++item.ActiveFieldCount;
        item.CurrentGravitySource = this;
    }

    private void OnTriggerExit(Collider c)
    {
        var rb = c.GetComponent<Rigidbody>();
        if (rb == null || !_objectsInRange.Contains(rb)) return;

        _objectsInRange.Remove(rb);

        var item = rb.GetComponent<GravityItem>() ?? rb.gameObject.AddComponent<GravityItem>();
        --item.ActiveFieldCount;
        item.CurrentDistance = Mathf.Infinity;
        item.CurrentGravitySource = null;
    }

    private void FixedUpdate()
    {
        // Iterate over each object within range of our gravity
        for (int i = 0; _objectsInRange != null && i < _objectsInRange.Count; ++i)
        {
            if (_objectsInRange[i] == null || !_objectsInRange[i].useGravity)
                continue;

            // Calculate initial gravity direction, just towards the gravity source transform
            var rb = _objectsInRange[i];
            var gravityDir = (transform.position - rb.transform.position).normalized;

            // Find out which of our child colliders is closest
            var closestHit = Mathf.Infinity;
            foreach (var gravityCollider in _gravityColliders)
            {
                // Skips this collider if it isn't a trigger
                if (!gravityCollider.isTrigger) continue;

                // Raycast in general direction of collider to find a normal of the surface
                var raycastTo = gravityCollider.transform.position;
                var toCollider = (raycastTo - rb.transform.position).normalized;
                var gravityRay = new Ray(rb.transform.position, toCollider);
                if (gravityCollider.Raycast(gravityRay, out var hitInfo, MaxRaycastDistance))
                {
                    if (enableDebug)
                    {
                        Debug.DrawRay(gravityRay.origin, gravityRay.direction * 2, Color.red);
                        Debug.DrawRay(hitInfo.point, hitInfo.normal * 2, Color.red);
                    }

                    // Set our new ray to point in the opposite direction of this normal, to raycast 'down' towards the closest point on the plane formed by the normal
                    gravityRay = new Ray(rb.transform.position, -hitInfo.normal);

                    // Update gravity direction guess if this was a closer hit
                    var dist = Vector3.Distance(hitInfo.point, gravityRay.origin);
                    if (dist < closestHit)
                    {
                        gravityDir = -hitInfo.normal;
                        closestHit = dist;
                    }
                }

                // Raycast a second time onto the collider with the refined 'down' direction
                if (gravityCollider.Raycast(gravityRay, out hitInfo, MaxRaycastDistance))
                {
                    if (enableDebug)
                    {
                        Debug.DrawRay(gravityRay.origin, gravityRay.direction * 2, Color.green);
                        Debug.DrawRay(hitInfo.point, hitInfo.normal * 2, Color.green);
                    }

                    var dist = Vector3.Distance(hitInfo.point, gravityRay.origin);
                    if (dist < closestHit)
                    {
                        gravityDir = -hitInfo.normal;
                        closestHit = dist;
                    }
                }
            }

            if (enableDebug)
            {
                Debug.DrawRay(rb.transform.position, gravityDir * 2, Color.blue);
            }

            // Now apply gravity if we are the closest source (only 1 source at a time applies gravity)
            var item = rb.GetComponent<GravityItem>();
            if (item.CurrentGravitySource == this || closestHit < item.CurrentDistance)
            {
                // Update tracking vars 
                item.CurrentDistance = closestHit;
                item.CurrentGravitySource = this;
                item.Up = Vector3.Lerp(item.Up, -gravityDir.normalized, Time.deltaTime * 2.0f);

                // Calculate force
                var force = gravityDir.normalized * gravity;
                var distRatio = Mathf.Clamp01(closestHit / radius);

                // Gravity gets scaled up with distance because games
                force *= 1.0f + distRatio;
                rb.AddForce(force * rb.mass);
            }
        }
    }
}