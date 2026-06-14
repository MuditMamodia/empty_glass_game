using UnityEngine;
using System.Collections;
using UnityEngine.Animations.Rigging;

public class foot_ik : MonoBehaviour
{
    [Header("Script reference")]
    //public movement_script ms;

    [Header("Foot references")]
    [SerializeField] private Transform right_foot;
    [SerializeField] private Transform left_foot;

    [Header("Detection settings")]
    [SerializeField] private float distance_to_floor = 1f;
    [SerializeField] private float sphere_radius = 0.05f;
    [SerializeField] private float adding_extra = 0f;
    [SerializeField] private float adding_extra_changeable = 0.05f;
    [SerializeField] private float adding_extra_hips = 0.02f;
    public LayerMask lm;

    [Header("IK Targets")]
    [SerializeField] private GameObject R_foot_target;
    [SerializeField] private GameObject L_foot_target;
    [SerializeField] private Transform hips;

    [Header("IK Constraints")]
    public TwoBoneIKConstraint[] tbict;

    // Internal
    private Vector3 hipsDefaultPos;
    private RaycastHit L_hit;
    private RaycastHit R_hit;

    private bool leftfoot_on_ground;
    private bool rightfoot_on_ground;

    private float currentIKWeight = 0f;
    private float desiredIKWeight = 0f;

    private void Start()
    {
        hipsDefaultPos = hips.localPosition;
        //ms = GetComponent<movement_script>();
    }

    private void LateUpdate()
    {
        // Determine if IK should be on
        //if (ms != null && ms.ismoving)
        //{
        //    // Instantly disable IK while moving to prevent flicker
        //    desiredIKWeight = 0f;
        //    currentIKWeight = 0f;
        //}
        //else
        //{
            // Standing still → use IK
            desiredIKWeight = 1f;
        //}

        // Smoothly blend when enabling IK
        if (desiredIKWeight > currentIKWeight)
        {
            currentIKWeight = Mathf.MoveTowards(currentIKWeight, desiredIKWeight, Time.deltaTime * 5f);
        }
        else
        {
            currentIKWeight = desiredIKWeight; // disable instantly
        }

        // Apply IK weight
        foreach (TwoBoneIKConstraint constraint in tbict)
        {
            constraint.weight = currentIKWeight;
        }

        // Only run raycasts if IK is active
        if (currentIKWeight > 0.01f)
        {
            float leftDistance = CheckFoot(left_foot, ref L_hit, L_foot_target, ref leftfoot_on_ground);
            float rightDistance = CheckFoot(right_foot, ref R_hit, R_foot_target, ref rightfoot_on_ground);

            // Adjust hips height based on higher foot
            float hipOffset = Mathf.Max(leftDistance, rightDistance);
            Vector3 targetHipsPos = hipsDefaultPos;
            targetHipsPos.y -= hipOffset;

            hips.localPosition = Vector3.Lerp(hips.localPosition, targetHipsPos + Vector3.up * adding_extra_hips, Time.deltaTime * 5f);
        }
    }

    private float CheckFoot(Transform foot, ref RaycastHit hitInfo, GameObject target, ref bool footOnGround)
    {
        if (Physics.Raycast(foot.position + Vector3.up * 0.1f, Vector3.down, out hitInfo, distance_to_floor, lm))
        {
            // Check if ground is uneven
            float slopeAmount = Vector3.Angle(hitInfo.normal, Vector3.up);

            // Only add extra height if slope is significant
            float extra = slopeAmount > 1f ? adding_extra : 0f;

            target.transform.position = hitInfo.point + Vector3.up * extra;

            footOnGround = true;
            return (foot.position.y - hitInfo.point.y);
        }
        else
        {
            footOnGround = false;
            return 0f;
        }
    }

    private void OnDrawGizmos()
    {
        if (left_foot == null || right_foot == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(left_foot.position, left_foot.position + Vector3.down * distance_to_floor);
        Gizmos.DrawLine(right_foot.position, right_foot.position + Vector3.down * distance_to_floor);

        if (L_hit.collider != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(L_hit.point, sphere_radius);
        }
        if (R_hit.collider != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(R_hit.point, sphere_radius);
        }
    }
}