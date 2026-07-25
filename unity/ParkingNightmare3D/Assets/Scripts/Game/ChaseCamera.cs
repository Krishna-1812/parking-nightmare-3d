using UnityEngine;
using PN3D.Core;

namespace PN3D.Game
{
    /// <summary>
    /// Chase camera that eases into an overhead assist view once the parking zone arms,
    /// mirroring the reference's camera behaviour (it switches to the assist view on
    /// entering the zone, with a long glide rather than a snap).
    /// </summary>
    public sealed class ChaseCamera : MonoBehaviour
    {
        public MissionDriver Driver;
        public Transform Target;

        public float ChaseDist = 9.5f;
        public float ChaseHeight = 4.6f;
        public float AssistHeight = 17f;
        public float AssistBack = 5.5f;

        float _assist;
        Vector3 _vel;

        void LateUpdate()
        {
            if (Target == null || Driver?.Run == null) return;

            bool inZone = Driver.Run.Park.InZone;
            _assist = Mathf.MoveTowards(_assist, inZone ? 1f : 0f, Time.deltaTime / 3.2f);
            float e = Mathf.SmoothStep(0f, 1f, _assist);

            Vector3 fwd = Target.forward;
            Vector3 desired = Target.position
                            - fwd * Mathf.Lerp(ChaseDist, AssistBack, e)
                            + Vector3.up * Mathf.Lerp(ChaseHeight, AssistHeight, e);

            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _vel, 0.18f);

            Vector3 look = Target.position + fwd * Mathf.Lerp(6f, 1.5f, e);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(look - transform.position, Vector3.up),
                1f - Mathf.Exp(-9f * Time.deltaTime));
        }

        public void SnapBehind()
        {
            if (Target == null) return;
            transform.position = Target.position - Target.forward * ChaseDist + Vector3.up * ChaseHeight;
            transform.rotation = Quaternion.LookRotation(
                (Target.position + Target.forward * 6f) - transform.position, Vector3.up);
        }
    }
}
