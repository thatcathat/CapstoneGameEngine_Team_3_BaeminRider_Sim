# 인도 보완 재개 위치 — 2026-11-01, Stage11 완료

## 사용자 요청
남은 구역 전부 보완. 구간별 저장/필요한 검토, 한도 전에 재개 기록. Play 진입 금지(사용자 직접 확인). 카메라 설정 유지. Unity 직렬화 자산은 Editor API로만 변경.

## 현재 저장 상태
- 프로젝트 C:/Users/PC/Desktop/game
- 씬 Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity
- 바닥 Assets/_Game/Art/Myeongji/Geometry/Ground_SidewalkStage11.asset
- 보행 경로 183개, 근육맨 후보45개, 하루8/8/8/8 유지
- 맵 프리팹/거리표/지도/배달위치/문 후보 동기화 및 저장 완료
- 마지막 검토 E/H 배달12지점, 차량572표본, 근육맨45후보 양방향 접근/회전 조회, 누락스크립트0. 최종 편집 이미지 SidewalkStage11_preview.png 확인. Play 미검증.
- A/B 및 남쪽 확장 E/H의 이번에 구성한 인도 경로 폭2m와 모퉁이 연결 보완 완료. 맵 전역이 완료된 것은 아님.

## 바로 다음 작업
1. 활성 씬/dirty/Stage11/183경로 확인. 사용자/팀 변경이 있으면 먼저 비교.
2. 동쪽 F/G 구역을 작은 구간으로 나눠 보완. 현재 최신 읽기 결과는 Docs/MapLayout/Myeongji/remaining_inspection.json. 기존 Stage11Paths에서 연결10개를 제거해 인덱스가 바뀌었다. 이전 단계의 97 이후 번호를 그대로 적용하면 안 됨.
3. F/G 후보 구간: 상부 F01/F02 앞 대각선 보도 → F03~F06과 G01~G05를 둘러싼 외곽 → 내부 가지/세로 골목(G06 포함). 맵 바깥쪽 기존 보행 경로도 실제 통행 공간 검토 필요.
4. 마지막에 전체 경로의 실제 폭/바닥/충돌과 외부 구역 간 연결을 검토. 가로등/신호등으로 인한 간섭, 골목 횡단과 차량 도로 횡단을 구분. 기존의 큰길 가장자리 우회/고정 halfWidth=1.2는 미수정 구역에 남아 있음.

## F/G 지형 참고 (치수는 재검토 후 적용)
- 동쪽 상부 도로: (116,0)→(84,-52), 폭7. F쪽은 도로 동쪽. 오른쪽 인도 중심은 x=116+(32/52)*z+5.45*sqrt(1+(32/52)^2). 벽 여유3.3m라면 5.45 대신6.8을 사용해 건물 최소x 후보 계산. 대각선 건물 최소x는 건물 zMax에서 검사.
- 남쪽 대각선 도로: (84,-52)→(64,-103), 폭7. F쪽 인도 중심 x=84+(20/51)*(z+52)+5.45*sqrt(1+(20/51)^2). 두 도로가 만나는 z=-52 부근은 실제 바닥과 코너 연결 검사 필수.
- F/G 외곽 상단 도로 z=-44, 폭4 → 남쪽 보도 중심 -47.95, 벽 zMax<=-49.3 후보. F02 북쪽 보도는 중심 -40.05, 벽 zMin>=-38.7 후보.
- 오른쪽 도로 x=117, 폭4 → 서쪽 보도 중심113.05, 벽 xMax<=111.7 후보. 동쪽 보도 중심120.95 후보.
- 바닥쪽 도로 z=-103, 폭6 → 북쪽 보도 중심-98.05, 벽 zMin>=-96.7 후보.
- 내부 도로 x=101,z=-63~-89, 폭3 및 z=-63,x=101~117, 폭3. 좁은 내부 건물 공간을 고려해 일괄 축소하지 말고 구간별 확인.

## 재사용 도구/주의
- pipeline.py: C:/Users/PC/Documents/Codex/2026-10-01/d/pipeline.py. editor_status, run_script {file:Tools/...,entry:Class.Method}. 포트 비밀값 출력 금지.
- 기본 보행 베이커 재실행 금지: 정리한 A/B/E 경로가 사라짐. 완료 단계 Geometry.Apply/Paths.Apply 중복 실행도 금지.
- 기하 작업은 기존 Plot 삼각형을 분할해 Pavement로 변경, 면 중첩 금지. ExpandPavement/Split은 Stage11 또는 대각선용 Stage10 재사용. 원본 Ground.fbx의 총면적과 비교해 지오메트리 손실 검토.
- 건물 renderer bounds를 기준으로 필요한 수평 축만 조정하고 anchors/layout/벽문 후보를 remap. 높이는 유지. 원점이0인 FBX 메시라 transform 위치만 보고 크기를 계산하면 안 됨.
- 구간마다 Assets 밖 백업→Geometry→Paths→build_myeongji_routes.py→Finish.Apply→Review.Run→저장. 새 경로/연결은 검증 성공 후 한 번에 Configure.
- Stage11의 Finish/Review를 복제할 경우 검사할 building ID 필터와 차량 범위를 새 구역으로 바꿔야 함.
- 임시 프리뷰 카메라만 사용 후 삭제/씬 저장. 최초 셰이더 변형 컴파일 때 프리뷰가 하늘만 나올 수 있음. 이번에는 재시도1회로 정상 렌더 확인. 반복 캡처 금지.
- Blender 원본에는 아직 보완이 이관되지 않았으므로 전체 FBX 프리팹 재생성 금지.
- Git 커밋/푸시는 하지 않음. 사용자/팀 변경 유지.
