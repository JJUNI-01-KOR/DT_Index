# dt_palletizer  ·  담당 D  ·  scope `palletizer`

AGV1이 도착하면 로봇팔4로 출고 팔레트(PALLET_2)에 2×2, 3단으로 쌓는다. 관련: FR-13, 15 / NFR-06

## 노드
| 실행 파일 | 노드 이름 |
|---|---|
| `palletizer_node` | `palletizer_node` |

## 예정 모듈 (docs/FOLDER_STRUCTURE.md 1.5)
`pallet_layout.py` (12칸 좌표 계산)

## 실행
```bash
ros2 run dt_palletizer palletizer_node
```
