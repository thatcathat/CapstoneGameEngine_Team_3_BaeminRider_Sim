using System.Collections;
using UnityEngine;

namespace DeliveryRider
{
    public sealed class CrashIncidentActor : IncidentActor
    {
        public override IEnumerator Execute(IncidentDirector director, IncidentTrigger source, IncidentDefinition definition)
        {
            var settings = (CrashIncidentDefinition)definition;
            director.SetNotice("위험! 충돌에 대비하세요");
            yield return new WaitForSeconds(settings.WarningSeconds);
            if (source == null) yield break;
            director.SetNotice("충돌! 잠시 후 다시 움직일 수 있습니다");
            try
            {
                director.Rider.BeginCrash(source.ImpactDirection * settings.HorizontalImpulse + Vector3.up * settings.UpwardImpulse);
                yield return new WaitForSeconds(settings.RecoverySeconds);
            }
            finally
            {
                if (director.Rider != null) director.Rider.EndCrash();
            }
            director.SetNotice("회복 완료 · 배달을 계속하세요");
            yield return new WaitForSeconds(1f);
        }
    }
}
