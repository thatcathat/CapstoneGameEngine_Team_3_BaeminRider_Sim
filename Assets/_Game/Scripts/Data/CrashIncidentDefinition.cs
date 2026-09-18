using System.Collections;
using UnityEngine;

namespace DeliveryRider
{
    [CreateAssetMenu(menuName = "Delivery Rider/Incidents/Crash")]
    public sealed class CrashIncidentDefinition : IncidentDefinition
    {
        [SerializeField, Min(0.1f)] private float warningSeconds = 0.8f;
        [SerializeField, Min(0f)] private float horizontalImpulse = 8f;
        [SerializeField, Min(0f)] private float upwardImpulse = 4f;
        [SerializeField, Min(0.1f)] private float recoverySeconds = 2f;

        public override IEnumerator Execute(IncidentDirector director, IncidentTrigger source)
        {
            director.SetNotice("위험! 주황색 구역에서 벗어나세요");
            yield return new WaitForSeconds(warningSeconds);
            if (source == null || !source.Contains(director.Rider.transform.position))
            {
                director.SetNotice("사고를 피했습니다!");
                yield return new WaitForSeconds(1f);
                yield break;
            }
            director.SetNotice("충돌! 잠시 후 다시 움직일 수 있습니다");
            director.Rider.BeginCrash(source.ImpactDirection * horizontalImpulse + Vector3.up * upwardImpulse);
            yield return new WaitForSeconds(recoverySeconds);
            director.Rider.EndCrash();
            director.SetNotice("회복 완료 · 배달을 계속하세요");
            yield return new WaitForSeconds(1f);
        }
    }
}
