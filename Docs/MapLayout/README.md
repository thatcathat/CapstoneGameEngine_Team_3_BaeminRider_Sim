# 맵 데이터 안내

문서가 아닌 맵 제작용 데이터와 이미지다. 일반적인 개발 흐름 파악에는 [4구역 월드](../4구역_지형_구현.md)만 읽는다.

| 파일 | 용도 |
|---|---|
| district_geometry.json | 현재 지형·도로·목적지 배치 데이터 |
| district_surface.png / district_ground.png | 지도 표시 / 지면 색 |
| route_table.json | 주문 보상·제한시간 등에 쓰는 거리표 |
| hazard_slots.json | 이전 고정 사건 자리 80곳. 함정 랜덤 배치로 바뀌어 현재 쓰지 않는다 |
| road_elevation.* / road_summary.csv | 초기 도로·고도 계획 자료. 현재 지형과 구분 |

이번 문서 정리에서 데이터·이미지는 변경하지 않았다. 필요한 작업에서 관련 파일만 확인한다.
기존 데이터는 유지하되 HTML 원본을 다시 읽거나 참조하지 않는다.
