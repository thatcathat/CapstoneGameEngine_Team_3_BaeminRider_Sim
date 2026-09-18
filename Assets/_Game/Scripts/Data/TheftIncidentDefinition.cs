using System.Collections;
using UnityEngine;

namespace DeliveryRider
{
    public enum TheftRecoveryMode { ChaseThenReplacement, ReplacementOnly }

    [CreateAssetMenu(menuName = "Delivery Rider/Incidents/Food Theft")]
    public sealed class TheftIncidentDefinition : IncidentDefinition
    {
        [SerializeField] private TheftRecoveryMode recoveryMode;
        [SerializeField] private FoodThief thiefPrefab;
        [SerializeField, Min(0.1f)] private float thiefSpeed = 3.6f;
        [SerializeField, Min(1f)] private float escapeSeconds = 18f;
        public override bool CanStart(DeliverySession session) => base.CanStart(session) && session.Stage == DeliveryStage.Carrying;
        public override bool CanUseSource(IncidentTrigger source, Vector3 playerPosition, bool timed) =>
            recoveryMode == TheftRecoveryMode.ReplacementOnly || (thiefPrefab != null && source.HasEscapeRoute);

        public override IEnumerator Execute(IncidentDirector director, IncidentTrigger source)
        {
            FoodThief thief = null;
            var session = director.Session;
            try
            {
                if (recoveryMode == TheftRecoveryMode.ChaseThenReplacement && source != null && source.HasEscapeRoute && thiefPrefab != null)
                {
                    thief = Instantiate(thiefPrefab, source.ThiefSpawn.position, source.ThiefSpawn.rotation);
                    thief.Initialize(session, source.EscapeRoute, thiefSpeed, escapeSeconds);
                }
                if (!session.TryLoseFood(thief != null ? thief.transform : null)) yield break;
                director.SetNotice(thief != null ? "음식 도난! 보라색 도둑에게 부딪혀 회수하세요" : "음식을 잃었습니다! 음식점에서 다시 받아주세요");
                while (thief != null && !thief.Finished && session.IsChasingFood) yield return null;
                if (session.IsRecoveringFood)
                {
                    director.SetNotice("음식점으로 돌아가 E로 음식을 다시 받으세요");
                    while (session.IsRecoveringFood) yield return null;
                }
                director.SetNotice("음식 회수 완료 · 고객에게 배달하세요");
                yield return new WaitForSeconds(1f);
            }
            finally
            {
                if (session != null && session.IsRecoveringFood && thief != null) session.RequireReplacement(thief.transform);
                if (thief != null) Destroy(thief.gameObject);
            }
        }
    }
}
