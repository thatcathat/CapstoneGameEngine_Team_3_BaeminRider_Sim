# 1층 외관 작업 완료 — 2026-11-05

- 전체52채 처리 완료: 49채 보완, A01/A02/B01 기존 상세 원본3채 보존. 이 작업의 미진행 구역 없음.
- B02~B18 17채, A03~A07 5채, E01~E13 13채, F01~F06/G01~G06/H01~H02 14채.
- 마지막14채: G02/G04 상가 및 H01 주택의 1층 재구성. 나머지11채는 이미 갖춰진 아치/기둥/깊은 창/출입구를 보존하고 기존 문에 입체 손잡이를 보완. F02/F05/G01/G05는 기존 문 위에 짧은 차양 추가. 원본 상층부/6색 범위/배달점/도로/인도/이벤트 유지.
- 저장 완료: Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity, Assets/_Game/Prefabs/Myeongji/MyeongjiMap.prefab, Assets/_Game/Art/Myeongji/Geometry/StreetFronts/*.asset. 대응 meta는 Unity 생성, 함께 버전관리. 원본 FBX 유지.
- 검사: 전체52채 씬-프리팹 메시 일치, MeshCollider 및 재질 슬롯 일치, 작업한49채 메시 저장 확인. 임시 카메라 잔여 없음. 전체 보행202경로8408표본 통과, 각 수정 건물 배달위치 캡슐검사 통과. 필요한 화면 검토 완료. Play는 사용자가 수행; 이번 작업에서 진입하지 않음.
- 결과 보고서 streetfront_final_check.json, sidewalk_final_audit.json, ground_floor_batch05.txt.
- 마지막 제작 도구 Tools/MyeongjiGroundFloorFinal.cs (4/4/3/3채 저장). 원본 프리팹도 최종 보완본으로 저장되었으므로 완료 Apply 재실행 금지. Special은 기존 원본 전체 유지+추가 기하, 중복 실행 차단.
- 검사자료 ground_floor_final_inspect.json, final_entry_inspect.json은 보완 전 원본 측정값. 변경 후 다시 덮어쓰지 말 것.
- A/E/Final Infill은 인접 셀 내부 면 생성을 생략. 메시 갱신은 Clear/vertices/SetTriangles/RecalculateNormals/Bounds 사용. CopySerialized(mesh,existing)는 표시 깨짐을 일으켰으므로 사용하지 않음.
- 백업 C:/Users/PC/Documents/Codex/Myeongji_District/Facade_Batch05_Backup
- 최종 미리보기 C:/Users/PC/Documents/Codex/Myeongji_District/GroundFloor_Final_After_20261105.png
- 기존 카메라 설정 유지. Git 커밋/푸시 없음. 새 작업은 사용자 다음 요청 기준.
