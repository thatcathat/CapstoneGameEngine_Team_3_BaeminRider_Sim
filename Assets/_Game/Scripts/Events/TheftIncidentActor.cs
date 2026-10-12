using System.Collections;
using UnityEngine;

namespace DeliveryRider
{
    public sealed class TheftIncidentActor : IncidentActor
    {
        private FoodThief thief;
        public void SetThief(FoodThief value) => thief = value;

        public override IEnumerator Execute(IncidentDirector director, IncidentTrigger source, IncidentDefinition definition)
        {
            var settings = (TheftIncidentDefinition)definition;
            var session = director.Session;
            try
            {
                bool stolen = session.TryLoseFood(thief != null ? thief.transform : null);
                if (thief != null)
                    thief.Initialize(session, source.EscapeRoute, settings.ThiefSpeed, settings.EscapeSeconds, stolen);
                director.SetNotice(!stolen ? "도난 시도! 보유한 음식이 없습니다" :
                    thief != null ? "음식 도난! 보라색 도둑에게 부딪혀 회수하세요" : "음식을 잃었습니다! 음식점에서 다시 받아주세요");
                if (!stolen && thief == null)
                {
                    yield return new WaitForSeconds(1f);
                    yield break;
                }
                while (thief != null && !thief.Finished && (!stolen || session.IsChasingFood)) yield return null;
                if (stolen && session.IsRecoveringFood)
                {
                    director.SetNotice("음식점으로 돌아가 E로 음식을 다시 받으세요");
                    while (session.IsRecoveringFood) yield return null;
                }
                director.SetNotice(stolen ? "음식 회수 완료 · 고객에게 배달하세요" : "도난 시도 종료");
                yield return new WaitForSeconds(1f);
            }
            finally
            {
                if (session != null && session.IsRecoveringFood && thief != null)
                    session.RequireReplacement(thief.transform);
            }
        }
    }
}
